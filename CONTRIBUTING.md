# Contributing

Thank you for helping improve this independent interoperability project.

## Before opening an issue

Use the latest tagged release, run
`ModrinthNoAdsSidecar.exe --self-test`, and search existing issues. Include the
sidecar version, Modrinth App version, Windows version, whether background
shortcut integration is installed, and minimal reproduction steps.

Do not post raw logs. First remove usernames, local paths, URLs, tokens,
cookies, and other personal information. Report security issues through
[SECURITY.md](SECURITY.md), not a public issue.

## Pull requests

1. Create a focused branch and keep changes narrowly scoped.
2. Run `tests\Test-Repository.ps1`.
3. Build on Windows with `build.ps1`.
4. Run `tests\Verify-Build.ps1`.
5. Run the release packaging and isolated shortcut integration tests.
6. Explain user-visible behavior, security implications, compatibility
   assumptions, and tests in the pull request.
7. Do not commit generated executables, archives, logs, shortcuts, backups,
   manifests, credentials, or personal paths.

## Safety and interoperability requirements

Contributions must preserve these defaults unless a security-reviewed design
justifies a change:

- random loopback-only CDP binding;
- strict endpoint, WebSocket, URL, label, title, and DOM validation;
- no `--remote-allow-origins=*`;
- bounded HTTP/WebSocket input and sanitized log messages;
- no administrator requirement, service, scheduled task, global environment
  change, or machine-wide registry change;
- no credential/session extraction, account-entitlement spoofing,
  authentication bypass, product-license bypass, or updater-signature bypass;
- no maintainer telemetry or external network service;
- reversible current-user shortcut changes with exact backups and fail-closed
  restoration;
- a visible failure when the background launcher cannot verify protection.

Do not copy Modrinth source code, binaries, logos, artwork, advertising content,
or other branding assets into this repository. Compatibility facts should link
to the relevant upstream file and commit when practical. If a contribution
contains third-party code, disclose its source and license before submission.

## Licensing

By submitting a contribution, you represent that you have the right to submit
it and agree that it will be licensed under this repository's MIT License. No
contributor license agreement is required.
