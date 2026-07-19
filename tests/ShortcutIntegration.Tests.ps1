[CmdletBinding()]
param(
	[Parameter(Mandatory = $true)][string] $PackageDirectory
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

$PackageDirectory = [IO.Path]::GetFullPath($PackageDirectory)
$installer = Join-Path $PackageDirectory 'Install Background Shortcut.ps1'
$restorer = Join-Path $PackageDirectory 'Restore Original Shortcuts.ps1'
$packageSidecar = Join-Path $PackageDirectory 'ModrinthNoAdsSidecar.exe'
foreach ($required in @($installer, $restorer, $packageSidecar)) {
	if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
		throw "Package test input is missing: $required"
	}
}

function Assert-True {
	param([bool] $Condition, [string] $Message)
	if (-not $Condition) {
		throw $Message
	}
}

function Get-NormalizedPath {
	param([Parameter(Mandatory = $true)][string] $Path)
	$fullPath = [IO.Path]::GetFullPath($Path)
	if (Test-Path -LiteralPath $fullPath) {
		$finalPath = [ModrinthNoAdsPathNative]::GetFinalPath($fullPath)
		if (-not [string]::IsNullOrWhiteSpace($finalPath)) {
			$fullPath = $finalPath
		}
	}
	return $fullPath.TrimEnd([char]'\', [char]'/')
}

function Test-PathWithin {
	param([string] $Path, [string] $Root)
	$fullPath = Get-NormalizedPath $Path
	$fullRoot = (Get-NormalizedPath $Root) + [IO.Path]::DirectorySeparatorChar
	return $fullPath.StartsWith($fullRoot, [StringComparison]::OrdinalIgnoreCase)
}

function New-Scenario {
	param([string] $Name, [string] $Parent, $Shell)
	$root = Join-Path $Parent $Name
	$shortcutRoot = Join-Path $root 'Shortcut Root'
	$appDirectory = Join-Path $root 'App Folder'
	$installDirectory = Join-Path $root 'Installed Runtime'
	$stateDirectory = Join-Path $root 'State'
	foreach ($directory in @($shortcutRoot, $appDirectory)) {
		New-Item -ItemType Directory -Path $directory -Force | Out-Null
	}

	$appPath = Join-Path $appDirectory 'Modrinth App.exe'
	Copy-Item -LiteralPath $packageSidecar -Destination $appPath -Force
	$otherPath = Join-Path $appDirectory 'Other App.exe'
	Copy-Item -LiteralPath $packageSidecar -Destination $otherPath -Force

	$officialPath = Join-Path $shortcutRoot 'Modrinth App.lnk'
	$official = $Shell.CreateShortcut($officialPath)
	$official.TargetPath = $appPath
	$official.Arguments = ''
	$official.WorkingDirectory = $appDirectory
	$official.IconLocation = $appPath + ',0'
	$official.Description = 'Test Modrinth shortcut'
	$official.Save()

	$argumentPath = Join-Path $shortcutRoot 'Modrinth With Arguments.lnk'
	$argumentLink = $Shell.CreateShortcut($argumentPath)
	$argumentLink.TargetPath = $appPath
	$argumentLink.Arguments = '--safe-mode'
	$argumentLink.WorkingDirectory = $appDirectory
	$argumentLink.IconLocation = $appPath + ',0'
	$argumentLink.Description = 'Must remain untouched'
	$argumentLink.Save()

	$unrelatedPath = Join-Path $shortcutRoot 'Unrelated.lnk'
	$unrelated = $Shell.CreateShortcut($unrelatedPath)
	$unrelated.TargetPath = $otherPath
	$unrelated.Arguments = ''
	$unrelated.WorkingDirectory = $appDirectory
	$unrelated.IconLocation = $otherPath + ',0'
	$unrelated.Description = 'Must remain untouched'
	$unrelated.Save()

	return [pscustomobject]@{
		Root = $root
		ShortcutRoot = $shortcutRoot
		AppPath = $appPath
		InstallDirectory = $installDirectory
		StateDirectory = $stateDirectory
		OfficialPath = $officialPath
		ArgumentPath = $argumentPath
		UnrelatedPath = $unrelatedPath
		StablePath = Join-Path $shortcutRoot 'Unofficial Modrinth No-Ads Launcher.lnk'
	}
}

function Install-Scenario {
	param($Scenario)
	& $installer `
		-InstallDirectory $Scenario.InstallDirectory `
		-StateDirectory $Scenario.StateDirectory `
		-ShortcutRoots @($Scenario.ShortcutRoot) `
		-AppPath $Scenario.AppPath
}

function Restore-Scenario {
	param($Scenario)
	& $restorer -StateDirectory $Scenario.StateDirectory
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
$testRoot = Join-Path $temporaryBase (
	'Modrinth NoAds Sidecar tests ' + [Guid]::NewGuid().ToString('N')
)
Assert-True (
	Test-PathWithin $testRoot $temporaryBase
) 'Refusing to create a test root outside the selected temporary directory.'
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null

$wsh = New-Object -ComObject WScript.Shell
try {
	$baseline = New-Scenario 'baseline with spaces' $testRoot $wsh
	$originalHash = (Get-FileHash -LiteralPath $baseline.OfficialPath -Algorithm SHA256).Hash
	$argumentHash = (Get-FileHash -LiteralPath $baseline.ArgumentPath -Algorithm SHA256).Hash
	$unrelatedHash = (Get-FileHash -LiteralPath $baseline.UnrelatedPath -Algorithm SHA256).Hash

	Install-Scenario $baseline
	$installedLauncher = Join-Path $baseline.InstallDirectory 'ModrinthNoAdsLauncher.exe'
	Assert-True (Test-Path -LiteralPath $installedLauncher) 'Launcher was not installed.'
	$patched = $wsh.CreateShortcut($baseline.OfficialPath)
	Assert-True (
		(Get-NormalizedPath $patched.TargetPath) -eq (Get-NormalizedPath $installedLauncher)
	) (
		'Official shortcut was not retargeted to the GUI launcher. Actual: ' +
		$patched.TargetPath +
		'; expected: ' +
		$installedLauncher
	)
	Assert-True (
		$patched.Arguments -eq ('--app "' + $baseline.AppPath + '"')
	) 'Patched shortcut arguments are incorrect.'
	Assert-True (Test-Path -LiteralPath $baseline.StablePath) 'Stable shortcut was not created.'

	$manifestPath = Join-Path $baseline.StateDirectory 'shortcuts.json'
	$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 |
		ConvertFrom-Json
	foreach ($root in @($manifest.allowedShortcutRoots)) {
		Assert-True (
			(Test-PathWithin ([string]$root) $baseline.Root) -or
			(Get-NormalizedPath ([string]$root) -eq (Get-NormalizedPath $baseline.ShortcutRoot))
		) 'Manifest allowed root escaped the test scenario.'
	}
	$backupCount = @(
		Get-ChildItem `
			-LiteralPath (Join-Path $baseline.StateDirectory 'backups') `
			-File
	).Count
	Assert-True ($backupCount -eq 1) 'Expected exactly one original shortcut backup.'

	Install-Scenario $baseline
	$backupCountAfterRerun = @(
		Get-ChildItem `
			-LiteralPath (Join-Path $baseline.StateDirectory 'backups') `
			-File
	).Count
	Assert-True ($backupCountAfterRerun -eq 1) 'Installer rerun created duplicate backups.'

	Restore-Scenario $baseline
	$restoredHash = (Get-FileHash -LiteralPath $baseline.OfficialPath -Algorithm SHA256).Hash
	Assert-True ($restoredHash -eq $originalHash) 'Original shortcut was not restored byte-for-byte.'
	Assert-True (-not (Test-Path -LiteralPath $baseline.StablePath)) 'Stable shortcut was not removed.'
	Assert-True (
		(Get-FileHash -LiteralPath $baseline.ArgumentPath -Algorithm SHA256).Hash -eq
		$argumentHash
	) 'Argument-bearing shortcut was changed.'
	Assert-True (
		(Get-FileHash -LiteralPath $baseline.UnrelatedPath -Algorithm SHA256).Hash -eq
		$unrelatedHash
	) 'Unrelated shortcut was changed.'

	$userEdit = New-Scenario 'user edit' $testRoot $wsh
	Install-Scenario $userEdit
	$edited = $wsh.CreateShortcut($userEdit.OfficialPath)
	$edited.Arguments = '--user-edit'
	$edited.Save()
	Restore-Scenario $userEdit
	$afterEditRestore = $wsh.CreateShortcut($userEdit.OfficialPath)
	Assert-True (
		$afterEditRestore.Arguments -eq '--user-edit'
	) 'Restore overwrote a user-edited shortcut.'

	$userDelete = New-Scenario 'user delete' $testRoot $wsh
	Install-Scenario $userDelete
	Remove-Item -LiteralPath $userDelete.OfficialPath -Force
	Restore-Scenario $userDelete
	Assert-True (
		-not (Test-Path -LiteralPath $userDelete.OfficialPath)
	) 'Restore recreated a shortcut deleted by the user.'

	$tamper = New-Scenario 'tampered manifest' $testRoot $wsh
	Install-Scenario $tamper
	$sentinelPath = Join-Path $tamper.Root 'outside-root-sentinel.lnk'
	$sentinel = $wsh.CreateShortcut($sentinelPath)
	$sentinel.TargetPath = $tamper.AppPath
	$sentinel.Arguments = '--sentinel'
	$sentinel.WorkingDirectory = Split-Path -Parent $tamper.AppPath
	$sentinel.IconLocation = $tamper.AppPath + ',0'
	$sentinel.Description = 'Sentinel'
	$sentinel.Save()
	$sentinelHash = (Get-FileHash -LiteralPath $sentinelPath -Algorithm SHA256).Hash
	$tamperedManifestPath = Join-Path $tamper.StateDirectory 'shortcuts.json'
	$tamperedManifest = Get-Content `
		-LiteralPath $tamperedManifestPath `
		-Raw `
		-Encoding UTF8 |
		ConvertFrom-Json
	$tamperedManifest.shortcuts = @($tamperedManifest.shortcuts) + @(
		[pscustomobject]@{
			path = $sentinelPath
			backupPath = ''
			createdByInstaller = $true
			appPath = $tamper.AppPath
			patchedTargetPath = $tamper.AppPath
			patchedArguments = '--sentinel'
			patchedWorkingDirectory = Split-Path -Parent $tamper.AppPath
			patchedIconLocation = $tamper.AppPath + ',0'
			patchedDescription = 'Sentinel'
		}
	)
	$tamperedManifest |
		ConvertTo-Json -Depth 6 |
		Set-Content -LiteralPath $tamperedManifestPath -Encoding UTF8
	$restoreRejected = $false
	try {
		Restore-Scenario $tamper
	}
	catch {
		$restoreRejected = $true
	}
	Assert-True $restoreRejected 'Restore accepted a manifest path outside allowed roots.'
	Assert-True (
		(Get-FileHash -LiteralPath $sentinelPath -Algorithm SHA256).Hash -eq $sentinelHash
	) 'Tampered manifest changed the outside sentinel.'
	$tamperLink = $wsh.CreateShortcut($tamper.OfficialPath)
	Assert-True (
		(Get-NormalizedPath $tamperLink.TargetPath) -eq
		(Get-NormalizedPath (Join-Path $tamper.InstallDirectory 'ModrinthNoAdsLauncher.exe'))
	) 'Restore partially changed shortcuts before rejecting the tampered manifest.'

	Write-Host 'Shortcut integration tests passed.'
}
finally {
	if (
		(Test-Path -LiteralPath $testRoot) -and
		(Test-PathWithin $testRoot $temporaryBase)
	) {
		Remove-Item -LiteralPath $testRoot -Recurse -Force
	}
}
