$ErrorActionPreference = 'Stop'
$exe = 'C:\Users\harker\AppData\Local\Temp\cr-cal-dist\CursorRemain.exe'
$out = 'C:\Users\harker\AppData\Local\Temp\cr-cal-shots'
$png = Join-Path $out 'settings-borders.png'
New-Item -ItemType Directory -Force -Path $out | Out-Null
Get-Process CursorRemain -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 400
if (-not (Test-Path $exe)) { throw "missing $exe" }
Start-Process $exe -ArgumentList '--settings'

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class WinShot4 {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);
    [DllImport("user32.dll")] public static extern bool SetFocus(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    public struct RECT { public int L, T, R, B; }
}
"@

$deadline = (Get-Date).AddSeconds(12)
$win = $null
while ((Get-Date) -lt $deadline) {
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty,
        (Get-Process CursorRemain -ErrorAction SilentlyContinue | Select-Object -First 1).Id
    )
    if ($cond) {
        $all = $root.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)
        foreach ($el in $all) {
            $name = $el.Current.Name
            if ($name -and ($name -match '设置' -or $name -match 'Settings' -or $name -match '余量')) {
                $win = $el
                break
            }
        }
        if (-not $win -and $all.Count -gt 0) { $win = $all[0] }
    }
    if ($win) { break }
    Start-Sleep -Milliseconds 250
}
if (-not $win) { throw 'settings window not found' }

$hwnd = [IntPtr]$win.Current.NativeWindowHandle
[void][WinShot4]::ShowWindow($hwnd, 9)
[void][WinShot4]::SetForegroundWindow($hwnd)
Start-Sleep -Milliseconds 300

$comboCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::ComboBox
)
$combo = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $comboCond)
if ($combo) {
    $combo.SetFocus()
    Start-Sleep -Milliseconds 250
}

$r = New-Object WinShot4+RECT
[void][WinShot4]::GetWindowRect($hwnd, [ref]$r)
$w = [Math]::Max(1, $r.R - $r.L)
$hh = [Math]::Max(1, $r.B - $r.T)
$bmp = New-Object System.Drawing.Bitmap $w, $hh
$g = [System.Drawing.Graphics]::FromImage($bmp)
[void][WinShot4]::PrintWindow($hwnd, $g.GetHdc(), 2)
$g.ReleaseHdc()
$bmp.Save($png, [System.Drawing.Imaging.ImageFormat]::Png)
Copy-Item $png 'X:\Documents\Git\cursorremain\.tmp-settings-borders.png' -Force
$g.Dispose(); $bmp.Dispose()
Write-Output ("SAVED {0}x{1} title={1}" -f $w, $hh, $win.Current.Name)
Write-Output ("COMBO_FOCUSED={0}" -f [bool]$combo)
