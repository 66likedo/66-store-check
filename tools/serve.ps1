# Dev tool: tiny static file server so the result sheet can be previewed in a browser.
param([int]$Port = 8765, [string]$Root = 'dist')

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $root) { $root = (Get-Location).Path }
$base = Join-Path $root $Root
if (-not (Test-Path $base)) { Write-Output ('no such dir: ' + $base); exit 1 }
$base = (Resolve-Path $base).Path

$listener = New-Object System.Net.HttpListener
$listener.Prefixes.Add(('http://localhost:' + $Port + '/'))
$listener.Start()
Write-Output ('serving ' + $base + ' at http://localhost:' + $Port + '/')

$types = @{ '.html' = 'text/html; charset=utf-8'; '.txt' = 'text/plain; charset=utf-8'; '.png' = 'image/png'; '.exe' = 'application/octet-stream' }

while ($listener.IsListening) {
    try {
        $ctx = $listener.GetContext()
        $rel = [Uri]::UnescapeDataString($ctx.Request.Url.AbsolutePath).TrimStart('/')
        $path = Join-Path $base $rel
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            $bytes = [IO.File]::ReadAllBytes($path)
            $ext = [IO.Path]::GetExtension($path).ToLower()
            if ($types.ContainsKey($ext)) { $ctx.Response.ContentType = $types[$ext] }
            $ctx.Response.ContentLength64 = $bytes.Length
            $ctx.Response.OutputStream.Write($bytes, 0, $bytes.Length)
        }
        else {
            $ctx.Response.StatusCode = 404
        }
        $ctx.Response.OutputStream.Close()
    }
    catch { }
}
