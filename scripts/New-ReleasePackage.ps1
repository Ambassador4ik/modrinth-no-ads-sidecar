[CmdletBinding()]
param(
	[string] $Version,
	[string] $OutputDirectory,
	[switch] $SkipBuild
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$versionPath = Join-Path $root 'VERSION'
if ([string]::IsNullOrWhiteSpace($Version)) {
	$Version = (Get-Content -LiteralPath $versionPath -Raw -Encoding UTF8).Trim()
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') {
	throw "Version must use x.y.z format; found '$Version'."
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
	$OutputDirectory = Join-Path $root 'dist'
}
elseif (-not [IO.Path]::IsPathRooted($OutputDirectory)) {
	$OutputDirectory = Join-Path $root $OutputDirectory
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

$binaryDirectory = Join-Path $root 'artifacts\bin'
if (-not $SkipBuild) {
	& (Join-Path $root 'build.ps1') -OutputDirectory $binaryDirectory
}

& (Join-Path $root 'tests\Verify-Build.ps1') `
	-BinaryDirectory $binaryDirectory `
	-ExpectedVersion $Version

$packageName = "ModrinthNoAdsSidecar-v$Version-windows"
$stagingRoot = Join-Path $root 'artifacts\package'
$packageDirectory = Join-Path $stagingRoot $packageName
$zipPath = Join-Path $OutputDirectory ($packageName + '.zip')
$zipChecksumPath = $zipPath + '.sha256.txt'

foreach ($path in @($packageDirectory, $zipPath, $zipChecksumPath)) {
	if (Test-Path -LiteralPath $path) {
		Remove-Item -LiteralPath $path -Recurse -Force
	}
}
New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null

$payload = @(
	@{
		Source = Join-Path $binaryDirectory 'ModrinthNoAdsSidecar.exe'
		Name = 'ModrinthNoAdsSidecar.exe'
	},
	@{
		Source = Join-Path $binaryDirectory 'ModrinthNoAdsLauncher.exe'
		Name = 'ModrinthNoAdsLauncher.exe'
	},
	@{ Source = Join-Path $root 'Install Background Shortcut.ps1'; Name = 'Install Background Shortcut.ps1' },
	@{ Source = Join-Path $root 'Install Background Shortcut.cmd'; Name = 'Install Background Shortcut.cmd' },
	@{ Source = Join-Path $root 'Restore Original Shortcuts.ps1'; Name = 'Restore Original Shortcuts.ps1' },
	@{ Source = Join-Path $root 'Restore Original Shortcuts.cmd'; Name = 'Restore Original Shortcuts.cmd' },
	@{ Source = Join-Path $root 'Launch Modrinth (No Ads).cmd'; Name = 'Launch Modrinth (No Ads).cmd' },
	@{ Source = Join-Path $root 'README.md'; Name = 'README.md' },
	@{ Source = Join-Path $root 'LICENSE'; Name = 'LICENSE' },
	@{ Source = Join-Path $root 'NOTICE.md'; Name = 'NOTICE.md' },
	@{ Source = Join-Path $root 'CHANGELOG.md'; Name = 'CHANGELOG.md' },
	@{ Source = Join-Path $root 'VERSION'; Name = 'VERSION' }
)

foreach ($item in $payload) {
	if (-not (Test-Path -LiteralPath $item.Source -PathType Leaf)) {
		throw "Release payload file is missing: $($item.Source)"
	}
	Copy-Item `
		-LiteralPath $item.Source `
		-Destination (Join-Path $packageDirectory $item.Name) `
		-Force
}

$checksumLines = @(
	Get-ChildItem -LiteralPath $packageDirectory -File -Recurse |
		Sort-Object FullName |
		ForEach-Object {
			$relative = $_.FullName.Substring($packageDirectory.Length + 1)
			$relative = $relative.Replace('\', '/')
			$hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
			"$hash  $relative"
		}
)
[IO.File]::WriteAllLines(
	(Join-Path $packageDirectory 'SHA256SUMS.txt'),
	$checksumLines,
	[Text.UTF8Encoding]::new($false)
)

Compress-Archive `
	-LiteralPath $packageDirectory `
	-DestinationPath $zipPath `
	-CompressionLevel Optimal

$zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
[IO.File]::WriteAllText(
	$zipChecksumPath,
	($zipHash + '  ' + [IO.Path]::GetFileName($zipPath) + [Environment]::NewLine),
	[Text.UTF8Encoding]::new($false)
)

Write-Host "Package: $zipPath"
Write-Host "Checksum: $zipChecksumPath"

[pscustomobject]@{
	Version = $Version
	PackageDirectory = $packageDirectory
	ZipPath = $zipPath
	ChecksumPath = $zipChecksumPath
}
