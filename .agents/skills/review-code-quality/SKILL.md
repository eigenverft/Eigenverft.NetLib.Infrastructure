---
name: review-code-quality
description: Review main-product source, solution, and project-definition files against repository-provided extension-mapped quality rules. Use only for an explicitly requested code-quality or rule review, return only concrete rule violations, and keep repository state unchanged.
---

# Review Code Quality

## Overview

Use this skill as a read-only dispatcher for main-product file rules. It discovers relevant catalog-registered file types, loads the rule files registered for them, and reports only concrete violations. It does not define extension-specific rules itself, implement corrections, or produce a compliance inventory.

## When To Use

- The user explicitly requests a code-quality or rule review.
- Reviewing selected main-product files, a main-product area, or the complete main product.
- Reviewing one or more programming or scripting languages, solution definitions, or project definitions.

## When Not To Use

- Do not activate this skill merely because files will be created, modified, or refactored.
- Implementation, correction, formatting, build, test, or analyzer tasks.
- Tasks that do not review code quality or repository-provided rules.
- Files outside a repository when no rule package is available.
- Auxiliary code used only for distribution, activation, bootstrapping, packaging, deployment, repository maintenance, or similar supporting workflows, unless the user explicitly includes it in the review.

## Read-Only Contract

- Discover, search, and read files only as needed for the requested review.
- Do not create, edit, move, or delete repository files.
- Do not run formatters, linters, analyzers, builds, tests, applications, or other executable verification.
- Do not stage, commit, or otherwise change Git state.
- Recommend corrections for qualifying findings, but do not implement them.

## Rule Package Contract

- `references/rules/index.md` is the authoritative rule catalog.
- The catalog maps normalized reviewable file extensions to one or more rule files.
- The catalog may mark rule files as always applicable.
- Rule-file paths are relative to `references/rules/` unless the catalog states otherwise.
- Every reportable rule in a mapped rule file has a stable ID and a human-readable name declared as a level-three Markdown heading in the form ``### `<rule-id>`: <Rule Name>``.
- The content after a reportable rule heading and before the next rule heading is that rule's normative definition and may use paragraphs, lists, examples, or code fences.
- Reportable rule IDs must be unique across the complete catalog. Keep an existing ID when its rule is reordered or clarified; never renumber or reuse it. Assign a new ID when adding a new rule.
- `common.md` defines shared review and reporting behavior. It does not declare reportable rules and is exempt from the stable-ID heading format.
- Extensions and rule filenames must not be hardcoded in this `SKILL.md`.
- New reviewable file types are supported by extending the catalog and adding their rule files, without changing the discovery workflow.

Repository instructions and established local conventions take precedence over generic rule files. Configuration, analyzers, and formatters may help establish intended conventions, but do not authorize their execution.

## File And Rule Discovery

Before reviewing files:

1. Determine the repository root and the files relevant to the requested review.
2. Identify the main-product source, solution, and project-definition files from their actual purpose and repository references, not from directory names alone. Exclude auxiliary distribution, activation, bootstrap, packaging, deployment, and maintenance helpers unless the user explicitly includes them.
3. Read `references/rules/index.md` completely.
4. Inspect in-scope filenames and collect their normalized, lowercase file extensions.
5. Identify supported file types exclusively from extensions registered in the catalog. Do not infer a mapping from the repository name, documentation, directory names, file contents, compiler output, or runtime metadata.
6. Ignore unregistered documentation, configuration, solution, project-definition, metadata, and data-file extensions. Do not report them as missing mappings merely because they exist in the repository.
7. Ignore `.git`, dependency directories, generated output, build output, vendored code, and binary files unless the requested review explicitly targets them.
8. Select every rule file marked as always applicable.
9. Select every rule file mapped to an extension in the requested review scope.
10. Select additional rule files explicitly required by the catalog for mixed-file or cross-cutting reviews.
11. Read every selected rule file completely before evaluating the in-scope files.
12. Do not load rule files for unrelated extensions.

If the requested review does not identify target files, use all catalog-registered extensions present in the main product. If its scope later includes another extension, repeat the selection and read every newly applicable rule file completely.

## Missing Or Ambiguous Mappings

- Report a missing mapping only when the requested review explicitly identifies a file as reviewable source, solution, or project-definition content and its extension has no catalog entry.
- Do not report an unmapped extension merely because files with that extension exist in the repository.
- Do not invent extension-specific rules or silently map an extension based on file contents.
- If a catalog entry references a missing or unreadable rule file, report the exact path as a review limitation and continue with independently reviewable rules.
- If multiple catalog entries apply, use the union of their rule files and read each selected file once.

## Review

After rule discovery:

1. Evaluate the selected rules against main-product files within the requested review scope and higher-priority repository guidance.
2. Re-evaluate the selection whenever the set of in-scope extensions changes.
3. Report only file-supported violations that have a concrete recommended correction.
4. Follow the review and output contract in `common.md`.
