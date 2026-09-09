Unicode True

!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "WinVer.nsh"
!include "x64.nsh"

!ifndef APP_VERSION
  !define APP_VERSION "1.0.0"
!endif
!ifndef PUBLISH_DIR
  !error "PUBLISH_DIR is required."
!endif
!ifndef OUTPUT_DIR
  !error "OUTPUT_DIR is required."
!endif

!define PRODUCT_NAME "温湿度巡检仪校准系统"
!define PRODUCT_ENGLISH_NAME "UpperComInspectionInstrument2022"
!define PRODUCT_PUBLISHER "工业设备校准软件项目组"
!define PRODUCT_EXE "UpperComInspectionInstrument2022.exe"
!define PRODUCT_KEY "Software\IndustrialEquipmentCalibration\UpperComInspectionInstrument2022"
!define UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\UpperComInspectionInstrument2022"

Name "${PRODUCT_NAME} ${APP_VERSION}"
OutFile "${OUTPUT_DIR}\UpperComInspectionInstrument2022-Setup-${APP_VERSION}.exe"
InstallDir "$LOCALAPPDATA\Programs\UpperComInspectionInstrument2022"
InstallDirRegKey HKCU "${PRODUCT_KEY}" "InstallDir"
RequestExecutionLevel user
SetCompressor /SOLID lzma
SetCompressorDictSize 32
CRCCheck on
ShowInstDetails show
ShowUninstDetails show
BrandingText "${PRODUCT_NAME}"

VIProductVersion "${APP_VERSION}.0"
VIAddVersionKey /LANG=2052 "ProductName" "${PRODUCT_NAME}"
VIAddVersionKey /LANG=2052 "CompanyName" "${PRODUCT_PUBLISHER}"
VIAddVersionKey /LANG=2052 "FileDescription" "${PRODUCT_NAME}安装程序"
VIAddVersionKey /LANG=2052 "FileVersion" "${APP_VERSION}"
VIAddVersionKey /LANG=2052 "ProductVersion" "${APP_VERSION}"
VIAddVersionKey /LANG=2052 "LegalCopyright" "Copyright 2026 ${PRODUCT_PUBLISHER}"

!define MUI_ABORTWARNING
!define MUI_ICON "${NSISDIR}\Contrib\Graphics\Icons\modern-install.ico"
!define MUI_UNICON "${NSISDIR}\Contrib\Graphics\Icons\modern-uninstall.ico"
!define MUI_FINISHPAGE_RUN "$INSTDIR\${PRODUCT_EXE}"
!define MUI_FINISHPAGE_RUN_TEXT "启动${PRODUCT_NAME}"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_UNPAGE_FINISH

!insertmacro MUI_LANGUAGE "SimpChinese"

Function .onInit
  ${IfNot} ${AtLeastWin10}
    MessageBox MB_OK|MB_ICONSTOP "本软件仅支持 64 位 Windows 10 或 Windows 11。"
    Abort
  ${EndIf}
  ${IfNot} ${RunningX64}
    MessageBox MB_OK|MB_ICONSTOP "本软件仅支持 64 位 Windows。"
    Abort
  ${EndIf}
FunctionEnd

Section "主程序" SEC_MAIN
  SectionIn RO
  SetShellVarContext current
  SetOutPath "$INSTDIR"
  File /r "${PUBLISH_DIR}\*.*"

  WriteUninstaller "$INSTDIR\uninstall.exe"
  WriteRegStr HKCU "${PRODUCT_KEY}" "InstallDir" "$INSTDIR"
  WriteRegStr HKCU "${PRODUCT_KEY}" "Version" "${APP_VERSION}"

  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayName" "${PRODUCT_NAME}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayVersion" "${APP_VERSION}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "Publisher" "${PRODUCT_PUBLISHER}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayIcon" "$INSTDIR\${PRODUCT_EXE}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "UninstallString" '$\"$INSTDIR\uninstall.exe$\"'
  WriteRegStr HKCU "${UNINSTALL_KEY}" "QuietUninstallString" '$\"$INSTDIR\uninstall.exe$\" /S'
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoRepair" 1

  CreateDirectory "$SMPROGRAMS\${PRODUCT_NAME}"
  CreateShortcut "$SMPROGRAMS\${PRODUCT_NAME}\${PRODUCT_NAME}.lnk" "$INSTDIR\${PRODUCT_EXE}"
  CreateShortcut "$SMPROGRAMS\${PRODUCT_NAME}\卸载${PRODUCT_NAME}.lnk" "$INSTDIR\uninstall.exe"
  CreateShortcut "$DESKTOP\${PRODUCT_NAME}.lnk" "$INSTDIR\${PRODUCT_EXE}"
SectionEnd

Section "Uninstall"
  SetShellVarContext current
  Delete "$DESKTOP\${PRODUCT_NAME}.lnk"
  RMDir /r "$SMPROGRAMS\${PRODUCT_NAME}"
  DeleteRegKey HKCU "${UNINSTALL_KEY}"
  DeleteRegKey HKCU "${PRODUCT_KEY}"
  RMDir /r "$INSTDIR"
  MessageBox MB_OK|MB_ICONINFORMATION "程序已卸载。校准数据、本机账户和系统设置均已保留。" /SD IDOK
SectionEnd
