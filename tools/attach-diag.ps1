param([int]$Pid2, [string]$Guid = '{37217669-42DA-4657-A55B-0D995D328250}')
$hook = 'D:\Projeler\Genel\netbindpro\src\NetBindPro.Hook\bin\x64\Debug\net48\NetBindPro.Hook.exe'
$o = "$env:LOCALAPPDATA\NetBindPro\test-out\ffattach.txt"
"admin=$(([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) target=$Pid2" | Out-File $o
$psi = New-Object Diagnostics.ProcessStartInfo
$psi.FileName = $hook
$psi.Arguments = "--network $Guid --attach $Pid2 --debug"
$psi.UseShellExecute = $false; $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true
$p = [Diagnostics.Process]::Start($psi)
$out = $p.StandardOutput.ReadToEnd(); $err = $p.StandardError.ReadToEnd()
$p.WaitForExit(20000) | Out-Null
"EXIT=$($p.ExitCode)" | Out-File $o -Append
"STDOUT:`n$out" | Out-File $o -Append
"STDERR:`n$err" | Out-File $o -Append
