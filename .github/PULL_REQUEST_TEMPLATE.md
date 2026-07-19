## Summary

Describe the narrowly scoped change and why it is needed.

## Security and compatibility

- What WebView2/CDP, shortcut, privacy, or update assumptions change?
- Does the change preserve loopback-only binding and strict target validation?
- Does it add external network access, telemetry, elevation, persistence, or
  third-party code/assets?

## Tests

- [ ] `tests\Test-Repository.ps1`
- [ ] `build.ps1`
- [ ] `tests\Verify-Build.ps1`
- [ ] `scripts\New-ReleasePackage.ps1`
- [ ] `tests\Verify-ReleasePackage.ps1`
- [ ] No generated binaries, archives, logs, shortcuts, or personal paths are committed.

## Third-party provenance

List any external code, documentation, selectors, protocol facts, or assets and
their source/license. Write “None” when not applicable.
