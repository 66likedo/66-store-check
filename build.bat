@echo off
chcp 65001 >nul
setlocal
rem 用 Windows 自带的 .NET Framework 编译器打包成单文件 exe（不需要装 VS / SDK）
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe
set SRC=%~dp0src
set OUT=%~dp0dist
if not exist "%OUT%" mkdir "%OUT%"

"%CSC%" /nologo /optimize+ /codepage:65001 /target:exe ^
  /out:"%OUT%\66门店巡检.exe" ^
  /win32manifest:"%SRC%\app.manifest" /win32icon:"%SRC%\icons\store.ico" ^
  /r:System.Management.dll /r:System.Core.dll /r:System.Xml.dll ^
  "%SRC%\Model.cs" "%SRC%\Native.cs" "%SRC%\Ui.cs" "%SRC%\Collectors.cs" ^
  "%SRC%\Report.cs" "%SRC%\Program.cs" "%SRC%\AssemblyInfo.cs"

if errorlevel 1 (
  echo BUILD FAILED
  exit /b 1
)
echo BUILD OK: %OUT%\66门店巡检.exe
