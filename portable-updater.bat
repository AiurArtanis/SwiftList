@echo off
setlocal enabledelayedexpansion
chcp 65001 >nul

:: 1. Check for Admin privileges and self-elevate
net session >nul 2>&1
if %errorLevel% neq 0 (
    powershell -Command "Start-Process -FilePath '%~f0' -ArgumentList '\"%~1\" \"%~2\"' -Verb RunAs"
    exit /b
)

:: %1: The source directory containing the new version files (unzipped temporary directory)
:: %2: The target installation directory of the current SwiftList instance
set "SRC_DIR=%~1"
set "DST_DIR=%~2"

if "%SRC_DIR%"=="" exit /b 1
if "%DST_DIR%"=="" exit /b 1

:KillApp
tasklist /FI "IMAGENAME eq SwiftList.App.exe" 2>NUL | find /I /N "SwiftList.App.exe" >NUL
if "%errorlevel%"=="0" (
    taskkill /F /IM SwiftList.App.exe >nul 2>&1
    timeout /t 1 /nobreak >nul
    goto KillApp
)

sc stop SwiftListService >nul 2>&1
timeout /t 1 /nobreak >nul

:KillService
tasklist /FI "IMAGENAME eq SwiftList.Service.exe" 2>NUL | find /I /N "SwiftList.Service.exe" >NUL
if "%errorlevel%"=="0" (
    taskkill /F /IM SwiftList.Service.exe >nul 2>&1
    timeout /t 1 /nobreak >nul
    goto KillService
)

:: Copy new files to destination directory, overwriting existing files
xcopy "%SRC_DIR%\*" "%DST_DIR%\" /E /Y /Q /R

:: Re-start the background service
sc start SwiftListService >nul 2>&1

:: Run SwiftList.App.exe as standard user via explorer.exe to avoid running App as administrator
start "" explorer.exe "%DST_DIR%\SwiftList.App.exe"

exit /b 0
