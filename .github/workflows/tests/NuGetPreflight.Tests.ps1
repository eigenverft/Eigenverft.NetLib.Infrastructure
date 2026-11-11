BeforeAll {
    . (Join-Path $PSScriptRoot '../NuGetPreflight.ps1')
}

Describe 'NuGet publication preflight' {
    BeforeEach {
        Set-StrictMode -Version 3
        $Root = Join-Path $TestDrive 'projects'
        $null = New-Item -ItemType Directory -Path $Root -Force
        foreach ($Name in @('Existing', 'New', 'Disabled', 'Tests')) {
            $Folder = New-Item -ItemType Directory -Path (Join-Path $Root $Name) -Force
            $Packable = if ($Name -eq 'Tests') { 'false' } else { 'true' }
            $Enabled = if ($Name -eq 'Disabled') { 'false' } else { 'true' }
            Set-Content -LiteralPath (Join-Path $Folder.FullName ($Name + '.csproj')) -Value @"
<Project><PropertyGroup><IsPackable>$Packable</IsPackable><IsCicdEnabled>$Enabled</IsCicdEnabled><PackageId>Eigenverft.$Name</PackageId></PropertyGroup></Project>
"@
        }
        Mock Invoke-NuGetPreflightRequest {
            if ($Method -eq 'Post') { return [pscustomobject]@{ StatusCode = 200; Content = '{"Key":"test-verification-key"}' } }
            $Status = if ($Uri -like '*/Eigenverft.New') { 404 } else { 200 }
            [pscustomobject]@{ StatusCode = $Status; Content = '' }
        }
    }

    It 'checks production branch <Branch> before setup without uploading a package' -TestCases @(
        @{ Branch = 'main' }, @{ Branch = 'master' }, @{ Branch = 'hotfix' },
        @{ Branch = 'main/nested' }, @{ Branch = 'hotfix/urgent' }
    ) {
        param($Branch)
        Invoke-NuGetPublicationPreflight -ApiKey 'test-push-key' -BranchName $Branch -ProjectsRoot $Root -InformationVariable Log
        ($Log -join ' ') | Should -Match '1 existing packages verified; 1 new package IDs'
        ($Log -join ' ') | Should -Not -Match 'test-push-key|test-verification-key'
        Should -Invoke Invoke-NuGetPreflightRequest -Exactly 2 -ParameterFilter {
            $Method -eq 'Post' -and $ApiKey -eq 'test-push-key' -and $Uri -like '*/package/create-verification-key/*'
        }
        Should -Invoke Invoke-NuGetPreflightRequest -Exactly 2 -ParameterFilter {
            $Method -eq 'Get' -and $ApiKey -eq 'test-verification-key' -and $Uri -like '*/verifykey/*'
        }
        Should -Invoke Invoke-NuGetPreflightRequest -Exactly 0 -ParameterFilter { $Uri -match 'Disabled|Tests' }
    }

    It 'skips unrelated publication channels on <Branch>' -TestCases @(
        @{ Branch = 'release' }, @{ Branch = 'release/next' }, @{ Branch = 'bugfix/fix' }, @{ Branch = 'feature/test' }
    ) {
        param($Branch)
        Invoke-NuGetPublicationPreflight -ApiKey '' -BranchName $Branch -ProjectsRoot $Root
        Should -Invoke Invoke-NuGetPreflightRequest -Exactly 0
    }

    It 'rejects missing production credentials before making any request' {
        { Invoke-NuGetPublicationPreflight -ApiKey '' -BranchName main -ProjectsRoot $Root } | Should -Throw '*NUGET_APIKEY is empty*'
        Should -Invoke Invoke-NuGetPreflightRequest -Exactly 0
    }

    It 'stops immediately when the API key is rejected' {
        Mock Invoke-NuGetPreflightRequest { [pscustomobject]@{ StatusCode = 403; Content = 'test-push-key' } }
        { Invoke-NuGetPublicationPreflight -ApiKey 'test-push-key' -BranchName main -ProjectsRoot $Root } | Should -Throw '*rejected NUGET_APIKEY (HTTP 403)*'
        Should -Invoke Invoke-NuGetPreflightRequest -Exactly 1
    }

    It 'rejects a key without permission for an existing package' {
        Mock Invoke-NuGetPreflightRequest { [pscustomobject]@{ StatusCode = 403; Content = '' } } -ParameterFilter { $Method -eq 'Get' }
        { Invoke-NuGetPublicationPreflight -ApiKey 'test-push-key' -BranchName main -ProjectsRoot $Root } | Should -Throw '*permission check failed*Eigenverft.Existing*HTTP 403*'
        Should -Invoke Invoke-NuGetPreflightRequest -Exactly 2
    }

    It 'does not misclassify service outages as rejected credentials' {
        Mock Invoke-NuGetPreflightRequest { [pscustomobject]@{ StatusCode = 503; Content = '' } }
        { Invoke-NuGetPublicationPreflight -ApiKey 'test-push-key' -BranchName main -ProjectsRoot $Root } | Should -Throw '*verification service returned HTTP 503*'
    }

    It 'suppresses malformed verification credential responses' {
        Mock Invoke-NuGetPreflightRequest { [pscustomobject]@{ StatusCode = 200; Content = 'invalid-json-test-push-key' } }
        try {
            Invoke-NuGetPublicationPreflight -ApiKey 'test-push-key' -BranchName main -ProjectsRoot $Root
            throw 'Expected preflight to reject malformed JSON.'
        }
        catch {
            $_.Exception.Message | Should -Match 'invalid verification response'
            ($_ | Out-String) | Should -Not -Match 'test-push-key'
        }
    }

    It 'does not pass when none of the package permissions could be verified' {
        Mock Invoke-NuGetPreflightRequest { [pscustomobject]@{ StatusCode = 404; Content = '' } } -ParameterFilter { $Method -eq 'Get' }
        { Invoke-NuGetPublicationPreflight -ApiKey 'test-push-key' -BranchName main -ProjectsRoot $Root } | Should -Throw '*any existing package*'
    }

    It 'fails if a packable project has an unevaluated package ID' {
        $Project = Join-Path $Root 'New/New.csproj'
        (Get-Content $Project -Raw).Replace('Eigenverft.New', '$(AssemblyName)') | Set-Content $Project
        { Invoke-NuGetPublicationPreflight -ApiKey 'test-push-key' -BranchName main -ProjectsRoot $Root } | Should -Throw '*requires a literal PackageId*'
        Should -Invoke Invoke-NuGetPreflightRequest -Exactly 0
    }
}

Describe 'NuGet HTTP boundary and workflow wiring' {
    It 'bounds request time and disables redirects without exposing the credential' {
        Mock Invoke-WebRequest { [pscustomobject]@{ StatusCode = 200; Content = '{"Key":"test-verification-key"}' } }
        $Response = Invoke-NuGetPreflightRequest -Method Post -Uri 'https://www.nuget.org/api/v2/package/create-verification-key/Example' -ApiKey 'test-push-key'
        $Response.StatusCode | Should -Be 200
        Should -Invoke Invoke-WebRequest -Exactly 1 -ParameterFilter {
            $TimeoutSec -eq 20 -and $MaximumRedirection -eq 0 -and $Headers['X-NuGet-ApiKey'] -eq 'test-push-key'
        }
    }

    It 'retains HTTP rejection codes while dropping response bodies' {
        Mock Invoke-WebRequest {
            $Failure = [Exception]::new('test-push-key')
            $Failure | Add-Member -NotePropertyName Response -NotePropertyValue ([pscustomobject]@{ StatusCode = 403 })
            throw $Failure
        }
        $Response = Invoke-NuGetPreflightRequest -Method Post -Uri 'https://www.nuget.org/example' -ApiKey 'test-push-key'
        $Response.StatusCode | Should -Be 403
        $Response.Content | Should -Be ''
    }

    It 'suppresses credentials in transport exception messages' {
        Mock Invoke-WebRequest { throw 'Transport failed: test-push-key' }
        try {
            Invoke-NuGetPreflightRequest -Method Post -Uri 'https://www.nuget.org/example' -ApiKey 'test-push-key'
            throw 'Expected a transport failure.'
        }
        catch {
            $_.Exception.Message | Should -Match 'could not reach the verification service'
            ($_ | Out-String) | Should -Not -Match 'test-push-key'
        }
    }

    It 'runs immediately after checkout before SDK setup and the build/deploy step' {
        $Workflow = Get-Content (Join-Path $PSScriptRoot '../cicd.yml') -Raw
        $Preflight = $Workflow.IndexOf('- name: Verify NuGet publication credentials')
        $Preflight | Should -BeGreaterThan $Workflow.IndexOf('- name: Checkout')
        $Preflight | Should -BeLessThan $Workflow.IndexOf('- name: Setup-dotnet')
        $Preflight | Should -BeLessThan $Workflow.IndexOf('- name: Workflow Build/Deploy')
        $Workflow | Should -Match 'NUGET_APIKEY: \$\{\{ secrets.NUGET_APIKEY \}\}'
    }
}
