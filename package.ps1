[CmdletBinding()]
param(
    [string]$Preset = "Windows Desktop",
    [string]$ExportFileName = "Rhythia.exe",
    [string]$ArchiveName = "windows.zip"
)

$ErrorActionPreference = "Stop"
$projectRoot = $PSScriptRoot
$Preset = $Preset.Trim()
$ExportFileName = $ExportFileName.Trim()
$ArchiveName = $ArchiveName.Trim()
$presetFile = Join-Path $projectRoot "export_presets.cfg"

if (-not (Test-Path -LiteralPath $presetFile -PathType Leaf)) {
    throw "Missing export_presets.cfg. Create an export preset named '$Preset' in the Godot editor first."
}

$presetContents = Get-Content -LiteralPath $presetFile -Raw
if ($presetContents -notmatch ('(?im)^name="' + [Regex]::Escape($Preset) + '"$')) {
    throw "Export preset '$Preset' was not found in export_presets.cfg."
}

$GodotExecutable = Join-Path $projectRoot ".godot-sdk\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe"
if (-not (Test-Path -LiteralPath $GodotExecutable -PathType Leaf)) {
    throw "Project-bundled Godot .NET executable not found: $GodotExecutable"
}

if ($Preset -eq "Windows Desktop") {
    $templateDirectory = Join-Path $env:APPDATA "Godot\export_templates\4.7.2.stable.mono"
    $requiredTemplates = @("windows_debug_x86_64.exe", "windows_release_x86_64.exe")
    $missingTemplates = @($requiredTemplates | Where-Object {
        -not (Test-Path -LiteralPath (Join-Path $templateDirectory $_) -PathType Leaf)
    })
    if ($missingTemplates.Count -gt 0) {
        throw "Windows export templates are missing from '$templateDirectory': $($missingTemplates -join ', '). Install the Godot 4.7.2 .NET export templates from the bundled editor's Project > Export > Manage Export Templates, then rerun this script."
    }
}

if ([IO.Path]::IsPathRooted($ArchiveName) -or [IO.Path]::GetFileName($ArchiveName) -ne $ArchiveName) {
    throw "ArchiveName must be a file name, not a path."
}
if ([IO.Path]::IsPathRooted($ExportFileName) -or [IO.Path]::GetFileName($ExportFileName) -ne $ExportFileName) {
    throw "ExportFileName must be a file name, not a path."
}

$outputDirectory = Join-Path $projectRoot "build\packages"
$archivePath = Join-Path $outputDirectory $ArchiveName
$stageDirectory = Join-Path $outputDirectory (".package-" + [Guid]::NewGuid().ToString("N"))
$exportPath = Join-Path $stageDirectory $ExportFileName
$projectFilesToPreserve = @{}
foreach ($fileName in @("project.godot", "packages.lock.json")) {
    $filePath = Join-Path $projectRoot $fileName
    if (Test-Path -LiteralPath $filePath -PathType Leaf) {
        $projectFilesToPreserve[$filePath] = [IO.File]::ReadAllBytes($filePath)
    }
}
$bundledDotnetDirectory = Join-Path $projectRoot ".dotnet"
$previousDotnetRoot = $env:DOTNET_ROOT
$previousPath = $env:PATH
if (Test-Path -LiteralPath (Join-Path $bundledDotnetDirectory "dotnet.exe") -PathType Leaf) {
    $env:DOTNET_ROOT = $bundledDotnetDirectory
    $env:PATH = "$bundledDotnetDirectory;$previousPath"
}

function Invoke-Godot {
    param([string[]]$Arguments, [string]$Action)

    $quotedArguments = $Arguments | ForEach-Object { '"' + $_.Replace('"', '\"') + '"' }
    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $GodotExecutable
    $startInfo.Arguments = $quotedArguments -join " "
    $startInfo.WorkingDirectory = $projectRoot
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true

    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $startInfo
    $process.Start() | Out-Null
    $standardOutput = $process.StandardOutput.ReadToEndAsync()
    $standardError = $process.StandardError.ReadToEndAsync()
    $process.WaitForExit()
    if ($standardOutput.Result) {
        [Console]::Out.Write($standardOutput.Result)
    }
    if ($standardError.Result) {
        [Console]::Error.Write($standardError.Result)
    }
    if ($process.ExitCode -ne 0) {
        throw "Godot failed to $Action (exit code $($process.ExitCode))."
    }
}

try {
    New-Item -ItemType Directory -Path $stageDirectory -Force | Out-Null
    Invoke-Godot -Arguments @("--headless", "--path", $projectRoot, "--editor", "--build-solutions", "--quit") -Action "build the C# solution"
    Invoke-Godot -Arguments @("--headless", "--path", $projectRoot, "--export-release", $Preset, $exportPath) -Action "export preset '$Preset'"
    if (-not (Test-Path -LiteralPath $exportPath -PathType Leaf)) {
        throw "Godot completed the export but did not create '$exportPath'."
    }

    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
    $stageContents = Get-ChildItem -LiteralPath $stageDirectory -Force
    Compress-Archive -Path $stageContents.FullName -DestinationPath $archivePath -Force
    Write-Output "Packaged game: $archivePath"
}
finally {
    try {
        foreach ($filePath in $projectFilesToPreserve.Keys) {
            [IO.File]::WriteAllBytes($filePath, $projectFilesToPreserve[$filePath])
        }
    } finally {
        if (Test-Path -LiteralPath $stageDirectory) {
            Remove-Item -LiteralPath $stageDirectory -Recurse -Force
        }
        $env:DOTNET_ROOT = $previousDotnetRoot
        $env:PATH = $previousPath
    }
}
