@echo off
chcp 65001 >nul
setlocal
rem 清理模块：独立 exe，与两个只读版本完全分开
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe
set SRC=%~dp0src
set OUT=%~dp0dist
if not exist "%OUT%" mkdir "%OUT%"

"%CSC%" /nologo /optimize+ /codepage:65001 /target:exe ^
  /out:"%OUT%\66系统清理.exe" ^
  /win32manifest:"%SRC%\app.manifest" /win32icon:"%SRC%\icons\clean.ico" ^
  /r:System.Management.dll /r:System.Core.dll /r:System.Xml.dll ^
  "%SRC%\Model.cs" "%SRC%\Native.cs" "%SRC%\Ui.cs" ^
  "%SRC%\Clean\CleanJobs.cs" "%SRC%\Clean\CleanProgram.cs" "%SRC%\Clean\AssemblyInfoClean.cs"

if errorlevel 1 (
  echo BUILD FAILED
  exit /b 1
)
echo BUILD OK: %OUT%\66系统清理.exe
