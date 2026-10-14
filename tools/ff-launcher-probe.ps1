# Checks whether MOZ_LAUNCHER_PROCESS=0 makes firefox.exe run as a single main process
# (no launcher -> main handoff), which is what lets injection stick.
param([string]$Mode = 'default')   # 'default' | 'env0' | 'pref'
$ff   = 'C:\Program Files\Mozilla Firefox\firefox.exe'
$prof = Join-Path $env:LOCALAPPDATA ("NetBindPro\ff-probe-" + $Mode)
New-Item -ItemType Directory -Force $prof | Out-Null

if ($Mode -eq 'pref') {
    # Pre-seed the profile pref that disables the launcher process.
    Set-Content -LiteralPath (Join-Path $prof 'user.js') -Value 'user_pref("browser.launcherProcess.enabled", false);' -Encoding ascii
}
if ($Mode -eq 'env0') { $env:MOZ_LAUNCHER_PROCESS = '0' } else { Remove-Item Env:MOZ_LAUNCHER_PROCESS -ErrorAction SilentlyContinue }

Get-CimInstance Win32_Process -Filter "Name='firefox.exe'" | Where-Object CommandLine -like "*$([IO.Path]::GetFileName($prof))*" | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
Start-Sleep 1

$p = Start-Process $ff -ArgumentList "-no-remote -profile `"$prof`" -headless about:blank" -PassThru
Start-Sleep 6
"launched pid=$($p.Id) (alive=$(-not $p.HasExited))"
$all = Get-CimInstance Win32_Process -Filter "Name='firefox.exe'" | Where-Object CommandLine -like "*$([IO.Path]::GetFileName($prof))*"
"total firefox procs for this profile: $(@($all).Count)"
$all | ForEach-Object {
    $t = if ($_.CommandLine -match '-contentproc') { 'child' } else { 'MAIN' }
    "{0,6} parent={1,6} {2}  launchedProc={3}" -f $_.ProcessId, $_.ParentProcessId, $t, ($_.ProcessId -eq $p.Id)
}
$all | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
