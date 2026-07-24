@echo off
setlocal
chcp 65001 >nul

set "ROOT=%~dp0"
set "OUT=%ROOT%publish\SwiftList"
set "DIST=%ROOT%dist"

echo ==========================================
echo SwiftList Build and Package Script (make)
echo ==========================================

:: 1. Check for dotnet CLI
where dotnet >nul 2>&1
if errorlevel 1 (
    echo [Error] dotnet CLI was not found. Please install the required .NET SDK.
    exit /b 1
)

:: 2. Clean and create directories
echo.
echo [1/6] Cleaning publish and dist directories...
if exist "%OUT%" (
    rmdir /s /q "%OUT%" >nul 2>&1
    if exist "%OUT%" (
        timeout /t 1 >nul
        rmdir /s /q "%OUT%" >nul 2>&1
    )
)
mkdir "%OUT%"

if exist "%DIST%" (
    rmdir /s /q "%DIST%" >nul 2>&1
    if exist "%DIST%" (
        timeout /t 1 >nul
        rmdir /s /q "%DIST%" >nul 2>&1
    )
)
mkdir "%DIST%"

:: 3. Publish App/Service/Cli in Release mode
::
:: Solution-level `dotnet publish -o` prints NETSDK1194 ("specifying a solution-level output path...
:: may result in inconsistent builds") since it's not an officially supported publish mode -- every
:: project in SwiftList.slnx just publishes into the same -o in whatever order MSBuild picks, rather
:: than each publish being independently well-defined the way `dotnet publish SomeProject.csproj -o`
:: is. Used anyway (as SwiftList.Plugins.slnx already does below, without issue) since it's simpler to
:: maintain than a separate pushd/publish/popd block per exe project, and has been verified to produce
:: the same merged output (App+Service+Cli+Core+PluginSdk all landing in %OUT%) as the three separate
:: publishes it replaces.
echo.
echo [2/6] Publishing App/Service/Cli in Release mode...
dotnet publish "%ROOT%SwiftList.slnx" -c Release -o "%OUT%" -v quiet
set "SLN_EXIT=%errorlevel%"
if not "%SLN_EXIT%"=="0" (
    echo [Error] App/Service/Cli publish failed.
    exit /b %SLN_EXIT%
)

:: 4. Publish Plugins in Release mode
echo.
echo [3/6] Publishing Plugins in Release mode...
dotnet publish "%ROOT%SwiftList.Plugins.slnx" -c Release -o "%OUT%\Plugins" -v quiet
set "PLUGINS_EXIT=%errorlevel%"
if not "%PLUGINS_EXIT%"=="0" (
    echo [Error] Plugins publish failed.
    exit /b %PLUGINS_EXIT%
)


:: 5. Copy portable updater/cleanup files and clean PDB files
echo.
echo [4/6] Copying portable updater/cleanup files and cleaning PDB files...
copy "%ROOT%portable-updater.bat" "%OUT%\" >nul
if errorlevel 1 (
    echo [Warning] Failed to copy portable-updater.bat.
)
copy "%ROOT%install-dotnet-runtime.bat" "%OUT%\" >nul
if errorlevel 1 (
    echo [Warning] Failed to copy install-dotnet-runtime.bat.
)
copy "%ROOT%portable-cleanup-registry.reg" "%OUT%\" >nul
if errorlevel 1 (
    echo [Warning] Failed to copy portable-cleanup-registry.reg.
)
del /s /q "%OUT%\*.pdb" >nul 2>&1

:: 6. Find Inno Setup compiler
set "ISCC="
where iscc >nul 2>&1
if "%errorlevel%"=="0" set "ISCC=iscc"
if "%ISCC%"=="" if exist "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" set "ISCC=C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
if "%ISCC%"=="" if exist "C:\Program Files\Inno Setup 6\ISCC.exe" set "ISCC=C:\Program Files\Inno Setup 6\ISCC.exe"

if "%ISCC%"=="" (
    echo [Error] Inno Setup compiler ISCC.exe not found.
    echo Please install Inno Setup or add it to your PATH.
    exit /b 1
)

:: 7. Extract application version from App.csproj
echo.
echo Extracting application version...
for /f "usebackq tokens=*" %%v in (`powershell -NoProfile -Command "([xml](Get-Content '%ROOT%App\App.csproj')).Project.PropertyGroup.Version"`) do set "APP_VER=%%v"
for /f "usebackq tokens=*" %%v in (`powershell -NoProfile -Command "$a = '%APP_VER%.0.0.0' -split '\.'; $a[0..3] -join '.'"`) do set "APP_VER_4=%%v"
echo App Version: %APP_VER% (PE Version: %APP_VER_4%)

:: 8. Compile Inno Setup Installer
echo.
echo [5/6] Compiling Inno Setup Installer...
echo Using Inno Setup compiler: "%ISCC%"
"%ISCC%" /DAppVersion="%APP_VER%" /DAppVersion4="%APP_VER_4%" "%ROOT%Installer\installer.iss"
if errorlevel 1 (
    echo [Error] Inno Setup compilation failed.
    exit /b 1
)

:: 9. Create Portable ZIP Archive
echo.
echo Creating Portable ZIP Archive...
powershell -Command "if (Test-Path '%DIST%\SwiftList-Portable.zip') { Remove-Item -Force '%DIST%\SwiftList-Portable.zip' }; Compress-Archive -Path '%OUT%' -DestinationPath '%DIST%\SwiftList-Portable.zip' -Force"
if errorlevel 1 (
    echo [Error] Portable ZIP creation failed.
    exit /b 1
)

:: 10. Clean up temporary publish folder
echo.
echo Cleaning up temporary publish folder...
rmdir /s /q "%ROOT%publish" >nul 2>&1

echo.
echo ==========================================
echo Build and Package Completed Successfully!
echo Installer: %DIST%\SwiftList-Setup.exe
echo Portable ZIP: %DIST%\SwiftList-Portable.zip
echo ==========================================
exit /b 0
