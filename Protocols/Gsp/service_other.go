//go:build !windows

package main

import "errors"

var errOnlyWindows = errors.New("служба Windows: только на Windows")

func runWinService(conf, name, statusPath string) error  { return errOnlyWindows }
func installService(conf, name, statusPath string) error { return errOnlyWindows }
func removeService(name string) error                    { return errOnlyWindows }
func serviceState(name string) (string, error)           { return "", errOnlyWindows }
func selftestRoute(ip string, hold int) error            { return errOnlyWindows }
