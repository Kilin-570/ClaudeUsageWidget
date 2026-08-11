# Project Status

Last updated: 2026-08-11

Current release: `v2.1.0`

Public project name: `AI Usage Widget`

Repository: `Kilin-570/AIUsageWidget`

Default branch: `main`

Last verified release commit: `6ed761142761310a736736f52803dedc218aa9c4`

Status: `v2.1.0` released and verified; Issue #12 fix implemented locally

This file is the public handoff point for continuing development on another
computer. It intentionally excludes credentials, account details, local file
paths, logs, and real usage values.

## Completed

- Unified Claude and ChatGPT usage in one Windows WPF widget.
- Added safe provider switching, localization (English and Traditional
  Chinese), dark/light themes, transparency, resize/collapse, tray controls,
  auto refresh, and disconnected-display recovery.
- Added provider-aware, theme-compatible watermark styling with a flat,
  borderless provider selector.
- Integrated ChatGPT through the official local Codex app-server without
  reading or storing Codex credential files.
- Kept Claude OAuth credentials local and protected with Windows DPAPI.
- Added a reliable updater with progress, cancellation during download,
  SHA-256 verification, rollback-safe replacement, and temporary-file cleanup.
- Added single-instance activation, stale-data preservation, redacted
  diagnostics, and clearer auto-start failure handling.
- Published `v2.1.0` with eight README screenshots covering both providers,
  both themes, and both supported languages.
- Renamed the public project and repository to provider-neutral
  `AI Usage Widget` / `AIUsageWidget` under Issue #10.

## Current provider behavior

### Claude

- Displays the limits returned by Claude, currently including `Session` and
  `Weekly (all)`.
- Shows reset timing when the provider returns it.
- Does not assume that a model-specific limit always exists.

### ChatGPT

- Displays the quota windows returned by the local Codex app-server.
- The current public examples show the weekly limit only.
- Does not assume a fixed five-hour limit; available windows may vary by
  account, plan, or service configuration.

## Design decisions

- Provider identity is conveyed by text, color, and a subtle vector watermark;
  the interface does not depend on color alone.
- Watermarks are drawn with cached WPF vector elements rather than large PNGs,
  blur effects, or continuous animation.
- Provider transitions are short and respect the Windows reduced-animation
  preference.
- The widget stores no ChatGPT token and does not inspect Codex `auth.json`.
- Diagnostic output must exclude tokens, account identifiers, usage values,
  logs, and full private paths.
- Release downloads must be verified against `SHA256SUMS.txt` before
  installation.
- Legacy technical identifiers such as `ClaudeUsageWidget.exe`, the release
  archive, app-data directory, Startup shortcut, mutex, and C# namespace remain
  unchanged so existing installations keep their settings and update path.

## Validation

- Pull request build and smoke tests passed for the `v2.1.0` UI changes.
- The `main` branch build and smoke tests passed after merge.
- The `v2.1.0` release workflow completed successfully.
- Release assets were verified to include:
  - `ClaudeUsageWidget-win-x64.zip`
  - `SHA256SUMS.txt`
- README examples and screenshots were reviewed for provider accuracy and
  private information.
- The provider-neutral repository rename passed a Release build with zero
  warnings and errors, the full local smoke-test suite, `git diff --check`, and
  a privacy scan before commit.

## Next steps

- Issue #12 adds a localized re-login hint when Anthropic rejects an expired or
  revoked Claude refresh token, without exposing the raw OAuth response.
- Open a pull request for the validated Issue #12 branch, then merge it after
  GitHub Actions passes.
- Before starting a feature, create or select a GitHub Issue and work on a
  dedicated branch.
- Re-check provider payloads and update examples if Claude or ChatGPT changes
  the quota windows it returns.
- Consider code signing for a future release to reduce Windows SmartScreen
  warnings.

## Known limitations

- Release binaries are not currently code-signed, so Windows may show a
  SmartScreen warning.
- Claude and Codex usage interfaces may evolve; parsing and labels may need
  maintenance when providers change their responses.
- The desktop interface targets Windows.
- A temporary provider or network failure can make cached data stale; the
  widget marks it as stale rather than replacing it with misleading values.

## Cross-device handoff

Before leaving one computer:

1. Run `git status` and make sure intended work is committed.
2. Update this file when the current handoff point changes.
3. Run the relevant tests.
4. Push the branch and open or update its pull request.
5. Do not rely on `git stash` for handoff; stashes stay on one computer.

On the other computer:

```powershell
git fetch origin
git switch main
git pull --ff-only
git status
```

If work continues on an existing feature branch, switch to that branch after
fetching and pull its latest remote commit before editing.

Suggested Codex continuation prompt:

> Please read `README.md`, `docs/PROJECT_STATUS.md`, the relevant open GitHub
> Issue or pull request, and the recent Git history before making changes.
> Confirm the current branch and working-tree status, then continue from the
> documented next step without discarding unrelated local changes.
