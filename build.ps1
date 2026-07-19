[CmdletBinding()]
param(
	[string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$versionPath = Join-Path $root 'VERSION'
$source = Join-Path $root 'src\ModrinthNoAdsSidecar.cs'
$launcherSource = Join-Path $root 'src\ModrinthNoAdsLauncher.cs'
$mainBootstrap = Join-Path $root 'src\bootstrap-main.js'
$adBootstrap = Join-Path $root 'src\bootstrap-ad.js'

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
	$OutputDirectory = Join-Path $root 'artifacts\bin'
}
elseif (-not [IO.Path]::IsPathRooted($OutputDirectory)) {
	$OutputDirectory = Join-Path $root $OutputDirectory
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

$output = Join-Path $OutputDirectory 'ModrinthNoAdsSidecar.exe'
$launcherOutput = Join-Path $OutputDirectory 'ModrinthNoAdsLauncher.exe'

if (-not (Test-Path -LiteralPath $versionPath -PathType Leaf)) {
	throw "Version file not found: $versionPath"
}
$version = (Get-Content -LiteralPath $versionPath -Raw -Encoding UTF8).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') {
	throw "VERSION must contain a semantic version such as 1.2.3; found '$version'."
}
$assemblyVersion = $version + '.0'
foreach ($versionedSource in @($source, $launcherSource)) {
	$content = Get-Content -LiteralPath $versionedSource -Raw -Encoding UTF8
	if ($content -notmatch [Regex]::Escape("AssemblyVersion(`"$assemblyVersion`")")) {
		throw "$versionedSource does not contain AssemblyVersion(`"$assemblyVersion`")."
	}
	if ($content -notmatch [Regex]::Escape("AssemblyFileVersion(`"$assemblyVersion`")")) {
		throw "$versionedSource does not contain AssemblyFileVersion(`"$assemblyVersion`")."
	}
}
if (
	(Get-Content -LiteralPath $source -Raw -Encoding UTF8) -notmatch
	[Regex]::Escape("private const string Version = `"$version`";")
) {
	throw "$source does not contain the CLI version '$version'."
}

$compilerCandidates = @(
	"$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
	"$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
)
$compiler = $compilerCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1

if (-not $compiler) {
	throw 'The Windows .NET Framework C# compiler was not found.'
}

$references = @(
	"$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\System.dll",
	"$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\System.Core.dll",
	"$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\System.Net.Http.dll",
	"$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\System.Web.Extensions.dll"
)

if ($compiler -like '*\Framework\*') {
	$references = $references -replace '\\Framework64\\', '\Framework\'
}

foreach ($reference in $references) {
	if (-not (Test-Path -LiteralPath $reference)) {
		throw "Required .NET Framework assembly not found: $reference"
	}
}

$arguments = @(
	'/nologo',
	'/target:exe',
	'/platform:anycpu',
	'/optimize+',
	'/debug-',
	'/warn:4',
	'/warnaserror+',
	"/out:$output",
	"/reference:$($references[0])",
	"/reference:$($references[1])",
	"/reference:$($references[2])",
	"/reference:$($references[3])",
	"/resource:$mainBootstrap,ModrinthNoAds.bootstrap-main.js",
	"/resource:$adBootstrap,ModrinthNoAds.bootstrap-ad.js",
	$source
)

& $compiler $arguments
if ($LASTEXITCODE -ne 0) {
	throw "Compilation failed with exit code $LASTEXITCODE."
}

$launcherArguments = @(
	'/nologo',
	'/target:winexe',
	'/platform:anycpu',
	'/optimize+',
	'/debug-',
	'/warn:4',
	'/warnaserror+',
	"/out:$launcherOutput",
	"/reference:$($references[0])",
	"/reference:$($references[1])",
	$launcherSource
)

& $compiler $launcherArguments
if ($LASTEXITCODE -ne 0) {
	throw "Background launcher compilation failed with exit code $LASTEXITCODE."
}

& $output --self-test
if ($LASTEXITCODE -ne 0) {
	throw "The compiled sidecar failed its self-test with exit code $LASTEXITCODE."
}

$launcherTest = Start-Process `
	-FilePath $launcherOutput `
	-ArgumentList '--self-test' `
	-Wait `
	-PassThru
if ($launcherTest.ExitCode -ne 0) {
	throw (
		"The windowless launcher failed to run the sidecar self-test with exit code " +
		$launcherTest.ExitCode +
		'.'
	)
}

Write-Host "Built: $output"
Write-Host "Built: $launcherOutput"
