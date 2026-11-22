Unicode true
Name "YouTube Downloader"
OutFile "${OUTPUT_FILE}"
InstallDir "$LOCALAPPDATA\Programs\YoutubeDownloader"
RequestExecutionLevel user
SetCompressor /SOLID lzma
SetCompressorDictSize 64

!include "MUI2.nsh"
!include "WinVer.nsh"
!include "x64.nsh"

!define UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\YoutubeDownloader"
InstallDirRegKey HKCU "${UNINSTALL_KEY}" "InstallLocation"
!define MUI_ICON "${ICON_FILE}"
!define MUI_UNICON "${ICON_FILE}"
!define MUI_ABORTWARNING
!define MUI_FINISHPAGE_RUN "$INSTDIR\YoutubeDownloader.exe"
!define MUI_FINISHPAGE_RUN_TEXT "Запустить YouTube Downloader"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "Russian"

VIProductVersion "${APP_VERSION}.0"
VIAddVersionKey /LANG=1049 "ProductName" "YouTube Downloader"
VIAddVersionKey /LANG=1049 "ProductVersion" "${APP_VERSION}"
VIAddVersionKey /LANG=1049 "FileVersion" "${APP_VERSION}"
VIAddVersionKey /LANG=1049 "FileDescription" "Установщик YouTube Downloader для Windows x64"
VIAddVersionKey /LANG=1049 "LegalCopyright" "Сведения о лицензиях включены в установщик"

Function .onInit
    ${IfNot} ${AtLeastWin10}
    ${OrIfNot} ${RunningX64}
        MessageBox MB_OK|MB_ICONSTOP "Для установки нужна Windows 10 или 11 с поддержкой x64." /SD IDOK
        SetErrorLevel 2
        Abort
    ${EndIf}
    SetRegView 64
    SetShellVarContext current
FunctionEnd

Function un.onInit
    SetRegView 64
    SetShellVarContext current
FunctionEnd

; Check known executables before changing any files.
!macro CheckPrograms
    !insertmacro CheckWritable "YoutubeDownloader.exe"
    !insertmacro CheckWritable "tools\yt-dlp.exe"
    !insertmacro CheckWritable "tools\deno.exe"
    !insertmacro CheckWritable "tools\ffmpeg.exe"
    !insertmacro CheckWritable "tools\ffprobe.exe"
!macroend

!macro CheckWritable relativePath
    ${If} ${FileExists} "$INSTDIR\${relativePath}"
        System::Call 'kernel32::CreateFileW(w "$INSTDIR\${relativePath}", i 0x40000000, i 7, p 0, i 3, i 0x80, p 0)p.r0'
        ${If} $0 == -1
            MessageBox MB_OK|MB_ICONSTOP "Закройте YouTube Downloader и проверьте доступ к папке установки. Затем повторите попытку." /SD IDOK
            SetErrorLevel 3
            Abort
        ${EndIf}
        System::Call 'kernel32::CloseHandle(p r0)'
    ${EndIf}
!macroend

Section "YouTube Downloader"
    !insertmacro CheckPrograms
    SetOverwrite on
    SetOutPath "$INSTDIR"
    File "${PAYLOAD_DIR}\YoutubeDownloader.exe"

    SetOutPath "$INSTDIR\tools"
    ; Keep a copy of yt-dlp already updated from the app.
    SetOverwrite off
    File "${TOOLS_DIR}\yt-dlp.exe"
    SetOverwrite on
    File "${TOOLS_DIR}\deno.exe"
    File "${TOOLS_DIR}\ffmpeg.exe"
    File "${TOOLS_DIR}\ffprobe.exe"

    SetOutPath "$INSTDIR\licenses"
    File "${LICENSE_DIR}\*"
    SetOutPath "$INSTDIR"
    WriteUninstaller "$INSTDIR\Uninstall.exe"
    CreateShortcut "$SMPROGRAMS\YouTube Downloader.lnk" "$INSTDIR\YoutubeDownloader.exe"
    WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayName" "YouTube Downloader"
    WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayVersion" "${APP_VERSION}"
    WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayIcon" "$INSTDIR\YoutubeDownloader.exe"
    WriteRegStr HKCU "${UNINSTALL_KEY}" "InstallLocation" "$INSTDIR"
    WriteRegStr HKCU "${UNINSTALL_KEY}" "UninstallString" '$\"$INSTDIR\Uninstall.exe$\"'
    WriteRegStr HKCU "${UNINSTALL_KEY}" "QuietUninstallString" '$\"$INSTDIR\Uninstall.exe$\" /S'
    WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoModify" 1
    WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoRepair" 1
SectionEnd

Section "Uninstall"
    !insertmacro CheckPrograms
    Delete "$SMPROGRAMS\YouTube Downloader.lnk"
    Delete "$INSTDIR\YoutubeDownloader.exe"
    Delete "$INSTDIR\tools\yt-dlp.exe"
    Delete "$INSTDIR\tools\deno.exe"
    Delete "$INSTDIR\tools\ffmpeg.exe"
    Delete "$INSTDIR\tools\ffprobe.exe"
    RMDir "$INSTDIR\tools"
    Delete "$INSTDIR\licenses\dotnet-runtime-license.txt"
    Delete "$INSTDIR\licenses\dotnet-desktop-license.txt"
    Delete "$INSTDIR\licenses\dotnet-third-party-notices.txt"
    Delete "$INSTDIR\licenses\nsis-license.txt"
    Delete "$INSTDIR\licenses\dependency-notices.txt"
    RMDir "$INSTDIR\licenses"
    DeleteRegKey HKCU "${UNINSTALL_KEY}"
    Delete "$INSTDIR\Uninstall.exe"
    RMDir "$INSTDIR"
SectionEnd
