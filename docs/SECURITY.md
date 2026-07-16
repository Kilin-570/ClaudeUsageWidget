# Security and privacy design

## Data inventory

| Data | Location | Protection | Leaves the PC through |
| --- | --- | --- | --- |
| Claude access/refresh tokens | `%APPDATA%\ClaudeUsageWidget\tokens.dat` | Windows DPAPI, current user | Anthropic OAuth and usage endpoints |
| ChatGPT/Codex tokens | Codex-managed storage | Owned by official Codex | Official Codex process only |
| Widget settings | `%APPDATA%\ClaudeUsageWidget\settings.json` | Normal user file permissions | Never |
| Widget log | `%APPDATA%\ClaudeUsageWidget\log.txt` | Normal user file permissions | Never automatically |

The repository and release package contain no account identifiers, credentials, local settings, or real usage payloads.

## Trust boundaries

### Claude

The widget directly owns Claude OAuth tokens because the existing Claude provider requires them. Tokens are encrypted with DPAPI and are not portable to another Windows user. OAuth state and PKCE verification reduce authorization-code interception risk.

### ChatGPT / Codex

The widget deliberately delegates authentication to OpenAI Codex:

- It does not inspect `~/.codex/auth.json`.
- It does not receive an access token from the app-server API.
- It uses the documented local JSONL protocol and the read-only `account/rateLimits/read` method.
- If Codex is in API-key mode, the widget warns before starting a ChatGPT login that would replace Codex's primary auth mode.

## Process execution safety

- `.exe` launches use `ProcessStartInfo.ArgumentList` with `UseShellExecute=false`.
- `.cmd` and `.bat` paths are resolved to an existing file, stripped of quote characters, quoted, and invoked only with the fixed `app-server --stdio` arguments.
- No usage value or network response is interpolated into a command.
- The child process is terminated when the widget exits.

## Logging

- No telemetry is implemented.
- Codex stdout notifications and stderr diagnostics are discarded rather than copied to the widget log.
- JSON-RPC errors are reduced to the documented numeric code and message.
- Users should still review `log.txt` before attaching it to a public issue because operating-system paths may appear in normal application diagnostics.

## Update boundary

The updater downloads the asset named `ClaudeUsageWidget-win-x64.zip` from this repository's latest GitHub Release. Releases are not currently code-signed, so users who require stronger supply-chain assurance should build from a reviewed commit.

## Reporting a security issue

Open a GitHub issue for non-sensitive problems. Do not paste tokens, `auth.json`, `tokens.dat`, browser cookies, or unredacted private logs into a public issue. For a sensitive report, contact the repository owner privately through their GitHub profile until a dedicated security advisory channel is enabled.
