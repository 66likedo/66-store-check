# Dev tool: render the newest sheet at A4 content width (703px) and report the
# height of each block in mm, so we can see which block is too tall.
$ErrorActionPreference = 'SilentlyContinue'
$root = Split-Path -Parent $PSScriptRoot
if (-not $root) { $root = (Get-Location).Path }
$dir = Join-Path $root 'dist'
$dir = Join-Path $dir ([string][char]0x62A5 + [string][char]0x544A)
$html = Get-ChildItem $dir -Filter '*.html' |
        Where-Object { $_.Name.StartsWith([string][char]0x7ED3 + [string][char]0x8BBA) } |
        Sort-Object LastWriteTime | Select-Object -Last 1
if (-not $html) { Write-Output 'NO SHEET'; exit 1 }

$src = [IO.File]::ReadAllText($html.FullName, [Text.Encoding]::UTF8)
$js = @'
<script>
window.onload=function(){
  var q=['.hdwrap','.bar','.grid','.blk','.sg','.ft'];
  var out=[];
  for(var i=0;i<q.length;i++){
    var els=document.querySelectorAll(q[i]);
    for(var j=0;j<els.length;j++){
      var r=els[j].getBoundingClientRect();
      out.push(q[i]+'#'+j+'='+(Math.round(r.height*0.2646*10)/10)+'mm');
    }
  }
  var rows=document.querySelectorAll('.grid tbody tr');
  for(var k=0;k<rows.length;k++){
    var rr=rows[k].getBoundingClientRect();
    out.push('row'+k+'='+(Math.round(rr.height*0.2646*10)/10));
  }
  out.push('TOTAL='+(Math.round(document.body.getBoundingClientRect().height*0.2646*10)/10)+'mm');
  var d=document.createElement('div');d.id='MB';d.textContent=out.join(' ; ');document.body.appendChild(d);
}
</script>
'@
$tmp = Join-Path $env:TEMP 'sc_blocks.html'
[IO.File]::WriteAllText($tmp, $src.Replace('</body>', $js + '</body>'), (New-Object Text.UTF8Encoding($false)))

$edge = 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'
$out = & $edge --headless=new --disable-gpu --hide-scrollbars --window-size=703,2000 --dump-dom $tmp 2>$null
$m = [regex]::Match(($out -join "`n"), 'id="MB">([^<]+)<')
if ($m.Success) { Write-Output ($m.Groups[1].Value -replace ' ; ', "`n") } else { Write-Output 'MEASURE FAILED' }
