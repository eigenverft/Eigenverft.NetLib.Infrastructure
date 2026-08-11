# Common Rule Processing

## Review Read Only

Evaluate every selected rule against main-product files within the user-requested scope. Do not edit files, implement corrections, or change repository state. Do not expand the scope solely to search for unrelated or pre-existing violations.

## Use Repository Files As Evidence

Determine rule applicability and violations from in-scope repository source, solution, and project-definition files. Do not use binaries, build output, reflection, decompiled code, compiler-generated metadata, or runtime metadata as rule evidence. Other project configuration may establish scope or intended conventions but does not replace evidence from the file type governed by a selected rule.

## Report Only Concrete Violations

Report a rule only when in-scope file evidence establishes an actual violation and a concrete correction can be recommended. Do not report rules that are compliant, inapplicable, outside the requested scope, speculative, or merely matters of preference.

## Handle Conflicts And Limitations

Evaluate all selected rules before reporting a shared target. If two or more applicable rules remain mutually exclusive after instruction precedence is considered, do not guess; report the rule IDs, names, affected paths, and incompatible requirements under a concise `Rule conflicts` heading. If a required rule file or in-scope file is missing or unreadable, report the exact issue under a concise `Review limitations` heading and continue with independently reviewable rules. Do not present conflicts or limitations as compliance findings.

## Do Not Execute Verification

Do not run formatters, linters, analyzers, builds, tests, applications, or runtime probes. File inspection is the evidence for this review. Executable verification or implementation requires a separate user request.

## Findings-Only Output

When the review completes without a qualifying violation, rule conflict, or review limitation, return exactly:

`Code quality review complete. Material findings: 0.`

When qualifying violations exist, report one Markdown table and no compliance inventory. Use one row per rule when its finding and recommended correction are uniform. When a rule has different findings or corrections across files, use separate rows and list the corresponding repository-relative paths in `ReferenceFiles`.

| Rule ID | Rule Name | Finding | Recommended correction | ReferenceFiles |
| --- | --- | --- | --- | --- |
| `XX001` | `Rule Name` | Concrete file-supported violation | Concrete correction | `relative/path` |

When conflicts or limitations exist without qualifying violations, omit the table and report only the required exception headings. Do not report checked, compliant, non-applicable, or unchanged rules. Beyond the required zero-finding response, do not add rule counts, a pre-existing-issues list, or a narrative asserting overall compliance.
