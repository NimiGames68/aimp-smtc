@echo off
setlocal
set "OUT=%LOCALAPPDATA%\AimpSmtc"
set "ZIP_URL=https://gitlab.com/NimiGames68/aimp-smtc/-/raw/main/assets/AIMPSMTC.zip"
set "ZIP_PATH=%TEMP%\AIMPSMTC.zip"
set "DOCS_DIR=%USERPROFILE%\Documents"

powershell -NoProfile -Command "Invoke-WebRequest -Uri '%ZIP_URL%' -OutFile '%ZIP_PATH%'"

powershell -NoProfile -Command "Expand-Archive -Path '%ZIP_PATH%' -DestinationPath '%DOCS_DIR%' -Force"

del "%ZIP_PATH%"

echo.
echo Closing old process
taskkill /IM AimpSmtc.exe /F >nul 2>&1
timeout /t 1 /nobreak >nul

echo Building
dotnet publish AimpSmtc.csproj -c Release -r win-x64 --self-contained false -o "%OUT%"
if errorlevel 1 ( echo ERROR: build failed. & pause & exit /b 1 )

echo Installed to: %OUT%\AimpSmtc.exe

echo Creating Startup shortcut
powershell -NoProfile -Command "$ws=New-Object -ComObject WScript.Shell; $lnk=$ws.CreateShortcut([Environment]::GetFolderPath('Startup')+'\AimpSmtc.lnk'); $lnk.TargetPath='%OUT%\AimpSmtc.exe'; $lnk.Save()"

echo.
echo Starting
start "" "%OUT%\AimpSmtc.exe"