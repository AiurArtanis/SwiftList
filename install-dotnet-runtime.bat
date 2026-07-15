@echo off
setlocal

:: Silently installs the .NET 10 Desktop Runtime if it isn't already present -- same check/URL/install
:: args as Installer\installer.iss's IsDotNet10Installed/PrepareToInstall, for the portable ZIP build
:: (which, unlike the installer, has no bootstrap of its own).

set "RUNTIME_DIR=%ProgramFiles%\dotnet\shared\Microsoft.WindowsDesktop.App"
set "FOUND=false"
if exist "%RUNTIME_DIR%" (
    for /d %%V in ("%RUNTIME_DIR%\10.*") do set "FOUND=true"
)
if "%FOUND%"=="true" exit /b 0

echo Installing .NET Desktop Runtime, please wait...

set "INSTALLER=%TEMP%\windowsdesktop-runtime-10-win-x64.exe"
curl.exe -L --fail -o "%INSTALLER%" "https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe"
if errorlevel 1 (
    echo Download failed.
    pause
    exit /b 1
)

"%INSTALLER%" /install /quiet /norestart
del /q "%INSTALLER%" >nul 2>&1
echo Done.
pause
exit /b 0
