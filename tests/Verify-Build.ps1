[CmdletBinding()]
param(
	[Parameter(Mandatory = $true)][string] $BinaryDirectory,
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
$BinaryDirectory = [IO.Path]::GetFullPath($BinaryDirectory)
$sidecar = Join-Path $BinaryDirectory 'ModrinthNoAdsSidecar.exe'
$launcher = Join-Path $BinaryDirectory 'ModrinthNoAdsLauncher.exe'

function Assert-True {
	param([bool] $Condition, [string] $Message)
	if (-not $Condition) {
		throw $Message
	}
}

function Get-PeSubsystem {
	param([Parameter(Mandatory = $true)][string] $Path)
	$bytes = [IO.File]::ReadAllBytes($Path)
	Assert-True ($bytes.Length -gt 256) "PE file is unexpectedly small: $Path"
	$peOffset = [BitConverter]::ToInt32($bytes, 0x3c)
	Assert-True (
		$peOffset -gt 0 -and $peOffset + 96 -lt $bytes.Length
	) "Invalid PE header offset: $Path"
	Assert-True (
		$bytes[$peOffset] -eq 0x50 -and
		$bytes[$peOffset + 1] -eq 0x45 -and
		$bytes[$peOffset + 2] -eq 0 -and
		$bytes[$peOffset + 3] -eq 0
	) "Missing PE signature: $Path"
	return [BitConverter]::ToUInt16($bytes, $peOffset + 24 + 68)
}

foreach ($path in @($sidecar, $launcher)) {
	Assert-True (Test-Path -LiteralPath $path -PathType Leaf) "Missing binary: $path"
	$null = [Reflection.AssemblyName]::GetAssemblyName($path)
	$fileVersion = (Get-Item -LiteralPath $path).VersionInfo.FileVersion
	Assert-True (
		$fileVersion -eq ($ExpectedVersion + '.0')
	) "Unexpected file version for $path`: $fileVersion"
	Assert-True (
		(Get-Item -LiteralPath $path).VersionInfo.CompanyName -eq 'Ambassador4ik'
	) "Unexpected company metadata for $path"
}

Assert-True (
	(Get-PeSubsystem $sidecar) -eq 3
) 'The diagnostic sidecar must use the Windows console subsystem.'
Assert-True (
	(Get-PeSubsystem $launcher) -eq 2
) 'The background launcher must use the Windows GUI subsystem.'

$reportedVersion = (& $sidecar --version | Out-String).Trim()
Assert-True ($LASTEXITCODE -eq 0) 'The sidecar --version command failed.'
Assert-True (
	$reportedVersion -eq ("Modrinth No-Ads Sidecar " + $ExpectedVersion)
) "Unexpected --version output: $reportedVersion"

& $sidecar --self-test
Assert-True ($LASTEXITCODE -eq 0) 'The sidecar self-test failed.'

$launcherTest = Start-Process `
	-FilePath $launcher `
	-ArgumentList '--self-test' `
	-Wait `
	-PassThru
Assert-True (
	$launcherTest.ExitCode -eq 0
) "The background launcher self-test failed with $($launcherTest.ExitCode)."

$assembly = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($sidecar))
$resources = @($assembly.GetManifestResourceNames())
Assert-True (
	$resources -contains 'ModrinthNoAds.bootstrap-main.js'
) 'The main-view bootstrap resource is missing.'
Assert-True (
	$resources -contains 'ModrinthNoAds.bootstrap-ad.js'
) 'The ad-view bootstrap resource is missing.'

Write-Host "Verified build $ExpectedVersion in $BinaryDirectory"
