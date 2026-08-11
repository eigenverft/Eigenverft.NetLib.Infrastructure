# Notice source layout

During the run-once phase, inspect the current repository once for clear deviations from this preferred source layout:

```text
src/
├── <solution/workspace files>  definitions that bind project and workspace content
├── prj/                        independent project roots
└── wrk/                        non-project resources scoped to repository workspaces
```

`src/` may contain multiple independent solution or workspace definitions. Each may bind any relevant subset of project roots under `src/prj/` and workspace resources under `src/wrk/`. Place material under `src/wrk/` when its scope belongs to a repository workspace rather than one project.

`src/prj/` also includes script, module, package, and comparable product roots; a solution or workspace file is optional.

Apply this instruction only when one or more concrete software products are clearly identifiable. Skip archive, template, and data-only repositories, as well as repositories without an explicit product structure; this check normalizes an existing product layout and must not invent products or project groupings.

For every repository to which this instruction applies, `src/prj/` and `src/wrk/` are required structural directories even when no current content is assigned to them.

Classify paths from their actual scope and repository references, not from names alone. Ignore paths that the repository's effective ignore rules or established conventions classify as generated or local-only; do not exclude tracked authoritative source, project, solution, or workspace content merely because a tool can regenerate it. Report missing required structural directories and clear migrations of existing content; do not propose speculative product groupings. Do not change the source layout during the run-once phase.

When a reported change is later requested, preserve contents and history throughout the migration. Move every recognized solution or workspace definition file into `src/`. If different files would have the same target name, preserve both by inserting `.moved` before the incoming file's extension, for example `name.moved.slnx`; use an additional collision-safe suffix when needed. As an integral part of the migration, locate and inspect all repository-owned references to every affected path, then update every valid reference and any internal relative paths in moved files. Verify that no stale references remain. If a particular move or required reference update cannot be resolved unambiguously, skip and report only that change, then continue with the remaining changes.

After all run-once instructions are complete, append the following block to the next user-facing response only when at least one source-layout change is recommended. Include only applicable change types.

```markdown
## SourceLayout

The following source-layout changes are recommended.

Recommended changes

1. Create `<missing-required-directory>`.
2. Move `<current-path>` → `<target-path>`.
```

## Suggested follow-up actions

When this instruction reports at least one finding, contribute one or more suitable suggested follow-up actions to the response. Use the following as the standard example, adapting, replacing, or extending its actions when the actual findings justify a different or additional follow-up:

```markdown
<letter>) Source: `080_NOTICESOURCELAYOUT.md`

1. Apply all reported source-layout changes and update affected repository references.
```
