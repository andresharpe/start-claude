#requires -Version 7.0
#requires -RunAsAdministrator
<#
.SYNOPSIS
  Install the StartClaude watchdog Windows Service plus its companion
  Scheduled Task that opens a visible pwsh+claude window in the user's
  interactive session.

.PARAMETER ServiceUser
  Identity used as the *Scheduled Task* principal - i.e. the user whose
  interactive session will host the visible pwsh+claude window. Defaults
  to the current interactive user.

.PARAMETER RunServiceAsUser
  When set, the Windows Service is also configured to run as $ServiceUser
  and you will be prompted for the account password. Otherwise the service
  runs as LocalSystem (recommended - no credentials to store, and the
  Scheduled Task still spawns claude in the user's session).

.PARAMETER InstallDir
  Where to publish the service binaries.

.PARAMETER RepoRoot
  Repository root - used as the working directory of the launched claude.

.PARAMETER ClaudeExecutablePath
  Absolute path to the claude.exe the watchdog keeps alive. Defaults to
  '<ServiceUser profile>\.local\bin\claude.exe'. Written into the deployed
  appsettings.json so the service uses it even when it runs as LocalSystem.

.PARAMETER HttpPort
  TCP port for the HTTP control API and dashboard. When omitted, the port
  already configured in the service's appsettings.json is used as the single
  source of truth. When supplied, it is written back into appsettings.json so
  the service binding and the firewall rule stay in sync.
#>
[CmdletBinding()]
param(
    [string]$ServiceUser = "$env:USERDOMAIN\$env:USERNAME",
    [switch]$RunServiceAsUser,
    [string]$InstallDir  = 'C:\Program Files\StartClaude',
    [string]$RepoRoot    = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [string]$ClaudeExecutablePath,
    [int]$HttpPort
)

$ErrorActionPreference = 'Stop'
$ServiceName     = 'StartClaudeService'
$TaskName        = 'StartClaudeLauncher'
$ScreenshotTask  = 'StartClaudeScreenshot'
$ScreenshotDir   = Join-Path $env:ProgramData 'StartClaude\screenshots'
$FirewallRule    = 'StartClaudeService HTTP API'
$ProjectPath     = Join-Path $RepoRoot 'src\StartClaude.Service\StartClaude.Service.csproj'
$ScreenshotScript = Join-Path $RepoRoot 'scripts\Take-Screenshots.ps1'

# Resolve a Windows account (DOMAIN\user) to its profile directory. Used to
# locate the interactive user's claude.exe without hardcoding a username.
function Resolve-UserHome {
    param([string]$Account)
    try {
        $sid = (New-Object System.Security.Principal.NTAccount($Account)).Translate(
            [System.Security.Principal.SecurityIdentifier]).Value
        $key = "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\$sid"
        $pip = (Get-ItemProperty -Path $key -ErrorAction Stop).ProfileImagePath
        if ($pip) { return [Environment]::ExpandEnvironmentVariables($pip) }
    } catch { }
    if ($Account -ieq "$env:USERDOMAIN\$env:USERNAME" -or $Account -ieq $env:USERNAME) {
        return $env:USERPROFILE
    }
    return (Join-Path $env:SystemDrive ('Users\' + ($Account.Split('\')[-1])))
}

Write-Host "==> Installing StartClaude" -ForegroundColor Cyan
Write-Host "    RepoRoot:        $RepoRoot"
Write-Host "    InstallDir:      $InstallDir"
Write-Host "    ServiceUser:     $ServiceUser (used as Scheduled Task principal)"
Write-Host "    ServiceIdentity: $(if ($RunServiceAsUser) { $ServiceUser } else { 'LocalSystem' })"

# 0. Stop any existing service so we can overwrite locked DLLs.
if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    Write-Host "==> Stopping existing service '$ServiceName'" -ForegroundColor Cyan
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    # Wait until the process actually exits so file locks are released.
    for ($i = 0; $i -lt 20; $i++) {
        $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
        if (-not $svc -or $svc.Status -eq 'Stopped') { break }
        Start-Sleep -Milliseconds 500
    }
}

# 1. Publish.
Write-Host "==> Publishing service" -ForegroundColor Cyan
if (Test-Path $InstallDir) { Remove-Item -Recurse -Force $InstallDir }
dotnet publish $ProjectPath -c Release -r win-x64 --self-contained false -o $InstallDir | Out-Host
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

$ServiceExe = Join-Path $InstallDir 'StartClaude.Service.exe'
if (-not (Test-Path $ServiceExe)) { throw "Published exe not found at $ServiceExe" }

# 1b. Write machine-specific settings into the deployed appsettings.json. This
#     gives the service concrete paths even when it runs as LocalSystem, and
#     makes appsettings.json the single source of truth for the HTTP port so the
#     firewall rule below cannot drift from what the service binds.
Write-Host "==> Configuring deployed appsettings.json" -ForegroundColor Cyan
$deployedSettings = Join-Path $InstallDir 'appsettings.json'
$cfg = Get-Content $deployedSettings -Raw | ConvertFrom-Json -Depth 32

if (-not $ClaudeExecutablePath) {
    $userHome = Resolve-UserHome $ServiceUser
    $ClaudeExecutablePath = Join-Path $userHome '.local\bin\claude.exe'
}
$cfg.Watchdog.ClaudeExecutablePath = $ClaudeExecutablePath
$cfg.Logging.FilePath = Join-Path $RepoRoot 'logs\start-claude-.log'

if ($PSBoundParameters.ContainsKey('HttpPort')) {
    $cfg.Http.Port = $HttpPort
} else {
    $HttpPort = [int]$cfg.Http.Port
}

$cfg | ConvertTo-Json -Depth 32 | Set-Content -Path $deployedSettings -Encoding UTF8
Write-Host "    ClaudeExecutablePath: $ClaudeExecutablePath"
Write-Host "    Log file:             $($cfg.Logging.FilePath)"
Write-Host "    HTTP port:            $HttpPort"
if (-not (Test-Path $ClaudeExecutablePath)) {
    Write-Warning "claude.exe not found at $ClaudeExecutablePath - the watchdog will keep trying to launch it. Pass -ClaudeExecutablePath to override."
}

# 2. Register / refresh the Scheduled Task launcher.
Write-Host "==> Registering Scheduled Task '$TaskName'" -ForegroundColor Cyan
if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) {
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
}

$pwshPath = (Get-Command pwsh.exe -ErrorAction SilentlyContinue).Source
if (-not $pwshPath) { $pwshPath = 'pwsh.exe' }
$launchCommand = "Set-Location '$RepoRoot'; claude --dangerously-skip-permissions"
$taskAction    = New-ScheduledTaskAction -Execute $pwshPath -Argument "-NoExit -NoProfile -Command `"$launchCommand`""
$taskSettings  = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
                    -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances Parallel `
                    -StartWhenAvailable
$taskPrincipal = New-ScheduledTaskPrincipal -UserId $ServiceUser -LogonType Interactive -RunLevel Limited
$task = New-ScheduledTask -Action $taskAction -Principal $taskPrincipal -Settings $taskSettings `
            -Description 'Launches an interactive pwsh+claude session for the StartClaude watchdog.'
Register-ScheduledTask -TaskName $TaskName -InputObject $task | Out-Null

# 2b. Screenshot task + shared output directory.
Write-Host "==> Preparing screenshot capture infrastructure" -ForegroundColor Cyan
if (-not (Test-Path $ScreenshotDir)) {
    New-Item -ItemType Directory -Path $ScreenshotDir -Force | Out-Null
}
# Grant the built-in Users group full control so any logged-on user can write PNGs there.
try {
    $acl = Get-Acl $ScreenshotDir
    $rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
        'BUILTIN\Users','Modify','ContainerInherit,ObjectInherit','None','Allow')
    $acl.AddAccessRule($rule)
    Set-Acl -Path $ScreenshotDir -AclObject $acl
} catch {
    Write-Warning "Could not adjust ACL on $ScreenshotDir : $($_.Exception.Message)"
}

if (Get-ScheduledTask -TaskName $ScreenshotTask -ErrorAction SilentlyContinue) {
    Unregister-ScheduledTask -TaskName $ScreenshotTask -Confirm:$false
}
$screenshotArg    = "-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$ScreenshotScript`" -OutputDir `"$ScreenshotDir`""
$screenshotAction = New-ScheduledTaskAction -Execute $pwshPath -Argument $screenshotArg
$screenshotSet    = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
                        -ExecutionTimeLimit ([TimeSpan]::FromMinutes(2)) -MultipleInstances IgnoreNew `
                        -StartWhenAvailable
$screenshotPrin   = New-ScheduledTaskPrincipal -UserId $ServiceUser -LogonType Interactive -RunLevel Limited
$screenshotTaskObj = New-ScheduledTask -Action $screenshotAction -Principal $screenshotPrin `
                        -Settings $screenshotSet `
                        -Description 'Captures every connected monitor to PNG for the StartClaude HTTP API.'
Register-ScheduledTask -TaskName $ScreenshotTask -InputObject $screenshotTaskObj | Out-Null

# 3. Create / update the Windows Service.
Write-Host "==> Creating Windows Service '$ServiceName'" -ForegroundColor Cyan
if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    Stop-Service -Name $ServiceName -ErrorAction SilentlyContinue
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

if ($RunServiceAsUser) {
    $cred = Get-Credential -UserName $ServiceUser -Message "Password for service account '$ServiceUser'"
    New-Service -Name $ServiceName -BinaryPathName "`"$ServiceExe`"" `
                -DisplayName 'StartClaude Watchdog' `
                -Description 'Keeps at least one Claude Code session alive and exposes an HTTP control API.' `
                -StartupType Automatic -Credential $cred | Out-Null
} else {
    New-Service -Name $ServiceName -BinaryPathName "`"$ServiceExe`"" `
                -DisplayName 'StartClaude Watchdog' `
                -Description 'Keeps at least one Claude Code session alive and exposes an HTTP control API.' `
                -StartupType Automatic | Out-Null
}

# 4. Merge global claude settings.
Write-Host "==> Merging global Claude settings" -ForegroundColor Cyan
$globalSettingsDir = Join-Path $env:USERPROFILE '.claude'
$globalSettings    = Join-Path $globalSettingsDir 'settings.json'
$fragmentPath      = Join-Path $RepoRoot 'config\global-claude-settings.json'
if (-not (Test-Path $globalSettingsDir)) {
    New-Item -ItemType Directory -Path $globalSettingsDir -Force | Out-Null
}
$fragment = Get-Content $fragmentPath -Raw | ConvertFrom-Json -Depth 32
if (Test-Path $globalSettings) {
    Copy-Item $globalSettings "$globalSettings.bak.$(Get-Date -Format yyyyMMddHHmmss)" -Force
    $existing = Get-Content $globalSettings -Raw | ConvertFrom-Json -Depth 32
} else {
    $existing = [pscustomobject]@{}
}
if (-not $existing.PSObject.Properties.Match('env').Count) {
    $existing | Add-Member -NotePropertyName env -NotePropertyValue ([pscustomobject]@{})
}
foreach ($prop in $fragment.env.PSObject.Properties) {
    if ($existing.env.PSObject.Properties.Match($prop.Name).Count) {
        $existing.env.($prop.Name) = $prop.Value
    } else {
        $existing.env | Add-Member -NotePropertyName $prop.Name -NotePropertyValue $prop.Value
    }
}
$existing | ConvertTo-Json -Depth 32 | Set-Content -Path $globalSettings -Encoding UTF8

# 5. Firewall rule (Private + Domain profiles - Tailscale typically maps to Private).
Write-Host "==> Adding firewall rule '$FirewallRule' for TCP $HttpPort" -ForegroundColor Cyan
if (Get-NetFirewallRule -DisplayName $FirewallRule -ErrorAction SilentlyContinue) {
    Remove-NetFirewallRule -DisplayName $FirewallRule
}
New-NetFirewallRule -DisplayName $FirewallRule -Direction Inbound -Action Allow `
    -Protocol TCP -LocalPort $HttpPort -Profile Private,Domain | Out-Null

# 6. Start the service.
Write-Host "==> Starting service" -ForegroundColor Cyan
Start-Service -Name $ServiceName

Start-Sleep -Seconds 2
$svc = Get-Service -Name $ServiceName
Write-Host "==> Done. Service status: $($svc.Status)" -ForegroundColor Green
Write-Host "    Status URL:   http://127.0.0.1:$HttpPort/status"
Write-Host "    Logs:         $RepoRoot\logs\start-claude-*.log"
