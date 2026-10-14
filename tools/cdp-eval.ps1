param(
    [int]$Port = 9333,
    [string]$Url = "https://api.ipify.org",
    [int]$WaitSec = 15
)
# Dev helper: opens $Url in a new tab of a Chrome started with
# --remote-debugging-port=$Port, waits for it to load, prints the start of
# document.body.innerText, then closes the tab.
$ErrorActionPreference = "Stop"
$base = "http://127.0.0.1:$Port"
$t = Invoke-RestMethod -Method Put -Uri "$base/json/new?$Url"
$ws = [System.Net.WebSockets.ClientWebSocket]::new()
$ws.ConnectAsync([Uri]$t.webSocketDebuggerUrl, [Threading.CancellationToken]::None).Wait()

function Send-Cdp($id, $method, $params) {
    $msg = @{ id = $id; method = $method; params = $params } | ConvertTo-Json -Compress -Depth 5
    $bytes = [Text.Encoding]::UTF8.GetBytes($msg)
    $ws.SendAsync([ArraySegment[byte]]::new($bytes), 'Text', $true, [Threading.CancellationToken]::None).Wait()
    $buf = New-Object byte[] 65536
    while ($true) {
        $sb = [Text.StringBuilder]::new()
        do {
            $r = $ws.ReceiveAsync([ArraySegment[byte]]::new($buf), [Threading.CancellationToken]::None).Result
            [void]$sb.Append([Text.Encoding]::UTF8.GetString($buf, 0, $r.Count))
        } while (-not $r.EndOfMessage)
        $o = $sb.ToString() | ConvertFrom-Json
        if ($o.id -eq $id) { return $o }
    }
}

$deadline = (Get-Date).AddSeconds($WaitSec)
$text = ""
$i = 1
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 700
    $expr = "document.readyState + '|' + location.href + '|' + (document.body ? document.body.innerText.slice(0, 300) : '')"
    $res = Send-Cdp $i "Runtime.evaluate" @{ expression = $expr; returnByValue = $true }
    $i++
    $text = "$($res.result.result.value)"
    if ($text.StartsWith("complete|") -and -not $text.Contains("|about:blank|")) { break }
}
Write-Output ("URL: $Url`nRESULT: " + ($text -replace '\s+', ' ').Trim())
try { Invoke-RestMethod -Uri "$base/json/close/$($t.id)" | Out-Null } catch {}
$ws.Dispose()
