<#
.SYNOPSIS
Restores and builds the repository without permitting an implicit restore.

.PARAMETER Configuration
The MSBuild configuration to build. Defaults to Debug.

.PARAMETER NoRestore
Skips the explicit restore when an earlier stage already restored the repository.

.PARAMETER UpdateLockFiles
Performs an unlocked restore for an intentional dependency update. Normal restores
use locked mode and fail if a package lock file is stale.
#>
[CmdletBinding()]
param(
    [ValidateNotNullOrEmpty()]
    [string] $Configuration = 'Debug',

    [switch] $NoRestore,

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

Push-Location -LiteralPath $repositoryRoot
try {
    if (-not $NoRestore) {
        $restoreArguments = @('restore', $solution)
        if (-not $UpdateLockFiles) {
            $restoreArguments += @('--locked-mode', '-p:RestoreLockedMode=true')
        }

        Invoke-DotNet -ArgumentList $restoreArguments
    }

    Invoke-DotNet -ArgumentList @(
        'build', $solution,
        '--configuration', $Configuration,
        '--no-restore'
    )
}
finally {
    Pop-Location
}
