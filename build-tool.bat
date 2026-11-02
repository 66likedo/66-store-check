@echo off
chcp 65001 >nul
setlocal
rem 维护模块：迁移 / 启动项 / 目录修复 / 重复文件 / 乱码名 / 卸载建议 / 图标整理 / 个性化修复
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe
set SRC=%~dp0src
set OUT=%~dp0dist
if not exist "%OUT%" mkdir "%OUT%"

"%CSC%" /nologo /optimize+ /codepage:65001 /target:exe ^
  /out:"%OUT%\66维护工具.exe" ^
  /win32manifest:"%SRC%\app.manifest" /win32icon:"%SRC%\icons\tool.ico" ^
  /r:System.Management.dll /r:System.Core.dll /r:System.Xml.dll ^
  "%SRC%\Model.cs" "%SRC%\Native.cs" "%SRC%\Ui.cs" ^
  "%SRC%\Tool\ToolCommon.cs" "%SRC%\Tool\ToolModules.cs" "%SRC%\Tool\ToolProgram.cs" ^
  "%SRC%\Tool\AssemblyInfoTool.cs"

if errorlevel 1 (
  echo BUILD FAILED
  exit /b 1
)
echo BUILD OK: %OUT%\66维护工具.exe
