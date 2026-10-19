; AchievementCaretaker
; Copyright (c) 2026 Yaroslav V Tatarenko
;
; See LICENSE in the repository root.
;
; Per-user installer. build-installer.sh passes VERSION, VERSION4, ARCH,
; PUBLISH_DIR, ICON, LICENSE_FILE, OUTFILE and HOMEPAGE with -D.
;
; Per-user on purpose: the payload is self-contained, nothing is registered
; system-wide, and installing under $LOCALAPPDATA needs no administrator. It
; also sidesteps the Program Files / Program Files (x86) split, so the x64 and
; x86 builds share one location and one uninstall entry - installing either
; replaces the other rather than leaving two half-installs behind.

Unicode true

!include "MUI2.nsh"
!include "FileFunc.nsh"
!include "LogicLib.nsh"

!define APP_NAME "Achievement Caretaker"
!define APP_ID "AchievementCaretaker"
!define APP_EXE "AchievementCaretaker.exe"
!define PUBLISHER "Yaroslav V Tatarenko"
!define UNINST_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_ID}"

Name "${APP_NAME} ${VERSION}"
OutFile "${OUTFILE}"
RequestExecutionLevel user
InstallDir "$LOCALAPPDATA\Programs\${APP_ID}"
InstallDirRegKey HKCU "Software\${APP_ID}" "InstallDir"
SetCompressor /SOLID lzma
ShowInstDetails show
ShowUnInstDetails show

VIProductVersion "${VERSION4}"
VIAddVersionKey "ProductName" "${APP_NAME}"
VIAddVersionKey "FileDescription" "${APP_NAME} ${ARCH} installer"
VIAddVersionKey "FileVersion" "${VERSION4}"
VIAddVersionKey "ProductVersion" "${VERSION}"
VIAddVersionKey "CompanyName" "${PUBLISHER}"
VIAddVersionKey "LegalCopyright" "Copyright (c) 2026 ${PUBLISHER}. Steam support based on Steam Achievement Manager, copyright (c) 2008-2024 Rick (gibbed); GOG support based on GOG Achievements Manager by Timendum."

!define MUI_ICON "${ICON}"
!define MUI_UNICON "${ICON}"
!define MUI_ABORTWARNING

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "${LICENSE_FILE}"
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"

; Start the application after an interactive install. Done here rather than with
; MUI_FINISHPAGE_RUN, which also started it during a silent install (winget, /S).
Function .onInstSuccess
    IfSilent done
    Exec '"$INSTDIR\${APP_EXE}"'
done:
FunctionEnd

Section "Install"
    ; A running copy keeps its files open, so an install over it would leave a mix
    ; of old and new. Refuse rather than kill it: it may hold unsaved changes.
    nsExec::Exec 'cmd /c tasklist /FI "IMAGENAME eq ${APP_EXE}" /NH | findstr /I "${APP_EXE}" >nul'
    Pop $0
    ${If} $0 == 0
        IfSilent +2 0
        MessageBox MB_OK|MB_ICONEXCLAMATION "Achievement Caretaker is running. Close it, then install again."
        SetErrorLevel 1
        Quit
    ${EndIf}

    SetOutPath "$INSTDIR"

    ; Clear the previous install so an upgrade - or a switch between the x64 and
    ; x86 builds - cannot leave stale assemblies behind for the runtime to load.
    ; Guarded on our own executable being there, so a user who pointed the
    ; directory page at something else does not lose it.
    ${If} ${FileExists} "$INSTDIR\${APP_EXE}"
        RMDir /r "$INSTDIR"
        CreateDirectory "$INSTDIR"
        SetOutPath "$INSTDIR"
    ${EndIf}

    File /r "${PUBLISH_DIR}\*.*"

    WriteUninstaller "$INSTDIR\Uninstall.exe"

    CreateShortCut "$SMPROGRAMS\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}"

    WriteRegStr HKCU "Software\${APP_ID}" "InstallDir" "$INSTDIR"
    WriteRegStr HKCU "Software\${APP_ID}" "Version" "${VERSION}"
    WriteRegStr HKCU "Software\${APP_ID}" "Architecture" "${ARCH}"

    WriteRegStr HKCU "${UNINST_KEY}" "DisplayName" "${APP_NAME}"
    WriteRegStr HKCU "${UNINST_KEY}" "DisplayVersion" "${VERSION}"
    WriteRegStr HKCU "${UNINST_KEY}" "DisplayIcon" "$INSTDIR\${APP_EXE}"
    WriteRegStr HKCU "${UNINST_KEY}" "Publisher" "${PUBLISHER}"
    WriteRegStr HKCU "${UNINST_KEY}" "URLInfoAbout" "${HOMEPAGE}"
    WriteRegStr HKCU "${UNINST_KEY}" "InstallLocation" "$INSTDIR"
    WriteRegStr HKCU "${UNINST_KEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
    ; _?= runs the uninstaller in place instead of copying it to %TEMP%. Without it the
    ; copy does the work, and the process that was waited on returns 0 at once.
    WriteRegStr HKCU "${UNINST_KEY}" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S _?=$INSTDIR'
    WriteRegDWORD HKCU "${UNINST_KEY}" "NoModify" 1
    WriteRegDWORD HKCU "${UNINST_KEY}" "NoRepair" 1

    ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
    IntFmt $0 "0x%08X" $0
    WriteRegDWORD HKCU "${UNINST_KEY}" "EstimatedSize" "$0"
SectionEnd

Section "Uninstall"
    ; Refuse while the application runs, for the same reason as the install: its
    ; files would stay behind, and it may hold unsaved changes.
    nsExec::Exec 'cmd /c tasklist /FI "IMAGENAME eq ${APP_EXE}" /NH | findstr /I "${APP_EXE}" >nul'
    Pop $0
    ${If} $0 == 0
        IfSilent +2 0
        MessageBox MB_OK|MB_ICONEXCLAMATION "Achievement Caretaker is running. Close it, then uninstall again."
        SetErrorLevel 1
        Quit
    ${EndIf}

    Delete "$SMPROGRAMS\${APP_NAME}.lnk"

    ; Only what was installed. The settings, the game cache and the user's own
    ; like/dislike ratings live in ~/.sac and are deliberately left alone: an
    ; uninstall is not a request to throw away data a reinstall would want.
    RMDir /r "$INSTDIR"

    ; Report a leftover executable as a failure, so the exit code is not 0 for a
    ; removal that did not happen.
    ${If} ${FileExists} "$INSTDIR\${APP_EXE}"
        SetErrorLevel 1
    ${EndIf}

    DeleteRegKey HKCU "${UNINST_KEY}"
    DeleteRegKey HKCU "Software\${APP_ID}"

    ; The uninstaller runs in place (_?=), so it cannot delete itself. A detached command
    ; removes it and the folder once this process has exited. rmdir takes only an empty
    ; folder, so nothing outside the program is touched.
    Exec 'cmd /c ping -n 3 127.0.0.1 >nul & del /f /q "$INSTDIR\Uninstall.exe" & rmdir "$INSTDIR"'
SectionEnd
