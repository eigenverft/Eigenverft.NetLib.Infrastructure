# PowerShell Rules

Rule IDs are stable references. Do not renumber them when rules are reordered or new rules are inserted.

### `PS001`: PowerShell Supports Required Versions And Platforms

Use Windows PowerShell 5.1-compatible syntax by default, even when repository or runtime indicators show that PowerShell 7 will execute the code. Do not introduce PowerShell 7-only syntax.

The code must run on all of the following:

- Windows PowerShell 5/5.1 on Windows.
- PowerShell 7 or later on Windows, macOS, and Linux.

When behavior differs by operating system, detect the platform with a Windows PowerShell 5.1-compatible mechanism and select the platform-specific branch inside the affected function. Do not use `$IsWindows`, `$IsMacOS`, or `$IsLinux`.

### `PS002`: Required External Commands Are Validated

If PowerShell code requires an external command, it must fail fast with a clear and actionable error when that command is unavailable.

### `PS003`: Automatic And Reserved Variables Are Not Shadowed

Parameters and local variables must not assign to or shadow PowerShell automatic or reserved variables in any casing, including:

- `$PSItem`
- `$args`
- `$IsWindows`, `$IsMacOS`, and `$IsLinux`
- `$Error`
- `$PID`
- `$LASTEXITCODE`
- `$Matches`
- `$HOME`
- `$PSBoundParameters`
- `$PSScriptRoot`
- `$MyInvocation`

### `PS004`: Function Verbs Are Windows PowerShell Approved

Exported or otherwise public PowerShell function names must use verbs approved by Windows PowerShell 5/5.1. An exception must have a one-line comment immediately before the function declaration that explains why it is necessary, and the function must use `[Diagnostics.CodeAnalysis.SuppressMessage("PSUseApprovedVerbs","")]`.

### `PS005`: Primary Function Nouns Are Singular

The primary name of a PowerShell function should use a singular noun.

### `PS006`: ShouldProcess Is Not Implemented By Default

PowerShell functions should not declare `SupportsShouldProcess` or implement `WhatIf` or `Confirm` behavior by default. An exception is compliant only when the function's behavior requires it, a one-line comment immediately before the function declaration explains why, and the implementation contains only the justified behavior.

### `PS007`: Parameters Do Not Bind From The Pipeline

PowerShell parameters must not use:

- `ValueFromPipeline`; or
- `ValueFromPipelineByPropertyName`.

### `PS008`: Policy Parameters Are Not Default True Switches

Policy and behavior parameters must not use default-true switches. Represent those choices with string parameters constrained by `ValidateSet` instead.

### `PS009`: Functions Are Safe With Stop Error Preference

PowerShell functions must behave correctly when the caller sets `ErrorActionPreference` to `Stop`.

### `PS010`: Try Catch Adds Actionable Context

Use `try`/`catch` only when it adds value. Rethrow caught failures with concise and actionable messages.

### `PS011`: Functions Are Idempotent

PowerShell functions must be idempotent so repeated runs converge to the same state without errors or drift.

### `PS012`: Functions Have Complete Comment Based Help

Every PowerShell function except an inline helper must provide comment-based help with:

- Synopsis;
- Description;
- Parameters;
- Examples; and
- Notes.

Example:

```powershell
function Get-ExampleValue {
    <#
    .SYNOPSIS
    Returns an example value.

    .DESCRIPTION
    Returns the supplied value unchanged and demonstrates complete comment-based help.

    .PARAMETER Value
    Value to return.

    .EXAMPLE
    Get-ExampleValue -Value 'sample'

    Returns 'sample'.

    .NOTES
    Supports Windows PowerShell 5.1 and PowerShell 7 or later.
    #>
    param(
        [string]$Value
    )

    return $Value
}
```

### `PS013`: Help Examples Are Relevant

Comment-based help examples must cover every relevant case without adding possible but nonsensical examples.

### `PS014`: PowerShell Code Is ASCII Only

PowerShell syntax, identifiers, string literals, and other executable code must contain only ASCII characters. Non-code prose such as comments and comment-based help may contain Unicode when appropriate, but em dashes and typographic quotation marks are forbidden. Use an ASCII hyphen and straight quotes (`"` or `'`) instead.

### `PS015`: Implementations Are Lean And Reviewable

PowerShell implementations must be lean, readable, and reviewer-friendly. Include inline comments when they help an external reviewer understand the code, and keep each inline comment brief.

### `PS016`: Review Stays Within The Requested Scope

Review only PowerShell code within the user-requested scope. Do not expand the review solely to search for unrelated pre-existing violations. Report qualifying violations found within that scope through `common.md`; do not create a separate pre-existing-issues list.

### `PS017`: C Style Ternary Operators Are Forbidden

PowerShell code must not contain the C-style ternary pattern with a question mark and colon. Use `if` and `else` instead.

### `PS018`: Null Is On The Left In Comparisons

Equality and inequality comparisons with null must place `$null` on the left side.

### `PS019`: Backtick Line Continuation Is Forbidden

PowerShell code must not use the backtick for line continuation. Use one of the following instead:

- keep the command on one line;
- use splatting; or
- use separate statements.

### `PS020`: Inline Helpers Use Local Scope And Distinct Names

Inline helper functions must use explicit local scope and a distinct collision-resistant prefix, for example `function local:_Normalize-List`.

### `PS021`: Inline Helpers Have Only A Brief Comment

An inline helper must have one brief one-line comment immediately before its function declaration that explains its purpose. It must not have a comment-based help block.

### `PS022`: Inline Helpers Suppress Unapproved Verb Warnings

The required collision-resistant prefix causes analyzers to treat even otherwise approved inline-helper verbs, such as `_Get`, as unapproved. Suppression is therefore the default for every inline helper and requires no separate justification. Place the following attribute between the function name and its `param` block:

```powershell
[Diagnostics.CodeAnalysis.SuppressMessage("PSUseApprovedVerbs","")]
```

### `PS023`: Inline Helpers Avoid Incidental Pipeline Output

Inline helpers must not emit incidental values to the pipeline. They must explicitly return only their intended result.

### `PS024`: Inline Helpers Are Not Exported

Inline helpers must exist only for the duration of the outer function call and must not be exported.

### `PS025`: Inline Helpers Are Deterministic

Inline helpers must not rely on session-global state and must be deterministic for the same inputs.

### `PS026`: Functions Are StrictMode Version 3 Safe

PowerShell functions must fully comply with StrictMode version 3 when it is active before the function call.

### `PS027`: Variables Are Initialized Before Use

PowerShell code must not reference uninitialized variables, including inside interpolated strings.

### `PS028`: Object Properties Exist Before Access

PowerShell code must not reference object properties that do not exist.

### `PS029`: Core Logic Avoids Uncontrolled Dynamic Invocation

Core logic must not use dynamic invocation with uncontrolled values. Normal method calls and static method calls are allowed.

### `PS030`: Array Indices Are Valid

PowerShell code must not use out-of-bounds or unresolved array indices.

### `PS031`: Collection Return Values Are Definitive

A function that returns multiple elements as one definitive collection value must use one of the following forms:

```powershell
,@(...)
,([type[]]@(...))
```
