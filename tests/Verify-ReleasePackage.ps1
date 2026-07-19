[CmdletBinding()]
param(
	[Parameter(Mandatory = $true)][string] $ZipPath,
	[string] $ExpectedVersion
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
if ([string]::IsNullOrWhiteSpace($ExpectedVersion)) {
	$ExpectedVersion = (
		Get-Content -LiteralPath (Join-Path $root 'VERSION') -Raw -Encoding UTF8
	).Trim()
}
$ZipPath = [IO.Path]::GetFullPath($ZipPath)
if (-not (Test-Path -LiteralPath $ZipPath -PathType Leaf)) {
	throw "Release ZIP is missing: $ZipPath"
}

function Assert-True {
	param([bool] $Condition, [string] $Message)
	if (-not $Condition) {
		throw $Message
	}
}

function Get-NormalizedPath {
	param([string] $Path)
	return [IO.Path]::GetFullPath($Path).TrimEnd([char]'\', [char]'/')
}

function Test-PathWithin {
	param([string] $Path, [string] $Root)
	$fullPath = Get-NormalizedPath $Path
	$fullRoot = (Get-NormalizedPath $Root) + [IO.Path]::DirectorySeparatorChar
	return $fullPath.StartsWith($fullRoot, [StringComparison]::OrdinalIgnoreCase)
}

$externalChecksum = $ZipPath + '.sha256.txt'
if (Test-Path -LiteralPath $externalChecksum -PathType Leaf) {
	$line = (Get-Content -LiteralPath $externalChecksum -Raw -Encoding UTF8).Trim()
	Assert-True (
		$line -match '^([0-9A-Fa-f]{64})  ([^\\/]+\.zip)$'
	) 'The external ZIP checksum file has an invalid format.'
	Assert-True (
		$Matches[2] -eq [IO.Path]::GetFileName($ZipPath)
	) 'The external checksum names a different ZIP.'
	$actualZipHash = (Get-FileHash -LiteralPath $ZipPath -Algorithm SHA256).Hash
	Assert-True (
		$actualZipHash -eq $Matches[1].ToUpperInvariant()
	) 'The external ZIP checksum does not match.'
}

$temporaryBase = if (
	-not [string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)
) {
	$env:RUNNER_TEMP
}
else {
	[IO.Path]::GetTempPath()
}
$temporaryBase = Get-NormalizedPath $temporaryBase
$extractRoot = Join-Path $temporaryBase (
	'Extracted release with spaces ' + [Guid]::NewGuid().ToString('N')
)
Assert-True (
	Test-PathWithin $extractRoot $temporaryBase
) 'Refusing to extract outside the selected temporary directory.'

try {
	Expand-Archive -LiteralPath $ZipPath -DestinationPath $extractRoot -Force
	$directories = @(Get-ChildItem -LiteralPath $extractRoot -Directory)
	$filesAtRoot = @(Get-ChildItem -LiteralPath $extractRoot -File)
	Assert-True ($directories.Count -eq 1) 'Release ZIP must contain one root directory.'
	Assert-True ($filesAtRoot.Count -eq 0) 'Release ZIP contains loose root files.'

	$packageDirectory = $directories[0].FullName
	Assert-True (
		$directories[0].Name -eq
		("ModrinthNoAdsSidecar-v$ExpectedVersion-windows")
	) 'Release ZIP root directory has an unexpected name.'

	$checksumPath = Join-Path $packageDirectory 'SHA256SUMS.txt'
	Assert-True (
		Test-Path -LiteralPath $checksumPath -PathType Leaf
	) 'Internal SHA256SUMS.txt is missing.'
	$listed = New-Object System.Collections.Generic.HashSet[string](
		[StringComparer]::OrdinalIgnoreCase
	)
	foreach ($line in Get-Content -LiteralPath $checksumPath -Encoding UTF8) {
		Assert-True (
			$line -match '^([0-9A-Fa-f]{64})  (.+)$'
		) "Invalid internal checksum line: $line"
		$relative = $Matches[2].Replace('/', '\')
		Assert-True (
			-not [IO.Path]::IsPathRooted($relative) -and
			$relative -notmatch '(^|\\)\.\.(\\|$)'
		) "Unsafe path in internal checksum: $relative"
		$file = [IO.Path]::GetFullPath((Join-Path $packageDirectory $relative))
		Assert-True (
			Test-PathWithin $file $packageDirectory
		) "Checksum path escaped the package: $relative"
		Assert-True (
			Test-Path -LiteralPath $file -PathType Leaf
		) "Checksum names a missing file: $relative"
		$actual = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
		Assert-True (
			$actual -eq $Matches[1].ToUpperInvariant()
		) "Checksum mismatch: $relative"
		$null = $listed.Add($relative)
	}
	$payloadFiles = @(
		Get-ChildItem -LiteralPath $packageDirectory -File -Recurse |
			Where-Object { $_.FullName -ne $checksumPath }
	)
	Assert-True (
		$listed.Count -eq $payloadFiles.Count
	) 'Internal checksum list does not cover every payload file exactly once.'

	& (Join-Path $root 'tests\Verify-Build.ps1') `
		-BinaryDirectory $packageDirectory `
		-ExpectedVersion $ExpectedVersion
	& (Join-Path $root 'tests\ShortcutIntegration.Tests.ps1') `
		-PackageDirectory $packageDirectory

	Write-Host "Verified release ZIP: $ZipPath"
}
finally {
	if (
		(Test-Path -LiteralPath $extractRoot) -and
		(Test-PathWithin $extractRoot $temporaryBase)
	) {
		Remove-Item -LiteralPath $extractRoot -Recurse -Force
	}
}
