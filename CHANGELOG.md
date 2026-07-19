# Changelog

All notable project changes are documented here. Versions follow semantic
versioning.

## 1.2.0 - 2026-07-19

- Added a verified startup handshake; the GUI launcher now reports failure
  unless the main suppression guard becomes active.
- Removed false-success paths when an unprotected process merely shares the
  Modrinth process name.
- Hardened inherited WebView2 argument parsing, CDP endpoint validation, proxy
  behavior, response limits, target IDs, process-path matching, app discovery,
  and log output.
- Made shortcut runtime replacement staged, self-tested, and rollback-aware.
- Added complete pre-mutation manifest validation and exact recorded shortcut
  state matching for restore and migration.
- Added source-only repository policy, Windows CI, tagged release automation,
  isolated shortcut tests, checksums, and GitHub build attestations.
- Added legal, trademark, privacy, security, contribution, and unsigned-binary
  disclosures for public distribution.

## 1.1.0 - 2026-07-19

- Added a Windows GUI-subsystem launcher so normal shortcut launches do not
  open a terminal window.
- Added reversible current-user shortcut installation with exact backups,
  idempotent reruns, a separately named shortcut, and fail-closed restoration.
- Added a named launch-request channel so an existing sidecar can activate or
  relaunch Modrinth.
