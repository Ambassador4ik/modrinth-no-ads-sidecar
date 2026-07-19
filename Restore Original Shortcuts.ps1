[CmdletBinding()]
param(
	[string] $StateDirectory = (
		Join-Path $env:LOCALAPPDATA 'ModrinthNoAdsSidecar\shortcut-integration'
	)
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2

if (-not ('ModrinthNoAdsPathNative' -as [type])) {
	Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class ModrinthNoAdsPathNative
{
	private const uint ShareAll = 0x00000001 | 0x00000002 | 0x00000004;
	private const uint OpenExisting = 3;
	private const uint BackupSemantics = 0x02000000;

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	public static extern uint GetLongPathName(
		string shortPath,
		StringBuilder longPath,
		uint bufferLength
	);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern IntPtr CreateFile(
		string fileName,
		uint desiredAccess,
		uint shareMode,
		IntPtr securityAttributes,
		uint creationDisposition,
		uint flagsAndAttributes,
		IntPtr templateFile
	);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern uint GetFinalPathNameByHandle(
		IntPtr file,
		StringBuilder path,
		uint pathLength,
		uint flags
	);

	[DllImport("kernel32.dll")]
	private static extern bool CloseHandle(IntPtr handle);

	public static string GetFinalPath(string path)
	{
		IntPtr handle = CreateFile(
			path,
			0,
			ShareAll,
			IntPtr.Zero,
			OpenExisting,
			BackupSemantics,
			IntPtr.Zero
		);
		if (handle == new IntPtr(-1))
			return null;

		try
		{
			StringBuilder result = new StringBuilder(32768);
			uint length = GetFinalPathNameByHandle(
				handle,
				result,
				(uint)result.Capacity,
				0
			);
			if (length == 0 || length >= result.Capacity)
				return null;

			string value = result.ToString();
			if (value.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
				return @"\\" + value.Substring(8);
			if (value.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
				return value.Substring(4);
			return value;
		}
		finally
		{
			CloseHandle(handle);
		}
	}
}
'@ | Out-Null
}

$manifestPath = Join-Path $StateDirectory 'shortcuts.json'
$backupDirectory = Join-Path $StateDirectory 'backups'

function Get-NormalizedPath {
	param([Parameter(Mandatory = $true)][string] $Path)
	$fullPath = [IO.Path]::GetFullPath(
		[Environment]::ExpandEnvironmentVariables($Path)
	)
	if (Test-Path -LiteralPath $fullPath) {
		$finalPath = [ModrinthNoAdsPathNative]::GetFinalPath($fullPath)
		if (-not [string]::IsNullOrWhiteSpace($finalPath)) {
			$fullPath = $finalPath
		}
	}
	return $fullPath.TrimEnd([char]'\', [char]'/')
}

function Test-SamePath {
	param([string] $Left, [string] $Right)
	if ([string]::IsNullOrWhiteSpace($Left) -or [string]::IsNullOrWhiteSpace($Right)) {
		return $false
	}
	try {
		return [string]::Equals(
			(Get-NormalizedPath $Left),
			(Get-NormalizedPath $Right),
			[StringComparison]::OrdinalIgnoreCase
		)
	}
	catch {
		return $false
	}
}

function Test-PathWithin {
	param(
		[Parameter(Mandatory = $true)][string] $Path,
		[Parameter(Mandatory = $true)][string] $Root
	)
	$fullPath = Get-NormalizedPath $Path
	$fullRoot = (Get-NormalizedPath $Root) + [IO.Path]::DirectorySeparatorChar
	return $fullPath.StartsWith($fullRoot, [StringComparison]::OrdinalIgnoreCase)
}

function Get-OptionalProperty {
	param(
		[Parameter(Mandatory = $true)] $Object,
		[Parameter(Mandatory = $true)][string] $Name,
		[string] $Default = ''
	)
	$property = $Object.PSObject.Properties[$Name]
	if ($null -eq $property -or $null -eq $property.Value) {
		return $Default
	}
	return [string]$property.Value
}

function Test-ShortcutMatchesExpectedState {
	param(
		[Parameter(Mandatory = $true)] $Shortcut,
		[Parameter(Mandatory = $true)] $Entry
	)
	return (
		(Test-SamePath $Shortcut.TargetPath $Entry.patchedTargetPath) -and
		[string]::Equals(
			[string]$Shortcut.Arguments,
			[string]$Entry.patchedArguments,
			[StringComparison]::Ordinal
		) -and
		(Test-SamePath $Shortcut.WorkingDirectory $Entry.patchedWorkingDirectory) -and
		[string]::Equals(
			[string]$Shortcut.IconLocation,
			[string]$Entry.patchedIconLocation,
			[StringComparison]::OrdinalIgnoreCase
		) -and
		[string]::Equals(
			[string]$Shortcut.Description,
			[string]$Entry.patchedDescription,
			[StringComparison]::Ordinal
		)
	)
}

if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
	throw "No shortcut integration manifest was found at $manifestPath"
}

try {
	$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 |
		ConvertFrom-Json
	if ([int]$manifest.version -ne 1) {
		throw 'Unsupported manifest version.'
	}
	if ([string]::IsNullOrWhiteSpace([string]$manifest.launcherPath)) {
		throw 'The launcher path is missing.'
	}
	if ([string]::IsNullOrWhiteSpace([string]$manifest.installDirectory)) {
		throw 'The install directory is missing.'
	}
	$allowedRoots = @($manifest.allowedShortcutRoots)
	if ($allowedRoots.Count -eq 0) {
		throw 'The allowed shortcut roots are missing.'
	}
}
catch {
	throw "The shortcut manifest is invalid; no shortcuts were changed. $($_.Exception.Message)"
}

$launcherPath = [string]$manifest.launcherPath
$installDirectory = [string]$manifest.installDirectory
$normalizedAllowedRoots = @(
	foreach ($root in $allowedRoots) {
		if (
			[string]::IsNullOrWhiteSpace([string]$root) -or
			-not [IO.Path]::IsPathRooted([string]$root)
		) {
			throw 'An allowed shortcut root is invalid.'
		}
		Get-NormalizedPath ([string]$root)
	}
) | Sort-Object -Unique

$validatedEntries = New-Object System.Collections.ArrayList
try {
	foreach ($rawEntry in @($manifest.shortcuts)) {
		$shortcutPath = [string]$rawEntry.path
		if (
			[string]::IsNullOrWhiteSpace($shortcutPath) -or
			-not [IO.Path]::IsPathRooted($shortcutPath)
		) {
			throw 'A shortcut path is missing or is not absolute.'
		}
		$shortcutPath = Get-NormalizedPath $shortcutPath

		$isAllowed = $false
		foreach ($root in $normalizedAllowedRoots) {
			if (Test-PathWithin $shortcutPath $root) {
				$isAllowed = $true
				break
			}
		}
		if (-not $isAllowed) {
			throw "A shortcut path is outside the allowed roots: $shortcutPath"
		}

		$createdByInstaller = [bool]$rawEntry.createdByInstaller
		$appPath = [string]$rawEntry.appPath
		$backupPath = [string]$rawEntry.backupPath
		if (-not $createdByInstaller) {
			if (
				[string]::IsNullOrWhiteSpace($backupPath) -or
				-not [IO.Path]::IsPathRooted($backupPath) -or
				-not (Test-PathWithin $backupPath $backupDirectory) -or
				-not (Test-Path -LiteralPath $backupPath -PathType Leaf)
			) {
				throw "A shortcut backup is missing or invalid: $shortcutPath"
			}
			$backupPath = Get-NormalizedPath $backupPath
		}

		$legacyDescription = if ($createdByInstaller) {
			'Modrinth App (background no-ads launcher)'
		}
		else {
			'Modrinth App, launched through the local background sidecar'
		}
		$entry = [ordered]@{
			path = $shortcutPath
			backupPath = $backupPath
			createdByInstaller = $createdByInstaller
			appPath = $appPath
			patchedTargetPath = Get-OptionalProperty $rawEntry 'patchedTargetPath' $launcherPath
			patchedArguments = Get-OptionalProperty `
				$rawEntry `
				'patchedArguments' `
				('--app "' + $appPath + '"')
			patchedWorkingDirectory = Get-OptionalProperty `
				$rawEntry `
				'patchedWorkingDirectory' `
				$installDirectory
			patchedIconLocation = Get-OptionalProperty `
				$rawEntry `
				'patchedIconLocation' `
				($appPath + ',0')
			patchedDescription = Get-OptionalProperty `
				$rawEntry `
				'patchedDescription' `
				$legacyDescription
		}
		if (
			[string]::IsNullOrWhiteSpace($entry.patchedTargetPath) -or
			[string]::IsNullOrWhiteSpace($entry.patchedWorkingDirectory)
		) {
			throw "Expected shortcut state is incomplete: $shortcutPath"
		}
		[void]$validatedEntries.Add($entry)
	}
}
catch {
	throw "The shortcut manifest is invalid; no shortcuts were changed. $($_.Exception.Message)"
}

$wsh = New-Object -ComObject WScript.Shell
$restored = 0
$removed = 0
$skipped = 0

foreach ($entry in @($validatedEntries)) {
	$shortcutPath = [string]$entry.path

	if ([bool]$entry.createdByInstaller) {
		if (-not (Test-Path -LiteralPath $shortcutPath -PathType Leaf)) {
			continue
		}
		try {
			$shortcut = $wsh.CreateShortcut($shortcutPath)
			if (Test-ShortcutMatchesExpectedState $shortcut $entry) {
				Remove-Item -LiteralPath $shortcutPath -Force
				$removed++
			}
			else {
				Write-Warning "Left a user-edited shortcut unchanged: $shortcutPath"
				$skipped++
			}
		}
		catch {
			Write-Warning "Could not remove installer-created shortcut $shortcutPath`: $($_.Exception.Message)"
			$skipped++
		}
		continue
	}

	$backupPath = [string]$entry.backupPath
	if (-not (Test-Path -LiteralPath $shortcutPath -PathType Leaf)) {
		Write-Warning "Did not recreate a shortcut that the user deleted: $shortcutPath"
		$skipped++
		continue
	}

	try {
		$current = $wsh.CreateShortcut($shortcutPath)
		if (-not (Test-ShortcutMatchesExpectedState $current $entry)) {
			Write-Warning "Left a user-edited shortcut unchanged: $shortcutPath"
			$skipped++
			continue
		}

		Copy-Item -LiteralPath $backupPath -Destination $shortcutPath -Force
		$restored++
	}
	catch {
		Write-Warning "Could not restore shortcut $shortcutPath`: $($_.Exception.Message)"
		$skipped++
	}
}

$restoredAt = [DateTime]::UtcNow.ToString('o')
if ($null -eq $manifest.PSObject.Properties['restoredAtUtc']) {
	$manifest | Add-Member -NotePropertyName restoredAtUtc -NotePropertyValue $restoredAt
}
else {
	$manifest.restoredAtUtc = $restoredAt
}
$json = $manifest | ConvertTo-Json -Depth 6
$tempPath = $manifestPath + '.' + $PID + '.tmp'
[IO.File]::WriteAllText($tempPath, $json, [Text.UTF8Encoding]::new($false))
Move-Item -LiteralPath $tempPath -Destination $manifestPath -Force

Write-Host ''
Write-Host 'Original shortcut integration restored.'
Write-Host "Restored backups: $restored"
Write-Host "Removed installer-created shortcuts: $removed"
Write-Host "Skipped: $skipped"
Write-Host ''
Write-Host 'The installed runtime was left in place and can be reused by running the installer again.'
