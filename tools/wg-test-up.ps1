param(
    [Parameter(Mandatory)][string]$Conf,      # path to a raw .conf in E:\vpn
    [switch]$StripDns                          # also remove DNS= (keeps system DNS untouched)
)
# Dev test: brings a WireGuard tunnel up SAFELY for per-process routing.
# It rewrites [Interface] to Table=off (so WG does NOT hijack the default route or
# install its all-blocking kill switch - which would drop an RDP session), installs
# the tunnel service, verifies the system default route is UNCHANGED, then adds a
# high-metric default route via the tunnel so only source-bound sockets use it.
$ErrorActionPreference = 'Stop'
$wg = 'C:\Program Files\WireGuard\wireguard.exe'
if (-not (Test-Path $wg)) { throw 'wireguard.exe not found' }

$name = [IO.Path]::GetFileNameWithoutExtension($Conf) -replace '[^A-Za-z0-9_=+.-]', '_'
$raw  = Get-Content -Raw $Conf

# --- rewrite [Interface]: force Table = off, drop existing Table/MTU (and DNS if asked)
$out = New-Object System.Collections.Generic.List[string]
$inIface = $false
foreach ($line in ($raw -replace "`r`n", "`n").Split("`n")) {
    $tr = $line.Trim()
    if ($tr.StartsWith('[')) {
        $inIface = $tr -ieq '[Interface]'
        $out.Add($line)
        if ($inIface) { $out.Add('Table = off') }
        continue
    }
    if ($inIface -and $tr -imatch '^(Table|MTU)\s*=') { continue }
    if ($inIface -and $StripDns -and $tr -imatch '^DNS\s*=') { continue }
    $out.Add($line)
}
$safeDir  = Join-Path $env:LOCALAPPDATA 'NetBindPro\test-tunnels'
New-Item -ItemType Directory -Force $safeDir | Out-Null
$safeConf = Join-Path $safeDir "$name.conf"
Set-Content -LiteralPath $safeConf -Value ($out -join "`r`n") -Encoding ascii
"Rewrote conf -> $safeConf (Table=off; DNS stripped=$StripDns)"

# --- snapshot default route BEFORE (RDP safety baseline)
$before = Get-NetRoute -AddressFamily IPv4 -DestinationPrefix 0.0.0.0/0 | Sort-Object RouteMetric | Select-Object -First 1
"BEFORE default route: ifIndex=$($before.ifIndex) nextHop=$($before.NextHop)"

& $wg /installtunnelservice $safeConf | Out-Null
Start-Sleep 2

# wait for the wintun adapter
$nic = $null
for ($i = 0; $i -lt 12 -and -not $nic; $i++) {
    $nic = Get-NetAdapter -ErrorAction SilentlyContinue | Where-Object { $_.Name -eq $name -and $_.Status -eq 'Up' }
    if (-not $nic) { Start-Sleep -Milliseconds 700 }
}
if (-not $nic) { throw "Tunnel adapter '$name' did not come up" }
"Tunnel adapter UP: $($nic.Name) ifIndex=$($nic.ifIndex) GUID=$($nic.InterfaceGuid)"

# --- verify default route UNCHANGED (this is the RDP-safety gate)
$after = Get-NetRoute -AddressFamily IPv4 -DestinationPrefix 0.0.0.0/0 | Sort-Object RouteMetric | Select-Object -First 1
if ($after.ifIndex -ne $before.ifIndex) {
    "!! DEFAULT ROUTE CHANGED to ifIndex=$($after.ifIndex) - rolling back for safety"
    & $wg /uninstalltunnelservice $name | Out-Null
    throw 'Default route was hijacked; tunnel removed. RDP protected.'
}
"OK default route still ifIndex=$($after.ifIndex) (RDP safe)"

# --- add high-metric default route via the tunnel (store=active, auto-gone on stop)
netsh interface ipv4 add route prefix=0.0.0.0/0 interface=$($nic.ifIndex) metric=9000 store=active | Out-Null
"Added metric-9000 default route via tunnel ifIndex=$($nic.ifIndex)"

$guid = '{' + $nic.InterfaceGuid.ToString().Trim('{').Trim('}') + '}'
"RESULT Name=$name Guid=$guid IfIndex=$($nic.ifIndex)"
