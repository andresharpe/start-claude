#requires -Version 7.0
#requires -RunAsAdministrator
<#
.SYNOPSIS
  Remove the StartClaude Windows Service, the launcher Scheduled Task, and
  the firewall rule. Leaves the global ~/.claude/settings.json in place
  (a timestamped backup was created at install time if you want to revert).

.PARAMETER InstallDir
  Location where the service binaries were published.
#>
[CmdletBinding()]
param(
    [string]$InstallDir = 'C:\Program Files\StartClaude'
)

$ErrorActionPreference = 'Stop'
$ServiceName     = 'StartClaudeService'
$TaskName        = 'StartClaudeLauncher'
$ScreenshotTask  = 'StartClaudeScreenshot'
$FirewallRule    = 'StartClaudeService HTTP API'

Write-Host "==> Stopping service '$ServiceName' (if present)" -ForegroundColor Cyan
if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    Stop-Service -Name $ServiceName -ErrorAction SilentlyContinue
    sc.exe delete $ServiceName | Out-Null
}

Write-Host "==> Removing Scheduled Task '$TaskName' (if present)" -ForegroundColor Cyan
if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) {
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
}
Write-Host "==> Removing Scheduled Task '$ScreenshotTask' (if present)" -ForegroundColor Cyan
if (Get-ScheduledTask -TaskName $ScreenshotTask -ErrorAction SilentlyContinue) {
    Unregister-ScheduledTask -TaskName $ScreenshotTask -Confirm:$false
}

Write-Host "==> Removing firewall rule '$FirewallRule' (if present)" -ForegroundColor Cyan
if (Get-NetFirewallRule -DisplayName $FirewallRule -ErrorAction SilentlyContinue) {
    Remove-NetFirewallRule -DisplayName $FirewallRule
}

Write-Host "==> Removing install directory '$InstallDir' (if present)" -ForegroundColor Cyan
if (Test-Path $InstallDir) {
    Remove-Item -Recurse -Force $InstallDir
}

Write-Host "==> Done. Global ~/.claude/settings.json was left in place." -ForegroundColor Green
Write-Host "    To revert the auto-compact override, restore from the .bak.* file in ~/.claude/."
