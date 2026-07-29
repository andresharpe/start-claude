---
name: new-repo
description: Creates a new project from nothing - a folder under ~/repos with a README and gitignore, a first commit, a private GitHub repository, and a fresh Claude Code session opened in a new pwsh window. Use this whenever the user wants to start a new project, spin up a repo, bootstrap a codebase, or begin work on an idea that has no folder yet. Trigger on phrasings like "create a repo for X", "start a new project called X", "set up X as a new codebase", "make me a repo and open claude in it", or "I want to build X" where X does not exist yet. Use it even when the user does not say the word "repo", and even when they only describe the idea and ask to get started.
---

# New repo

One command takes a project from an idea to a running Claude Code session.
`scripts/New-Repo.ps1` creates the folder under `~/repos`, writes a README and a
general gitignore, commits, creates the private GitHub repository, pushes, and
opens a new pwsh window running `claude --dangerously-skip-permissions` in it.

## Before running

You need two things from the user, and only two:

- **Name**: the folder and repository name. Letters, digits, dots, hyphens, and
  underscores. If they gave you a project name in prose, convert it to a sensible
  slug and say which one you picked rather than asking.
- **Description**: one line saying what the project is. This lands in the README,
  the GitHub description, and the commit message, so it is worth getting right.
  If the user described the idea in conversation, write the line yourself from
  what they said rather than making them repeat it.

Everything else is fixed on purpose. The repository is always private, always
under `~/repos`, and a window always opens. That is what makes this a single
call instead of a negotiation.

## Running it

```pwsh
pwsh -NoProfile -File scripts/New-Repo.ps1 -Name '<name>' -Description '<one line>'
```

Inline multi-line PowerShell through the Bash tool is unreliable here, which is
why the work lives in the `.ps1` rather than in this file.

## Afterwards

Report the local path, the remote URL, and the fact that a window opened. Then
mention the watchdog interaction, because it surprises people: the service only
relaunches when no `claude.exe` is running anywhere. While the new session is
open, the start-claude session will not be restarted if it exits. The user
should close the new window when finished with it.

Do not start writing code in the new repository from this session. The whole
point is that the new window picks that up with a clean context.

## When it fails

The script stops at the first problem and leaves everything it already created
in place, so the error tells you exactly how far it got. The common causes are a
folder that already exists, a GitHub repository of the same name already there,
or an expired login that needs `gh auth login`. Read the message, fix the actual
cause, and run it again. Do not work around a failure by doing the remaining
steps by hand, because a half-scripted repo is harder to reason about later than
one that failed cleanly.
