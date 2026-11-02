# Dev tool: print the newest sheet exactly as A4 (no overrides) and count pages.
# -Pro switches to the professional report folder.
param([switch]$Pro)

$ErrorActionPreference = 'SilentlyContinue'
$root = Split-Path -Parent $PSScriptRoot
if (-not $root) { $root = (Get-Location).Path }
$dir = Join-Path $root 'dist'
if ($Pro) {
    $dir = Join-Path $dir ([string][char]0x4E13 + [string][char]0x4E1A + [string][char]0x62A5 + [string][char]0x544A)
    $prefix = ([string][char]0x4E13 + [string][char]0x4E1A + [string][char]0x62A5 + [string][char]0x544A)
} else {
    $dir = Join-Path $dir ([string][char]0x62A5 + [string][char]0x544A)
    $prefix = ([string][char]0x7ED3 + [string][char]0x8BBA + [string][char]0x5355)
}
$html = Get-ChildItem $dir -Filter '*.html' | Where-Object { $_.Name.StartsWith($prefix) } |
        Sort-Object LastWriteTime | Select-Object -Last 1
if (-not $html) { Write-Output 'NO SHEET'; exit 1 }

$pdf = Join-Path $env:TEMP 'sc_a4.pdf'
if (Test-Path $pdf) { Remove-Item $pdf -Force }
$edge = 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'
Start-Process -FilePath $edge -ArgumentList @('--headless=new','--disable-gpu','--no-pdf-header-footer',('--print-to-pdf=' + $pdf),$html.FullName) -Wait | Out-Null
Start-Sleep -Milliseconds 800
$x = [IO.File]::ReadAllText($pdf, [Text.Encoding]::ASCII)
$n = ([regex]::Matches($x, '/Type\s*/Page[^s]')).Count
Write-Output ('SHEET: ' + $html.Name)
Write-Output ('A4 PAGES: ' + $n)
if ($n -ge 1) { Write-Output ('RESULT: printed OK (' + $n + ' page)') } else { Write-Output 'RESULT: FAIL' }
