# Replicates the INSTALLED app's exact launch path: an elevated process de-elevates the
# Hook via CreateProcessWithTokenW(shell token)+CreateEnvironmentBlock, launches Chrome
# bound to the tunnel, then reports whether Chrome stayed and bound (tunnel IP) or
# de-elevated/leaked. Uses a throwaway Chrome profile so the user's Chrome is untouched.
$o = "$env:LOCALAPPDATA\NetBindPro\test-out\apppath.txt"
"admin=$(([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator))" | Out-File $o
Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices;
public static class DeElevA {
[StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] public struct SI { public int cb; public string r1,r2,r3; public int a,b,c,d,e,f,g,h; public short i,j; public IntPtr k,l,m,n; }
[StructLayout(LayoutKind.Sequential)] public struct PI { public IntPtr p,t; public int pid,tid; }
[DllImport("user32.dll")] static extern IntPtr GetShellWindow();
[DllImport("user32.dll")] static extern int GetWindowThreadProcessId(IntPtr h, out int pid);
[DllImport("kernel32.dll", SetLastError=true)] static extern IntPtr OpenProcess(int a, bool i, int p);
[DllImport("advapi32.dll", SetLastError=true)] static extern bool OpenProcessToken(IntPtr h, int a, out IntPtr t);
[DllImport("advapi32.dll", SetLastError=true)] static extern bool DuplicateTokenEx(IntPtr t, int a, IntPtr x, int imp, int ty, out IntPtr d);
[DllImport("userenv.dll", SetLastError=true)] static extern bool CreateEnvironmentBlock(out IntPtr e, IntPtr t, bool i);
[DllImport("advapi32.dll", SetLastError=true, CharSet=CharSet.Unicode)] static extern bool CreateProcessWithTokenW(IntPtr t, int lf, string app, string cmd, int cf, IntPtr env, string dir, ref SI si, out PI pi);
public static string Launch(string app, string cmd) {
  IntPtr sh=GetShellWindow(); if(sh==IntPtr.Zero) return "NO_SHELL";
  int pid; GetWindowThreadProcessId(sh,out pid);
  IntPtr hp=OpenProcess(0x0400,false,pid); if(hp==IntPtr.Zero) return "OpenProcess "+Marshal.GetLastWin32Error();
  IntPtr tok; if(!OpenProcessToken(hp,0x000A,out tok)) return "OpenProcessToken "+Marshal.GetLastWin32Error();
  IntPtr dup; if(!DuplicateTokenEx(tok,0x02000000,IntPtr.Zero,2,1,out dup)) return "DuplicateTokenEx "+Marshal.GetLastWin32Error();
  IntPtr env; int cf=0x08000000; if(CreateEnvironmentBlock(out env,dup,false)) cf|=0x00000400; else env=IntPtr.Zero;
  var si=new SI(); si.cb=Marshal.SizeOf(typeof(SI)); PI p;
  if(!CreateProcessWithTokenW(dup,0,app,"\""+app+"\" "+cmd,cf,env,System.IO.Path.GetDirectoryName(app),ref si,out p)) return "CPWT "+Marshal.GetLastWin32Error();
  return "OK pid="+p.pid;
}
}
'@
$hook = (Join-Path ${env:ProgramFiles} 'NetBind Pro\hook\NetBindPro.Hook.exe')
$cp = "$env:LOCALAPPDATA\NetBindPro\chrome-apppath"
Get-CimInstance Win32_Process -Filter "Name='chrome.exe'" | Where-Object CommandLine -like '*chrome-apppath*' | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
Get-ChildItem "$env:TEMP\NetBindPro-chrome*.log" -ErrorAction SilentlyContinue | ForEach-Object { [IO.File]::Delete($_.FullName) }
Start-Sleep 1
$cmd = "--network {B8B23E36-F7FF-505B-1FD7-7F80753FCA2F} --execute `"C:\Program Files\Google\Chrome\Application\chrome.exe`" --args `"--user-data-dir=$cp --no-first-run --no-default-browser-check --new-window https://api.ipify.org`" --delay 0 --debug"
"Launch result: $([DeElevA]::Launch($hook, $cmd))" | Out-File $o -Append
Start-Sleep 8
$procs = Get-CimInstance Win32_Process -Filter "Name='chrome.exe'" | Where-Object CommandLine -like '*chrome-apppath*'
"chrome procs: $(@($procs).Count)" | Out-File $o -Append
$procs | ForEach-Object {
  $t = if ($_.CommandLine -match '--type=(\S+)') { $Matches[1] } else { 'BROWSER' }
  $de = $_.CommandLine -like '*--do-not-de-elevate*'
  $hk = Test-Path -LiteralPath "$env:TEMP\NetBindPro-chrome[$($_.ProcessId)].log"
  "  {0,6} {1,-16} hooked={2} deElevFlag={3}" -f $_.ProcessId, $t, $hk, $de | Out-File $o -Append
}
$ns = $procs | Where-Object CommandLine -like '*NetworkService*'
if ($ns) { Get-NetTCPConnection -OwningProcess $ns.ProcessId -State Established -ErrorAction SilentlyContinue | Where-Object RemoteAddress -notin '127.0.0.1','::1' | Select-Object -First 3 | ForEach-Object { "  netsvc $($_.LocalAddress) -> $($_.RemoteAddress):$($_.RemotePort)" | Out-File $o -Append } }
"hook logs:" | Out-File $o -Append
Get-ChildItem "$env:TEMP\NetBindPro-chrome*.log" -ErrorAction SilentlyContinue | ForEach-Object { Get-Content -LiteralPath $_.FullName | Select-String 'installed|BindV4|ChildInject' | Select-Object -Last 3 | ForEach-Object { "  $($_.Line)" | Out-File $o -Append } }
$procs | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
