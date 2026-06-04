@echo off
setlocal
chcp 65001 >nul

set "ROOT=%~dp0"
set "OUT=%ROOT%publish\SwiftList"

echo ==========================================
echo SwiftList Release publish
echo Output: %OUT%
echo ==========================================

where dotnet >nul 2>&1
if errorlevel 1 (
    echo [Error] dotnet CLI was not found. Please install the required .NET SDK.
    pause
    exit /b 1
)

echo.
echo [1/4] Cleaning publish directory...
if exist "%OUT%" (
    rmdir /s /q "%OUT%" >nul 2>&1
    if exist "%OUT%" (
        :: If it still exists, wait 1s and try once more
        timeout /t 1 >nul
        rmdir /s /q "%OUT%" >nul 2>&1
        if exist "%OUT%" (
            echo [Error] Failed to clean publish directory: %OUT%
            pause
            exit /b 1
        )
    )
)
mkdir "%OUT%"
if errorlevel 1 (
    echo [Error] Failed to create publish directory: %OUT%
    pause
    exit /b 1
)

echo [2/4] Publishing App in Release mode...
pushd "%ROOT%App"
dotnet publish ".\App.csproj" -c Release -o "%OUT%" -v quiet >nul
set "APP_EXIT=%errorlevel%"
popd
if not "%APP_EXIT%"=="0" (
    echo [Error] App publish failed.
    pause
    exit /b %APP_EXIT%
)

echo.
echo [3/4] Publishing Service in Release mode...
pushd "%ROOT%Service"
dotnet publish ".\Service.csproj" -c Release -o "%OUT%" -v quiet >nul
set "SERVICE_EXIT=%errorlevel%"
popd
if not "%SERVICE_EXIT%"=="0" (
    echo [Error] Service publish failed.
    pause
    exit /b %SERVICE_EXIT%
)

echo.
echo [4/4] Publishing Plugins in Release mode...
dotnet publish "%ROOT%SwiftList.Plugins.slnx" -c Release -o "%OUT%\Plugins" -v quiet >nul
set "PLUGINS_EXIT=%errorlevel%"
if not "%PLUGINS_EXIT%"=="0" (
    echo [Error] Plugins publish failed.
    pause
    exit /b %PLUGINS_EXIT%
)


echo.
echo ==========================================
echo Publish completed successfully.
echo Output: %OUT%
echo ==========================================
exit /b 0

