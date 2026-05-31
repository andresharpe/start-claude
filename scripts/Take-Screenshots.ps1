<#
.SYNOPSIS
  Captures each connected monitor in the current interactive session to PNG.

.DESCRIPTION
  Designed to be invoked by the StartClaude watchdog via the
  'StartClaudeScreenshot' Scheduled Task, which runs in the user's
  interactive session (Session 0 services can't see the desktop).

.PARAMETER OutputDir
  Directory where the PNGs and metadata.json are written. Defaults to
  C:\ProgramData\StartClaude\screenshots.
#>
[CmdletBinding()]
param(
    [string]$OutputDir = (Join-Path $env:ProgramData 'StartClaude\screenshots')
)

$ErrorActionPreference = 'Stop'

# Belt-and-suspenders: even when launched with -WindowStyle Hidden, pwsh can
# briefly flash a console window. Force-hide ours and wait a beat so the
# desktop has fully redrawn before we capture pixels.
Add-Type -Name SCWin -Namespace SC -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("kernel32.dll")]
public static extern System.IntPtr GetConsoleWindow();
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool ShowWindow(System.IntPtr hWnd, int nCmdShow);
'@
$consoleHandle = [SC.SCWin]::GetConsoleWindow()
if ($consoleHandle -ne [System.IntPtr]::Zero) {
    [SC.SCWin]::ShowWindow($consoleHandle, 0) | Out-Null  # SW_HIDE = 0
}
Start-Sleep -Milliseconds 300

Add-Type -AssemblyName System.Windows.Forms, System.Drawing

# Become DPI-aware BEFORE first touching System.Windows.Forms.Screen,
# otherwise Screen.AllScreens returns DPI-scaled (logical) bounds and
# captures end up smaller than the physical resolution.
Add-Type -Name SCDpi -Namespace SC -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool SetProcessDPIAware();
[System.Runtime.InteropServices.DllImport("shcore.dll")]
public static extern int SetProcessDpiAwareness(int value);
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool SetProcessDpiAwarenessContext(System.IntPtr ctx);
'@
try { [SC.SCDpi]::SetProcessDpiAwarenessContext([System.IntPtr]::new(-4)) | Out-Null } catch {} # PER_MONITOR_AWARE_V2
try { [SC.SCDpi]::SetProcessDpiAwareness(2) | Out-Null } catch {}                                # PER_MONITOR
try { [SC.SCDpi]::SetProcessDPIAware()      | Out-Null } catch {}                                # legacy system aware

New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

$captureTime = (Get-Date).ToUniversalTime().ToString('o')
$screens     = [System.Windows.Forms.Screen]::AllScreens
$index       = 0
$entries     = @()

foreach ($screen in $screens) {
    $bounds = $screen.Bounds
    $bitmap = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
        $outPath = Join-Path $OutputDir ("screen-{0}.png" -f $index)
        $bitmap.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
    } finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }

    $entries += [pscustomobject]@{
        index      = $index
        file       = ("screen-{0}.png" -f $index)
        width      = $bounds.Width
        height     = $bounds.Height
        x          = $bounds.X
        y          = $bounds.Y
        deviceName = $screen.DeviceName
        primary    = $screen.Primary
    }
    $index++
}

$metadata = [pscustomobject]@{
    capturedAtUtc = $captureTime
    screenCount   = $entries.Count
    screens       = $entries
}

# Atomic write: tmp + rename so the C# side never sees a half-written file.
$metaPath = Join-Path $OutputDir 'metadata.json'
$tmpPath  = "$metaPath.tmp"
$metadata | ConvertTo-Json -Depth 5 | Set-Content -Path $tmpPath -Encoding UTF8
Move-Item -Path $tmpPath -Destination $metaPath -Force
