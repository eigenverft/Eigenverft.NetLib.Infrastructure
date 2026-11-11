BeforeAll {
    # Load only the query functions; running cicd.ps1 itself installs tools and publishes packages.
    $PipelinePath = Join-Path $PSScriptRoot '../cicd.ps1'
    $Tokens = $null
    $ParseErrors = $null
    $Ast = [System.Management.Automation.Language.Parser]::ParseFile(
        $PipelinePath, [ref]$Tokens, [ref]$ParseErrors)
    if ($ParseErrors.Count) { throw ($ParseErrors -join [Environment]::NewLine) }
    $Ast.FindAll({
        param($Node)
        $Node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
        $Node.Name -in @('Invoke-DotnetPropertyRestore', 'Restore-DotnetProjectProperties')
    }, $false) | ForEach-Object { Invoke-Expression $_.Extent.Text }

    function Invoke-ProcessTyped {
        param($Executable, $Arguments, $CommonArguments, $CaptureOutput, $ReturnType)
        throw 'The process boundary must be mocked in these tests.'
    }
}

Describe 'CI project-property restore' {
    BeforeEach {
        Set-StrictMode -Version 3
        $Project = New-Item -ItemType File -Path (Join-Path $TestDrive 'source with spaces.csproj') -Force
        $Common = @('-p:Configuration=Release', '-p:ContinuousIntegrationBuild=true', '-m:1')
        $Generated = [pscustomobject]@{ VersionFull = '1.2.3.4' }
        $script:QueryPaths = [System.Collections.Generic.List[string]]::new()
        $script:QueryMode = 'normal'
        $script:HasNerdbank = $false
        Mock Invoke-ProcessTyped {
            $ResultPath = ($Arguments | Where-Object { $_ -like '-getResultOutputFile:*' }).Substring(21)
            $script:QueryPaths.Add($ResultPath)
            if ($script:QueryMode -eq 'missing') { return }
            if ($script:QueryMode -eq 'invalid') {
                Set-Content -LiteralPath $ResultPath -Value 'not JSON'
                return
            }
            Set-Content -LiteralPath $ResultPath -Value (@{ Properties = @{
                UsingMicrosoftNETSdk = 'true'; TargetFrameworkVersion = ''; TargetFramework = ''
                TargetFrameworks = 'net8.0;net10.0'; IsTestProject = 'true'; IsPackable = 'false'
                IsPublishable = 'false'; IsCicdEnabled = ''
                GitVersionBaseDirectory = $(if ($script:HasNerdbank) { 'Properties' } else { '' })
                NuGetPackageVersion = '2.3.4.5'; AssemblyInformationalVersion = '2.3.4.5+test'
                BuildVersionSimple = '2.3.4'
            }} | ConvertTo-Json)
            if ($script:QueryMode -eq 'failure') { throw 'Native restore failed with exit code 1.' }
            Write-Host 'Restore diagnostic: warning with {braces} outside the result JSON.'
            'A captured diagnostic must never become property data.'
        }
    }

    It 'reads file results despite unrelated process output and preserves multi-target metadata' {
        $Result = Restore-DotnetProjectProperties $Project $Common $Generated '-preview' -InformationVariable RestoreLog
        ($RestoreLog -join [Environment]::NewLine) | Should -Match 'A captured diagnostic'
        $Result.TargetFrameworks | Should -Be @('net8.0', 'net10.0')
        $Result.IsTestProject | Should -BeTrue
        $Result.IsPackable | Should -BeFalse
        $Result.IsCicdEnabled | Should -BeTrue
        $Result.Source | Should -Be 'GeneratedVersion'
        $Result.NuGetPackageVersion | Should -Be '1.2.3.4-preview'
        $script:QueryPaths.Count | Should -Be 1
        Test-Path -LiteralPath $script:QueryPaths[0] | Should -BeFalse
        Should -Invoke Invoke-ProcessTyped -Exactly 1 -ParameterFilter {
            $Executable -eq 'dotnet' -and $CaptureOutput -eq $true -and $ReturnType -eq 'Text' -and
            $CommonArguments -contains '-p:Configuration=Release' -and
            -not ($Arguments -match '^-p:TargetFramework=')
        }
    }

    It 'isolates and cleans both project and Nerdbank target-query results' {
        $script:HasNerdbank = $true
        $Result = Restore-DotnetProjectProperties $Project $Common $Generated ''
        $Result.Source | Should -Be 'Nerdbank.GitVersioning'
        $Result.NuGetPackageVersion | Should -Be '2.3.4.5'
        $Result.AssemblyInformationalVersion | Should -Be '2.3.4.5+test'
        $script:QueryPaths.Count | Should -Be 2
        $script:QueryPaths[0] | Should -Not -Be $script:QueryPaths[1]
        $script:QueryPaths | ForEach-Object { Test-Path -LiteralPath $_ | Should -BeFalse }
        Should -Invoke Invoke-ProcessTyped -Exactly 1 -ParameterFilter { $Arguments -contains '-t:GetBuildVersion' }
    }

    It 'fails on <Mode> results and leaves no query file' -TestCases @(
        @{ Mode = 'invalid' }, @{ Mode = 'missing' }, @{ Mode = 'failure' }
    ) {
        param($Mode)
        $script:QueryMode = $Mode
        $ExpectedError = if ($Mode -eq 'failure') { '*Native restore failed*' } else { '*invalid property data*' }
        { Restore-DotnetProjectProperties $Project $Common $Generated '' } | Should -Throw -ExpectedMessage $ExpectedError
        $script:QueryPaths.Count | Should -Be 1
        Test-Path -LiteralPath $script:QueryPaths[0] | Should -BeFalse
    }
}
