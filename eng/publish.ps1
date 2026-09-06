<#
.SYNOPSIS
Creates and smoke-tests the GravelReview Windows x64 release artifacts.

.DESCRIPTION
Performs a locked restore, publishes the WPF host with its repository-owned
self-contained single-file profile, and validates the exact release inventory.
The GitHub-ready ZIP contains the portable executable and the required iRacing
SDK notice. The executable is also left beside the ZIP for local use.
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Find-DotNet {
    $command = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($null -ne $command) {
        return $command.Path
    }

    $dotnetRoot = [Environment]::GetEnvironmentVariable('DOTNET_ROOT')
    if (-not [string]::IsNullOrWhiteSpace($dotnetRoot)) {
        $candidate = Join-Path -Path $dotnetRoot -ChildPath 'dotnet.exe'
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return $candidate
        }
    }

    $roots = @(
        [Environment]::GetEnvironmentVariable('ProgramW6432'),
        [Environment]::GetEnvironmentVariable('ProgramFiles'),
        'C:\Program Files'
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique

    foreach ($root in $roots) {
        $candidate = Join-Path -Path $root -ChildPath 'dotnet\dotnet.exe'
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return $candidate
        }
    }

    throw 'The .NET SDK could not be found on PATH or under the standard Windows installation roots.'
}

function Invoke-DotNet {
    param([Parameter(Mandatory)][string[]] $ArgumentList)

    & $script:DotNet @ArgumentList
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet exited with code $LASTEXITCODE."
    }
}

function Resolve-OwnedChildDirectory {
    param(
        [Parameter(Mandatory)][string] $OwnedRoot,
        [Parameter(Mandatory)][string] $Candidate
    )

    $fullRoot = [IO.Path]::GetFullPath($OwnedRoot).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    $fullCandidate = [IO.Path]::GetFullPath($Candidate).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    $requiredPrefix = $fullRoot + [IO.Path]::DirectorySeparatorChar

    if (-not $fullCandidate.StartsWith($requiredPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean a directory outside the owned root '$fullRoot': $fullCandidate"
    }

    $pathToInspect = $fullCandidate
    while ($pathToInspect.StartsWith($fullRoot, [StringComparison]::OrdinalIgnoreCase)) {
        if (Test-Path -LiteralPath $pathToInspect) {
            $attributes = (Get-Item -LiteralPath $pathToInspect -Force).Attributes
            if (($attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Refusing to clean through a reparse point: $pathToInspect"
            }
        }

        if ([string]::Equals($pathToInspect, $fullRoot, [StringComparison]::OrdinalIgnoreCase)) {
            break
        }

        $parent = [IO.Directory]::GetParent($pathToInspect)
        if ($null -eq $parent) {
            throw "The owned directory '$fullCandidate' has no parent within '$fullRoot'."
        }
        $pathToInspect = $parent.FullName
    }

    return $fullCandidate
}

function Get-RelativeFileInventory {
    param([Parameter(Mandatory)][string] $Directory)

    $fullDirectory = [IO.Path]::GetFullPath($Directory).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    $directoryPrefix = $fullDirectory + [IO.Path]::DirectorySeparatorChar
    return @(
        Get-ChildItem -LiteralPath $Directory -File -Recurse |
            ForEach-Object {
                if (-not $_.FullName.StartsWith(
                        $directoryPrefix,
                        [StringComparison]::OrdinalIgnoreCase)) {
                    throw "Release file escaped the expected directory: $($_.FullName)"
                }

                $_.FullName.Substring($directoryPrefix.Length).Replace('\', '/')
            } |
            Sort-Object
    )
}

function Assert-FileInventory {
    param(
        [Parameter(Mandatory)][string] $Directory,
        [Parameter(Mandatory)][string[]] $ExpectedPaths
    )

    $actual = @(Get-RelativeFileInventory -Directory $Directory)
    $expected = @($ExpectedPaths | Sort-Object)
    $difference = @(Compare-Object -ReferenceObject $expected -DifferenceObject $actual)
    if ($difference.Count -ne 0) {
        $actualDescription = if ($actual.Count -eq 0) { '<empty>' } else { $actual -join ', ' }
        throw "Unexpected release inventory in '$Directory': $actualDescription"
    }
}

function New-ReleaseArchive {
    param(
        [Parameter(Mandatory)][string] $Executable,
        [Parameter(Mandatory)][string] $Notice,
        [Parameter(Mandatory)][string] $ArchivePath
    )

    $archive = [IO.Compression.ZipFile]::Open(
        $ArchivePath,
        [IO.Compression.ZipArchiveMode]::Create)
    try {
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $archive,
            $Executable,
            'GravelReview-win-x64.exe',
            [IO.Compression.CompressionLevel]::Optimal)
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $archive,
            $Notice,
            'THIRD-PARTY-NOTICES/iracing-sdk-1.20.md',
            [IO.Compression.CompressionLevel]::Optimal)
    }
    finally {
        $archive.Dispose()
    }

    $readArchive = [IO.Compression.ZipFile]::OpenRead($ArchivePath)
    try {
        $entries = @(
            $readArchive.Entries |
                Where-Object { -not $_.FullName.EndsWith('/', [StringComparison]::Ordinal) } |
                ForEach-Object { $_.FullName.Replace('\', '/') } |
                Sort-Object
        )
        $expectedEntries = @(
            'GravelReview-win-x64.exe',
            'THIRD-PARTY-NOTICES/iracing-sdk-1.20.md'
        ) | Sort-Object
        if (@(Compare-Object -ReferenceObject $expectedEntries -DifferenceObject $entries).Count -ne 0) {
            throw "Unexpected ZIP inventory: $($entries -join ', ')"
        }

        foreach ($entry in $readArchive.Entries) {
            if (-not $entry.FullName.EndsWith('/', [StringComparison]::Ordinal) -and
                $entry.Length -le 0) {
                throw "ZIP entry '$($entry.FullName)' is empty."
            }
        }
    }
    finally {
        $readArchive.Dispose()
    }
}

function Invoke-StartupSmoke {
    param(
        [Parameter(Mandatory)][string] $Executable,
        [Parameter(Mandatory)][string] $WorkingDirectory
    )

    $smokeRoot = [IO.Path]::GetFullPath((Join-Path -Path ([IO.Path]::GetTempPath()) -ChildPath 'GravelReview.PublishSmoke'))
    $runDirectory = Resolve-OwnedChildDirectory `
        -OwnedRoot $smokeRoot `
        -Candidate (Join-Path -Path $smokeRoot -ChildPath ([Guid]::NewGuid().ToString('D')))
    [void][IO.Directory]::CreateDirectory($runDirectory)
    $databasePath = Join-Path -Path $runDirectory -ChildPath 'incident-review.db'
    $unavailableRuntimeRoot = Join-Path -Path $runDirectory -ChildPath 'no-dotnet-runtime'

    try {
        $hashBefore = (Get-FileHash -LiteralPath $Executable -Algorithm SHA256).Hash
        $startInfo = [Diagnostics.ProcessStartInfo]::new()
        $startInfo.FileName = $Executable
        $startInfo.WorkingDirectory = $WorkingDirectory
        $startInfo.UseShellExecute = $false
        $startInfo.CreateNoWindow = $true
        $startInfo.RedirectStandardOutput = $true
        $startInfo.RedirectStandardError = $true
        $startInfo.EnvironmentVariables['DOTNET_ROOT'] = $unavailableRuntimeRoot
        $startInfo.EnvironmentVariables['DOTNET_ROOT_X64'] = $unavailableRuntimeRoot
        $startInfo.EnvironmentVariables['DOTNET_MULTILEVEL_LOOKUP'] = '0'
        $startInfo.Arguments =
            "--verify-startup --database-path `"$databasePath`" --startup-timeout-seconds 10"

        $process = [Diagnostics.Process]::new()
        $process.StartInfo = $startInfo
        try {
            if (-not $process.Start()) {
                throw 'The packaged host process could not be started.'
            }

            $standardOutput = $process.StandardOutput.ReadToEndAsync()
            $standardError = $process.StandardError.ReadToEndAsync()
            if (-not $process.WaitForExit(60000)) {
                $process.Kill()
                $process.WaitForExit()
                throw 'The packaged host did not finish startup verification in 60 seconds.'
            }

            $outputText = $standardOutput.GetAwaiter().GetResult()
            $errorText = $standardError.GetAwaiter().GetResult()
            if ($process.ExitCode -ne 0) {
                throw "The packaged host startup verification exited with code $($process.ExitCode). Output: $outputText Error: $errorText"
            }
        }
        finally {
            $process.Dispose()
        }

        if (-not (Test-Path -LiteralPath $databasePath -PathType Leaf) -or
            (Get-Item -LiteralPath $databasePath).Length -le 0) {
            throw 'The packaged host did not create its isolated smoke-test database.'
        }

        $hashAfter = (Get-FileHash -LiteralPath $Executable -Algorithm SHA256).Hash
        if (-not [string]::Equals($hashBefore, $hashAfter, [StringComparison]::Ordinal)) {
            throw 'The packaged executable changed during startup verification.'
        }
    }
    finally {
        $validatedRunDirectory = Resolve-OwnedChildDirectory `
            -OwnedRoot $smokeRoot `
            -Candidate $runDirectory
        if (Test-Path -LiteralPath $validatedRunDirectory -PathType Container) {
            Remove-Item -LiteralPath $validatedRunDirectory -Recurse -Force
        }
    }
}

function Write-ArtifactReport {
    param(
        [Parameter(Mandatory)][string] $Label,
        [Parameter(Mandatory)][string] $Path
    )

    $file = Get-Item -LiteralPath $Path
    $hash = Get-FileHash -LiteralPath $Path -Algorithm SHA256
    Write-Output "$Label path: $($file.FullName)"
    Write-Output "$Label size: $($file.Length) bytes"
    Write-Output "$Label SHA256: $($hash.Hash)"
}

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path -Path $PSScriptRoot -ChildPath '..'))
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path -Path $repositoryRoot -ChildPath 'artifacts'))
$releaseDirectory = Resolve-OwnedChildDirectory `
    -OwnedRoot $artifactsRoot `
    -Candidate (Join-Path -Path $artifactsRoot -ChildPath 'release\win-x64')
$hostProject = Join-Path -Path $repositoryRoot -ChildPath 'src\IncidentReview.Host.Wpf\IncidentReview.Host.Wpf.csproj'
$publishProfile = Join-Path -Path $repositoryRoot -ChildPath 'src\IncidentReview.Host.Wpf\Properties\PublishProfiles\win-x64-single-file.pubxml'
$internalExecutable = Join-Path -Path $releaseDirectory -ChildPath 'IncidentReview.Host.Wpf.exe'
$releaseExecutable = Join-Path -Path $releaseDirectory -ChildPath 'GravelReview-win-x64.exe'
$notice = Join-Path -Path $releaseDirectory -ChildPath 'THIRD-PARTY-NOTICES\iracing-sdk-1.20.md'
$releaseArchive = Join-Path -Path $releaseDirectory -ChildPath 'GravelReview-win-x64.zip'

foreach ($requiredFile in @($hostProject, $publishProfile)) {
    if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
        throw "Required release input not found: $requiredFile"
    }
}

[xml]$hostProjectXml = Get-Content -LiteralPath $hostProject -Raw
$assemblyNameNode = $hostProjectXml.SelectSingleNode('/Project/PropertyGroup/AssemblyName')
if ($null -eq $assemblyNameNode -or $assemblyNameNode.InnerText -ne 'IncidentReview.Host.Wpf') {
    throw 'The compatibility-stable host assembly identity must remain IncidentReview.Host.Wpf.'
}
$singleFileNode = $hostProjectXml.SelectSingleNode('/Project/PropertyGroup/PublishSingleFile')
if ($null -eq $singleFileNode -or $singleFileNode.InnerText -ne 'true') {
    throw 'The host project must keep PublishSingleFile enabled for its stable locked restore graph.'
}

[xml]$publishProfileXml = Get-Content -LiteralPath $publishProfile -Raw
$requiredProfileProperties = [ordered]@{
    RuntimeIdentifier = 'win-x64'
    SelfContained = 'true'
    IncludeNativeLibrariesForSelfExtract = 'true'
    PublishTrimmed = 'false'
    DebugSymbols = 'false'
    DebugType = 'None'
    CopyOutputSymbolsToPublishDirectory = 'false'
}
foreach ($property in $requiredProfileProperties.GetEnumerator()) {
    $propertyNode = $publishProfileXml.SelectSingleNode("/Project/PropertyGroup/$($property.Key)")
    if ($null -eq $propertyNode -or $propertyNode.InnerText -cne $property.Value) {
        throw "Publish profile property '$($property.Key)' must be '$($property.Value)'."
    }
}

if (Test-Path -LiteralPath $releaseDirectory -PathType Container) {
    Remove-Item -LiteralPath $releaseDirectory -Recurse -Force
}
[void][IO.Directory]::CreateDirectory($releaseDirectory)

$script:DotNet = Find-DotNet
Push-Location -LiteralPath $repositoryRoot
try {
    Invoke-DotNet -ArgumentList @(
        'restore', $hostProject,
        '--locked-mode',
        '-p:RestoreLockedMode=true',
        '-p:PublishProfile=win-x64-single-file'
    )
    Invoke-DotNet -ArgumentList @(
        'publish', $hostProject,
        '--configuration', 'Release',
        '--no-restore',
        '--output', $releaseDirectory,
        '-p:PublishProfile=win-x64-single-file'
    )
}
finally {
    Pop-Location
}

Assert-FileInventory -Directory $releaseDirectory -ExpectedPaths @(
    'IncidentReview.Host.Wpf.exe',
    'THIRD-PARTY-NOTICES/iracing-sdk-1.20.md'
)
Move-Item -LiteralPath $internalExecutable -Destination $releaseExecutable

$versionInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($releaseExecutable)
if ($versionInfo.FileDescription -ne 'GravelReview' -or
    $versionInfo.ProductName -ne 'GravelReview') {
    throw 'The packaged executable is missing GravelReview product branding.'
}

New-ReleaseArchive `
    -Executable $releaseExecutable `
    -Notice $notice `
    -ArchivePath $releaseArchive
Assert-FileInventory -Directory $releaseDirectory -ExpectedPaths @(
    'GravelReview-win-x64.exe',
    'GravelReview-win-x64.zip',
    'THIRD-PARTY-NOTICES/iracing-sdk-1.20.md'
)

Invoke-StartupSmoke -Executable $releaseExecutable -WorkingDirectory $releaseDirectory

Write-Output 'GravelReview Windows x64 release passed its packaged startup smoke test.'
Write-ArtifactReport -Label 'Executable' -Path $releaseExecutable
Write-ArtifactReport -Label 'Release ZIP' -Path $releaseArchive
Write-ArtifactReport -Label 'iRacing notice' -Path $notice
