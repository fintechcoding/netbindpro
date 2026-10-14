param([string]$Guid = '{B8B23E36-F7FF-505B-1FD7-7F80753FCA2F}')
# Comprehensive per-app egress test. Launches each app bound to the tunnel via the Hook
# (medium IL = the app's de-elevated launch path) and checks whether its traffic actually
# leaves via the tunnel (public IP for curl; socket local address = tunnel IP for GUI apps).
$ErrorActionPreference = 'Continue'
$hook  = 'D:\Projeler\Genel\netbindpro\src\NetBindPro.Hook\bin\x64\Debug\net48\NetBindPro.Hook.exe'
$work  = Join-Path $env:LOCALAPPDATA 'NetBindPro\fulltest'
New-Item -ItemType Directory -Force $work | Out-Null
$tunIp = '10.241.66.232'   # imvpn-137 interface IP
function HookLaunch($exe, $argStr) { & $hook --network $Guid --execute $exe --args $argStr --delay 0 --debug 2>&1 | Select-String 'SUCCESS|FATAL|ERROR' | ForEach-Object { $_.Line } }
function StopApp($pattern) { Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -like "*$pattern*" } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue } }
function Egress($pids) {
    $r = @()
    foreach ($p in $pids) { Get-NetTCPConnection -OwningProcess $p -State Established -ErrorAction SilentlyContinue |
        Where-Object RemoteAddress -notin '127.0.0.1','::1' | ForEach-Object { $r += $_.LocalAddress } }
    $r | Group-Object | ForEach-Object { "$($_.Name) x$($_.Count)" }
}

"==================== BASELINE ===================="
"server real IP (unbound)     : $(curl.exe -s --max-time 10 https://api.ipify.org)"
"tunnel IP egress (--interface): $(curl.exe -s --interface $tunIp --max-time 10 https://api.ipify.org)"

"`n==================== 1) curl (simple CLI app) ===================="
$f = "$work\curl-ip.txt"; Remove-Item $f -ErrorAction SilentlyContinue
HookLaunch 'C:\Windows\System32\curl.exe' "-s --max-time 12 -o `"$f`" https://api.ipify.org"
Start-Sleep 6
"curl egress IP THROUGH HOOK  : $(Get-Content $f -ErrorAction SilentlyContinue)   (tunnel exit = bound OK)"

"`n==================== 2) Chrome ===================="
StopApp 'fulltest\chrome'
$cp = "$work\chrome"; [IO.File]::Delete("$cp\SingletonLock") 2>$null
HookLaunch 'C:\Program Files\Google\Chrome\Application\chrome.exe' "--user-data-dir=$cp --no-first-run --no-default-browser-check --headless=new https://example.com"
Start-Sleep 7
$cpids = (Get-CimInstance Win32_Process -Filter "Name='chrome.exe'" | Where-Object CommandLine -like '*fulltest\chrome*').ProcessId
"chrome procs: $($cpids.Count); egress local addrs:"; Egress $cpids | ForEach-Object { "   $_" }
StopApp 'fulltest\chrome'

"`n==================== 3) Telegram ===================="
StopApp 'fulltest\tg'
$tg = "$work\tg"; New-Item -ItemType Directory -Force $tg | Out-Null
HookLaunch 'C:\Users\admin\AppData\Roaming\Telegram Desktop\Telegram.exe' "-workdir `"$tg`""
Start-Sleep 9
$tpids = (Get-CimInstance Win32_Process -Filter "Name='Telegram.exe'" | Where-Object CommandLine -like '*fulltest\tg*').ProcessId
"telegram procs: $($tpids.Count); egress local addrs:"; Egress $tpids | ForEach-Object { "   $_" }
StopApp 'fulltest\tg'

"`n==================== 4) Firefox ===================="
StopApp 'fulltest\ff'
$fp = "$work\ff"; New-Item -ItemType Directory -Force $fp | Out-Null
HookLaunch 'C:\Program Files\Mozilla Firefox\firefox.exe' "-no-remote -profile `"$fp`" -headless https://example.com"
Start-Sleep 8
$fpids = (Get-CimInstance Win32_Process -Filter "Name='firefox.exe'" | Where-Object CommandLine -like '*fulltest\ff*').ProcessId
"firefox procs: $($fpids.Count); egress local addrs:"; Egress $fpids | ForEach-Object { "   $_" }
StopApp 'fulltest\ff'

"`n(tunnel IP = $tunIp  -> bound; 144.76.111.177 = Ethernet -> NOT bound/leak)"
