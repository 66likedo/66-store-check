# Dev tool: peek inside the original launcher (metadata + embedded payload only,
# no code is executed) so we can see which features the old tool offered.
param([Parameter(Mandatory = $true)][string]$Exe)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $Exe)) { Write-Output ('not found: ' + $Exe); exit 1 }
$out = Join-Path $env:TEMP 'old_payload'
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Path $out | Out-Null

$asm = [Reflection.Assembly]::LoadFile($Exe)
Write-Output ('--- resources of ' + [IO.Path]::GetFileName($Exe) + ' ---')
$names = $asm.GetManifestResourceNames()
$names | ForEach-Object { Write-Output ('  ' + $_) }

foreach ($n in $names) {
    $s = $asm.GetManifestResourceStream($n)
    $dst = Join-Path $out ([IO.Path]::GetFileName($n))
    $fs = [IO.File]::Create($dst)
    $s.CopyTo($fs)
    $fs.Close(); $s.Close()
    Write-Output ('  extracted ' + $n + ' -> ' + $dst + ' (' + (Get-Item $dst).Length + ' bytes)')
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
foreach ($z in (Get-ChildItem $out -Filter '*.zip')) {
    $dir = Join-Path $out 'unzip'
    [IO.Compression.ZipFile]::ExtractToDirectory($z.FullName, $dir)
    Write-Output ('--- entries in ' + $z.Name + ' ---')
    Get-ChildItem $dir -Recurse -File | ForEach-Object {
        Write-Output ('  ' + $_.FullName.Substring($dir.Length + 1) + '  ' + $_.Length + ' bytes')
    }
}
