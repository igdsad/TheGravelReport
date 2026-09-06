<#
.SYNOPSIS
Builds and tests the repository without allowing test-time restore or rebuild.

.PARAMETER Configuration
The configuration to build and test. Defaults to Debug.

.PARAMETER NoRestore
Uses an existing restore. The build and test commands still pass --no-restore.

.PARAMETER NoBuild
Uses existing binaries. Tests still pass --no-build and --no-restore.

.PARAMETER UpdateLockFiles
Regenerates package lock files during the explicit restore.
#>
[CmdletBinding()]
param(
    [ValidateNotNullOrEmpty()]
    [string] $Configuration = 'Debug',

    [switch] $NoRestore,

    [switch] $NoBuild,

    [switch] $UpdateLockFiles
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($NoRestore -and $UpdateLockFiles) {
    throw '-NoRestore and -UpdateLockFiles cannot be used together.'
}

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

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path -Path $PSScriptRoot -ChildPath '..'))
$solution = Join-Path -Path $repositoryRoot -ChildPath 'IncidentReview.slnx'
if (-not (Test-Path -LiteralPath $solution -PathType Leaf)) {
    throw "Repository solution not found: $solution"
}

$script:DotNet = Find-DotNet

if (-not $NoBuild) {
    $buildParameters = @{ Configuration = $Configuration }
    if ($NoRestore) {
        $buildParameters.NoRestore = $true
    }
    if ($UpdateLockFiles) {
        $buildParameters.UpdateLockFiles = $true
    }

    & (Join-Path -Path $PSScriptRoot -ChildPath 'build.ps1') @buildParameters
}
elseif (-not $NoRestore) {
    Push-Location -LiteralPath $repositoryRoot
    try {
        $restoreArguments = @('restore', $solution)
        if (-not $UpdateLockFiles) {
            $restoreArguments += @('--locked-mode', '-p:RestoreLockedMode=true')
        }
        Invoke-DotNet -ArgumentList $restoreArguments
    }
    finally {
        Pop-Location
    }
}

Push-Location -LiteralPath $repositoryRoot
try {
    Invoke-DotNet -ArgumentList @(
        'test', $solution,
        '--configuration', $Configuration,
        '--no-restore',
        '--no-build'
    )
}
finally {
    Pop-Location
}
