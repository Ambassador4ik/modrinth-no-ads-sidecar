[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$requiredFiles = @(
	'VERSION',
	'README.md',
	'LICENSE',
	'NOTICE.md',
	'SECURITY.md',
	'CONTRIBUTING.md',
	'CODE_OF_CONDUCT.md',
	'CHANGELOG.md',
	'build.ps1',
	'Install Background Shortcut.ps1',
	'Restore Original Shortcuts.ps1',
	'src\ModrinthNoAdsSidecar.cs',
	'src\ModrinthNoAdsLauncher.cs',
	'src\bootstrap-main.js',
	'src\bootstrap-ad.js'
)

foreach ($relative in $requiredFiles) {
	$path = Join-Path $root $relative
	if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
		throw "Required repository file is missing: $relative"
	}
}

$version = (Get-Content -LiteralPath (Join-Path $root 'VERSION') -Raw -Encoding UTF8).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') {
	throw "VERSION is not x.y.z: $version"
}
$assemblyVersion = $version + '.0'
$sidecarSource = Get-Content `
	-LiteralPath (Join-Path $root 'src\ModrinthNoAdsSidecar.cs') `
	-Raw `
	-Encoding UTF8
$launcherSource = Get-Content `
	-LiteralPath (Join-Path $root 'src\ModrinthNoAdsLauncher.cs') `
	-Raw `
	-Encoding UTF8
foreach ($source in @($sidecarSource, $launcherSource)) {
	if ($source -notmatch [Regex]::Escape("AssemblyVersion(`"$assemblyVersion`")")) {
		throw "AssemblyVersion does not match VERSION ($assemblyVersion)."
	}
	if ($source -notmatch [Regex]::Escape("AssemblyFileVersion(`"$assemblyVersion`")")) {
		throw "AssemblyFileVersion does not match VERSION ($assemblyVersion)."
	}
}
if (
	$sidecarSource -notmatch
	[Regex]::Escape("private const string Version = `"$version`";")
) {
	throw 'The sidecar CLI version does not match VERSION.'
}

$license = Get-Content -LiteralPath (Join-Path $root 'LICENSE') -Raw -Encoding UTF8
if ($license -notmatch 'Copyright \(c\) 2026 Ambassador4ik and contributors') {
	throw 'LICENSE does not identify the copyright holder.'
}

$parseErrors = New-Object System.Collections.Generic.List[object]
Get-ChildItem -LiteralPath $root -Recurse -File -Filter '*.ps1' |
	Where-Object {
		$_.FullName -notlike (Join-Path $root 'artifacts\*') -and
		$_.FullName -notlike (Join-Path $root 'dist\*')
	} |
	ForEach-Object {
		$tokens = $null
		$errors = $null
		$null = [Management.Automation.Language.Parser]::ParseFile(
			$_.FullName,
			[ref]$tokens,
			[ref]$errors
		)
		foreach ($parseError in @($errors)) {
			$parseErrors.Add([pscustomobject]@{
				File = $_.FullName
				Message = $parseError.Message
			})
		}
	}
if ($parseErrors.Count -gt 0) {
	throw ("PowerShell parse errors:`n" + ($parseErrors | Out-String))
}

if (Test-Path -LiteralPath (Join-Path $root '.git')) {
	$tracked = @(& git -C $root ls-files)
	if ($LASTEXITCODE -ne 0) {
		throw 'git ls-files failed.'
	}
	$forbidden = @(
		$tracked |
			Where-Object {
				$_ -match '(?i)(\.exe|\.pdb|\.zip|\.lnk|\.log)$' -or
				$_ -match '(?i)(^|/)SHA256SUMS[^/]*\.txt$' -or
				$_ -match '(?i)(^|/)shortcuts\.json$' -or
				$_ -match '(?i)(^|/)backups/'
			}
	)
	if ($forbidden.Count -gt 0) {
		throw ("Generated or private files are tracked:`n" + ($forbidden -join "`n"))
	}
}

$textExtensions = @('.cs', '.js', '.ps1', '.cmd', '.md', '.yml', '.yaml', '.txt')
$textFiles = Get-ChildItem -LiteralPath $root -Recurse -File |
	Where-Object {
		$textExtensions -contains $_.Extension.ToLowerInvariant() -and
		$_.FullName -notlike (Join-Path $root 'artifacts\*') -and
		$_.FullName -notlike (Join-Path $root 'dist\*')
	}
foreach ($file in $textFiles) {
	$content = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8
	if ($content -match '-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----') {
		throw "Private key material found in $($file.FullName)."
	}
	if ($content -match '(?i)gh[pousr]_[A-Za-z0-9_]{20,}') {
		throw "A GitHub token-shaped value was found in $($file.FullName)."
	}
}

$workflowFiles = Get-ChildItem `
	-LiteralPath (Join-Path $root '.github\workflows') `
	-File `
	-Include '*.yml', '*.yaml'
foreach ($workflow in $workflowFiles) {
	$content = Get-Content -LiteralPath $workflow.FullName -Raw -Encoding UTF8
	$references = [Regex]::Matches($content, 'uses:\s+[^@\s]+@([^\s#]+)')
	foreach ($reference in $references) {
		if ($reference.Groups[1].Value -notmatch '^[0-9a-f]{40}$') {
			throw "Action is not pinned to a full commit SHA in $($workflow.Name): $($reference.Value)"
		}
	}
}

Write-Host "Repository checks passed for version $version."
