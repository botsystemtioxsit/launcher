package main

// Постквантовая защита GSP Beta (ML-KEM-768, FIPS 203, из стандартной библиотеки Go).
//
// Схема та же, что у Mullvad и Rosenpass: общий секрет, полученный через ML-KEM, подмешивается в рукопожатие как PRESHARED KEY.
// Клиент заранее знает открытый ключ ML-KEM сервера (`KemPub` в профиле), в первом сообщении присылает «шифртекст» ML-KEM, сервер его раскрывает,
// и обе стороны получают одинаковый секрет `ss`. Дальше psk = HKDF-SHA256(соль = BetaKey, материал = ss). Этот psk входит в Noise XKpsk3 (psk3):
// ключи сеанса зависят и от Curve25519, и от ML-KEM. Чтобы прочитать записанный сегодня трафик, придётся сломать ОБА (в том числе, когда
// появится квантовый компьютер, ломающий Curve25519). Новых шифров нет: ML-KEM — стандарт NIST, HKDF — RFC 5869; всё — готовые вещи.
//
// Что НЕ даёт: проверка подлинности по-прежнему на Curve25519 (подделать сервер «потом» нельзя: подпись ему нужна в момент соединения);
// у ML-KEM-768 нет возраста проверки, как у Curve25519, поэтому он добавляется к нему, а не вместо него.

import (
	"crypto/hkdf"
	"crypto/mlkem"
	"crypto/sha256"
	"encoding/base64"
	"errors"
)

const (
	kemCtLen   = mlkem.CiphertextSize768
	kemPubLen  = mlkem.EncapsulationKeySize768
	kemSeedLen = mlkem.SeedSize
)

// genKem создаёт пару ключей: «семя» закрытого ключа (для сервера) и открытый ключ (для клиентов), оба в base64.
func genKem() (seedB64, pubB64 string, err error) {
	dk, err := mlkem.GenerateKey768()
	if err != nil {
		return "", "", err
	}
	return base64.StdEncoding.EncodeToString(dk.Bytes()), base64.StdEncoding.EncodeToString(dk.EncapsulationKey().Bytes()), nil
}

func kemSeedFromB64(s string) ([]byte, error) {
	b, err := base64.StdEncoding.DecodeString(s)
	if err != nil || len(b) != kemSeedLen {
		return nil, errors.New("KemKey: ожидается 64 байта в base64")
	}
	return b, nil
}

func kemPubFromB64(s string) ([]byte, error) {
	b, err := base64.StdEncoding.DecodeString(s)
	if err != nil || len(b) != kemPubLen {
		return nil, errors.New("KemPub: ожидается открытый ключ ML-KEM-768 (1184 байта в base64)")
	}
	if _, err := mlkem.NewEncapsulationKey768(b); err != nil {
		return nil, errors.New("KemPub: ключ не разбирается")
	}
	return b, nil
}

// pqPSK — preshared key для Noise: HKDF-SHA256 от секрета ML-KEM с солью BetaKey.
func pqPSK(betaKey, ss []byte) []byte {
	k, _ := hkdf.Key(sha256.New, ss, betaKey, "gsp beta v3 pq psk", 32)
	return k
}
