# Dev tool: measure the real content height (mm) of the newest result sheet,
# by printing it to PDF with a series of page heights and binary-searching the
# height at which it still fits on ONE page. A4 usable height = 297 - 24 = 273mm.
param([int]$Lo = 200, [int]$Hi = 420)

$ErrorActionPreference = 'SilentlyContinue'
$root = Split-Path -Parent $PSScriptRoot
if (-not $root) { $root = (Get-Location).Path }
$sheetDir = Join-Path $root 'dist'
$sheetDir = Join-Path $sheetDir ([string][char]0x62A5 + [string][char]0x544A)

$html = Get-ChildItem $sheetDir -Filter '*.html' |
        Where-Object { $_.Name.StartsWith([string][char]0x7ED3 + [string][char]0x8BBA) } |
        Sort-Object LastWriteTime | Select-Object -Last 1
if (-not $html) { Write-Output 'NO SHEET FOUND'; exit 1 }
Write-Output ('SHEET: ' + $html.Name)

$edge = 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'
if (-not (Test-Path $edge)) { Write-Output 'EDGE NOT FOUND'; exit 1 }
$tmp = Join-Path $env:TEMP 'sc_measure.html'
$pdf = Join-Path $env:TEMP 'sc_measure.pdf'

function Print-Once([int]$pageH) {
    $t = [IO.File]::ReadAllText($html.FullName, [Text.Encoding]::UTF8)
    $t = $t.Replace('@page{size:A4 portrait;margin:12mm;}', ('@page{size:210mm ' + $pageH + 'mm;margin:12mm;}'))
    [IO.File]::WriteAllText($tmp, $t, (New-Object Text.UTF8Encoding($false)))
    if (Test-Path $pdf) { Remove-Item $pdf -Force }
    Start-Process -FilePath $edge -ArgumentList @('--headless=new','--disable-gpu','--no-pdf-header-footer',('--print-to-pdf=' + $pdf),$tmp) -Wait | Out-Null
    Start-Sleep -Milliseconds 700
    if (-not (Test-Path $pdf)) { return 99 }
    $txt = [IO.File]::ReadAllText($pdf, [Text.Encoding]::ASCII)
    return ([regex]::Matches($txt, '/Type\s*/Page[^s]')).Count
}

$n = Print-Once $Hi
if ($n -gt 1) { Write-Output ('control ' + $Hi + 'mm still multi-page, raise -Hi'); exit 1 }

while (($Hi - $Lo) -gt 2) {
    $mid = [int](($Lo + $Hi) / 2)
    $c = Print-Once $mid
    Write-Output ('  ' + $mid + 'mm -> ' + $c + ' page(s)')
    if ($c -le 1) { $Hi = $mid } else { $Lo = $mid }
}
Write-Output ('REQUIRED PAGE HEIGHT ~ ' + $Hi + ' mm   (A4 = 297 mm)')
if ($Hi -le 294) { Write-Output 'RESULT: PASS (one A4 page)' } else { Write-Output 'RESULT: FAIL (overflow)' }
