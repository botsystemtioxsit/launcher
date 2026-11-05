//go:build !linux

package main

// На Windows адрес и маршруты ставит служба-обёртка (этап встраивания в Gateway Plus).
func configureInterface(name string, fc *FileConfig) error { return nil }
