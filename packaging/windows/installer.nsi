; THE WINDOWS INSTALLER — built by tools/release.sh with makensis, never by hand. It takes the exported game
; (build/windows) and wraps it in one setup .exe a tester can double-click.
;
; ⚠ PER USER, NO ADMIN PROMPT. It installs under %LOCALAPPDATA%\Programs (where Windows itself puts per-user
; apps) and writes its uninstall entry under HKCU. A closed-alpha tester should not need to be an administrator,
; and a game that asks for admin rights to be installed is a game some testers will rightly not install.
;
; ⚠ THE UNINSTALLER REMOVES WHAT THIS INSTALLER PUT THERE, BY NAME — never `RMDir /r $INSTDIR`. The player may
; point the installer at a folder that already holds other things, and an uninstaller that wipes the folder it
; was installed into takes those with it. The player's saves and run history are not in the install folder at
; all (Godot keeps them under %APPDATA%\Godot\app_userdata) and are left alone on purpose.
;
; Defines passed by tools/release.sh: VERSION, SRCDIR (build/windows), OUTFILE, ICON.

Unicode true
SetCompressor /SOLID lzma

!include "MUI2.nsh"

!define APPNAME "Bureaucrats & Broomsticks"
!define EXE "bureaucrats-and-broomsticks.exe"
!define PCK "bureaucrats-and-broomsticks.pck"
!define DATADIR "data_BnbGodot_windows_x86_64"
!define UNINSTKEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\BureaucratsAndBroomsticks"

Name "${APPNAME}"
OutFile "${OUTFILE}"
InstallDir "$LOCALAPPDATA\Programs\Bureaucrats and Broomsticks"
InstallDirRegKey HKCU "${UNINSTKEY}" "InstallLocation"
RequestExecutionLevel user
BrandingText "Moonvine Forge · closed alpha ${VERSION}"

VIProductVersion "${VERSION}.0"
VIAddVersionKey "ProductName" "${APPNAME}"
VIAddVersionKey "ProductVersion" "${VERSION}"
VIAddVersionKey "FileVersion" "${VERSION}"
VIAddVersionKey "CompanyName" "Moonvine Forge"
VIAddVersionKey "FileDescription" "${APPNAME} setup"
VIAddVersionKey "LegalCopyright" "© 2026 Moonvine Forge"

!define MUI_ICON "${ICON}"
!define MUI_UNICON "${ICON}"
!define MUI_ABORTWARNING
!define MUI_WELCOMEPAGE_TITLE "${APPNAME} — closed alpha ${VERSION}"
!define MUI_WELCOMEPAGE_TEXT "This installs ${APPNAME} for you alone — no administrator rights needed.$\r$\n$\r$\nYour runs are recorded and sent home to help balance the game. Thank you for testing!"
!define MUI_FINISHPAGE_RUN "$INSTDIR\${EXE}"
!define MUI_FINISHPAGE_RUN_TEXT "Play now"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"

Section "!${APPNAME}" SecGame
  SectionIn RO
  SetOutPath "$INSTDIR"
  ; An update installs over the last one: the .NET folder goes first, so no assembly of the old build is left
  ; lying beside the new ones.
  RMDir /r "$INSTDIR\${DATADIR}"
  File "${SRCDIR}/${EXE}"
  File "${SRCDIR}/${PCK}"
  File /nonfatal "${SRCDIR}/bugreport.cfg"
  File /nonfatal "${SRCDIR}/runlog.cfg"
  File /r "${SRCDIR}/${DATADIR}"
  File "${ICON}"

  WriteUninstaller "$INSTDIR\uninstall.exe"
  CreateDirectory "$SMPROGRAMS\${APPNAME}"
  CreateShortCut "$SMPROGRAMS\${APPNAME}\${APPNAME}.lnk" "$INSTDIR\${EXE}" "" "$INSTDIR\${EXE}" 0
  CreateShortCut "$SMPROGRAMS\${APPNAME}\Uninstall ${APPNAME}.lnk" "$INSTDIR\uninstall.exe"

  WriteRegStr HKCU "${UNINSTKEY}" "DisplayName" "${APPNAME}"
  WriteRegStr HKCU "${UNINSTKEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "${UNINSTKEY}" "Publisher" "Moonvine Forge"
  WriteRegStr HKCU "${UNINSTKEY}" "DisplayIcon" "$INSTDIR\${EXE}"
  WriteRegStr HKCU "${UNINSTKEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTKEY}" "UninstallString" '"$INSTDIR\uninstall.exe"'
  WriteRegDWORD HKCU "${UNINSTKEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINSTKEY}" "NoRepair" 1
SectionEnd

Section "Desktop shortcut" SecDesktop
  CreateShortCut "$DESKTOP\${APPNAME}.lnk" "$INSTDIR\${EXE}" "" "$INSTDIR\${EXE}" 0
SectionEnd

!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
  !insertmacro MUI_DESCRIPTION_TEXT ${SecGame} "The game itself."
  !insertmacro MUI_DESCRIPTION_TEXT ${SecDesktop} "A shortcut on your desktop."
!insertmacro MUI_FUNCTION_DESCRIPTION_END

Section "Uninstall"
  Delete "$INSTDIR\${EXE}"
  Delete "$INSTDIR\${PCK}"
  Delete "$INSTDIR\bugreport.cfg"
  Delete "$INSTDIR\runlog.cfg"
  Delete "$INSTDIR\icon.ico"
  RMDir /r "$INSTDIR\${DATADIR}"
  Delete "$INSTDIR\uninstall.exe"
  RMDir "$INSTDIR" ; only if nothing else is left in it

  Delete "$DESKTOP\${APPNAME}.lnk"
  Delete "$SMPROGRAMS\${APPNAME}\${APPNAME}.lnk"
  Delete "$SMPROGRAMS\${APPNAME}\Uninstall ${APPNAME}.lnk"
  RMDir "$SMPROGRAMS\${APPNAME}"
  DeleteRegKey HKCU "${UNINSTKEY}"
SectionEnd
