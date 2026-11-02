@echo off
chcp 65001 >nul
setlocal
rem 专业版：与门店版共用采集引擎，追加深度只读项；输出单文件 exe
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe
set SRC=%~dp0src
set OUT=%~dp0dist
if not exist "%OUT%" mkdir "%OUT%"

"%CSC%" /nologo /optimize+ /codepage:65001 /target:exe ^
  /out:"%OUT%\66专业检测.exe" ^
  /win32manifest:"%SRC%\app.manifest" /win32icon:"%SRC%\icons\pro.ico" ^
  /r:System.Management.dll /r:System.Core.dll /r:System.Xml.dll ^
  "%SRC%\Model.cs" "%SRC%\Native.cs" "%SRC%\Ui.cs" "%SRC%\Collectors.cs" ^
  "%SRC%\Pro\ProCollect.cs" "%SRC%\Pro\ProReport.cs" "%SRC%\Pro\ProProgram.cs" ^
  "%SRC%\Pro\AssemblyInfoPro.cs"

if errorlevel 1 (
  echo BUILD FAILED
  exit /b 1
)
echo BUILD OK: %OUT%\66专业检测.exe
