[CmdletBinding()]
param(
	[string] $InstallDirectory = (
		Join-Path $env:LOCALAPPDATA 'Programs\ModrinthNoAdsSidecar'
	),
	[string] $StateDirectory = (
		Join-Path $env:LOCALAPPDATA 'ModrinthNoAdsSidecar\shortcut-integration'
	),
	[string[]] $ShortcutRoots,
	[string] $AppPath,
	[switch] $SeparateShortcutOnly
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

$packageDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$sidecarName = 'ModrinthNoAdsSidecar.exe'
$launcherName = 'ModrinthNoAdsLauncher.exe'
$manifestPath = Join-Path $StateDirectory 'shortcuts.json'
$backupDirectory = Join-Path $StateDirectory 'backups'
$installedSidecar = Join-Path $InstallDirectory $sidecarName
$installedLauncher = Join-Path $InstallDirectory $launcherName

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

function Get-PathId {
	param([Parameter(Mandatory = $true)][string] $Path)
	$sha = [Security.Cryptography.SHA256]::Create()
	try {
		$bytes = [Text.Encoding]::UTF8.GetBytes((Get-NormalizedPath $Path).ToLowerInvariant())
		return ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '')
	}
	finally {
		$sha.Dispose()
	}
}

function Write-Manifest {
	param(
		[Parameter(Mandatory = $true)] $Entries,
		[Parameter(Mandatory = $true)][string[]] $Roots
	)

	$manifest = [ordered]@{
		version = 1
		launcherPath = $installedLauncher
		installDirectory = $InstallDirectory
		allowedShortcutRoots = @($Roots)
		updatedAtUtc = [DateTime]::UtcNow.ToString('o')
		shortcuts = @($Entries)
	}
	$json = $manifest | ConvertTo-Json -Depth 6
	$tempPath = $manifestPath + '.' + $PID + '.tmp'
	[IO.File]::WriteAllText($tempPath, $json, [Text.UTF8Encoding]::new($false))
	Move-Item -LiteralPath $tempPath -Destination $manifestPath -Force
}

function Find-ModrinthApp {
	param($Shortcut)

	$candidates = New-Object System.Collections.Generic.List[string]
	if (-not [string]::IsNullOrWhiteSpace($AppPath)) {
		$candidates.Add($AppPath)
	}
	if ($null -ne $Shortcut) {
		if (-not [string]::IsNullOrWhiteSpace($Shortcut.TargetPath)) {
			$candidates.Add($Shortcut.TargetPath)
		}
		if (-not [string]::IsNullOrWhiteSpace($Shortcut.WorkingDirectory)) {
			$candidates.Add((Join-Path $Shortcut.WorkingDirectory 'Modrinth App.exe'))
		}
	}

	$candidates.Add((Join-Path $env:LOCALAPPDATA 'Modrinth App\Modrinth App.exe'))
	$candidates.Add((Join-Path $env:LOCALAPPDATA 'Programs\Modrinth App\Modrinth App.exe'))
	if (-not [string]::IsNullOrWhiteSpace($env:ProgramFiles)) {
		$candidates.Add((Join-Path $env:ProgramFiles 'Modrinth App\Modrinth App.exe'))
	}
	$programFilesX86 = [Environment]::GetFolderPath(
		[Environment+SpecialFolder]::ProgramFilesX86
	)
	if (-not [string]::IsNullOrWhiteSpace($programFilesX86)) {
		$candidates.Add((Join-Path $programFilesX86 'Modrinth App\Modrinth App.exe'))
	}

	foreach ($candidate in $candidates) {
		if (
			-not [string]::IsNullOrWhiteSpace($candidate) -and
			[IO.Path]::GetFileName($candidate) -ieq 'Modrinth App.exe' -and
			(Test-Path -LiteralPath $candidate -PathType Leaf)
		) {
			return (Get-NormalizedPath $candidate)
		}
	}
	return $null
}

function Find-Entry {
	param(
		[Parameter(Mandatory = $true)] $Entries,
		[Parameter(Mandatory = $true)][string] $Path
	)
	foreach ($entry in $Entries) {
		if (Test-SamePath $entry.path $Path) {
			return $entry
		}
	}
	return $null
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

function Set-PatchedShortcutState {
	param(
		[Parameter(Mandatory = $true)] $Entry,
		[Parameter(Mandatory = $true)] $Shortcut
	)
	$Entry['patchedTargetPath'] = [string]$Shortcut.TargetPath
	$Entry['patchedArguments'] = [string]$Shortcut.Arguments
	$Entry['patchedWorkingDirectory'] = [string]$Shortcut.WorkingDirectory
	$Entry['patchedIconLocation'] = [string]$Shortcut.IconLocation
	$Entry['patchedDescription'] = [string]$Shortcut.Description
}

function Test-ShortcutMatchesRecordedState {
	param(
		[Parameter(Mandatory = $true)] $Shortcut,
		[Parameter(Mandatory = $true)] $Entry,
		[Parameter(Mandatory = $true)][string] $FallbackLauncher,
		[Parameter(Mandatory = $true)][string] $FallbackInstallDirectory
	)
	$expectedTarget = [string]$Entry.patchedTargetPath
	if ([string]::IsNullOrWhiteSpace($expectedTarget)) {
		$expectedTarget = $FallbackLauncher
	}
	$expectedArguments = [string]$Entry.patchedArguments
	if ([string]::IsNullOrWhiteSpace($expectedArguments)) {
		$expectedArguments = '--app "' + [string]$Entry.appPath + '"'
	}
	$expectedWorkingDirectory = [string]$Entry.patchedWorkingDirectory
	if ([string]::IsNullOrWhiteSpace($expectedWorkingDirectory)) {
		$expectedWorkingDirectory = $FallbackInstallDirectory
	}
	$expectedIcon = [string]$Entry.patchedIconLocation
	if ([string]::IsNullOrWhiteSpace($expectedIcon)) {
		$expectedIcon = [string]$Entry.appPath + ',0'
	}
	$expectedDescription = [string]$Entry.patchedDescription
	if ([string]::IsNullOrWhiteSpace($expectedDescription)) {
		$expectedDescription = if ([bool]$Entry.createdByInstaller) {
			'Modrinth App (background no-ads launcher)'
		}
		else {
			'Modrinth App, launched through the local background sidecar'
		}
	}

	return (
		(Test-SamePath $Shortcut.TargetPath $expectedTarget) -and
		[string]::Equals(
			[string]$Shortcut.Arguments,
			$expectedArguments,
			[StringComparison]::Ordinal
		) -and
		(Test-SamePath $Shortcut.WorkingDirectory $expectedWorkingDirectory) -and
		[string]::Equals(
			[string]$Shortcut.IconLocation,
			$expectedIcon,
			[StringComparison]::OrdinalIgnoreCase
		) -and
		[string]::Equals(
			[string]$Shortcut.Description,
			$expectedDescription,
			[StringComparison]::Ordinal
		)
	)
}

$runtimeFiles = @(
	$sidecarName,
	$launcherName,
	'Restore Original Shortcuts.ps1',
	'Restore Original Shortcuts.cmd',
	'README.md',
	'LICENSE',
	'NOTICE.md',
	'VERSION'
)

foreach ($required in @(
	$runtimeFiles
)) {
	$source = Join-Path $packageDirectory $required
	if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
		throw "Required package file is missing: $source"
	}
}

New-Item -ItemType Directory -Path $InstallDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $backupDirectory -Force | Out-Null

$runningInstalledSidecar = @(
	Get-Process -Name 'ModrinthNoAdsSidecar' -ErrorAction SilentlyContinue |
		Where-Object {
			try {
				Test-SamePath $_.Path $installedSidecar
			}
			catch {
				$false
			}
		}
)
if ($runningInstalledSidecar.Count -gt 0) {
	throw (
		'The installed sidecar is running. Fully quit Modrinth and the sidecar, ' +
		'then run this installer again.'
	)
}

$stagingDirectory = Join-Path $InstallDirectory ('.staging-' + $PID)
$rollbackDirectory = Join-Path $InstallDirectory ('.rollback-' + $PID)
foreach ($temporaryDirectory in @($stagingDirectory, $rollbackDirectory)) {
	if (Test-Path -LiteralPath $temporaryDirectory) {
		Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force
	}
	New-Item -ItemType Directory -Path $temporaryDirectory -Force | Out-Null
}

try {
	foreach ($file in $runtimeFiles) {
		Copy-Item `
			-LiteralPath (Join-Path $packageDirectory $file) `
			-Destination (Join-Path $stagingDirectory $file) `
			-Force
	}

	$stagedSidecar = Join-Path $stagingDirectory $sidecarName
	$stagedLauncher = Join-Path $stagingDirectory $launcherName
	& $stagedSidecar --self-test | Out-Null
	if ($LASTEXITCODE -ne 0) {
		throw "The staged sidecar failed its self-test with exit code $LASTEXITCODE."
	}
	$launcherTest = Start-Process `
		-FilePath $stagedLauncher `
		-ArgumentList '--self-test' `
		-Wait `
		-PassThru
	if ($launcherTest.ExitCode -ne 0) {
		throw (
			'The staged background launcher failed its self-test with exit code ' +
			$launcherTest.ExitCode +
			'.'
		)
	}

	$changedFiles = New-Object System.Collections.Generic.List[string]
	try {
		foreach ($file in $runtimeFiles) {
			$destination = Join-Path $InstallDirectory $file
			if (Test-Path -LiteralPath $destination -PathType Leaf) {
				Copy-Item `
					-LiteralPath $destination `
					-Destination (Join-Path $rollbackDirectory $file) `
					-Force
			}
			Copy-Item `
				-LiteralPath (Join-Path $stagingDirectory $file) `
				-Destination $destination `
				-Force
			$changedFiles.Add($file)
		}
	}
	catch {
		foreach ($file in $changedFiles) {
			$backup = Join-Path $rollbackDirectory $file
			$destination = Join-Path $InstallDirectory $file
			if (Test-Path -LiteralPath $backup -PathType Leaf) {
				Copy-Item -LiteralPath $backup -Destination $destination -Force
			}
			elseif (Test-Path -LiteralPath $destination -PathType Leaf) {
				Remove-Item -LiteralPath $destination -Force
			}
		}
		throw
	}
}
catch {
	throw (
		'Could not install the runtime. Fully quit Modrinth and the existing ' +
		'sidecar, then run this installer again. ' +
		$_.Exception.Message
	)
}
finally {
	foreach ($temporaryDirectory in @($stagingDirectory, $rollbackDirectory)) {
		if (Test-Path -LiteralPath $temporaryDirectory) {
			Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force
		}
	}
}

if ($null -eq $ShortcutRoots -or $ShortcutRoots.Count -eq 0) {
	$ShortcutRoots = @(
		[Environment]::GetFolderPath([Environment+SpecialFolder]::Desktop),
		[Environment]::GetFolderPath([Environment+SpecialFolder]::Programs),
		(Join-Path $env:APPDATA 'Microsoft\Internet Explorer\Quick Launch\User Pinned')
	)
}

$normalizedRoots = @(
	$ShortcutRoots |
		Where-Object {
			-not [string]::IsNullOrWhiteSpace($_) -and
			(Test-Path -LiteralPath $_ -PathType Container)
		} |
		ForEach-Object { Get-NormalizedPath $_ } |
		Sort-Object -Unique
)

if ($normalizedRoots.Count -eq 0) {
	throw 'No writable current-user shortcut locations were found.'
}

$entries = New-Object System.Collections.ArrayList
$previousLauncherPath = ''
$previousInstallDirectory = ''
$existingAllowedRoots = @()
if (Test-Path -LiteralPath $manifestPath -PathType Leaf) {
	try {
		$existingManifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 |
			ConvertFrom-Json
		if ([int]$existingManifest.version -ne 1) {
			throw 'Unsupported manifest version.'
		}
		$previousLauncherPath = [string]$existingManifest.launcherPath
		$previousInstallDirectory = [string]$existingManifest.installDirectory
		$existingAllowedRoots = @($existingManifest.allowedShortcutRoots)
		foreach ($item in @($existingManifest.shortcuts)) {
			[void]$entries.Add([ordered]@{
				path = [string]$item.path
				backupPath = [string]$item.backupPath
				createdByInstaller = [bool]$item.createdByInstaller
				appPath = [string]$item.appPath
				patchedTargetPath = Get-OptionalProperty $item 'patchedTargetPath'
				patchedArguments = Get-OptionalProperty $item 'patchedArguments'
				patchedWorkingDirectory = Get-OptionalProperty $item 'patchedWorkingDirectory'
				patchedIconLocation = Get-OptionalProperty $item 'patchedIconLocation'
				patchedDescription = Get-OptionalProperty $item 'patchedDescription'
			})
		}
	}
	catch {
		throw "The existing shortcut manifest is invalid; no shortcuts were changed. $($_.Exception.Message)"
	}
}

$preservedRoots = @(
	foreach ($root in $existingAllowedRoots) {
		try {
			if (
				-not [string]::IsNullOrWhiteSpace([string]$root) -and
				[IO.Path]::IsPathRooted([string]$root)
			) {
				Get-NormalizedPath ([string]$root)
			}
		}
		catch {
		}
	}
)
$normalizedRoots = @(
	@($normalizedRoots) + @($preservedRoots) |
		Sort-Object -Unique
)

$wsh = New-Object -ComObject WScript.Shell
$patched = 0
$alreadyPatched = 0
$skipped = 0
$defaultAppPath = $null

$candidateLinks = @()
if (-not $SeparateShortcutOnly) {
	$candidateLinks = @(
		foreach ($root in $normalizedRoots) {
			if (Test-Path -LiteralPath $root -PathType Container) {
				Get-ChildItem `
					-LiteralPath $root `
					-Filter '*.lnk' `
					-File `
					-Recurse `
					-ErrorAction SilentlyContinue
			}
		}
	) | Sort-Object FullName -Unique
}

foreach ($file in $candidateLinks) {
	$shortcut = $null
	try {
		$shortcut = $wsh.CreateShortcut($file.FullName)
		$entry = Find-Entry $entries $file.FullName
		$targetLeaf = [IO.Path]::GetFileName($shortcut.TargetPath)
		if (Test-SamePath $shortcut.TargetPath $installedLauncher) {
			if ($null -ne $entry) {
				Set-PatchedShortcutState $entry $shortcut
				if ([string]::IsNullOrWhiteSpace($defaultAppPath)) {
					$defaultAppPath = [string]$entry.appPath
				}
			}
			$alreadyPatched++
			continue
		}
		if (
			$null -ne $entry -and
			-not [string]::IsNullOrWhiteSpace($previousLauncherPath) -and
			(Test-SamePath $shortcut.TargetPath $previousLauncherPath)
		) {
			if (
				-not (
					Test-ShortcutMatchesRecordedState `
						$shortcut `
						$entry `
						$previousLauncherPath `
						$previousInstallDirectory
				)
			) {
				Write-Warning "Left a user-edited integrated shortcut unchanged: $($file.FullName)"
				$skipped++
				continue
			}
			$appPath = [string]$entry.appPath
			if ([string]::IsNullOrWhiteSpace($appPath)) {
				Write-Warning "Skipped an old integrated shortcut with no app path: $($file.FullName)"
				$skipped++
				continue
			}
			$shortcut.TargetPath = $installedLauncher
			$shortcut.Arguments = '--app "' + $appPath + '"'
			$shortcut.WorkingDirectory = $InstallDirectory
			$shortcut.IconLocation = $appPath + ',0'
			$shortcut.Description = 'Unofficial local sidecar launcher; not affiliated with Rinth, Inc.'
			$shortcut.Save()
			Set-PatchedShortcutState $entry $shortcut
			if ([string]::IsNullOrWhiteSpace($defaultAppPath)) {
				$defaultAppPath = $appPath
			}
			$patched++
			continue
		}
		if ($targetLeaf -ine 'Modrinth App.exe') {
			continue
		}
		if (-not [string]::IsNullOrWhiteSpace($shortcut.Arguments)) {
			Write-Warning "Skipped shortcut with unexpected arguments: $($file.FullName)"
			$skipped++
			continue
		}

		$appPath = Find-ModrinthApp $shortcut
		if ([string]::IsNullOrWhiteSpace($appPath)) {
			Write-Warning "Skipped shortcut because Modrinth App.exe could not be found: $($file.FullName)"
			$skipped++
			continue
		}
		if ([string]::IsNullOrWhiteSpace($defaultAppPath)) {
			$defaultAppPath = $appPath
		}

		if ($null -eq $entry) {
			$backupPath = Join-Path $backupDirectory ((Get-PathId $file.FullName) + '.lnk')
			$entry = [ordered]@{
				path = Get-NormalizedPath $file.FullName
				backupPath = $backupPath
				createdByInstaller = $false
				appPath = $appPath
				patchedTargetPath = ''
				patchedArguments = ''
				patchedWorkingDirectory = ''
				patchedIconLocation = ''
				patchedDescription = ''
			}
			[void]$entries.Add($entry)
		}
		else {
			$backupPath = [string]$entry.backupPath
			$entry.appPath = $appPath
		}

		Copy-Item -LiteralPath $file.FullName -Destination $backupPath -Force
		Write-Manifest $entries $normalizedRoots

		$shortcut.TargetPath = $installedLauncher
		$shortcut.Arguments = '--app "' + $appPath + '"'
		$shortcut.WorkingDirectory = $InstallDirectory
		$shortcut.IconLocation = $appPath + ',0'
		$shortcut.Description = 'Unofficial local sidecar launcher; not affiliated with Rinth, Inc.'
		$shortcut.Save()
		Set-PatchedShortcutState $entry $shortcut
		Write-Manifest $entries $normalizedRoots
		$patched++
	}
	catch {
		Write-Warning "Could not patch shortcut $($file.FullName): $($_.Exception.Message)"
		$skipped++
	}
}

if ([string]::IsNullOrWhiteSpace($defaultAppPath)) {
	$defaultAppPath = Find-ModrinthApp $null
}

$stableReady = $false
if (-not [string]::IsNullOrWhiteSpace($defaultAppPath)) {
	$programsDirectory = [Environment]::GetFolderPath(
		[Environment+SpecialFolder]::Programs
	)
	if ($normalizedRoots.Count -eq 1 -and $ShortcutRoots.Count -eq 1) {
		$programsDirectory = $normalizedRoots[0]
	}
	if (-not [string]::IsNullOrWhiteSpace($programsDirectory)) {
		New-Item -ItemType Directory -Path $programsDirectory -Force | Out-Null
		$stablePath = Join-Path $programsDirectory 'Unofficial Modrinth No-Ads Launcher.lnk'
		$existingEntry = Find-Entry $entries $stablePath
		$mayCreate = -not (Test-Path -LiteralPath $stablePath -PathType Leaf)

		if (-not $mayCreate) {
			$existingStable = $wsh.CreateShortcut($stablePath)
			if (Test-SamePath $existingStable.TargetPath $installedLauncher) {
				$mayCreate = $true
			}
			else {
				Write-Warning "Left an unrelated shortcut unchanged: $stablePath"
			}
		}

		if ($mayCreate) {
			if ($null -eq $existingEntry) {
				$existingEntry = [ordered]@{
					path = Get-NormalizedPath $stablePath
					backupPath = ''
					createdByInstaller = $true
					appPath = $defaultAppPath
					patchedTargetPath = ''
					patchedArguments = ''
					patchedWorkingDirectory = ''
					patchedIconLocation = ''
					patchedDescription = ''
				}
				[void]$entries.Add($existingEntry)
			}
			Write-Manifest $entries $normalizedRoots

			$stableShortcut = $wsh.CreateShortcut($stablePath)
			$stableShortcut.TargetPath = $installedLauncher
			$stableShortcut.Arguments = '--app "' + $defaultAppPath + '"'
			$stableShortcut.WorkingDirectory = $InstallDirectory
			$stableShortcut.IconLocation = $installedLauncher + ',0'
			$stableShortcut.Description = 'Unofficial launcher; not affiliated with Rinth, Inc.'
			$stableShortcut.Save()
			Set-PatchedShortcutState $existingEntry $stableShortcut
			Write-Manifest $entries $normalizedRoots
			$stableReady = $true
		}
	}
}

Write-Manifest $entries $normalizedRoots

if ($patched -eq 0 -and $alreadyPatched -eq 0 -and -not $stableReady) {
	throw (
		'No Modrinth shortcut was integrated. Use -SeparateShortcutOnly with a ' +
		'discoverable Modrinth installation, or pass a shortcut root containing ' +
		'an unmodified Modrinth App shortcut.'
	)
}

Write-Host ''
Write-Host 'Background shortcut integration installed.'
Write-Host "Patched shortcuts: $patched"
Write-Host "Already integrated: $alreadyPatched"
Write-Host "Skipped: $skipped"
Write-Host "Runtime: $InstallDirectory"
Write-Host ''
Write-Host (
	'For a taskbar pin, unpin the old entry and pin ' +
	'"Unofficial Modrinth No-Ads Launcher" from Start.'
)
