# Captures the README screenshots (English UI, demo rules). Must run elevated (the app drives WinDivert).
# It backs up %APPDATA%\TrafficLimiter\settings.json, writes demo settings, drives the UI via UI Automation,
# saves window-only PNGs to docs\screenshots and restores your settings afterwards.
#   powershell -ExecutionPolicy Bypass -File tools\capture-screenshots.ps1 [-Exe path\to\TrafficLimiter.exe]
param(
    [string]$Exe = (Join-Path (Split-Path -Parent $PSScriptRoot) 'publish\TrafficLimiter.exe'),
    [string]$Log = $null
)
$ErrorActionPreference = 'Stop'
function Log($m) { if ($Log) { Add-Content -Path $Log -Value ("{0:HH:mm:ss} {1}" -f (Get-Date), $m) } }

Add-Type -AssemblyName System.Windows.Forms, System.Drawing, UIAutomationClient, UIAutomationTypes
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class Win {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, int dx, int dy, uint d, UIntPtr e);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h, int x, int y, int w, int hh, bool r);
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out RECT r, int s);
  public struct RECT { public int L, T, R, B; }
}
"@
[Win]::SetProcessDPIAware() | Out-Null

$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'docs\screenshots'
New-Item -ItemType Directory -Force -Path $out | Out-Null
$cfgDir = Join-Path $env:APPDATA 'TrafficLimiter'
$cfg = Join-Path $cfgDir 'settings.json'
$backup = Join-Path $env:TEMP 'TrafficLimiter.settings.backup.json'
$hadCfg = Test-Path $cfg

function Stop-App { Get-Process TrafficLimiter -ErrorAction SilentlyContinue | Stop-Process -Force; Start-Sleep 1 }

function Bounds($h) {
    $r = New-Object Win+RECT
    [Win]::DwmGetWindowAttribute($h, 9, [ref]$r, [Runtime.InteropServices.Marshal]::SizeOf($r)) | Out-Null  # DWMWA_EXTENDED_FRAME_BOUNDS
    return $r
}
function Shot($h, $name, [int]$extraBottom = 0) {
    [Win]::SetCursorPos(5, 5); Start-Sleep -Milliseconds 700   # park the mouse away from the window
    $r = Bounds $h; $c = 2   # crop 2px: rounded-corner bleed of the desktop behind the window
    $w = $r.R - $r.L - 2 * $c; $hh = $r.B - $r.T - 2 * $c + $extraBottom
    $bmp = New-Object System.Drawing.Bitmap $w, $hh
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.L + $c, $r.T + $c, 0, 0, (New-Object System.Drawing.Size $w, $hh))
    $bmp.Save((Join-Path $out $name), [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose(); Log "saved $name ($w x $hh)"
}
function Find-Element($root, $pattern) {
    $all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($e in $all) { if ($e.Current.Name -like $pattern) { return $e } }
    throw "UI element not found: $pattern"
}
function Find-ByHelp($root, $help) {
    $all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($e in $all) { if ($e.Current.HelpText -eq $help) { return $e } }
    throw "UI element not found (tooltip): $help"
}
function Click($el) {
    $r = $el.Current.BoundingRectangle
    [Win]::SetCursorPos([int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2)); Start-Sleep -Milliseconds 250
    [Win]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero); [Win]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 900
}

try {
    Stop-App
    New-Item -ItemType Directory -Force -Path $cfgDir | Out-Null
    if ($hadCfg) { Copy-Item $cfg $backup -Force }

    # Demo settings: harmless example apps only (never block anything the system or this machine relies on)
    $edge  = @{ AppPath = 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'; DownLimit = 0; UpLimit = 524288; Block = $false; Priority = 0; Enabled = $true }
    $ssh   = @{ AppPath = 'C:\Windows\System32\OpenSSH\ssh.exe'; DownLimit = 0; UpLimit = 0; Block = $true; Priority = 1; Enabled = $true }
    $curl  = @{ AppPath = 'C:\Windows\System32\curl.exe'; DownLimit = 2097152; UpLimit = 0; Block = $false; Priority = 2; Enabled = $true }
    $demo = @{
        Rules = @($edge, $ssh, $curl); GlobalDownLimit = 0; GlobalUpLimit = 0
        Language = 'en'; StartMinimized = $false; Autostart = $false; EngineEnabled = $true; ShowChart = $true
        Profiles = @(
            @{ Name = 'Gaming'; Rules = @($edge, $curl); GlobalDownLimit = 0; GlobalUpLimit = 0 },
            @{ Name = 'Work';   Rules = @($ssh);        GlobalDownLimit = 0; GlobalUpLimit = 0 })
        ActiveProfile = 'Gaming'
    }
    $demo | ConvertTo-Json -Depth 8 | Set-Content -Path $cfg -Encoding UTF8
    Log 'demo settings written'

    Start-Process $Exe
    Start-Sleep 8
    $dl = Start-Process curl.exe -PassThru -WindowStyle Hidden -ArgumentList '-s', '-o', 'NUL', '--max-time', '70', 'https://proof.ovh.net/files/1Gb.dat'
    Start-Sleep 12
    $proc = Get-Process TrafficLimiter
    $h = $proc.MainWindowHandle
    [Win]::ShowWindow($h, 9) | Out-Null
    [Win]::MoveWindow($h, 40, 10, 1650, 1200, $true) | Out-Null
    [Win]::SetForegroundWindow($h) | Out-Null
    Start-Sleep 3
    $win = [System.Windows.Automation.AutomationElement]::FromHandle($h)

    # 1) Overview: computer row selected, whole-machine chart
    Shot $h 'overview.png'

    # 2) Select an app: rules panel + per-app chart
    Click (Find-Element $win 'The curl*')
    Shot $h 'app-rules.png'

    # 3) Settings dialog
    Click (Find-ByHelp $win 'Settings')
    Start-Sleep 1
    $dh = [Win]::FindWindow([NullString]::Value, 'Settings')
    if ($dh -ne [IntPtr]::Zero) {
        Shot $dh 'settings.png'
        Click (Find-Element ([System.Windows.Automation.AutomationElement]::FromHandle($dh)) 'Close')
    } else { Log 'settings dialog not found' }

    # 4) Profile menu
    Start-Sleep 1
    Click (Find-Element $win 'Profile:*')
    Start-Sleep 1
    Shot $h 'profiles.png'
    [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
    Log 'done'
}
finally {
    if ($dl) { Stop-Process -Id $dl.Id -Force -ErrorAction SilentlyContinue }
    Stop-App
    if ($hadCfg) { Copy-Item $backup $cfg -Force; Remove-Item $backup -Force -ErrorAction SilentlyContinue }
    else { Remove-Item $cfg -Force -ErrorAction SilentlyContinue }
    Log 'settings restored'
}
