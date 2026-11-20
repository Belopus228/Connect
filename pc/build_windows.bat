@echo off
REM Сборка ПК-версии Connect в один .exe (нужен Python 3.10+ с pip)
setlocal
cd /d %~dp0
python -m pip install --upgrade pyinstaller
python -m PyInstaller --noconfirm --clean --onefile --windowed --name Connect-PC ConnectPC.py
echo.
echo Готово: dist\Connect-PC.exe
pause
