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

:: Which runtime to fetch follows the architecture of the app sitting next to this script, read out of
:: its PE header, rather than the machine's own. Those differ in the case that matters: the x64 package
:: on an arm64 machine needs the x64 runtime, because the app in it is x64 and runs emulated. Asking
:: Windows what IT is would fetch arm64 there and leave the app still unable to start. One copy of this
:: script ships in both packages, so it has to work this out at run time rather than being baked in.
set "ARCH_SUFFIX=x64"
if exist "%~dp0SwiftList.App.exe" (
    for /f "usebackq tokens=*" %%a in (`powershell -NoProfile -Command "try { $b=[IO.File]::ReadAllBytes('%~dp0SwiftList.App.exe'); $p=[BitConverter]::ToUInt32($b,0x3C); if ([BitConverter]::ToUInt16($b,$p+4) -eq 0xAA64) { 'arm64' } else { 'x64' } } catch { 'x64' }"`) do set "ARCH_SUFFIX=%%a"
)

echo Installing .NET Desktop Runtime (%ARCH_SUFFIX%), please wait...

set "INSTALLER=%TEMP%\windowsdesktop-runtime-10-win-%ARCH_SUFFIX%.exe"
curl.exe -L --fail -o "%INSTALLER%" "https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-%ARCH_SUFFIX%.exe"
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
