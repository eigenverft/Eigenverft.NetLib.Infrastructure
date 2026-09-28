param(
    [Parameter(Mandatory = $true)]
    [string] $RepositoryRoot,

    [Parameter(Mandatory = $true)]
    [string] $TestProject
)

$ErrorActionPreference = 'Stop'

Push-Location $RepositoryRoot
try {
    & dotnet restore $TestProject | ForEach-Object { Write-Host $_ }
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE." }

    $projectDirectory = Split-Path -Parent $TestProject
    Remove-Item (Join-Path $projectDirectory 'CoverletOutput') -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $projectDirectory 'MSTestResults') -Recurse -Force -ErrorAction SilentlyContinue

    $arguments = @(
        'test',
        $TestProject,
        '-c', 'Release',
        '--framework', 'net10.0',
        '--no-restore',
        '-p:Stage=test',
        '-p:Configuration=Release',
        '-p:Platform=AnyCPU',
        '-v:minimal',
        '-p:Deterministic=true',
        '-p:ContinuousIntegrationBuild=false',
        '-p:UseSharedCompilation=false',
        '-p:Threshold=0',
        '-m:1'
    )

    & dotnet @arguments | ForEach-Object { Write-Host $_ }
    $exitCode = $LASTEXITCODE
    Write-Host "net10 CIB=false Threshold=0 exitCode=$exitCode"
    if ($exitCode -ne 0) { throw "Diagnostic test run failed with exit code $exitCode." }

    $coveragePath = Join-Path $projectDirectory 'CoverletOutput/coverage.net10.0.opencover.xml'
    if (-not (Test-Path $coveragePath)) { throw "Coverage report not found: $coveragePath" }

    [xml] $coverage = Get-Content $coveragePath
    $files = @{}
    foreach ($file in $coverage.SelectNodes('//File')) {
        $files[[string]$file.uid] = [string]$file.fullPath
    }

    Write-Host ''
    Write-Host '================ UNCOVERED SEQUENCE POINTS ================'
    $sequencePoints = foreach ($point in $coverage.SelectNodes('//SequencePoint[@vc="0"]')) {
        [pscustomobject]@{
            File = $files[[string]$point.fileid]
            Line = [int]$point.sl
        }
    }
    $sequencePoints | Sort-Object File, Line -Unique | Format-Table -AutoSize | Out-String -Width 300 | Write-Host

    Write-Host '================ UNCOVERED BRANCH POINTS =================='
    $branchPoints = foreach ($point in $coverage.SelectNodes('//BranchPoint[@vc="0"]')) {
        [pscustomobject]@{
            File = $files[[string]$point.fileid]
            Line = [int]$point.sl
            Path = [int]$point.path
        }
    }
    $branchPoints | Sort-Object File, Line, Path -Unique | Format-Table -AutoSize | Out-String -Width 300 | Write-Host
}
finally {
    Pop-Location
}
