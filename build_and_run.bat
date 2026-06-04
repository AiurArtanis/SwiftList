@echo off
chcp 65001 >nul
cd /d "%~dp0"

echo ==========================================
echo 1. Stopping SwiftList background service and frontend App...
echo ==========================================
echo Stopping frontend App...
taskkill /f /im SwiftList.App.exe >nul 2>&1
powershell -Command "Start-Process taskkill -ArgumentList '/f /im SwiftList.App.exe' -Verb RunAs -WindowStyle Hidden"

echo Requesting Administrator privileges to stop SwiftListService...
powershell -Command "Start-Process sc -ArgumentList 'stop SwiftListService' -Verb RunAs -WindowStyle Hidden"

echo Requesting Administrator privileges to kill hook subprocess (SwiftList.Service.exe --hook)...
powershell -Command "Start-Process taskkill -ArgumentList '/f /im SwiftList.Service.exe' -Verb RunAs -WindowStyle Hidden"

:: Wait 3 seconds for the service and app to exit completely and release file lock
ping 127.0.0.1 -n 4 >nul

echo ==========================================
echo 2. Building projects (dotnet build)...
echo ==========================================
dotnet build SwiftList.slnx >nul 2>&1
if %errorLevel% neq 0 (
    echo [ERROR] Main program compilation failed, please check the build output!
    pause
    exit /b
)

dotnet build SwiftList.Plugins.slnx >nul 2>&1
if %errorLevel% neq 0 (
    echo [ERROR] Plugins compilation failed, please check the build output!
    pause
    exit /b
)


echo ==========================================
echo 3. Starting background service...
echo ==========================================
echo Requesting Administrator privileges to start SwiftListService...
powershell -Command "Start-Process sc -ArgumentList 'start SwiftListService' -Verb RunAs -WindowStyle Hidden"
ping 127.0.0.1 -n 2 >nul

echo ==========================================
echo 4. Launching WPF frontend application with standard user privileges...
echo ==========================================
start "" "%~dp0App\bin\Debug\net10.0-windows\SwiftList.App.exe"

echo Build and run script completed successfully.
ping 127.0.0.1 -n 4 >nul
