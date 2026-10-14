param(
    [Parameter(Mandatory)][string]$AdapterGuid,
    [string]$Hook = "$PSScriptRoot\..\src\NetBindPro.Hook\bin\x64\Debug\net48\NetBindPro.Hook.exe",
    [string[]]$Urls = @('https://api.ipify.org'),
    [switch]$Visible,
    [switch]$KeepOpen,
    [string]$ExtraChromeArgs = ""
)
# Dev test harness: launches an ISOLATED test Chrome (its own profile under
# %LOCALAPPDATA%\NetBindPro\chrome-test — never the user's real profile) through the
# Hook, shows which Chrome processes got hooked, loads $Urls via CDP and prints the
# result plus the network-service sockets' local addresses.
$profileDir = Join-Path $env:LOCALAPPDATA 'NetBindPro\chrome-test'
$work = Join-Path $env:LOCALAPPDATA 'NetBindPro\test-out'
New-Item -ItemType Directory -Force $work | Out-Null

function Get-TestChrome {
    Get-CimInstance Win32_Process -Filter "Name='chrome.exe'" | Where-Object CommandLine -like '*NetBindPro\chrome-test*'
}
Get-TestChrome | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
Start-Sleep -Milliseconds 800
Remove-Item "$env:TEMP\NetBindPro-chrome*.log" -ErrorAction SilentlyContinue

$headless = if ($Visible) { '' } else { '--headless=new ' }
$chromeArgs = "--user-data-dir=$profileDir --remote-debugging-port=9333 --no-first-run --no-default-browser-check $ExtraChromeArgs ${headless}about:blank"
$hookArgs = "--network $AdapterGuid --execute `"C:\Program Files\Google\Chrome\Application\chrome.exe`" --args `"$chromeArgs`" --delay 0 --debug"
$p = Start-Process $Hook -ArgumentList $hookArgs -PassThru -NoNewWindow `
        -RedirectStandardOutput "$work\hook-out.txt" -RedirectStandardError "$work\hook-err.txt"
$p.WaitForExit(30000) | Out-Null
"HOOK EXIT: $($p.ExitCode)"
Get-Content "$work\hook-out.txt", "$work\hook-err.txt" -ErrorAction SilentlyContinue
Start-Sleep 3

"--- test chrome processes"
Get-TestChrome | ForEach-Object {
    $t  = if ($_.CommandLine -match '--type=(\S+)') { $Matches[1] } else { 'BROWSER' }
    $st = if ($_.CommandLine -match '--utility-sub-type=(\S+)') { $Matches[1] } else { '' }
    $hooked = Test-Path -LiteralPath "$env:TEMP\NetBindPro-chrome[$($_.ProcessId)].log"
    "{0,6} parent={1,6} {2,-16} {3,-32} hooked={4}" -f $_.ProcessId, $_.ParentProcessId, $t, $st, $hooked
}

foreach ($u in $Urls) {
    try { & "$PSScriptRoot\cdp-eval.ps1" -Url $u } catch { "CDP error for ${u}: $($_.Exception.Message)" }
}

"--- browser + network-service sockets"
Get-TestChrome | Where-Object { $_.CommandLine -like '*NetworkService*' -or $_.CommandLine -notlike '*--type=*' } | ForEach-Object {
    Get-NetTCPConnection -OwningProcess $_.ProcessId -ErrorAction SilentlyContinue |
        Where-Object RemoteAddress -notin '0.0.0.0', '127.0.0.1', '::' |
        ForEach-Object { "{0} -> {1}:{2} {3}" -f $_.LocalAddress, $_.RemoteAddress, $_.RemotePort, $_.State }
}

"--- hook logs"
Get-ChildItem "$env:TEMP\NetBindPro-chrome*.log" -ErrorAction SilentlyContinue | ForEach-Object {
    "== $($_.Name)"
    Get-Content -LiteralPath $_.FullName |
        Select-String -Pattern 'installed|EnsureBound|BLOCK|fail|CreateProcess|skip|bind' |
        Select-Object -Last 10 | ForEach-Object { $_.Line }
}

if (-not $KeepOpen) {
    Get-TestChrome | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
}
