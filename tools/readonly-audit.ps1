# Dev tool: prove the tool is read-only.
# Snapshots startup/registry/uninstall/services/files, runs the inspector once,
# snapshots again and diffs. Usage:
#   readonly-audit.ps1                 -> store edition
#   readonly-audit.ps1 -Kind pro       -> professional edition (degraded mode)
param([string]$Kind = 'store')

$ErrorActionPreference = 'SilentlyContinue'
$root = Split-Path -Parent $PSScriptRoot
if (-not $root) { $root = (Get-Location).Path }

if ($Kind -eq 'pro') {
    $exeName = '66' + [string][char]0x4E13 + [string][char]0x4E1A + [string][char]0x68C0 + [string][char]0x6D4B + '.exe'
    $exe = Join-Path $root ('dist\' + $exeName)
    $extraArgs = '--degraded'
    $skipName = ([string][char]0x4E13 + [string][char]0x4E1A + [string][char]0x62A5 + [string][char]0x544A)
} else {
    $exeName = '66' + [string][char]0x95E8 + [string][char]0x5E97 + [string][char]0x5DE1 + [string][char]0x68C0 + '.exe'
    $exe = Join-Path $root ('dist\' + $exeName)
    $extraArgs = ''
    $skipName = ([string][char]0x62A5 + [string][char]0x544A)
}
if (-not (Test-Path $exe)) { Write-Output 'EXE NOT FOUND'; exit 1 }

function Snap {
    $run = @{}
    $paths = @(
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run',
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run',
        'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run',
        'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce'
    )
    foreach ($p in $paths) {
        $k = Get-Item $p
        if ($k) { $run[$p] = (($k.GetValueNames() | Sort-Object) -join ',') } else { $run[$p] = '' }
    }
    $u1 = @(Get-ChildItem 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall').Count
    $u2 = @(Get-ChildItem 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall').Count
    $u3 = @(Get-ChildItem 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall').Count
    $svc = @(Get-Service).Count
    $tmp1 = Test-Path (Join-Path $env:TEMP '66check')
    $tmp2 = Test-Path (Join-Path $env:TEMP '66check_pro')
    $sep = [IO.Path]::DirectorySeparatorChar
    $skip1 = '*' + $sep + $skipName + $sep + '*'
    $skip2 = '*' + $sep + ([string][char]0x62A5 + [string][char]0x544A) + $sep + '*'
    $files = @(Get-ChildItem (Join-Path $root 'dist') -Recurse -File |
               Where-Object { $_.FullName -notlike $skip1 -and $_.FullName -notlike $skip2 } |
               ForEach-Object { $_.FullName + '|' + $_.Length }) -join ';'
    [pscustomobject]@{ Run = $run; Uninst = "$u1/$u2/$u3"; Svc = $svc; Tmp = ($tmp1 -or $tmp2); Files = $files }
}

Write-Output ('--- ' + $Kind + ' : snapshot BEFORE ---')
$before = Snap
Write-Output ('uninstall keys: ' + $before.Uninst + '   services: ' + $before.Svc)

$sw = [Diagnostics.Stopwatch]::StartNew()
$env:SC_NOOPEN = '1'
$cmd = '"' + $exe + '" ' + $extraArgs
$out = ('AuditStore' + "`n" + 'Auditor' + "`n") | & cmd /c $cmd
$sw.Stop()
Write-Output ('--- run took ' + $sw.Elapsed.ToString() + ' ---')

$after = Snap
Write-Output ('uninstall keys: ' + $after.Uninst + '   services: ' + $after.Svc)

$fail = 0
foreach ($k in $before.Run.Keys) {
    if ($before.Run[$k] -ne $after.Run[$k]) { Write-Output ('CHANGED: ' + $k); $fail++ }
}
if ($before.Uninst -ne $after.Uninst) { Write-Output 'CHANGED: uninstall key count'; $fail++ }
if ($before.Svc -ne $after.Svc) { Write-Output 'CHANGED: service count'; $fail++ }
if ($after.Tmp) { Write-Output 'LEAK: temp folder created'; $fail++ }
if ($before.Files -ne $after.Files) {
    Write-Output 'CHANGED: files outside report dir'
    $b = $before.Files -split ';'; $a = $after.Files -split ';'
    Compare-Object $b $a | ForEach-Object { Write-Output ('  ' + $_.SideIndicator + ' ' + $_.InputObject) }
    $fail++
}

$bytes = [IO.File]::ReadAllBytes($exe)
$txt = [Text.Encoding]::ASCII.GetString($bytes)
if ($txt.Contains('asInvoker')) { Write-Output 'manifest: asInvoker (UAC asked at runtime, not at launch)' } else { Write-Output 'manifest: asInvoker NOT FOUND'; $fail++ }
if ($txt.Contains('requireAdministrator')) { Write-Output 'WARN: manifest forces admin'; $fail++ }

if ($fail -eq 0) { Write-Output 'RESULT: PASS (read-only, nothing changed)' } else { Write-Output ('RESULT: FAIL (' + $fail + ' issues)') }
