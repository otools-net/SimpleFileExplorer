@echo off
rem Removes the SFE Windows service (its settings and data stay in this folder). Asks for administrator rights first.
net session >nul 2>&1
if errorlevel 1 (
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)
"%~dp0Sfe.Api.exe" --uninstall-service
pause
