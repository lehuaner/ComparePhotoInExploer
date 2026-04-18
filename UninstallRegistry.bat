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

:: Get script directory
set "SCRIPT_DIR=%~dp0"

:: Run PowerShell script with UTF-8 encoding
powershell -ExecutionPolicy Bypass -NoProfile -Command ^
    "[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; " ^
    "& '%SCRIPT_DIR%UninstallRegistry.ps1'"

echo.
pause
