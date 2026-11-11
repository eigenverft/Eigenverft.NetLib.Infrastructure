function Invoke-NuGetPreflightRequest {
    [CmdletBinding()]
    param([string]$Method, [string]$Uri, [string]$ApiKey)

    try {
        $Response = Invoke-WebRequest -UseBasicParsing -Method $Method -Uri $Uri `
            -Headers @{ 'X-NuGet-ApiKey' = $ApiKey; 'X-NuGet-Protocol-Version' = '4.1.0' } `
            -TimeoutSec 20 -MaximumRedirection 0 -ErrorAction Stop
        return [pscustomobject]@{ StatusCode = [int]$Response.StatusCode; Content = [string]$Response.Content }
    }
    catch {
        # Never propagate a request exception or response body containing credentials.
        $ResponseProperty = $_.Exception.PSObject.Properties['Response']
        if ($ResponseProperty -and $ResponseProperty.Value) {
            return [pscustomobject]@{ StatusCode = [int]$ResponseProperty.Value.StatusCode; Content = '' }
        }
        throw 'NuGet.org credential preflight could not reach the verification service (20-second request timeout).'
    }
}

function Invoke-NuGetPublicationPreflight {
    [CmdletBinding()]
    param([string]$ApiKey, [string]$BranchName, [string]$ProjectsRoot)

    if ([string]::IsNullOrWhiteSpace($BranchName)) { throw 'NuGet preflight requires the build branch name.' }
    # Match the first-segment production mapping used by Drydock and cicd.ps1.
    if (($BranchName -split '/')[0] -notin @('main', 'master', 'hotfix')) {
        Write-Host "NuGet.org preflight skipped: '$BranchName' does not publish to nuget.org."
        return
    }
    if ([string]::IsNullOrWhiteSpace($ApiKey)) { throw 'NuGet.org preflight: repository secret NUGET_APIKEY is empty.' }

    # This repository declares packability, CI opt-outs and package IDs literally in its projects.
    $PackageIds = @(foreach ($Directory in Get-ChildItem -LiteralPath $ProjectsRoot -Directory -ErrorAction Stop) {
        $ProjectPath = Join-Path $Directory.FullName ($Directory.Name + '.csproj')
        if (-not (Test-Path -LiteralPath $ProjectPath -PathType Leaf)) { continue }
        $Project = [xml](Get-Content -LiteralPath $ProjectPath -Raw -ErrorAction Stop)
        if ($Project.SelectSingleNode("//IsCicdEnabled[normalize-space(.)='false']")) { continue }
        if (-not $Project.SelectSingleNode("//IsPackable[normalize-space(.)='true']")) { continue }
        $IdNode = $Project.SelectSingleNode('//PackageId')
        if (-not $IdNode -or $IdNode.InnerText -notmatch '^[A-Za-z0-9_.-]+$') {
            throw "NuGet preflight requires a literal PackageId in '$ProjectPath'."
        }
        $IdNode.InnerText
    })
    $PackageIds = @($PackageIds | Sort-Object -Unique)
    if ($PackageIds.Count -eq 0) { throw 'NuGet preflight found no CI-enabled packages to verify.' }

    $Verified = 0
    $NewPackages = 0
    foreach ($PackageId in $PackageIds) {
        $VerificationKey = $null
        $CreateResponse = $null
        try {
            $CreateResponse = Invoke-NuGetPreflightRequest -Method Post `
                -Uri "https://www.nuget.org/api/v2/package/create-verification-key/$PackageId" -ApiKey $ApiKey
            if ($CreateResponse.StatusCode -in @(401, 403)) {
                throw "NuGet.org rejected NUGET_APIKEY (HTTP $($CreateResponse.StatusCode)). Check expiration and push permissions before rebuilding."
            }
            if ($CreateResponse.StatusCode -ne 200) {
                throw "NuGet.org verification service returned HTTP $($CreateResponse.StatusCode); publication credentials could not be checked."
            }
            try {
                $Credential = ConvertFrom-Json -InputObject $CreateResponse.Content -ErrorAction Stop
                $KeyProperty = $Credential.PSObject.Properties['Key']
                if (-not $KeyProperty -or [string]::IsNullOrWhiteSpace([string]$KeyProperty.Value)) { throw 'Missing verification credential.' }
                $VerificationKey = [string]$KeyProperty.Value
            }
            catch { throw 'NuGet.org returned an invalid verification response; its body has been suppressed.' }
            finally { $Credential = $null }

            # nuget.org requires a temporary verify-scope key here; a direct push-key GET can return 400.
            $Response = Invoke-NuGetPreflightRequest -Method Get `
                -Uri "https://www.nuget.org/api/v2/verifykey/$PackageId" -ApiKey $VerificationKey
            switch ($Response.StatusCode) {
                200 {
                    $Verified++
                    Write-Host "NuGet.org credentials verified for '$PackageId'."
                }
                404 {
                    $NewPackages++
                    Write-Host "NuGet.org package '$PackageId' is new. First publication still requires permission for new IDs and a matching package/glob scope."
                }
                default { throw "NuGet.org package permission check failed for '$PackageId' (HTTP $($Response.StatusCode))." }
            }
        }
        finally {
            $VerificationKey = $null
            $CreateResponse = $null
        }
    }
    if ($Verified -eq 0) { throw 'NuGet preflight could not verify permission for any existing package.' }
    Write-Host "NuGet.org preflight passed: $Verified existing packages verified; $NewPackages new package IDs."
}
