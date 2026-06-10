@echo off
chcp 65001 >nul
cd /d "%~dp0"

echo ==========================================
echo 1. Stopping SwiftList background service and frontend App...
echo ==========================================
echo Stopping frontend App...
taskkill /f /im SwiftList.App.exe >nul 2>&1
powershell -Command "Start-Process taskkill -ArgumentList '/f /im SwiftList.App.exe' -Verb RunAs -WindowStyle Hidden"

echo Stopping Tutorial...
taskkill /f /im SwiftList.Tutorial.exe >nul 2>&1
powershell -Command "Start-Process taskkill -ArgumentList '/f /im SwiftList.Tutorial.exe' -Verb RunAs -WindowStyle Hidden"

echo Requesting Administrator privileges to stop SwiftListService...
powershell -Command "Start-Process sc -ArgumentList 'stop SwiftListService' -Verb RunAs -WindowStyle Hidden"

echo Requesting Administrator privileges to kill hook subprocess (SwiftList.Service.exe --hook)...
powershell -Command "Start-Process taskkill -ArgumentList '/f /im SwiftList.Service.exe' -Verb RunAs -WindowStyle Hidden"

:: Wait 3 seconds for the service and app to exit completely and release file lock
ping 127.0.0.1 -n 4 >nul

echo Cleaning up debug directory...
if exist "%~dp0debug" (
    rmdir /s /q "%~dp0debug" >nul 2>&1
    if exist "%~dp0debug" (
        ping 127.0.0.1 -n 2 >nul
        rmdir /s /q "%~dp0debug" >nul 2>&1
    )
)

echo ==========================================
echo 2. Building projects (dotnet build directly to debug directory)...
echo ==========================================
dotnet build SwiftList.slnx /p:OutputPath="%~dp0debug/" >nul 2>&1
if %errorLevel% neq 0 (
    echo [ERROR] Main program compilation failed, please check the build output!
    pause
    exit /b
)

dotnet build SwiftList.Plugins.slnx /p:OutputPath="%~dp0debug/Plugins/" >nul 2>&1
if %errorLevel% neq 0 (
    echo [ERROR] Plugins compilation failed, please check the build output!
    pause
    exit /b
)

echo.
echo ==========================================
echo 3. Launching WPF frontend application with standard user privileges...
echo ==========================================
powershell -Command "Start-Process -FilePath '%~dp0debug\SwiftList.App.exe' -WorkingDirectory '%~dp0debug'"

echo Build and run script completed successfully.
