# Notice local test artifacts

During the run-once phase, inspect the current repository worktree once for local artifacts produced by tests, checks, coverage, profiling, smoke or acceptance runs, visual verification, and related test diagnostics. Include ignored paths, but do not run builds, tests, or other project tools for this check.

Classify artifacts by their demonstrated purpose from their contents and repository-owned test source, configuration, scripts, or commands. A directory name, file extension, ignore status, or proximity to test source is not sufficient evidence. Recommend only artifacts that are clearly reproducible and disposable.

Exclude test source, fixtures, checked-in or authoritative expected snapshots, golden files, baselines, other checked-in expected output, and intentionally retained verification or diagnostic evidence. Also exclude ordinary build, cache, runtime, dependency, vendored, and product data unless it is clearly disposable test output.

Report only the highest fully disposable artifact root in each tree. When retained and disposable data share a tree, report only independently removable subtrees and omit ambiguous items.

Also inspect whether repository-owned test code, configuration, scripts, or commands clearly cause equivalent disposable output to be scattered across locations or retained without a useful lifecycle. Recommend consolidation only when the responsible control point and a safe change are unambiguous. Preserve an established clean convention; otherwise prefer one effectively ignored repository-local artifact tree or one suitable external temporary location, with collision-safe subpaths and clear cleanup behavior. Do not invent repository configuration merely to override ad-hoc tool defaults.

Do not remove artifacts or change their producers during the run-once phase. When a reported follow-up is later requested, remove only the exact reported disposable roots and apply only the reported repository-owned output changes, including affected repository references. If an individual cleanup or output change becomes ambiguous, skip and report only that item, then continue with the remaining changes.

After all run-once instructions are complete, append the following block to the next user-facing response only when at least one cleanup or output-location change is recommended. Include only applicable sections.

```markdown
## LocalTestArtifacts

The following local test-artifact changes are recommended.

Recommended cleanup

1. `path/to/artifact-root`

Recommended output-location changes

1. In `path/to/config-or-test-code`: consolidate `<current-location>` → `<target-location>` and update affected repository references.
```

## Suggested follow-up actions

When this instruction reports at least one finding, contribute one or more suitable suggested follow-up actions to the response. Use the following as the standard example, adapting, replacing, or extending its actions when the actual findings justify a different or additional follow-up:

```markdown
<letter>) Source: `090_NOTICELOCALTESTARTIFACTS.md`

1. Remove all reported disposable local test artifacts.
2. Apply all reported test-output location changes and update affected repository references.
```
