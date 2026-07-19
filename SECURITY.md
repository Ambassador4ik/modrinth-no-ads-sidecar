# Security Policy

## Supported versions

Only the latest tagged release receives security fixes. Development snapshots
and older releases are unsupported.

## Reporting a vulnerability

Use [GitHub Private Vulnerability Reporting](https://github.com/Ambassador4ik/modrinth-no-ads-sidecar/security/advisories/new).
If private reporting is unavailable, email
[thebluedawn@outlook.com](mailto:thebluedawn@outlook.com).

Do not open a public issue containing exploit details, credentials, raw logs,
or shortcut backups. The maintainer aims to acknowledge a report within seven
days. This project has no bug-bounty program.

Include the affected sidecar version, Windows and WebView2 versions,
reproduction steps, expected impact, and a minimal proof of concept. Redact
usernames, tokens, cookies, target URLs, local paths, and other personal data.

Report vulnerabilities in Modrinth's own software or service to Rinth under its
[Security Notice](https://modrinth.com/legal/security), not to this project. Do
not test against accounts, systems, or services you do not own or have explicit
permission to assess.

## Security model and expected behavior

This utility is intended for a trusted, single-user Windows computer. It is not
designed to protect against another malicious local process or user session.
Expected behavior includes:

- launching Modrinth with an unauthenticated CDP endpoint on a random loopback
  port;
- attaching only to targets that pass endpoint, WebSocket, URL, label, title,
  and DOM checks;
- evaluating the bundled guard scripts in verified WebViews;
- writing a local rotating diagnostic log;
- optionally copying two executables into
  `%LOCALAPPDATA%\Programs\ModrinthNoAdsSidecar`;
- optionally changing current-user shortcuts after explicit installation;
- leaving the official App, updater, account entitlements, and authentication
  unchanged.

The CDP endpoint exists for the lifetime of Modrinth's WebView2 process, not the
sidecar process. Fully quit Modrinth to close it. Do not run this project
elevated or on an untrusted/shared machine.

There is an unavoidable local race between choosing a free port and WebView2
binding it. Endpoint identity checks, strict target checks, response limits,
and proxy disabling reduce the impact of an accidental or malicious local
listener, but they do not authenticate CDP or defend against a process already
running with the same user's privileges.

## In scope

- unintended non-loopback exposure;
- endpoint or target-validation bypass;
- arbitrary code execution outside the verified WebViews;
- unsafe shortcut installation or restoration;
- privilege escalation;
- credential or session collection;
- undisclosed external network or telemetry behavior;
- a background launcher reporting success without a verified main guard.

## Out of scope

- changes made by a user who manually edits the source or manifest;
- failures caused solely by unsupported Modrinth/WebView2 versions;
- SmartScreen warnings caused solely by the lack of Authenticode signing;
- claims about the legality or third-party terms compliance of using the tool.
