@echo off
chcp 65001 >nul 2>&1

:: Check admin privileges
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo [ERROR] This script requires administrator privileges.
    echo Please right-click and select "Run as administrator".
    echo.
    pause
    exit /b 1
)

:: Get script directory and cd to it
set "SCRIPT_DIR=%~dp0"
cd /d "%SCRIPT_DIR%"

:: Run PowerShell script with UTF-8 encoding
powershell -ExecutionPolicy Bypass -NoProfile -Command ^
    "[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; " ^
    "[Console]::InputEncoding = [System.Text.Encoding]::UTF8; " ^
    "Set-Location -LiteralPath (Get-Location).Path; " ^
    "& './InstallRegistry.ps1'"

echo.
pause
