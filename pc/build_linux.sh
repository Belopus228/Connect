#!/usr/bin/env bash
# Сборка ПК-версии Connect под Linux (нужен python3 + tkinter: sudo apt install python3-tk)
set -e
cd "$(dirname "$0")"
python3 -m pip install --upgrade pyinstaller
python3 -m PyInstaller --noconfirm --clean --onefile --windowed --name Connect-PC ConnectPC.py
echo "Готово: dist/Connect-PC"
