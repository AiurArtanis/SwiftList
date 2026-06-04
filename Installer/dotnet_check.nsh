; =====================================================================
; SwiftList .NET 10.0 Desktop Runtime Detection and Installation Script
; =====================================================================

; 0. Check and install .NET 10.0 Desktop Runtime
DetailPrint "$(CheckNet10Text)"
StrCpy $R0 "0"

ClearErrors
FindFirst $R1 $R2 "$PROGRAMFILES64\dotnet\shared\Microsoft.WindowsDesktop.App\10.*"
IfErrors check_done
StrCpy $R0 "1"
FindClose $R1

check_done:
StrCmp $R0 "1" dotnet_installed
    DetailPrint "$(Net10NotFoundText)"
    InitPluginsDir
    
    ; Download .NET 10 Desktop Runtime using BITS via PowerShell
    nsExec::ExecToLog 'powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Start-BitsTransfer -Source $\"https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe$\" -Destination $\"$PLUGINSDIR\windowsdesktop-runtime-10-win-x64.exe$\""'
    Pop $R3
    
    ; Fallback to curl if file does not exist or BITS failed
    IfFileExists "$PLUGINSDIR\windowsdesktop-runtime-10-win-x64.exe" download_success
        DetailPrint "$(Net10DownloadAltText)"
        nsExec::ExecToLog 'curl.exe -L -o "$PLUGINSDIR\windowsdesktop-runtime-10-win-x64.exe" "https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe"'
        Pop $R3
        
        IfFileExists "$PLUGINSDIR\windowsdesktop-runtime-10-win-x64.exe" download_success
            MessageBox MB_OK|MB_ICONSTOP "$(Net10DownloadFailedText)"
            Abort "Missing dependency: .NET 10.0 Desktop Runtime"
            
download_success:
    DetailPrint "$(Net10InstallingText)"
    nsExec::ExecToLog '"$PLUGINSDIR\windowsdesktop-runtime-10-win-x64.exe" /install /quiet /norestart'
    Pop $R3
    
    ; Verify installation
    ClearErrors
    FindFirst $R1 $R2 "$PROGRAMFILES64\dotnet\shared\Microsoft.WindowsDesktop.App\10.*"
    IfErrors install_failed
        FindClose $R1
        DetailPrint "$(Net10InstalledText)"
        Goto dotnet_installed
        
install_failed:
    MessageBox MB_OK|MB_ICONSTOP "$(Net10InstallFailedText) (Code: $R3)"
    Abort "Missing dependency: .NET 10.0 Desktop Runtime"
    
dotnet_installed:
    DetailPrint "$(Net10ReadyText)"
