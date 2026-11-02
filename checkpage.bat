@echo off
setlocal
rem 用 Edge 无头模式把最新一份结论单打印成 PDF，用来验证"打印预览正好一页 A4"
set SRC=%~dp0dist\报告
set EDGE=C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe
set PDF=%TEMP%\66check_page.pdf

for /f "delims=" %%i in ('dir /b /od "%SRC%\结论单_*.html"') do set LAST=%%i
echo HTML: %LAST%
if exist "%PDF%" del /q "%PDF%"

"%EDGE%" --headless=new --disable-gpu --no-pdf-header-footer --print-to-pdf="%PDF%" "%SRC%\%LAST%"

if exist "%PDF%" (
  echo PDF OK: %PDF%
  powershell -NoProfile -Command "$t=[IO.File]::ReadAllText($env:TEMP+'\66check_page.pdf',[Text.Encoding]::ASCII); $c=([regex]::Matches($t,'/Type\s*/Page[^s]')).Count; Write-Output ('PDF_PAGES=' + $c)"
) else (
  echo PDF FAILED
)
