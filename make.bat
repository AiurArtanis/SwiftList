@echo off
setlocal
chcp 65001 >nul

set "ROOT=%~dp0"

echo ==========================================
echo SwiftList Build and Package Script (make)
echo ==========================================

echo.
echo [1/2] Running dotnet release publish...
call "%ROOT%publish_release.bat"
if errorlevel 1 (
    echo [Error] Release publish failed.
    exit /b 1
)

:: Clean up PDB files from the publish directory so both Installer and Portable ZIP are lightweight
echo Cleaning PDB files from publish directory...
del /s /q "%ROOT%publish\SwiftList\*.pdb" >nul 2>&1

echo.
echo [2/2] Compiling NSIS Installer...

:: Find makensis.exe using flat if statements to avoid (x86) parentheses parsing bugs
set "MAKENSIS="
where makensis >nul 2>&1
if "%errorlevel%"=="0" set "MAKENSIS=makensis"
if "%MAKENSIS%"=="" if exist "C:\Program Files (x86)\NSIS\makensis.exe" set "MAKENSIS=C:\Program Files (x86)\NSIS\makensis.exe"
if "%MAKENSIS%"=="" if exist "C:\Program Files\NSIS\makensis.exe" set "MAKENSIS=C:\Program Files\NSIS\makensis.exe"

if "%MAKENSIS%"=="" (
    echo [Error] NSIS compiler makensis.exe not found.
    echo Please install NSIS or add it to your PATH.
    exit /b 1
)

echo Using NSIS compiler: "%MAKENSIS%"
"%MAKENSIS%" "%ROOT%installer.nsi"
if errorlevel 1 (
    echo [Error] NSIS compilation failed.
    exit /b 1
)

echo.
echo [3/3] Creating Portable ZIP Archive...
powershell -Command "if (Test-Path '%ROOT%SwiftList-Portable.zip') { Remove-Item -Force '%ROOT%SwiftList-Portable.zip' }; Compress-Archive -Path '%ROOT%publish\SwiftList' -DestinationPath '%ROOT%SwiftList-Portable.zip' -Force"
if errorlevel 1 (
    echo [Error] Portable ZIP creation failed.
    exit /b 1
)

echo.
echo ==========================================
echo Build and Package Completed Successfully!
echo Installer: %ROOT%SwiftList-Setup.exe
echo Portable ZIP: %ROOT%SwiftList-Portable.zip
echo ==========================================
exit /b 0
