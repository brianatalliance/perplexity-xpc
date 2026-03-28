# Changelog

All notable changes to PerplexityXPC are documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [1.0.0] - 2026-03-28

### Added

- **Windows Service broker** (`PerplexityXPC.Service`) with HTTP and WebSocket endpoints bound exclusively to `127.0.0.1:47777` via Kestrel
- **System tray application** (`PerplexityXPC.Tray`) with `Ctrl+Shift+A` global hotkey, floating query popup, dark/light theme support, and per-MCP-server status indicators
- **Explorer context menu integration** (`PerplexityXPC.ContextMenu`) for right-click analysis of text files and folders via Windows Shell extension
- **MCP server manager** with JSON-RPC 2.0 over stdio, supporting auto-start, auto-restart on crash, and per-server enable/disable
- **Perplexity Sonar API proxy** supporting all four models: `sonar`, `sonar-pro`, `sonar-reasoning-pro`, `sonar-deep-research`
- **SSE streaming proxy** (`POST /perplexity/stream`) with real-time token forwarding to clients
- **WebSocket interface** (`WS /ws`) for persistent streaming connections
- **PowerShell module** with 14 functions across five categories:
  - Core: `Invoke-Perplexity`, `Get-XPCStatus`, `Get-XPCConfig`, `Set-XPCConfig`
  - MCP Management: `Get-McpServer`, `Restart-McpServer`, `Invoke-McpRequest`
  - File Analysis: `Invoke-PerplexityFileAnalysis`, `Invoke-PerplexityFolderAnalysis`
  - Batch Research: `Invoke-PerplexityBatch`, `Invoke-PerplexityReport`
  - IT Integration: `Invoke-PerplexityTicketAnalysis`, `Invoke-PerplexityDeviceAnalysis`, `Invoke-PerplexitySecurityAnalysis`
- **DPAPI-encrypted API key storage** using `ProtectedData.Protect` with machine+user scope - key is never exposed via HTTP
- **Named Pipe IPC** with per-user ACL via `PipeAccessRule` restricting pipe access to the current user SID
- **PowerShell installer** (`Install-PerplexityXPC.ps1`) with parameters for API key, install path, optional components, and quiet mode
- **PowerShell uninstaller** (`Uninstall-PerplexityXPC.ps1`) with `-KeepData` flag to preserve config and logs
- **Build script** (`Build-PerplexityXPC.ps1`) producing self-contained single-file executables for all three components
- **Context menu registrar** (`Register-ContextMenu.ps1`) for re-registration without a full reinstall
- **Windows Firewall inbound block rule** for port 47777 as defense-in-depth against loopback bypass
- **Atera RMM integration** via `Invoke-PerplexityTicketAnalysis` - pipe ticket objects from the Atera module for AI-assisted triage
- **Intune MDM integration** via `Invoke-PerplexityDeviceAnalysis` - pipe `Get-MgDeviceManagementManagedDevice` objects for compliance and security analysis
- **Security analysis workflow** via `Invoke-PerplexitySecurityAnalysis` - analyze CVEs, Defender alerts, and ASR findings with remediation guidance
- **Batch query runner** with progress bar, configurable delay between requests, and CSV/JSON export
- **Auto-report generator** that researches a topic across multiple sub-questions and compiles a structured markdown report with citations
- **File analysis pipeline support** - `Invoke-PerplexityFileAnalysis` accepts `Get-ChildItem` pipeline input via `FullName` alias
- **Folder structure analysis** - `Invoke-PerplexityFolderAnalysis` sends a recursive directory listing (up to 200 items) to Perplexity
- **JSON with comments (JSONC) support** in `appsettings.json` for annotated configuration files
- **Configuration template files** (`appsettings.template.json`, `mcp-servers.template.json`) with full inline documentation
- **Module manifest** (`PerplexityXPC.psd1`) with `FunctionsToExport`, tags, release notes, and compatibility declarations for PS 5.1 and PS 7+
- **Self-contained executables** - no .NET runtime installation required on target machines (runtime is bundled)
- **Structured logging** via Microsoft.Extensions.Logging with configurable log level and file rotation

---

## [Unreleased]

No unreleased changes at this time.
