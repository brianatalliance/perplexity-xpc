# PerplexityXPC - Windows Integrations

This directory contains scripts and configuration fragments to integrate
PerplexityXPC into your Windows shell and editor toolchain.

---

## Contents

| File | Purpose |
|------|---------|
| `Install-PerplexityXPCProfile.ps1` | Add PerplexityXPC to your PowerShell profile |
| `windows-terminal-settings.jsonc` | Windows Terminal profile and keybinding fragments |
| `Install-TerminalProfile.ps1` | Auto-patch Windows Terminal settings.json |
| `vscode-settings.jsonc` | VS Code settings and keybinding fragments |
| `vscode-tasks.json` | VS Code task definitions for common operations |
| `Install-VSCodeIntegration.ps1` | Auto-merge VS Code keybindings and tasks |

---

## 1. PowerShell Profile Setup

`Install-PerplexityXPCProfile.ps1` appends an integration block to your
PowerShell profile (`$PROFILE.CurrentUserAllHosts`). It does not overwrite
any existing profile content.

### Quick install

```powershell
.\integrations\Install-PerplexityXPCProfile.ps1
```

### Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `-SkipModuleInstall` | switch | Skip copying the module to PSModulePath |
| `-NoAliases` | switch | Do not add aliases or helper functions |
| `-NoPrompt` | switch | Do not add Get-XPCPromptTag function |
| `-Uninstall` | switch | Remove all PerplexityXPC entries from profile |

### What gets added to the profile

- Auto-import of `PerplexityXPC` module if available
- Six aliases (see table below)
- `Ask-LastError` function - analyzes `$Error[0]` with `sonar-pro`
- `Explain-Output` function - accepts pipeline input and explains it
- `Get-XPCPromptTag` function - returns `[XPC]` or `[XPC:OFF]` based on broker status

### Uninstall

```powershell
.\integrations\Install-PerplexityXPCProfile.ps1 -Uninstall
```

---

## 2. Windows Terminal Setup

### Option A - Manual (recommended for first-time users)

1. Open Windows Terminal
2. Press `Ctrl+,` to open Settings
3. Click **Open JSON file** (bottom-left)
4. Refer to `windows-terminal-settings.jsonc` and merge the `profiles.list`
   entry and `actions` entries into your `settings.json`

### Option B - Automatic patch

```powershell
.\integrations\Install-TerminalProfile.ps1
```

The script checks the following locations for `settings.json`:

| Location | Description |
|----------|-------------|
| `%LOCALAPPDATA%\Packages\Microsoft.WindowsTerminal_8wekyb3d8bbwe\LocalState\` | Stable store release |
| `%LOCALAPPDATA%\Packages\Microsoft.WindowsTerminalPreview_8wekyb3d8bbwe\LocalState\` | Preview store release |
| `%LOCALAPPDATA%\Microsoft\Windows Terminal\` | Sideloaded or unpackaged release |

A timestamped backup is created before any changes are made.

### Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `-Uninstall` | switch | Remove PerplexityXPC profile and keybindings |

### Uninstall

```powershell
.\integrations\Install-TerminalProfile.ps1 -Uninstall
```

---

## 3. VS Code Setup

### Option A - Manual

**settings.json** (`Ctrl+,` then click the `{}` icon):
- Refer to `vscode-settings.jsonc` Section 1 and merge the
  `terminal.integrated.env.windows` block.

**keybindings.json** (`Ctrl+K Ctrl+S` then click the `{}` icon):
- Refer to `vscode-settings.jsonc` Section 2 and add the keybinding entries.

**tasks.json** (per-workspace):
- Copy `vscode-tasks.json` to `<your-project>/.vscode/tasks.json`
- Or merge the tasks array into your existing tasks file.

### Option B - Automatic install

```powershell
.\integrations\Install-VSCodeIntegration.ps1
```

This merges keybindings into `%APPDATA%\Code\User\keybindings.json` and
tasks into `%APPDATA%\Code\User\tasks.json`. Existing entries are preserved.

### Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `-Uninstall` | switch | Remove all PerplexityXPC keybindings and tasks |
| `-TasksOnly` | switch | Only install/remove tasks |
| `-KeybindingsOnly` | switch | Only install/remove keybindings |

### Uninstall

```powershell
.\integrations\Install-VSCodeIntegration.ps1 -Uninstall
```

---

## Available Aliases

These aliases are registered by `Install-PerplexityXPCProfile.ps1`.

| Alias | Maps to | Description |
|-------|---------|-------------|
| `pplx` | `Invoke-Perplexity` | Run a Perplexity query |
| `pplxcode` | `Invoke-PerplexityCodeReview` | Code review a file |
| `pplxnet` | `Invoke-PerplexityNetDiag` | Network diagnostics |
| `pplxevt` | `Invoke-PerplexityEventAnalysis` | Analyze event logs |
| `pplxclip` | `Invoke-PerplexityClipboard` | Query clipboard content |
| `pplxerr` | `Ask-LastError` | Explain the last PowerShell error |

### Usage examples

```powershell
# Ask a question
pplx "What is the difference between Get-Item and Get-ChildItem?"

# Review a script
pplxcode -Path .\Deploy.ps1 -Focus review

# Diagnose network
pplxnet -Target api.perplexity.ai

# Query clipboard
pplxclip

# Analyze recent errors
pplxerr

# Pipe output for explanation
Get-Process | Out-String | Explain-Output
```

---

## Keybinding Reference

### Windows Terminal

| Shortcut | Action |
|----------|--------|
| `Ctrl+Shift+Alt+P` | Query clipboard with Perplexity |
| `Ctrl+Shift+Alt+S` | Show XPC broker status |

### VS Code (terminal must be focused)

| Shortcut | Action |
|----------|--------|
| `Ctrl+Shift+Alt+P` | Query clipboard with Perplexity |
| `Ctrl+Shift+Alt+R` | Code review current file |
| `Ctrl+Shift+Alt+D` | Debug assistance for current file |
| `Ctrl+Shift+Alt+S` | Security audit current file |

---

## VS Code Task Reference

Access tasks via `Terminal > Run Task` or `Ctrl+Shift+P > "Run Task"`.

| Task Label | Command Executed |
|------------|-----------------|
| Perplexity: Review Current File | `Invoke-PerplexityCodeReview -Path '${file}' -Focus review` |
| Perplexity: Security Audit Current File | `Invoke-PerplexityCodeReview -Path '${file}' -Focus security` |
| Perplexity: Debug Current File | `Invoke-PerplexityCodeReview -Path '${file}' -Focus debug` |
| Perplexity: Explain Current File | `Invoke-PerplexityCodeReview -Path '${file}' -Focus explain` |
| Perplexity: Analyze Event Logs | `Invoke-PerplexityEventAnalysis -GroupBySource` |
| Perplexity: XPC Broker Status | `Get-XPCStatus` |
| Perplexity: Query Clipboard | `Invoke-PerplexityClipboard` |

---

## Requirements

- PowerShell 5.1 or PowerShell 7+
- PerplexityXPC module installed and on `$env:PSModulePath`
- XPC broker service running (port 47777) for status indicators
- Windows Terminal (for terminal profile and keybindings)
- VS Code (for VS Code integration)
- `PERPLEXITY_API_KEY` environment variable set

---

## Troubleshooting

**Module not found after install**
Run `$env:PSModulePath -split ';'` and confirm the PerplexityXPC folder
is in one of those directories.

**Aliases not available in new sessions**
Reload your profile: `. $PROFILE` or open a new terminal. Verify the
integration block is present with `Get-Content $PROFILE`.

**Windows Terminal settings not updating**
Launch Windows Terminal at least once to generate `settings.json` before
running `Install-TerminalProfile.ps1`.

**VS Code keybindings conflict**
If a shortcut does not fire, open `keybindings.json` and check for duplicate
entries with the same key. VS Code uses the last definition in the file.

**XPC:OFF shows in prompt**
The XPC broker is not running. Start it with `Start-XPCBroker` or check
the service status with `Get-XPCStatus`.
