@echo off
rem Installs SFE as a Windows service that starts with the computer. Asks for administrator rights first.
net session >nul 2>&1
if errorlevel 1 (
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)
"%~dp0Sfe.Api.exe" --install-service
pause
