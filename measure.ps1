# Measure real content height (mm) via Edge headless print, binary search.
param([int]$Lo = 240, [int]$Hi = 460)

$ErrorActionPreference = 'SilentlyContinue'
$srcDir = Join-Path $PSScriptRoot 'dist'
$srcDir = Join-Path $srcDir ([char]0x62A5 + [char]0x544A)   # "报告"
$html = Get-ChildItem $srcDir -Filter '*.html' | Where-Object { $_.Name.StartsWith([char]0x7ED3 + [char]0x8BBA) } | Sort-Object LastWriteTime | Select-Object -Last 1
$edge = 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'
$tmp = Join-Path $env:TEMP '66measure.html'
$pdf = Join-Path $env:TEMP '66measure.pdf'

function Print-Once([int]$pageH) {
    $t = [IO.File]::ReadAllText($html.FullName, [Text.Encoding]::UTF8)
    $t = $t -replace '@page\{size:A4 portrait;margin:12mm;\}', ('@page{{size:210mm {0}mm;margin:12mm;}}' -f $pageH)
    [IO.File]::WriteAllText($tmp, $t, (New-Object Text.UTF8Encoding($false)))
    if (Test-Path $pdf) { Remove-Item $pdf -Force }
    Start-Process -FilePath $edge -ArgumentList @('--headless=new','--disable-gpu','--no-pdf-header-footer',"--print-to-pdf=$pdf",$tmp) -Wait | Out-Null
    Start-Sleep -Milliseconds 900
    if (-not (Test-Path $pdf)) { return 99 }
    $txt = [IO.File]::ReadAllText($pdf, [Text.Encoding]::ASCII)
    return ([regex]::Matches($txt, '/Type\s*/Page[^s]')).Count
}

$n0 = Print-Once $Hi
Write-Output ("control pages at {0}mm = {1}" -f $Hi, $n0)
if ($n0 -gt 1) { Write-Output "WARN control not single page"; }

while (($Hi - $Lo) -gt 2) {
    $mid = [int](($Lo + $Hi) / 2)
    $n = Print-Once $mid
    Write-Output ("try {0}mm -> {1} page(s)" -f $mid, $n)
    if ($n -le 1) { $Hi = $mid } else { $Lo = $mid }
}
Write-Output ("RESULT content height about {0} mm (A4 usable = 273 mm)" -f $Hi)
