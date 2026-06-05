; =====================================================================
; SwiftList Nullsoft Scriptable Install System (NSIS) Script
; =====================================================================

Unicode true
SetCompressor lzma

!define APP_NAME "SwiftList"
!ifndef APP_VERSION
  !define APP_VERSION "1.2.6"
!endif
!ifndef APP_VERSION_4
  !define APP_VERSION_4 "1.2.6.0"
!endif
!define APP_PUBLISHER "SwiftList developer"
!define APP_WEBSITE "https://github.com/swiftlist/SwiftList"
!define APP_EXE_NAME "SwiftList.App.exe"
!define SERVICE_EXE_NAME "SwiftList.Service.exe"
!define SERVICE_NAME "SwiftListService"

; ==========================================
; Version Info Resources
; ==========================================
VIProductVersion "${APP_VERSION_4}"
VIAddVersionKey "ProductName" "${APP_NAME}"
VIAddVersionKey "CompanyName" "${APP_PUBLISHER}"
VIAddVersionKey "LegalCopyright" "Copyright (C) 2026"
VIAddVersionKey "FileDescription" "${APP_NAME} Installer"
VIAddVersionKey "FileVersion" "${APP_VERSION}"
VIAddVersionKey "ProductVersion" "${APP_VERSION}"

Name "${APP_NAME}"
OutFile "..\dist\SwiftList-Setup.exe"
InstallDir "$PROGRAMFILES64\${APP_NAME}"
InstallDirRegKey HKLM "Software\${APP_NAME}" "InstallDir"
RequestExecutionLevel admin

; ==========================================
; Modern UI Configurations
; ==========================================
!include "MUI2.nsh"

!define MUI_ABORTWARNING
!define MUI_ICON "..\App\logo.ico"
!define MUI_UNICON "..\App\logo.ico"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "..\LICENSE"
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
; Multilingual Strings (Includes language files)
; ==========================================
!include "Languages\en-US.nsh"
!include "Languages\zh-CN.nsh"

; ==========================================
; Installation Steps
; ==========================================
Section "" SecInstall
    SectionIn RO

    ; 0. Check and install .NET 10.0 Desktop Runtime
    !include "dotnet_check.nsh"

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
    File /r /x *.pdb "..\publish\SwiftList\*.*"

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
