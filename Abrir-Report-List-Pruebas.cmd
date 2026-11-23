@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\start-report-list-usability.ps1"
if errorlevel 1 pause
