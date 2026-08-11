param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("create", "update", "delete", "validate", "cleanup-personal")]
    [string]$Action,
    [string]$SkillName,
    [string]$Resources = "",
    [switch]$Examples,
    [string]$Interface = "",
    [string]$Description,
    [string]$Title,
    [string]$BodyFile,
    [switch]$PruneResources,
    [switch]$ConfirmDestructive
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$MaxSkillNameLength = 64
$AllowedResources = @("scripts", "references", "assets")
$AllowedInterfaceKeys = @("display_name", "short_description", "icon_small", "icon_large", "brand_color", "default_prompt")
$AllowedFrontmatterKeys = @("name", "description", "license", "allowed-tools", "metadata")

$SkillTemplate = @'
---
name: __SKILL_NAME__
description: [TODO: Complete and informative explanation of what the skill does and when to use it. Include WHEN to use this skill - specific scenarios, file types, or tasks that trigger it.]
---

# __SKILL_TITLE__

## Overview

[TODO: 1-2 sentences explaining what this skill enables]

## Structuring This Skill

[TODO: Choose the structure that best fits this skill's purpose. Common patterns:

**1. Workflow-Based** (best for sequential processes)
- Works well when there are clear step-by-step procedures
- Structure: ## Overview -> ## Workflow Decision Tree -> ## Step 1 -> ## Step 2...

**2. Task-Based** (best for tool collections)
- Works well when the skill offers different operations/capabilities
- Structure: ## Overview -> ## Quick Start -> ## Task Category 1 -> ## Task Category 2...

**3. Reference/Guidelines** (best for standards or specifications)
- Works well for standards or requirements
- Structure: ## Overview -> ## Guidelines -> ## Specifications -> ## Usage...

**4. Capabilities-Based** (best for integrated systems)
- Works well when the skill provides multiple interrelated features
- Structure: ## Overview -> ## Core Capabilities -> ### 1. Feature -> ### 2. Feature...

Delete this entire "Structuring This Skill" section when done.]

## [TODO: Replace with the first main section based on chosen structure]

[TODO: Add content here.]

## Resources (optional)

Create only the resource directories this skill actually needs. Delete this section if no resources are required.

### scripts/
Executable code that can be run directly to perform specific operations.

### references/
Documentation and reference material intended to be loaded into context.

### assets/
Files intended for output use, not for prompt context.
'@

$ExampleScript = @'
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Write-Output "This is an example script for __SKILL_NAME__."
'@

$ExampleReference = @'
# Reference Documentation

Replace this placeholder with real reference documentation for this skill.
'@

$ExampleAsset = @'
This is a placeholder asset file.
Replace it with actual assets or delete it if not needed.
'@

function Get-GitRoot {
    $raw = & git rev-parse --show-toplevel 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to resolve git root. Run this command inside a git repository."
    }

    $line = ($raw | Select-Object -First 1)
    if ([string]::IsNullOrWhiteSpace($line)) {
        throw "Unable to resolve git root. Run this command inside a git repository."
    }

    return [System.IO.Path]::GetFullPath($line.Trim())
}

function Get-SkillsRoot {
    param([string]$GitRoot)
    return Join-Path -Path $GitRoot -ChildPath ".agents/skills"
}

function Get-NormalizedSkillName {
    param([string]$RawName)

    $normalized = $RawName.Trim().ToLowerInvariant()
    $normalized = [regex]::Replace($normalized, "[^a-z0-9]+", "-")
    $normalized = $normalized.Trim("-")
    $normalized = [regex]::Replace($normalized, "-{2,}", "-")
    return $normalized
}

function Get-SkillTitle {
    param([string]$NormalizedSkillName)

    $parts = $NormalizedSkillName.Split("-") | Where-Object { $_ -ne "" }
    $titleParts = New-Object System.Collections.Generic.List[string]
    foreach ($part in $parts) {
        if ($part.Length -eq 1) {
            [void]$titleParts.Add($part.ToUpperInvariant())
        } else {
            $title = $part.Substring(0, 1).ToUpperInvariant() + $part.Substring(1).ToLowerInvariant()
            [void]$titleParts.Add($title)
        }
    }
    return [string]::Join(" ", $titleParts.ToArray())
}

function Get-ResourceList {
    param([string]$RawResources)

    if ([string]::IsNullOrWhiteSpace($RawResources)) {
        return @()
    }

    $items = $RawResources.Split(",") | ForEach-Object { $_.Trim().ToLowerInvariant() } | Where-Object { $_ -ne "" }
    $invalid = @()
    foreach ($item in $items) {
        if ($AllowedResources -notcontains $item) {
            $invalid += $item
        }
    }

    if ($invalid.Count -gt 0) {
        $invalidValues = [string]::Join(", ", ($invalid | Select-Object -Unique))
        $allowedValues = [string]::Join(", ", $AllowedResources)
        throw "Unknown resource type(s): $invalidValues. Allowed: $allowedValues"
    }

    $deduped = @()
    foreach ($item in $items) {
        if ($deduped -notcontains $item) {
            $deduped += $item
        }
    }

    return $deduped
}

function Get-YamlQuotedValue {
    param([string]$Value)

    if ($null -eq $Value) {
        $Value = ""
    }

    $escaped = $Value.Replace("\", "\\")
    $escaped = $escaped.Replace('"', '\"')
    $escaped = $escaped.Replace("`r", "")
    $escaped = $escaped.Replace("`n", "\n")
    return '"' + $escaped + '"'
}

function Get-DisplayName {
    param([string]$SkillNameValue)
    return Get-SkillTitle -NormalizedSkillName $SkillNameValue
}

function Get-ShortDescription {
    param([string]$DisplayName)

    $description = "Help with $DisplayName tasks"
    if ($description.Length -lt 25) {
        $description = "Help with $DisplayName tasks and workflows"
    }
    if ($description.Length -lt 25) {
        $description = "Help with $DisplayName tasks with guidance"
    }
    if ($description.Length -gt 64) {
        $description = "Help with $DisplayName"
    }
    if ($description.Length -gt 64) {
        $description = "$DisplayName helper"
    }
    if ($description.Length -gt 64) {
        $description = "$DisplayName tools"
    }
    if ($description.Length -gt 64) {
        $description = $description.Substring(0, 64).TrimEnd()
    }

    return $description
}

function Get-DefaultPrompt {
    param([string]$SkillNameValue)
    return 'Use $' + $SkillNameValue + ' to help with the requested task.'
}

function Get-InterfaceOverrides {
    param([string]$RawInterface)

    $overrides = @{}
    $optionalOrder = New-Object System.Collections.ArrayList

    if ([string]::IsNullOrWhiteSpace($RawInterface)) {
        return [pscustomobject]@{
            Overrides = $overrides
            OptionalOrder = $optionalOrder
        }
    }

    $pairs = $RawInterface.Split(",") | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne "" }
    foreach ($pair in $pairs) {
        if ($pair -notmatch "=") {
            throw "Invalid interface override '$pair'. Use key=value."
        }

        $parts = $pair.Split("=", 2)
        $key = $parts[0].Trim()
        $value = $parts[1].Trim()
        if ([string]::IsNullOrWhiteSpace($key)) {
            throw "Invalid interface override '$pair'. Key is empty."
        }
        if ($AllowedInterfaceKeys -notcontains $key) {
            $allowed = [string]::Join(", ", $AllowedInterfaceKeys)
            throw "Unknown interface field '$key'. Allowed: $allowed"
        }

        if (($key -eq "display_name") -and $value.Contains("$")) {
            throw "display_name must not include '$'."
        }

        $overrides[$key] = $value
        if (($key -ne "display_name") -and ($key -ne "short_description") -and (-not $optionalOrder.Contains($key))) {
            [void]$optionalOrder.Add($key)
        }
    }

    return [pscustomobject]@{
        Overrides = $overrides
        OptionalOrder = $optionalOrder
    }
}

function Write-NewOpenAiYaml {
    param(
        [string]$SkillDirectoryPath,
        [string]$SkillNameValue,
        [string]$InterfaceCsv
    )

    $parsed = Get-InterfaceOverrides -RawInterface $InterfaceCsv
    $overrides = $parsed.Overrides
    $optionalOrder = $parsed.OptionalOrder

    if ($overrides.ContainsKey("display_name")) {
        $displayName = $overrides["display_name"]
    } else {
        $displayName = Get-DisplayName -SkillNameValue $SkillNameValue
    }

    if ($displayName.Contains("$")) {
        throw "display_name must not include '$'."
    }

    if ($overrides.ContainsKey("short_description")) {
        $shortDescription = $overrides["short_description"]
    } else {
        $shortDescription = Get-ShortDescription -DisplayName $displayName
    }

    if (($shortDescription.Length -lt 25) -or ($shortDescription.Length -gt 64)) {
        throw "short_description must be 25-64 characters (got $($shortDescription.Length))."
    }

    if ($overrides.ContainsKey("default_prompt")) {
        $defaultPrompt = $overrides["default_prompt"]
    } else {
        $defaultPrompt = Get-DefaultPrompt -SkillNameValue $SkillNameValue
    }
    $requiredInvocation = '$' + $SkillNameValue
    if (-not $defaultPrompt.Contains($requiredInvocation)) {
        throw "default_prompt must mention '$requiredInvocation'."
    }

    $lines = New-Object System.Collections.Generic.List[string]
    [void]$lines.Add("interface:")
    [void]$lines.Add("  display_name: $(Get-YamlQuotedValue -Value $displayName)")
    [void]$lines.Add("  short_description: $(Get-YamlQuotedValue -Value $shortDescription)")
    [void]$lines.Add("  default_prompt: $(Get-YamlQuotedValue -Value $defaultPrompt)")
    foreach ($key in $optionalOrder) {
        if ($key -eq "default_prompt") {
            continue
        }
        [void]$lines.Add("  ${key}: $(Get-YamlQuotedValue -Value $overrides[$key])")
    }

    $agentsDir = Join-Path -Path $SkillDirectoryPath -ChildPath "agents"
    New-Item -Path $agentsDir -ItemType Directory -Force | Out-Null
    $yamlPath = Join-Path -Path $agentsDir -ChildPath "openai.yaml"
    $yaml = [string]::Join("`n", $lines) + "`n"
    Set-Content -LiteralPath $yamlPath -Value $yaml -Encoding UTF8
    Write-Output "[OK] Created agents/openai.yaml"
}

function Update-OpenAiYaml {
    param(
        [string]$SkillDirectoryPath,
        [string]$SkillNameValue,
        [string]$InterfaceCsv
    )

    $parsed = Get-InterfaceOverrides -RawInterface $InterfaceCsv
    $overrides = $parsed.Overrides
    if ($overrides.Count -eq 0) {
        return
    }

    if ($overrides.ContainsKey("display_name") -and $overrides["display_name"].Contains("$")) {
        throw "display_name must not include '$'."
    }
    if ($overrides.ContainsKey("short_description")) {
        $shortDescription = $overrides["short_description"]
        if (($shortDescription.Length -lt 25) -or ($shortDescription.Length -gt 64)) {
            throw "short_description must be 25-64 characters (got $($shortDescription.Length))."
        }
    }
    if ($overrides.ContainsKey("default_prompt")) {
        $requiredInvocation = '$' + $SkillNameValue
        if (-not $overrides["default_prompt"].Contains($requiredInvocation)) {
            throw "default_prompt must mention '$requiredInvocation'."
        }
    }

    $agentsDir = Join-Path -Path $SkillDirectoryPath -ChildPath "agents"
    New-Item -Path $agentsDir -ItemType Directory -Force | Out-Null
    $yamlPath = Join-Path -Path $agentsDir -ChildPath "openai.yaml"
    if (-not (Test-Path -LiteralPath $yamlPath -PathType Leaf)) {
        Write-NewOpenAiYaml -SkillDirectoryPath $SkillDirectoryPath -SkillNameValue $SkillNameValue -InterfaceCsv $InterfaceCsv
        return
    }

    $content = Get-Content -LiteralPath $yamlPath -Raw -Encoding UTF8
    $sourceLines = @($content -split "`r?`n")
    if (($sourceLines.Count -gt 0) -and ($sourceLines[$sourceLines.Count - 1] -eq "")) {
        if ($sourceLines.Count -eq 1) {
            $sourceLines = @()
        } else {
            $sourceLines = @($sourceLines[0..($sourceLines.Count - 2)])
        }
    }
    $lines = New-Object System.Collections.Generic.List[string]
    foreach ($line in $sourceLines) {
        [void]$lines.Add($line)
    }

    $interfaceIndex = -1
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '^interface\s*:\s*$') {
            $interfaceIndex = $i
            break
        }
    }

    if ($interfaceIndex -lt 0) {
        $newBlock = New-Object System.Collections.Generic.List[string]
        [void]$newBlock.Add("interface:")
        [void]$newBlock.Add("  display_name: $(Get-YamlQuotedValue -Value (Get-DisplayName -SkillNameValue $SkillNameValue))")
        [void]$newBlock.Add("  short_description: $(Get-YamlQuotedValue -Value (Get-ShortDescription -DisplayName (Get-DisplayName -SkillNameValue $SkillNameValue)))")
        [void]$newBlock.Add("  default_prompt: $(Get-YamlQuotedValue -Value (Get-DefaultPrompt -SkillNameValue $SkillNameValue))")
        [void]$newBlock.Add("")
        for ($i = $newBlock.Count - 1; $i -ge 0; $i--) {
            $lines.Insert(0, $newBlock[$i])
        }
        $interfaceIndex = 0
    }

    $interfaceEnd = $lines.Count
    for ($i = $interfaceIndex + 1; $i -lt $lines.Count; $i++) {
        if (($lines[$i] -match '^\S') -and ($lines[$i] -notmatch '^\s*#')) {
            $interfaceEnd = $i
            break
        }
    }

    foreach ($key in $overrides.Keys) {
        $replacement = "  ${key}: $(Get-YamlQuotedValue -Value $overrides[$key])"
        $escapedKey = [regex]::Escape($key)
        $existingIndex = -1
        for ($i = $interfaceIndex + 1; $i -lt $interfaceEnd; $i++) {
            if ($lines[$i] -match "^\s{2}${escapedKey}\s*:") {
                $existingIndex = $i
                break
            }
        }

        if ($existingIndex -ge 0) {
            $lines[$existingIndex] = $replacement
        } else {
            $lines.Insert($interfaceEnd, $replacement)
            $interfaceEnd++
        }
    }

    $yaml = [string]::Join("`n", $lines.ToArray()) + "`n"
    Set-Content -LiteralPath $yamlPath -Value $yaml -Encoding UTF8
    Write-Output "[OK] Updated agents/openai.yaml without replacing unselected fields"
}

function New-ResourceDirectories {
    param([string]$SkillDirectoryPath, [string[]]$ResourceList)

    foreach ($resource in $ResourceList) {
        $dir = Join-Path -Path $SkillDirectoryPath -ChildPath $resource
        New-Item -Path $dir -ItemType Directory -Force | Out-Null
        Write-Output "[OK] Created $resource/"
    }
}

function New-ExampleFiles {
    param([string]$SkillDirectoryPath, [string]$SkillNameValue, [string[]]$ResourceList)

    foreach ($resource in $ResourceList) {
        $dir = Join-Path -Path $SkillDirectoryPath -ChildPath $resource
        if ($resource -eq "scripts") {
            $path = Join-Path -Path $dir -ChildPath "example.ps1"
            $content = $ExampleScript.Replace("__SKILL_NAME__", $SkillNameValue)
            Set-Content -LiteralPath $path -Value $content -Encoding UTF8
            Write-Output "[OK] Created scripts/example.ps1"
        } elseif ($resource -eq "references") {
            $path = Join-Path -Path $dir -ChildPath "api_reference.md"
            Set-Content -LiteralPath $path -Value $ExampleReference -Encoding UTF8
            Write-Output "[OK] Created references/api_reference.md"
        } elseif ($resource -eq "assets") {
            $path = Join-Path -Path $dir -ChildPath "example_asset.txt"
            Set-Content -LiteralPath $path -Value $ExampleAsset -Encoding UTF8
            Write-Output "[OK] Created assets/example_asset.txt"
        }
    }
}

function Remove-UnlistedResources {
    param([string]$SkillDirectoryPath, [string[]]$KeepList)

    foreach ($resource in $AllowedResources) {
        if ($KeepList -contains $resource) {
            continue
        }
        $path = Join-Path -Path $SkillDirectoryPath -ChildPath $resource
        if (Test-Path -LiteralPath $path) {
            Remove-Item -LiteralPath $path -Recurse -Force
            Write-Output "[OK] Removed $resource/"
        }
    }
}

function Split-FrontmatterAndBody {
    param([string]$SkillContent)

    if ($SkillContent -notmatch '(?s)\A---\r?\n(.*?)\r?\n---\r?\n?(.*)\z') {
        throw "Invalid SKILL.md frontmatter format."
    }

    return [pscustomobject]@{
        Frontmatter = $Matches[1]
        Body = $Matches[2]
    }
}

function Set-FrontmatterStringValue {
    param([string]$FrontmatterText, [string]$Key, [string]$Value)

    $quoted = Get-YamlQuotedValue -Value $Value
    $lines = $FrontmatterText -split "`r?`n"
    $found = $false
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        if ($line -match '^\S' -and $line -match "^$Key\s*:") {
            $lines[$i] = "${Key}: $quoted"
            $found = $true
            break
        }
    }

    if (-not $found) {
        $lines += "${Key}: $quoted"
    }

    return [string]::Join("`n", $lines)
}

function Set-BodyTitle {
    param([string]$BodyText, [string]$NewTitle)

    $updated = [regex]::Replace($BodyText, '(?m)^#\s+.*$', "# $NewTitle", 1)
    if ($updated -eq $BodyText) {
        $trimmed = $BodyText.TrimStart()
        if ([string]::IsNullOrWhiteSpace($trimmed)) {
            return "# $NewTitle`n"
        }
        return "# $NewTitle`n`n$trimmed"
    }
    return $updated
}

function Write-SkillDocument {
    param([string]$SkillMdPath, [string]$FrontmatterText, [string]$BodyText)

    $content = "---`n$FrontmatterText`n---`n`n$BodyText"
    if (-not $content.EndsWith("`n")) {
        $content += "`n"
    }
    Set-Content -LiteralPath $SkillMdPath -Value $content -Encoding UTF8
}

function Get-UnquotedValue {
    param([string]$Value)

    $trimmed = $Value.Trim()
    if (($trimmed.Length -ge 2) -and (
            (($trimmed.StartsWith('"')) -and ($trimmed.EndsWith('"'))) -or
            (($trimmed.StartsWith("'")) -and ($trimmed.EndsWith("'")))
        )) {
        return $trimmed.Substring(1, $trimmed.Length - 2)
    }
    return $trimmed
}

function Get-TopLevelFrontmatterMap {
    param([string]$FrontmatterText)

    $map = @{}
    $lines = $FrontmatterText -split "`r?`n"
    foreach ($line in $lines) {
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }
        if ($line -match "^\s+#") {
            continue
        }
        if ($line -match "^\S") {
            if ($line -notmatch "^([A-Za-z0-9_-]+)\s*:\s*(.*)$") {
                throw "Invalid YAML in frontmatter: unsupported line '$line'"
            }
            $key = $Matches[1]
            $rawValue = $Matches[2]
            if ([string]::IsNullOrWhiteSpace($rawValue)) {
                $map[$key] = $null
            } else {
                $map[$key] = Get-UnquotedValue -Value $rawValue
            }
        }
    }
    return $map
}

function Get-OpenAiInterfaceMap {
    param([string]$YamlContent)

    $map = @{}
    $inInterface = $false
    $lines = $YamlContent -split "`r?`n"
    foreach ($line in $lines) {
        if ($line -match '^interface\s*:\s*$') {
            $inInterface = $true
            continue
        }
        if ($inInterface -and ($line -match '^\S') -and ($line -notmatch '^\s*#')) {
            break
        }
        if (-not $inInterface) {
            continue
        }
        if ([string]::IsNullOrWhiteSpace($line) -or ($line -match '^\s*#')) {
            continue
        }
        if ($line -notmatch '^\s{2}([A-Za-z0-9_]+)\s*:\s*(.*)$') {
            throw "Invalid agents/openai.yaml interface line '$line'"
        }
        $map[$Matches[1]] = $Matches[2].Trim()
    }
    return $map
}

function Test-QuotedYamlString {
    param([string]$RawValue)
    return ($RawValue -match '^"(?:[^"\\]|\\.)*"$') -or ($RawValue -match "^'(?:[^']|'')*'$")
}

function Test-SkillValidity {
    param([string]$SkillDirectoryPath)

    $skillMdPath = Join-Path -Path $SkillDirectoryPath -ChildPath "SKILL.md"
    if (-not (Test-Path -LiteralPath $skillMdPath -PathType Leaf)) {
        return [pscustomobject]@{ Valid = $false; Message = "SKILL.md not found" }
    }

    $content = Get-Content -LiteralPath $skillMdPath -Raw -Encoding UTF8
    if (-not $content.StartsWith("---")) {
        return [pscustomobject]@{ Valid = $false; Message = "No YAML frontmatter found" }
    }
    if ($content -notmatch '(?s)\A---\r?\n(.*?)\r?\n---') {
        return [pscustomobject]@{ Valid = $false; Message = "Invalid frontmatter format" }
    }

    $frontmatterText = $Matches[1]
    try {
        $frontmatter = Get-TopLevelFrontmatterMap -FrontmatterText $frontmatterText
    } catch {
        return [pscustomobject]@{ Valid = $false; Message = $_.Exception.Message }
    }

    $unexpected = @()
    foreach ($key in $frontmatter.Keys) {
        if ($AllowedFrontmatterKeys -notcontains $key) {
            $unexpected += $key
        }
    }
    if ($unexpected.Count -gt 0) {
        $allowed = [string]::Join(", ", $AllowedFrontmatterKeys)
        $bad = [string]::Join(", ", ($unexpected | Sort-Object))
        return [pscustomobject]@{ Valid = $false; Message = "Unexpected key(s) in SKILL.md frontmatter: $bad. Allowed properties are: $allowed" }
    }

    if (-not $frontmatter.ContainsKey("name")) {
        return [pscustomobject]@{ Valid = $false; Message = "Missing 'name' in frontmatter" }
    }
    if (-not $frontmatter.ContainsKey("description")) {
        return [pscustomobject]@{ Valid = $false; Message = "Missing 'description' in frontmatter" }
    }

    $name = ([string]$frontmatter["name"]).Trim()
    if ($name -notmatch "^[a-z0-9-]+$") {
        return [pscustomobject]@{ Valid = $false; Message = "Name '$name' should be hyphen-case (lowercase letters, digits, and hyphens only)" }
    }
    if ($name.StartsWith("-") -or $name.EndsWith("-") -or $name.Contains("--")) {
        return [pscustomobject]@{ Valid = $false; Message = "Name '$name' cannot start/end with hyphen or contain consecutive hyphens" }
    }
    if ($name.Length -gt $MaxSkillNameLength) {
        return [pscustomobject]@{ Valid = $false; Message = "Name is too long ($($name.Length) characters). Maximum is $MaxSkillNameLength characters." }
    }
    $directoryName = Split-Path -Path $SkillDirectoryPath -Leaf
    if ($name -ne $directoryName) {
        return [pscustomobject]@{ Valid = $false; Message = "Frontmatter name '$name' must match skill directory '$directoryName'" }
    }

    $descriptionValue = ([string]$frontmatter["description"]).Trim()
    if ([string]::IsNullOrWhiteSpace($descriptionValue)) {
        return [pscustomobject]@{ Valid = $false; Message = "Description must not be empty" }
    }
    if ($descriptionValue.Contains("<") -or $descriptionValue.Contains(">")) {
        return [pscustomobject]@{ Valid = $false; Message = "Description cannot contain angle brackets (< or >)" }
    }
    if ($descriptionValue.Length -gt 1024) {
        return [pscustomobject]@{ Valid = $false; Message = "Description is too long ($($descriptionValue.Length) characters). Maximum is 1024 characters." }
    }

    $openAiYamlPath = Join-Path -Path $SkillDirectoryPath -ChildPath "agents/openai.yaml"
    if (-not (Test-Path -LiteralPath $openAiYamlPath -PathType Leaf)) {
        return [pscustomobject]@{ Valid = $false; Message = "agents/openai.yaml not found" }
    }
    try {
        $interface = Get-OpenAiInterfaceMap -YamlContent (Get-Content -LiteralPath $openAiYamlPath -Raw -Encoding UTF8)
    } catch {
        return [pscustomobject]@{ Valid = $false; Message = $_.Exception.Message }
    }

    foreach ($requiredKey in @("display_name", "short_description", "default_prompt")) {
        if (-not $interface.ContainsKey($requiredKey)) {
            return [pscustomobject]@{ Valid = $false; Message = "Missing interface.$requiredKey in agents/openai.yaml" }
        }
        if (-not (Test-QuotedYamlString -RawValue $interface[$requiredKey])) {
            return [pscustomobject]@{ Valid = $false; Message = "interface.$requiredKey must be a quoted string" }
        }
    }

    $displayName = Get-UnquotedValue -Value $interface["display_name"]
    if ($displayName.Contains("$")) {
        return [pscustomobject]@{ Valid = $false; Message = "display_name must not include '$'" }
    }

    $shortDescription = Get-UnquotedValue -Value $interface["short_description"]
    if (($shortDescription.Length -lt 25) -or ($shortDescription.Length -gt 64)) {
        return [pscustomobject]@{ Valid = $false; Message = "short_description must be 25-64 characters (got $($shortDescription.Length))" }
    }

    $defaultPrompt = Get-UnquotedValue -Value $interface["default_prompt"]
    $requiredInvocation = '$' + $name
    if (-not $defaultPrompt.Contains($requiredInvocation)) {
        return [pscustomobject]@{ Valid = $false; Message = "default_prompt must mention '$requiredInvocation'" }
    }

    return [pscustomobject]@{ Valid = $true; Message = "Skill is valid!" }
}

try {
    $gitRoot = Get-GitRoot
    $skillsRoot = Get-SkillsRoot -GitRoot $gitRoot
    New-Item -Path $skillsRoot -ItemType Directory -Force | Out-Null

    if ($Action -ne "cleanup-personal") {
        if ([string]::IsNullOrWhiteSpace($SkillName)) {
            throw "-SkillName is required for action '$Action'."
        }

        $normalizedSkillName = Get-NormalizedSkillName -RawName $SkillName
        if ([string]::IsNullOrWhiteSpace($normalizedSkillName)) {
            throw "Skill name must include at least one letter or digit."
        }
        if ($normalizedSkillName.Length -gt $MaxSkillNameLength) {
            throw "Skill name '$normalizedSkillName' is too long ($($normalizedSkillName.Length) characters). Maximum is $MaxSkillNameLength."
        }
        if ($normalizedSkillName -ne $SkillName) {
            Write-Output "Note: Normalized skill name from '$SkillName' to '$normalizedSkillName'."
        }

        $skillDirectory = Join-Path -Path $skillsRoot -ChildPath $normalizedSkillName
        $skillMdPath = Join-Path -Path $skillDirectory -ChildPath "SKILL.md"
        $resourceList = @(Get-ResourceList -RawResources $Resources)
    }

    switch ($Action) {
        "create" {
            if ($Examples -and ($resourceList.Count -eq 0)) {
                throw "-Examples requires -Resources to be set."
            }
            if (Test-Path -LiteralPath $skillDirectory) {
                throw "Skill directory already exists: $skillDirectory"
            }

            $createdByThisRun = $false
            try {
                New-Item -Path $skillDirectory -ItemType Directory -Force | Out-Null
                $createdByThisRun = $true
                Write-Output "[OK] Created skill directory: $skillDirectory"

                $skillTitle = Get-SkillTitle -NormalizedSkillName $normalizedSkillName
                $content = $SkillTemplate.Replace("__SKILL_NAME__", $normalizedSkillName).Replace("__SKILL_TITLE__", $skillTitle)
                Set-Content -LiteralPath $skillMdPath -Value $content -Encoding UTF8
                Write-Output "[OK] Created SKILL.md"

                Write-NewOpenAiYaml -SkillDirectoryPath $skillDirectory -SkillNameValue $normalizedSkillName -InterfaceCsv $Interface
                if ($resourceList.Count -gt 0) {
                    New-ResourceDirectories -SkillDirectoryPath $skillDirectory -ResourceList $resourceList
                    if ($Examples) {
                        New-ExampleFiles -SkillDirectoryPath $skillDirectory -SkillNameValue $normalizedSkillName -ResourceList $resourceList
                    }
                }

                Write-Output "[OK] Created skill '$normalizedSkillName'"
            } catch {
                if ($createdByThisRun -and (Test-Path -LiteralPath $skillDirectory -PathType Container)) {
                    $resolvedSkillDirectory = [System.IO.Path]::GetFullPath($skillDirectory)
                    $resolvedParent = [System.IO.Path]::GetFullPath((Split-Path -Path $resolvedSkillDirectory -Parent))
                    $resolvedSkillsRoot = [System.IO.Path]::GetFullPath($skillsRoot)
                    if (-not $resolvedParent.Equals($resolvedSkillsRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
                        throw "Create failed and rollback target escaped the skills root: $resolvedSkillDirectory"
                    }
                    Remove-Item -LiteralPath $resolvedSkillDirectory -Recurse -Force
                    Write-Output "[OK] Rolled back incomplete skill directory: $resolvedSkillDirectory"
                }
                throw
            }
        }
        "update" {
            if (-not (Test-Path -LiteralPath $skillDirectory -PathType Container)) {
                throw "Skill directory not found: $skillDirectory"
            }

            $hasDescription = -not [string]::IsNullOrWhiteSpace($Description)
            $hasTitle = -not [string]::IsNullOrWhiteSpace($Title)
            $hasBodyFile = -not [string]::IsNullOrWhiteSpace($BodyFile)
            $hasResources = $resourceList.Count -gt 0
            $hasInterface = -not [string]::IsNullOrWhiteSpace($Interface)

            if (-not ($hasDescription -or $hasTitle -or $hasBodyFile -or $hasResources -or $PruneResources -or $hasInterface)) {
                throw "Update requires at least one of -Description, -Title, -BodyFile, -Resources, -Interface, or -PruneResources."
            }
            if ($PruneResources -and ($resourceList.Count -eq 0)) {
                throw "-PruneResources requires -Resources to be set."
            }
            if ($PruneResources -and (-not $ConfirmDestructive)) {
                throw "-PruneResources removes directories. Re-run with -ConfirmDestructive after verifying the exact target skill '$skillDirectory'."
            }

            $skillContent = Get-Content -LiteralPath $skillMdPath -Raw -Encoding UTF8
            $parts = Split-FrontmatterAndBody -SkillContent $skillContent
            $frontmatter = $parts.Frontmatter
            $body = $parts.Body

            if ($hasBodyFile) {
                if (-not (Test-Path -LiteralPath $BodyFile -PathType Leaf)) {
                    throw "Body file not found: $BodyFile"
                }
                $body = Get-Content -LiteralPath $BodyFile -Raw -Encoding UTF8
            }
            if ($hasDescription) {
                $frontmatter = Set-FrontmatterStringValue -FrontmatterText $frontmatter -Key "description" -Value $Description
            }
            if ($hasTitle) {
                $body = Set-BodyTitle -BodyText $body -NewTitle $Title
            }
            if ($hasDescription -or $hasTitle -or $hasBodyFile) {
                Write-SkillDocument -SkillMdPath $skillMdPath -FrontmatterText $frontmatter -BodyText $body
                Write-Output "[OK] Updated SKILL.md"
            }

            if ($hasResources) {
                New-ResourceDirectories -SkillDirectoryPath $skillDirectory -ResourceList $resourceList
            }
            if ($PruneResources) {
                Remove-UnlistedResources -SkillDirectoryPath $skillDirectory -KeepList $resourceList
            }
            if ($hasInterface) {
                Update-OpenAiYaml -SkillDirectoryPath $skillDirectory -SkillNameValue $normalizedSkillName -InterfaceCsv $Interface
            }

            Write-Output "[OK] Updated skill '$normalizedSkillName'"
        }
        "delete" {
            if (-not (Test-Path -LiteralPath $skillDirectory -PathType Container)) {
                throw "Skill directory not found: $skillDirectory"
            }
            if (-not $ConfirmDestructive) {
                throw "Delete removes the complete skill directory. Re-run with -ConfirmDestructive after verifying the exact target '$skillDirectory'."
            }
            Remove-Item -LiteralPath $skillDirectory -Recurse -Force
            Write-Output "[OK] Deleted skill '$normalizedSkillName'"
        }
        "validate" {
            if (-not (Test-Path -LiteralPath $skillDirectory -PathType Container)) {
                throw "Skill directory not found: $skillDirectory"
            }
            $result = Test-SkillValidity -SkillDirectoryPath $skillDirectory
            Write-Output $result.Message
            if ($result.Valid) {
                exit 0
            }
            exit 1
        }
        "cleanup-personal" {
            $userProfilePath = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
            if ([string]::IsNullOrWhiteSpace($userProfilePath)) {
                throw "Unable to resolve the user profile directory."
            }
            $personalSkillsRoot = [System.IO.Path]::GetFullPath((Join-Path -Path $userProfilePath -ChildPath ".codex/skills"))
            $personalPath = [System.IO.Path]::GetFullPath((Join-Path -Path $personalSkillsRoot -ChildPath "test-skill"))
            $expectedPersonalPath = [System.IO.Path]::GetFullPath((Join-Path -Path $userProfilePath -ChildPath ".codex/skills/test-skill"))
            if (-not $personalPath.Equals($expectedPersonalPath, [System.StringComparison]::OrdinalIgnoreCase)) {
                throw "Resolved cleanup target escaped the expected personal skill path: $personalPath"
            }
            if (-not $ConfirmDestructive) {
                throw "Cleanup removes the personal test skill recursively. Re-run with -ConfirmDestructive after verifying the exact target '$personalPath'."
            }
            if (Test-Path -LiteralPath $personalPath -PathType Container) {
                Remove-Item -LiteralPath $personalPath -Recurse -Force
                Write-Output "[OK] Removed personal skill at $personalPath"
            } else {
                Write-Output "[OK] Personal skill already absent: $personalPath"
            }
        }
    }

    exit 0
} catch {
    Write-Output "[ERROR] $($_.Exception.Message)"
    exit 1
}
