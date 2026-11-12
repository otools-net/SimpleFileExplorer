@echo off
rem Installs SFE as a Windows service that starts with the computer. Asks for administrator rights first.
rem What follows the command goes to SFE: --name <service>, --allow-open-folder.
net session >nul 2>&1
if errorlevel 1 (
    if "%~1"=="" (
        powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    ) else (
        powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -ArgumentList '%*' -Verb RunAs"
    )
    exit /b
)
"%~dp0Sfe.Api.exe" --install-service %*
pause
