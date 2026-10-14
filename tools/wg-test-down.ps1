param([Parameter(Mandatory)][string]$Name)
# Tears down a test tunnel brought up by wg-test-up.ps1.
$wg = 'C:\Program Files\WireGuard\wireguard.exe'
& $wg /uninstalltunnelservice $Name 2>&1 | Out-Null
Start-Sleep 1
"Tunnel '$Name' removed. Default route now:"
Get-NetRoute -AddressFamily IPv4 -DestinationPrefix 0.0.0.0/0 | Sort-Object RouteMetric |
    Select-Object ifIndex, NextHop, RouteMetric | Format-Table | Out-String
