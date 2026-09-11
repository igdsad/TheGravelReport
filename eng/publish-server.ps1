<#
.SYNOPSIS
Creates and validates the self-contained GravelReview Linux x64 server artifact.

.DESCRIPTION
Performs a locked restore, publishes the headless custom-event receiver, verifies
its Linux apphost/native SQLite inventory, and creates a versioned tar.gz plus a
SHA-256 sidecar for deployment or release upload.
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

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

    throw 'The .NET SDK could not be found on PATH or through DOTNET_ROOT.'
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
        throw "Refusing to clean a directory outside '$fullRoot': $fullCandidate"
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
            throw "The release directory '$fullCandidate' has no parent within '$fullRoot'."
        }

        $pathToInspect = $parent.FullName
    }

    return $fullCandidate
}

function Invoke-DotNet {
    param([Parameter(Mandatory)][string[]] $ArgumentList)

    & $script:DotNet @ArgumentList
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet exited with code $LASTEXITCODE."
    }
}

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path -Path $PSScriptRoot -ChildPath '..'))
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path -Path $repositoryRoot -ChildPath 'artifacts'))
$releaseDirectory = Resolve-OwnedChildDirectory `
    -OwnedRoot $artifactsRoot `
    -Candidate (Join-Path -Path $artifactsRoot -ChildPath 'release\linux-x64')
$publishDirectory = Join-Path -Path $releaseDirectory -ChildPath 'server'
$hostProject = Join-Path -Path $repositoryRoot -ChildPath 'src\IncidentReview.Host.Server\IncidentReview.Host.Server.csproj'

if (-not (Test-Path -LiteralPath $hostProject -PathType Leaf)) {
    throw "Server project not found: $hostProject"
}

[xml]$projectXml = Get-Content -LiteralPath $hostProject -Raw
$properties = $projectXml.SelectSingleNode('/Project/PropertyGroup')
$version = $properties.Version
if ([string]::IsNullOrWhiteSpace($version) -or
    $properties.RuntimeIdentifier -cne 'linux-x64' -or
    $properties.SelfContained -cne 'true') {
    throw 'The server project must declare a version and remain self-contained linux-x64.'
}

if ($version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') {
    throw "The server version is not release-safe: $version"
}

$archiveName = "GravelReview-server-linux-x64-v$version.tar.gz"
$archivePath = Join-Path -Path $releaseDirectory -ChildPath $archiveName
$checksumPath = $archivePath + '.sha256'

if (Test-Path -LiteralPath $releaseDirectory -PathType Container) {
    Remove-Item -LiteralPath $releaseDirectory -Recurse -Force
}
[void][IO.Directory]::CreateDirectory($publishDirectory)

$script:DotNet = Find-DotNet
Push-Location -LiteralPath $repositoryRoot
try {
    Invoke-DotNet -ArgumentList @(
        'restore', $hostProject,
        '--locked-mode',
        '-p:RestoreLockedMode=true'
    )
    Invoke-DotNet -ArgumentList @(
        'publish', $hostProject,
        '--configuration', 'Release',
        '--no-restore',
        '--output', $publishDirectory
    )
}
finally {
    Pop-Location
}

$appHost = Join-Path -Path $publishDirectory -ChildPath 'IncidentReview.Host.Server'
$nativeSqlite = Join-Path -Path $publishDirectory -ChildPath 'libe_sqlite3.so'
$deploymentGuide = Join-Path -Path $publishDirectory -ChildPath 'DEPLOYMENT.md'
foreach ($requiredFile in @($appHost, $nativeSqlite, $deploymentGuide)) {
    if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf) -or
        (Get-Item -LiteralPath $requiredFile).Length -le 0) {
        throw "Required server release file is missing or empty: $requiredFile"
    }
}

if (@(Get-ChildItem -LiteralPath $publishDirectory -Filter '*.pdb' -File -Recurse).Count -ne 0) {
    throw 'The Release server artifact unexpectedly contains portable symbols.'
}

$stream = [IO.File]::OpenRead($appHost)
try {
    $magic = [byte[]]::new(4)
    if ($stream.Read($magic, 0, $magic.Length) -ne $magic.Length -or
        $magic[0] -ne 0x7f -or
        $magic[1] -ne 0x45 -or
        $magic[2] -ne 0x4c -or
        $magic[3] -ne 0x46) {
        throw 'The published server apphost is not a Linux ELF executable.'
    }
}
finally {
    $stream.Dispose()
}

$tar = Get-Command tar -CommandType Application -ErrorAction Stop | Select-Object -First 1
& $tar.Path -C $publishDirectory -czf $archivePath .
if ($LASTEXITCODE -ne 0) {
    throw "tar exited with code $LASTEXITCODE."
}

if (-not (Test-Path -LiteralPath $archivePath -PathType Leaf) -or
    (Get-Item -LiteralPath $archivePath).Length -le 0) {
    throw 'The server release archive was not created.'
}

$archiveEntries = @(
    & $tar.Path -tzf $archivePath |
        ForEach-Object {
            $entry = $_.Replace('\', '/')
            if ($entry.StartsWith('./', [StringComparison]::Ordinal)) {
                $entry.Substring(2)
            }
            else {
                $entry
            }
        }
)
if ($LASTEXITCODE -ne 0 -or
    $archiveEntries -notcontains 'IncidentReview.Host.Server' -or
    $archiveEntries -notcontains 'libe_sqlite3.so' -or
    $archiveEntries -notcontains 'DEPLOYMENT.md' -or
    @($archiveEntries | Where-Object { $_ -like '*.pdb' }).Count -ne 0) {
    throw 'The server release archive inventory is invalid.'
}

$hash = Get-FileHash -LiteralPath $archivePath -Algorithm SHA256
[IO.File]::WriteAllText(
    $checksumPath,
    "$($hash.Hash.ToLowerInvariant())  $archiveName`n",
    [Text.UTF8Encoding]::new($false))

$fileCount = @(Get-ChildItem -LiteralPath $publishDirectory -File -Recurse).Count
$byteCount = (Get-ChildItem -LiteralPath $publishDirectory -File -Recurse |
    Measure-Object -Property Length -Sum).Sum
Write-Output 'GravelReview Linux x64 server release passed artifact validation.'
Write-Output "Published files: $fileCount"
Write-Output "Published bytes: $byteCount"
Write-Output "Archive path: $archivePath"
Write-Output "Archive SHA256: $($hash.Hash)"
Write-Output "Checksum path: $checksumPath"
