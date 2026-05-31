# start-claude

This repository is the always-on Claude Code session entry point on this machine. A Windows Service (`StartClaude.Service`) watches for the absence of a `claude.exe` process from `%USERPROFILE%\.local\bin\claude.exe` (configured via `Watchdog:ClaudeExecutablePath`) and re-launches a visible pwsh window in this folder running `claude --dangerously-skip-permissions`.

The project-level `.claude/settings.json` sets the auto-compaction threshold to 50% via `CLAUDE_AUTOCOMPACT_PCT_OVERRIDE`. The same value is also merged into `~/.claude/settings.json` at install time.

If you are reading this inside a claude session, you are likely inside the watchdog-spawned session. Treat unexpected exits as something the watchdog will recover from within ~60 seconds.
