# Dev tool: find which block makes the sheet overflow. Prints the current sheet
# on a 273mm-tall page with one block hidden at a time, and reports page count.
$ErrorActionPreference = 'SilentlyContinue'
$root = Split-Path -Parent $PSScriptRoot
if (-not $root) { $root = (Get-Location).Path }
$dir = Join-Path $root 'dist'
$dir = Join-Path $dir ([string][char]0x62A5 + [string][char]0x544A)
$html = Get-ChildItem $dir -Filter '*.html' |
        Where-Object { $_.Name.StartsWith([string][char]0x7ED3 + [string][char]0x8BBA) } |
        Sort-Object LastWriteTime | Select-Object -Last 1
$src = [IO.File]::ReadAllText($html.FullName, [Text.Encoding]::UTF8)
$edge = 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'
$p = Join-Path $env:TEMP 'sc_diag.html'
$pdf = Join-Path $env:TEMP 'sc_diag.pdf'

$variants = @(
    @('full',        ''),
    @('font9.6',     '.grid{font-size:9.6pt}'),
    @('autolayout',  '.grid{table-layout:auto}'),
    @('9.6+auto',    '.grid{font-size:9.6pt;table-layout:auto}'),
    @('oldwidths',   '.grid td.nm{width:20mm}.grid td.ms{width:56mm}.grid td.pv{width:24mm}.grid td.vd{width:16mm}'),
    @('tightpad',    '.grid td{padding:0.6mm 1.1mm}')
)

foreach ($v in $variants) {
    $t = $src.Replace('@page{size:A4 portrait;margin:12mm;}', '@page{size:210mm 273mm;margin:12mm;}')
    if ($v[1] -ne '') { $t = $t.Replace('</style>', $v[1] + '</style>') }
    [IO.File]::WriteAllText($p, $t, (New-Object Text.UTF8Encoding($false)))
    if (Test-Path $pdf) { Remove-Item $pdf -Force }
    Start-Process -FilePath $edge -ArgumentList @('--headless=new','--disable-gpu','--no-pdf-header-footer',('--print-to-pdf=' + $pdf),$p) -Wait | Out-Null
    Start-Sleep -Milliseconds 700
    $n = 0
    if (Test-Path $pdf) {
        $x = [IO.File]::ReadAllText($pdf, [Text.Encoding]::ASCII)
        $n = ([regex]::Matches($x, '/Type\s*/Page[^s]')).Count
    }
    Write-Output ('{0,-12} pages={1}' -f $v[0], $n)
}
