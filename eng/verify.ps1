<#
.SYNOPSIS
Runs the locked Release-style repository verification pipeline.

.PARAMETER Configuration
The configuration to verify. Defaults to Release.

.PARAMETER NoRestore
Reuses an already completed locked restore. All later commands remain restore-free.
#>
[CmdletBinding()]
param(
    [ValidateNotNullOrEmpty()]
    [string] $Configuration = 'Release',

    [switch] $NoRestore
)

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
$verificationProject = Join-Path -Path $repositoryRoot -ChildPath 'tools\IncidentReview.Verification\IncidentReview.Verification.csproj'
if (-not (Test-Path -LiteralPath $verificationProject -PathType Leaf)) {
    throw "Verification project not found: $verificationProject"
}

$script:DotNet = Find-DotNet

$buildParameters = @{ Configuration = $Configuration }
if ($NoRestore) {
    $buildParameters.NoRestore = $true
}
& (Join-Path -Path $PSScriptRoot -ChildPath 'build.ps1') @buildParameters

& (Join-Path -Path $PSScriptRoot -ChildPath 'test.ps1') `
    -Configuration $Configuration `
    -NoRestore `
    -NoBuild

Push-Location -LiteralPath $repositoryRoot
try {
    Invoke-DotNet -ArgumentList @(
        'run',
        '--project', $verificationProject,
        '--configuration', $Configuration,
        '--no-launch-profile',
        '--no-restore',
        '--no-build'
    )
}
finally {
    Pop-Location
}
