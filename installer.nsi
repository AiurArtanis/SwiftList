; =====================================================================
; SwiftList Nullsoft Scriptable Install System (NSIS) Script
; =====================================================================

Unicode true
SetCompressor lzma

!define APP_NAME "SwiftList"
!define APP_VERSION "1.0.0"
!define APP_PUBLISHER "Google DeepMind"
!define APP_WEBSITE "https://github.com/swiftlist/SwiftList"
!define APP_EXE_NAME "SwiftList.App.exe"
!define SERVICE_EXE_NAME "SwiftList.Service.exe"
!define SERVICE_NAME "SwiftListService"

; ==========================================
; Version Info Resources
; ==========================================
VIProductVersion "1.0.0.0"
VIAddVersionKey "ProductName" "${APP_NAME}"
VIAddVersionKey "CompanyName" "${APP_PUBLISHER}"
VIAddVersionKey "LegalCopyright" "Copyright (C) 2026"
VIAddVersionKey "FileDescription" "${APP_NAME} Installer"
VIAddVersionKey "FileVersion" "${APP_VERSION}"
VIAddVersionKey "ProductVersion" "${APP_VERSION}"

Name "${APP_NAME}"
OutFile "SwiftList-Setup.exe"
InstallDir "$PROGRAMFILES64\${APP_NAME}"
InstallDirRegKey HKLM "Software\${APP_NAME}" "InstallDir"
RequestExecutionLevel admin

; ==========================================
; Modern UI Configurations
; ==========================================
!include "MUI2.nsh"

!define MUI_ABORTWARNING
!define MUI_ICON "App\logo.ico"
!define MUI_UNICON "App\logo.ico"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "LICENSE"
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES

; Finish Page: Option to launch SwiftList App immediately as standard user
!define MUI_FINISHPAGE_RUN
!define MUI_FINISHPAGE_RUN_FUNCTION "LaunchAppAsUser"
!define MUI_FINISHPAGE_RUN_TEXT "$(LaunchAppText)"
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

; ==========================================
; Languages (NSIS automatically detects OS language)
; ==========================================
!insertmacro MUI_LANGUAGE "English"
!insertmacro MUI_LANGUAGE "SimpChinese"

; ==========================================
; Multilingual Strings
; ==========================================
LangString LaunchAppText ${LANG_ENGLISH} "Launch ${APP_NAME}"
LangString LaunchAppText ${LANG_SIMPCHINESE} "启动 ${APP_NAME}"

LangString SecInstallText ${LANG_ENGLISH} "Core Program (Required)"
LangString SecInstallText ${LANG_SIMPCHINESE} "核心主程序 (必选)"

LangString SecDesktopText ${LANG_ENGLISH} "Create Desktop Shortcut"
LangString SecDesktopText ${LANG_SIMPCHINESE} "创建桌面快捷方式"

LangString SecStartMenuText ${LANG_ENGLISH} "Create Start Menu Shortcuts"
LangString SecStartMenuText ${LANG_SIMPCHINESE} "创建开始菜单快捷方式"

LangString UninstallShortcutText ${LANG_ENGLISH} "Uninstall ${APP_NAME}"
LangString UninstallShortcutText ${LANG_SIMPCHINESE} "卸载 ${APP_NAME}"

LangString DescInstall ${LANG_ENGLISH} "Installs SwiftList core binaries and background system service."
LangString DescInstall ${LANG_SIMPCHINESE} "安装 ${APP_NAME} 核心运行文件与后台系统服务（必选）。"

LangString DescDesktop ${LANG_ENGLISH} "Creates a shortcut to SwiftList on your desktop."
LangString DescDesktop ${LANG_SIMPCHINESE} "在桌面上创建 ${APP_NAME} 的快捷方式。"

LangString DescStartMenu ${LANG_ENGLISH} "Creates shortcuts to SwiftList and the uninstaller in the Start Menu."
LangString DescStartMenu ${LANG_SIMPCHINESE} "在开始菜单中创建 ${APP_NAME} 的快捷方式与卸载程序项。"

; ==========================================
; Installation Steps
; ==========================================
Section "" SecInstall
    SectionIn RO
    SetOutPath "$INSTDIR"

    ; 1. Close running instances
    DetailPrint "Closing running instances of ${APP_NAME}..."
    nsExec::Exec 'taskkill /F /IM ${APP_EXE_NAME}'
    DetailPrint "Stopping background service..."
    nsExec::Exec 'sc.exe stop ${SERVICE_NAME}'
    Sleep 1000
    nsExec::Exec 'taskkill /F /IM ${SERVICE_EXE_NAME}'
    Sleep 500

    ; 2. Copy publish files recursively (excluding pdb files to keep installer lightweight)
    File /r /x *.pdb "publish\SwiftList\*.*"

    ; 3. Create Windows Service using native sc.exe
    DetailPrint "Registering and configuring background service..."
    ; Delete old service if exists to ensure clean reinstall
    nsExec::Exec 'sc.exe delete ${SERVICE_NAME}'
    ; Create service with auto start
    nsExec::Exec 'sc.exe create ${SERVICE_NAME} binPath= "\"$INSTDIR\${SERVICE_EXE_NAME}\"" start= auto DisplayName= "SwiftList Background Service"'
    nsExec::Exec 'sc.exe description ${SERVICE_NAME} "SwiftList NTFS USN Journal file indexing and real-time monitoring service."'

    ; 4. Registry Configuration & Uninstaller Info
    WriteRegStr HKLM "Software\${APP_NAME}" "InstallDir" "$INSTDIR"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "DisplayName" "${APP_NAME}"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "UninstallString" '"$INSTDIR\uninstall.exe"'
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "DisplayIcon" '"$INSTDIR\${APP_EXE_NAME}"'
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "Publisher" "${APP_PUBLISHER}"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "HelpLink" "${APP_WEBSITE}"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "DisplayVersion" "${APP_VERSION}"
    WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "NoModify" 1
    WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "NoRepair" 1

    WriteUninstaller "$INSTDIR\uninstall.exe"
SectionEnd

Section "" SecDesktopShortcut
    SetOutPath "$INSTDIR"
    SetShellVarContext all
    CreateShortcut "$DESKTOP\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE_NAME}"
SectionEnd

Section "" SecStartMenuShortcut
    SetOutPath "$INSTDIR"
    SetShellVarContext all
    CreateDirectory "$SMPROGRAMS\${APP_NAME}"
    CreateShortcut "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE_NAME}"
    CreateShortcut "$SMPROGRAMS\${APP_NAME}\$(UninstallShortcutText).lnk" "$INSTDIR\uninstall.exe"
SectionEnd

; ==========================================
; Component Descriptions
; ==========================================
!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
    !insertmacro MUI_DESCRIPTION_TEXT ${SecInstall} "$(DescInstall)"
    !insertmacro MUI_DESCRIPTION_TEXT ${SecDesktopShortcut} "$(DescDesktop)"
    !insertmacro MUI_DESCRIPTION_TEXT ${SecStartMenuShortcut} "$(DescStartMenu)"
!insertmacro MUI_FUNCTION_DESCRIPTION_END

; ==========================================
; Functions
; ==========================================
Function LaunchAppAsUser
    Exec '"explorer.exe" "$INSTDIR\${APP_EXE_NAME}"'
FunctionEnd

Function .onInit
    ; Dynamically set section texts on install initialization
    SectionSetText ${SecInstall} "$(SecInstallText)"
    SectionSetText ${SecDesktopShortcut} "$(SecDesktopText)"
    SectionSetText ${SecStartMenuShortcut} "$(SecStartMenuText)"
FunctionEnd

; ==========================================
; Uninstallation Steps
; ==========================================
Section "Uninstall"
    ; 1. Close application
    nsExec::Exec 'taskkill /F /IM ${APP_EXE_NAME}'

    ; 2. Stop and delete service
    DetailPrint "Stopping and removing background service..."
    nsExec::Exec 'sc.exe stop ${SERVICE_NAME}'
    Sleep 1000
    nsExec::Exec 'taskkill /F /IM ${SERVICE_EXE_NAME}'
    nsExec::Exec 'sc.exe delete ${SERVICE_NAME}'
    Sleep 500

    ; 3. Clean files & directories automatically
    Delete "$INSTDIR\*.*"
    RMDir /r "$INSTDIR\runtimes"
    RMDir /r "$INSTDIR\Plugins"
    RMDir "$INSTDIR"

    ; 4. Clean Shortcuts automatically (from all users public directories)
    SetShellVarContext all
    Delete "$SMPROGRAMS\${APP_NAME}\*.*"
    RMDir "$SMPROGRAMS\${APP_NAME}"
    Delete "$DESKTOP\${APP_NAME}.lnk"

    ; 5. Clean Registry
    DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}"
    DeleteRegKey HKLM "Software\${APP_NAME}"

SectionEnd
