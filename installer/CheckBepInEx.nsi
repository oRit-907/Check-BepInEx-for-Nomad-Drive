; Check BepInEx installer (3.2.0 on). Built with NSIS (makensis), on Linux or Windows:
;   makensis -DVERSION=3.2.0 -DAPP=../src/publish/NomadModDownloader.exe -DOUT=CheckBepInEx-Setup-3.2.0.exe CheckBepInEx.nsi
; Installs for the current Windows user only (no admin prompt) into %LOCALAPPDATA%\Programs\Check BepInEx, with Start
; menu and desktop shortcuts and an uninstaller (Windows Settings > Apps). The app's settings and backups in
; %AppData%\NomadModDownloader are left alone on uninstall.
; Quiet install (the app's own updates): CheckBepInEx-Setup-X.Y.Z.exe /S /UPDATE /D=<folder>

!ifndef VERSION
  !error "Pass -DVERSION=x.y.z"
!endif
!ifndef APP
  !define APP "..\src\publish\NomadModDownloader.exe"
!endif
!ifndef OUT
  !define OUT "CheckBepInEx-Setup-${VERSION}.exe"
!endif

Unicode true
SetCompressor /SOLID lzma
RequestExecutionLevel user
ManifestDPIAware true

!define NAME "Check BepInEx"
!define EXE "CheckBepInEx.exe"
!define UNINST "Uninstall Check BepInEx.exe"
!define REGKEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\CheckBepInEx"
!define PUBLISHER "Nomad Drive Mods"
!define SITE "https://www.nomaddrivemods.online/bepinex"

Name "${NAME}"
OutFile "${OUT}"
InstallDir "$LOCALAPPDATA\Programs\${NAME}"
InstallDirRegKey HKCU "${REGKEY}" "InstallLocation"
BrandingText "${NAME} ${VERSION} · nomaddrivemods.online"
VIProductVersion "${VERSION}.0"
VIAddVersionKey "ProductName" "${NAME}"
VIAddVersionKey "ProductVersion" "${VERSION}"
VIAddVersionKey "FileVersion" "${VERSION}"
VIAddVersionKey "FileDescription" "${NAME} installer"
VIAddVersionKey "CompanyName" "${PUBLISHER}"
VIAddVersionKey "LegalCopyright" "${PUBLISHER}"

!include "MUI2.nsh"
!include "FileFunc.nsh"
!include "LogicLib.nsh"
!define MUI_ICON "..\src\app.ico"
!define MUI_UNICON "..\src\app.ico"
!define MUI_ABORTWARNING
!define MUI_WELCOMEPAGE_TEXT "This installs ${NAME} ${VERSION} for you (no administrator needed).$\r$\n$\r$\nIt sets BepInEx up for Nomad Drive, keeps it working and shows why a mod isn't loading. It updates itself from nomaddrivemods.online.$\r$\n$\r$\nIf ${NAME} is open, it will be closed."
!define MUI_FINISHPAGE_RUN "$INSTDIR\${EXE}"
!define MUI_FINISHPAGE_RUN_TEXT "Open ${NAME}"
!define MUI_FINISHPAGE_LINK "nomaddrivemods.online"
!define MUI_FINISHPAGE_LINK_LOCATION "${SITE}"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"

Var Update

Function .onInit
  ${GetParameters} $0
  ClearErrors
  ${GetOptions} $0 "/UPDATE" $1
  ${If} ${Errors}
    StrCpy $Update "0"
  ${Else}
    StrCpy $Update "1"
  ${EndIf}
FunctionEnd

Section "Install"
  ; Close a running copy from this folder (an update already closed itself first).
  nsExec::Exec 'taskkill /F /IM "${EXE}"'
  Sleep 500
  SetOutPath "$INSTDIR"
  File "/oname=${EXE}" "${APP}"
  File "/oname=app.ico" "..\src\app.ico"
  WriteUninstaller "$INSTDIR\${UNINST}"

  ; Shortcuts (kept as they are on an update, so ones the person moved or removed stay that way).
  ${If} $Update != "1"
    CreateShortCut "$SMPROGRAMS\${NAME}.lnk" "$INSTDIR\${EXE}" "" "$INSTDIR\app.ico"
    CreateShortCut "$DESKTOP\${NAME}.lnk" "$INSTDIR\${EXE}" "" "$INSTDIR\app.ico"
  ${EndIf}

  ; Windows Settings > Apps
  WriteRegStr HKCU "${REGKEY}" "DisplayName" "${NAME}"
  WriteRegStr HKCU "${REGKEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "${REGKEY}" "Publisher" "${PUBLISHER}"
  WriteRegStr HKCU "${REGKEY}" "DisplayIcon" "$INSTDIR\app.ico"
  WriteRegStr HKCU "${REGKEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${REGKEY}" "URLInfoAbout" "${SITE}"
  WriteRegStr HKCU "${REGKEY}" "UninstallString" '"$INSTDIR\${UNINST}"'
  WriteRegStr HKCU "${REGKEY}" "QuietUninstallString" '"$INSTDIR\${UNINST}" /S'
  WriteRegDWORD HKCU "${REGKEY}" "NoModify" 1
  WriteRegDWORD HKCU "${REGKEY}" "NoRepair" 1
  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  WriteRegDWORD HKCU "${REGKEY}" "EstimatedSize" $0
SectionEnd

Section "Uninstall"
  nsExec::Exec 'taskkill /F /IM "${EXE}"'
  Sleep 500
  Delete "$INSTDIR\${EXE}"
  Delete "$INSTDIR\${EXE}.old.exe"
  Delete "$INSTDIR\app.ico"
  Delete "$INSTDIR\${UNINST}"
  RMDir "$INSTDIR"
  Delete "$SMPROGRAMS\${NAME}.lnk"
  Delete "$DESKTOP\${NAME}.lnk"
  DeleteRegKey HKCU "${REGKEY}"
SectionEnd
