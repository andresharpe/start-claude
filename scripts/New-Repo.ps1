#Requires -Version 7
<#
.SYNOPSIS
    Creates a new private GitHub repository under ~/repos and opens a Claude Code
    session in it.

.DESCRIPTION
    Runs the whole bootstrap in one pass:

      1. Creates ~/repos/<Name> with a README and a general .gitignore.
      2. Initialises git on main and makes the first commit.
      3. Creates a private GitHub repository and pushes to it.
      4. Opens a visible pwsh window in the new folder running
         claude --dangerously-skip-permissions.

    The script stops at the first failure and leaves whatever it has already
    created in place, so you can see how far it got and fix the cause by hand.

.PARAMETER Name
    The repository name. Also the folder name under ~/repos and the name of the
    GitHub repository. Letters, digits, dots, hyphens, and underscores only.

.PARAMETER Description
    One line describing what the project is. It becomes the GitHub repository
    description and the opening line of the README.

.EXAMPLE
    pwsh -NoProfile -File scripts/New-Repo.ps1 -Name poppy-cms -Description 'Poppy is a hyper fast headless CMS'
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Name,

    [string]$Description
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# git and gh signal failure through exit codes rather than exceptions, so every
# call goes through here. Without this a failed push would be invisible and the
# script would happily report success.
function Invoke-Native {
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(Mandatory)][string[]]$ArgumentList,
        [Parameter(Mandatory)][string]$Activity
    )

    $output = & $FilePath @ArgumentList 2>&1
    if ($LASTEXITCODE -ne 0) {
        $detail = ($output | Out-String).Trim()
        throw "$Activity failed (exit $LASTEXITCODE): $detail"
    }
    return $output
}

if ($Name -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]*$') {
    throw "Repository name '$Name' is not valid. Use letters, digits, dots, hyphens, and underscores, starting with a letter or digit."
}

if ([string]::IsNullOrWhiteSpace($Description)) {
    $Description = "$Name is a new project."
}

$reposRoot = Join-Path $HOME 'repos'
$repoPath = Join-Path $reposRoot $Name

if (-not (Test-Path -LiteralPath $reposRoot)) {
    throw "Repository root '$reposRoot' does not exist."
}

if (Test-Path -LiteralPath $repoPath) {
    throw "'$repoPath' already exists. Pick a different name or remove the folder first."
}

foreach ($tool in @('git', 'gh')) {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
        throw "'$tool' is not on PATH. Install it before running this script."
    }
}

# Check credentials up front. Discovering a broken login after the local repo
# and first commit already exist means cleaning up by hand.
& gh auth status *> $null
if ($LASTEXITCODE -ne 0) {
    throw 'Not logged in to GitHub. Run: gh auth login'
}

$account = (Invoke-Native -FilePath 'gh' -ArgumentList @('api', 'user', '--jq', '.login') -Activity 'Reading the GitHub account').Trim()

& gh repo view "$account/$Name" *> $null
if ($LASTEXITCODE -eq 0) {
    throw "GitHub repository $account/$Name already exists."
}

Write-Host "Creating $repoPath"
New-Item -ItemType Directory -Path $repoPath | Out-Null

$readme = @"
# $Name

$Description

## Status

The repository is new and holds no implementation yet. The stack and structure
are still open.

## Getting started

Nothing to run yet. This section will describe how to start $Name locally once
the first code lands.
"@

$gitignore = @'
# Dependencies
node_modules/
vendor/

# Build output
dist/
build/
out/
target/
bin/
obj/

# Environment and secrets
.env
.env.*
!.env.example
*.pem
*.key

# Logs
*.log
npm-debug.log*
yarn-error.log*

# Editor and OS
.vscode/
.idea/
.DS_Store
Thumbs.db

# Local scratch
scratch/
tmp/
'@

Set-Content -LiteralPath (Join-Path $repoPath 'README.md') -Value $readme -Encoding utf8NoBOM
Set-Content -LiteralPath (Join-Path $repoPath '.gitignore') -Value $gitignore -Encoding utf8NoBOM

Push-Location -LiteralPath $repoPath
try {
    Write-Host 'Initialising git and making the first commit'
    Invoke-Native -FilePath 'git' -ArgumentList @('init', '-b', 'main', '--quiet') -Activity 'git init' | Out-Null
    Invoke-Native -FilePath 'git' -ArgumentList @('add', '-A') -Activity 'git add' | Out-Null

    $commitMessage = @"
Initial commit: README and gitignore

$Description

The repository starts with a README describing what it is and a gitignore
covering the usual build, dependency, and secret files.
"@
    Invoke-Native -FilePath 'git' -ArgumentList @('commit', '--quiet', '-m', $commitMessage) -Activity 'git commit' | Out-Null

    Write-Host "Creating the private GitHub repository $account/$Name and pushing"
    Invoke-Native -FilePath 'gh' -ArgumentList @(
        'repo', 'create', $Name,
        '--private',
        '--source', '.',
        '--remote', 'origin',
        '--push',
        '--description', $Description
    ) -Activity 'gh repo create' | Out-Null
}
finally {
    Pop-Location
}

$repoUrl = "https://github.com/$account/$Name"

Write-Host 'Opening a new Claude Code window'
$session = & (Join-Path $PSScriptRoot 'Start-Session.ps1') -Name $Name

Write-Host ''
Write-Host "Local path:  $repoPath"
Write-Host "Remote:      $repoUrl (private)"
Write-Host "New window:  pwsh PID $($session.ProcessId)"
