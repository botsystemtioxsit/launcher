//go:build windows

package main

import (
	"errors"
	"fmt"
	"net/netip"
	"os"
	"regexp"
	"time"

	"golang.org/x/sys/windows/svc"
	"golang.org/x/sys/windows/svc/mgr"
	"golang.zx2c4.com/wireguard/windows/tunnel/winipcfg"
)

var nameRe = regexp.MustCompile(`^[A-Za-z0-9_=+.\-]{1,32}$`)

func serviceName(n string) string { return "GhostShellGsp$" + n }

type gspService struct{ conf, status string }

func (s *gspService) Execute(args []string, r <-chan svc.ChangeRequest, changes chan<- svc.Status) (bool, uint32) {
	const accepted = svc.AcceptStop | svc.AcceptShutdown
	changes <- svc.Status{State: svc.StartPending}
	stop, err := startNode(s.conf, s.status, false)
	if err != nil {
		return true, 1
	}
	changes <- svc.Status{State: svc.Running, Accepts: accepted}
	for c := range r {
		switch c.Cmd {
		case svc.Interrogate:
			changes <- c.CurrentStatus
		case svc.Stop, svc.Shutdown:
			changes <- svc.Status{State: svc.StopPending}
			stop()
			return false, 0
		}
	}
	return false, 0
}

func runWinService(conf, name, statusPath string) error {
	if !nameRe.MatchString(name) {
		return errors.New("плохое имя шлюза")
	}
	return svc.Run(serviceName(name), &gspService{conf: conf, status: statusPath})
}

func installService(conf, name, statusPath string) error {
	if !nameRe.MatchString(name) {
		return errors.New("плохое имя шлюза")
	}
	if _, err := os.Stat(conf); err != nil {
		return fmt.Errorf("файл настроек: %w", err)
	}
	exe, err := os.Executable()
	if err != nil {
		return err
	}
	m, err := mgr.Connect()
	if err != nil {
		return fmt.Errorf("менеджер служб (нужны права администратора): %w", err)
	}
	defer m.Disconnect()
	sn := serviceName(name)
	if s, err := m.OpenService(sn); err == nil { // старая копия — убираем
		stopAndDelete(s)
		s.Close()
		time.Sleep(300 * time.Millisecond)
	}
	args := []string{"winservice", "-c", conf, "-name", name}
	if statusPath != "" {
		args = append(args, "-status", statusPath)
	}
	s, err := m.CreateService(sn, exe, mgr.Config{
		DisplayName: "Ghost Shell Gateway (GSP) — " + name,
		Description: "Шлюз Gateway Plus по протоколу GSP. Управляется плагином Gateway Plus.",
		StartType:   mgr.StartManual,
	}, args...)
	if err != nil {
		return fmt.Errorf("создание службы: %w", err)
	}
	defer s.Close()
	if err := s.Start(); err != nil {
		_ = s.Delete()
		return fmt.Errorf("запуск службы: %w", err)
	}
	for i := 0; i < 60; i++ { // ждём «работает»
		st, err := s.Query()
		if err == nil && st.State == svc.Running {
			return nil
		}
		if err == nil && st.State == svc.Stopped {
			_ = s.Delete()
			return errors.New("служба остановилась сразу после запуска (причина — в файле состояния)")
		}
		time.Sleep(500 * time.Millisecond)
	}
	return errors.New("служба не запустилась за 30 секунд")
}

func stopAndDelete(s *mgr.Service) {
	if st, err := s.Query(); err == nil && st.State != svc.Stopped {
		_, _ = s.Control(svc.Stop)
		for i := 0; i < 40; i++ {
			if st, err := s.Query(); err != nil || st.State == svc.Stopped {
				break
			}
			time.Sleep(250 * time.Millisecond)
		}
	}
	_ = s.Delete()
}

func removeService(name string) error {
	if !nameRe.MatchString(name) {
		return errors.New("плохое имя шлюза")
	}
	m, err := mgr.Connect()
	if err != nil {
		return fmt.Errorf("менеджер служб (нужны права администратора): %w", err)
	}
	defer m.Disconnect()
	s, err := m.OpenService(serviceName(name))
	if err != nil {
		return nil // службы уже нет
	}
	defer s.Close()
	stopAndDelete(s)
	return nil
}

func serviceState(name string) (string, error) {
	if !nameRe.MatchString(name) {
		return "", errors.New("плохое имя шлюза")
	}
	m, err := mgr.Connect()
	if err != nil {
		return "", err
	}
	defer m.Disconnect()
	s, err := m.OpenService(serviceName(name))
	if err != nil {
		return "none", nil
	}
	defer s.Close()
	st, err := s.Query()
	if err != nil {
		return "", err
	}
	if st.State == svc.Running {
		return "running", nil
	}
	return "stopped", nil
}

// Для проверок на Windows-машине GitHub: ставит узкий маршрут к адресу «как сейчас до него добирается Windows», держит hold секунд и убирает.
// С watch — ещё и следит за маршрутом (как при работе службы): если его убрать руками, он должен вернуться.
func selftestRoute(ip string, hold int, watch bool) error {
	a, err := netip.ParseAddr(ip)
	if err != nil {
		return err
	}
	er := newEndpointRoute(winipcfg.LUID(0), a)
	if _, err := er.ensure(); err != nil {
		return err
	}
	fmt.Println("маршрут поставлен")
	if watch {
		er.watch(500*time.Millisecond, func() { fmt.Println("маршрут переставлен (сеть сменилась)") })
	}
	time.Sleep(time.Duration(hold) * time.Second)
	er.remove()
	fmt.Println("маршрут убран")
	return nil
}
