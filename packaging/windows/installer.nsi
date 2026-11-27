; AchievementCaretaker
; Copyright (c) 2026 Yaroslav V Tatarenko
;
; See LICENSE in the repository root.
;
; Per-user installer. build-installer.sh passes VERSION, VERSION4, ARCH,
; PUBLISH_DIR, ICON, LICENSE_FILE, OUTFILE, HOMEPAGE and FILE_LIST with -D.
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
; Written into the application's folder. Only a folder that holds it is ever removed whole.
!define MARKER ".achievement-caretaker-install"
; What this version installed, one relative path a line (a folder ends in \). It is what is
; deleted, one by one, from a folder that is not marked.
!define FILELIST ".achievement-caretaker-files"
; Written to the registry by every installer that knows about the marker. Without it, the
; install was made by 2.2.0 or older, which marked nothing.
!define LAYOUT "2"

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
VIAddVersionKey "LegalCopyright" "Copyright (c) 2026 ${PUBLISHER}. Steam support based on Steam Achievement Manager, copyright (c) 2008-2024 Rick (gibbed); GOG support based on GOG Achievements Manager by Timendum; Ubisoft Connect support based on UbiSlot by BalTor02, Achievements by PSerban93, galaxy-integration-uplay by melcom and UplayKit by UplayDB."

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

; Deletes what ${FILELIST} names, and nothing that is not inside $INSTDIR: a line with ":" or
; "..", or one that starts with "\", is skipped. Folders are removed only when empty.
!macro DELETE_LISTED UN
Function ${UN}DeleteListed
    Push $9
    Push $8
    Push $7
    Push $6
    Push $5
    ClearErrors
    FileOpen $9 "$INSTDIR\${FILELIST}" r
    IfErrors done
    next:
        ClearErrors
        FileRead $9 $8
        IfErrors close
    strip:
        StrCpy $7 $8 1 -1
        StrCmp $7 "$\r" cut
        StrCmp $7 "$\n" cut
        Goto stripped
    cut:
        StrCpy $8 $8 -1
        Goto strip
    stripped:
        StrCmp $8 "" next
        StrCpy $7 $8 1
        StrCmp $7 "\" next
        StrLen $6 $8
        StrCpy $5 0
    scan:
        IntCmp $5 $6 scanned
        StrCpy $7 $8 2 $5
        StrCmp $7 ".." next
        StrCpy $7 $8 1 $5
        StrCmp $7 ":" next
        IntOp $5 $5 + 1
        Goto scan
    scanned:
        StrCpy $7 $8 1 -1
        StrCmp $7 "\" folder
        Delete "$INSTDIR\$8"
        Goto next
    folder:
        StrCpy $8 $8 -1
        RMDir "$INSTDIR\$8"
        Goto next
    close:
        FileClose $9
    done:
    Pop $5
    Pop $6
    Pop $7
    Pop $8
    Pop $9
FunctionEnd
!macroend
!insertmacro DELETE_LISTED ""
!insertmacro DELETE_LISTED "un."

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

    ; The application always lives in a folder of its own, whatever was chosen: the install
    ; and the uninstall remove that folder, so it must never be one the user already had.
    ; Done here rather than on the directory page, so a silent /D= gets it too.
    ${GetFileName} "$INSTDIR" $R0
    ${If} $R0 != "${APP_ID}"
        StrCpy $INSTDIR "$INSTDIR\${APP_ID}"
    ${EndIf}

    ; An earlier version could have been installed straight into a folder with other things in
    ; it, and its uninstaller removes that folder whole. Take that uninstaller away.
    ReadRegStr $R1 HKCU "Software\${APP_ID}" "InstallDir"
    ReadRegStr $R5 HKCU "Software\${APP_ID}" "Layout"
    ${If} $R1 != ""
    ${AndIf} $R1 != "$INSTDIR"
    ${AndIf} ${FileExists} "$R1\${APP_EXE}"
        Delete "$R1\Uninstall.exe"
    ${EndIf}

    ; Asked before anything is written: was there nothing here already?
    StrCpy $R3 "empty"
    ${If} ${FileExists} "$INSTDIR\*.*"
        ${DirState} "$INSTDIR" $R4
        ${If} $R4 == 1
            StrCpy $R3 "used"
        ${EndIf}
    ${EndIf}

    SetOutPath "$INSTDIR"

    ; Clear the previous install so an upgrade - or a switch between the x64 and
    ; x86 builds - cannot leave stale assemblies behind for the runtime to load.
    ; Only a folder we installed into: it holds the marker, or - for 2.2.0 and older, which
    ; wrote none and set no Layout - it is the registered install folder and holds their
    ; uninstaller. A version that writes the marker and did not is in a folder with files of
    ; the user's, so it is never wiped. Finding our executable is not enough either: a
    ; portable copy may have been unzipped among other files.
    StrCpy $R2 "no"
    ${If} ${FileExists} "$INSTDIR\${MARKER}"
        StrCpy $R2 "yes"
    ${ElseIf} $R5 == ""
    ${AndIf} $R1 == "$INSTDIR"
    ${AndIf} ${FileExists} "$INSTDIR\Uninstall.exe"
        StrCpy $R2 "yes"
    ${EndIf}
    ${If} $R2 == "yes"
        SetOutPath "$TEMP"
        RMDir /r "$INSTDIR"
        SetOutPath "$INSTDIR"
    ${Else}
        ; The previous install's own files, one by one, so none is left stale.
        Call DeleteListed
        SetOutPath "$INSTDIR"
    ${EndIf}

    File /r "${PUBLISH_DIR}\*.*"
    File "/oname=${FILELIST}" "${FILE_LIST}"

    ; A folder that already had files of its own is not marked, so it is never removed whole.
    ; After a wipe it holds only what was just installed.
    ${If} $R3 == "empty"
    ${OrIf} $R2 == "yes"
        FileOpen $0 "$INSTDIR\${MARKER}" w
        FileClose $0
    ${EndIf}

    WriteUninstaller "$INSTDIR\Uninstall.exe"

    CreateShortCut "$SMPROGRAMS\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}"

    WriteRegStr HKCU "Software\${APP_ID}" "InstallDir" "$INSTDIR"
    WriteRegStr HKCU "Software\${APP_ID}" "Version" "${VERSION}"
    WriteRegStr HKCU "Software\${APP_ID}" "Architecture" "${ARCH}"
    WriteRegStr HKCU "Software\${APP_ID}" "Layout" "${LAYOUT}"

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
    ; Whole only when the folder is marked as ours. Otherwise what was installed goes, file
    ; by file, and whatever else is there stays.
    ${If} ${FileExists} "$INSTDIR\${MARKER}"
        RMDir /r "$INSTDIR"
    ${Else}
        Call un.DeleteListed
        Delete "$INSTDIR\${FILELIST}"
        Delete "$INSTDIR\${APP_EXE}"
    ${EndIf}

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
    ; The path goes through an environment variable, so cmd never reads it as part of the
    ; command: a folder name with % in it would otherwise be expanded into another path.
    System::Call 'Kernel32::SetEnvironmentVariable(t "SACDIR", t "$INSTDIR")'
    Exec 'cmd /d /c ping -n 3 127.0.0.1 >nul & del /f /q "%SACDIR%\Uninstall.exe" & rmdir "%SACDIR%"'
SectionEnd
