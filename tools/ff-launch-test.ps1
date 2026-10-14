param(
    [string]$Guid = '{37217669-42DA-4657-A55B-0D995D328250}',
    [string]$Url  = 'https://example.com',
    [switch]$Headless
)
$hook = 'D:\Projeler\Genel\netbindpro\src\NetBindPro.Hook\bin\x64\Debug\net48\NetBindPro.Hook.exe'
$ff   = 'C:\Program Files\Mozilla Firefox\firefox.exe'
$prof = Join-Path $env:LOCALAPPDATA 'NetBindPro\ff-test'
New-Item -ItemType Directory -Force $prof | Out-Null
Get-ChildItem "$env:TEMP\NetBindPro-firefox*.log" -ErrorAction SilentlyContinue | ForEach-Object { [IO.File]::Delete($_.FullName) }
Get-CimInstance Win32_Process -Filter "Name='firefox.exe'" | Where-Object CommandLine -like '*ff-test*' | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
Start-Sleep 1

$hl = if ($Headless) { '-headless ' } else { '' }
$ffArgs = "-no-remote -profile `"$prof`" $hl$Url"
& $hook --network $Guid --execute $ff --args $ffArgs --delay 0 --debug 2>&1 | Select-Object -First 6
Start-Sleep 8

"--- test firefox procs ---"
$procs = Get-CimInstance Win32_Process -Filter "Name='firefox.exe'" | Where-Object CommandLine -like '*ff-test*'
foreach ($p in $procs) {
    $hk = Test-Path -LiteralPath "$env:TEMP\NetBindPro-firefox[$($p.ProcessId)].log"
    $t  = if ($p.CommandLine -match '-contentproc') { if ($p.CommandLine -match '(\w+)\s*$') { $Matches[1] } else { 'child' } } else { 'MAIN' }
    "{0,6} {1,-8} hooked={2}" -f $p.ProcessId, $t, $hk
}
"--- non-loopback sockets of hooked firefox procs ---"
foreach ($p in $procs) {
    Get-NetTCPConnection -OwningProcess $p.ProcessId -State Established -ErrorAction SilentlyContinue |
        Where-Object RemoteAddress -notin '127.0.0.1', '::1' |
        ForEach-Object { "{0,6} {1} -> {2}:{3}" -f $p.ProcessId, $_.LocalAddress, $_.RemoteAddress, $_.RemotePort }
}
"--- hook logs (bind activity) ---"
Get-ChildItem "$env:TEMP\NetBindPro-firefox*.log" -ErrorAction SilentlyContinue | ForEach-Object {
    "== $($_.Name)"
    Get-Content -LiteralPath $_.FullName | Select-String 'installed|BindV4|Child PID|EnsureBound|FATAL' | Select-Object -Last 5 | ForEach-Object { $_.Line }
}
