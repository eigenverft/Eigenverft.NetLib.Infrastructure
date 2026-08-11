# Agent guide

Persistent instructions for coding agents and automation working in this repository.

Read this file before changing code.

Follow these instructions directly. Do not guess project behavior from general training data.

## Purpose

Use this file as the repository-level working guide for coding agents.

The coding agent has two responsibilities:

1. Complete the requested task.
2. Keep reusable project knowledge available for future agent sessions.

Maintaining the runbook is a core requirement, not optional documentation work.

A later chat session may not have access to the same conversation history, discoveries, failed attempts, environment details, or reasoning path. If project knowledge is not preserved in the repository, future agents may need to rediscover it by guessing, scanning excessive source, or repeating failed commands.

`AGENTS/RUNBOOK/` exists to preserve operational knowledge across sessions so future agents can work faster, safer, and with fewer incorrect assumptions.

Before making changes:

1. Read `AGENTS.md`.
2. Read this file.
3. Read relevant files in `AGENTS/RUNBOOK/` when they exist.
4. Inspect the actual source code, configuration, scripts, tests, and documentation.
5. Compare runbook guidance against the current repository state.
6. Follow existing project conventions.

This setup is vendor-neutral. It does not assume a specific editor, model, coding agent, harness, or automation system.

## Repository paths

Use repository-relative paths.

`AGENTS/` contains agent instructions and agent-maintained operational guidance.

`AGENTS/RUNBOOK/` contains project-specific operational knowledge maintained by coding agents while they work.

Do not put agent-maintained runbook files in general human documentation folders unless the user explicitly asks for that.

Human-facing documentation may live in folders such as `docs/`, `Docs/`, or `documentation/`.

The root `AGENTS.md` should stay small. It should only route agents to the relevant instruction files.

## Operational guidance

Project-specific operational guidance lives in `AGENTS/RUNBOOK/`.

## Efficiency

Maintain `AGENTS/EFFICIENCY.md` as a minimal, always-current record of reusable efficiency observations and improvements. Keep it short, practical, and focused on reducing unnecessary token, tool, context, console output, logging output, test output, and churn waste without reducing correctness.

## Work history

Maintain `AGENTS/WORKHISTORY.md` as a minimal, always-current trace of user requests and delivered outcomes. Prefix each new entry with **today's calendar date** in ISO form `yyyy-MM-dd` (e.g. `2026-06-17`), then a space, then `#` and the next sequence number for that day (`#1`, `#2`, …). Do **not** paste the literal text `yyyy-mm-dd` — substitute the real date. Keep each entry very short, preferably one line. Briefly state what the user requested, what was delivered, and only include interpretation notes when they are essential.

## Release notes

Maintain `AGENTS/RELEASENOTESLOCATIONS.md` as a short, up-to-date list of all places in the repository that contain release notes, changelogs, release history, or similar documentation.
Find these locations by searching the repository. Do not rely only on paths already listed in the file. Update the list when a release-note location is added, removed, renamed, or moved.
Use repository-relative paths. Add a short description only when the purpose of a location is not clear from its path.
Before pushing changes to a remote, review the list and decide whether the changes being pushed require release-note updates under the repository’s existing conventions. Update only the relevant release-note locations, and make sure those updates are committed before the push.
Do not add release notes for every commit or for routine internal changes unless the repository’s release-note rules require it.

## Commit messages

If a commit is requested, use a multiline commit message: a short summary line, a blank line, and a longer body explaining what changed and why.

## Instruction precedence

Follow instructions in this order:

1. System, platform, safety, and tool instructions.
2. Explicit user instructions for the current task.
3. Root `AGENTS.md`.
4. This file: `AGENTS/GUIDE.md`.
5. Project-specific guidance in `AGENTS/RUNBOOK/`.
6. Existing source code, tests, scripts, CI configuration, and documentation.
7. General knowledge.

If instructions conflict, follow the higher-priority instruction.

Do not use this file to override user instructions, security rules, or tool limitations.

If this file or `AGENTS/RUNBOOK/` conflicts with the actual codebase, inspect the repository and update the relevant runbook file when appropriate.

## Runbook directory

Maintain this folder when project-specific operational guidance is needed:

`AGENTS/RUNBOOK/`

This folder contains practical project knowledge for future coding agents and contributors.

Create the folder only when one of these is true:

* Agent support is being initialized.
* The current task depends on missing operational guidance.
* A failure or ambiguity revealed missing guidance that future agents need.
* The repository contains no runbook yet, and the current task requires build, test, debug, infrastructure, storage, authentication, interface, external documentation, or environment knowledge.

Do not create or edit runbook files for unrelated one-line changes, formatting-only changes, typo fixes, or local refactors.

Treat `AGENTS/RUNBOOK/` as a living operational runbook.

Keep it accurate.

Do not leave important operational knowledge only in chat history.

## Required runbook files

Use these files when the topic applies to the repository.

Equivalent filenames are acceptable only when the repository already uses another clear convention.

Keep equivalent topic coverage.

| File                                       | Purpose                                                                                                                                                                                                                                                                     |
| ------------------------------------------ | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `AGENTS/RUNBOOK/COMMANDS.md`               | Canonical commands for restore/install, build, test, lint, format, run, migrations, code generation, cleanup, and other safe repository operations.                                                                                                                         |
| `AGENTS/RUNBOOK/TECHSTACK.md`              | Languages, frameworks, runtimes, major libraries, project layout, and how parts fit together.                                                                                                                                                                               |
| `AGENTS/RUNBOOK/EXTERNAL_DOCUMENTATION.md` | Source-of-truth external documentation links for technologies, frameworks, libraries, APIs, tools, protocols, and services used by this repository. Use this when a bug, technology question, or unclear behavior requires authoritative documentation instead of guessing. |
| `AGENTS/RUNBOOK/INFRASTRUCTURE.md`         | Environment-specific infrastructure: development, testing, staging, and production servers, file shares, database addresses, service endpoints, queues, storage, ports, configuration keys, access requirements, and environment separation rules. Do not store secrets.    |
| `AGENTS/RUNBOOK/DEBUGGING.md`              | Practical debugging guidance: how to run locally, attach a debugger, useful breakpoints, log locations, common failure modes, and debugging tips while the app is already running.                                                                                          |
| `AGENTS/RUNBOOK/DEBUGRELEASE.md`           | How Debug and Release, or equivalent modes, differ: compiler flags, optimizations, logging, feature toggles, environment assumptions, and runtime behavior.                                                                                                                 |
| `AGENTS/RUNBOOK/DATASTORAGE.md`            | Where and how data is persisted: databases, files, blobs, connection/config, schema or entity map, keys, relationships, migrations, safe inspection paths, and snapshot versus live-reference rules.                                                                        |
| `AGENTS/RUNBOOK/AUTHENTICATION.md`         | How users, services, and clients authenticate and authorize: sessions, tokens, roles, scopes, policies, and where enforcement lives in code.                                                                                                                                |
| `AGENTS/RUNBOOK/INTERFACES.md`             | External and internal integration surfaces: APIs, events, schemas, request/response conventions, domain contracts, and compatibility rules.                                                                                                                                 |

Do not create empty template files just to satisfy this list.

Create or update a runbook file when the current task needs it or when useful operational knowledge has been discovered.

## Runbook ownership

You are responsible for the accuracy of `AGENTS/RUNBOOK/` for the parts of the repository you touch.

At the start of a task:

1. Check whether relevant runbook files exist.
2. Read the relevant runbook files.
3. Verify important instructions against current source, scripts, configuration, and tests before relying on them.

During a task:

1. Notice missing guidance.
2. Notice stale guidance.
3. Notice commands that no longer work.
4. Notice undocumented setup requirements.
5. Notice undocumented failure modes.
6. Notice changed interfaces, storage, authentication, infrastructure, build behavior, external documentation, or debugging behavior.

Before finishing a task:

1. Review every changed source, configuration, project, build, deployment, workflow, interface, storage, authentication, infrastructure, and operational script file against the relevant runbook topics.
2. Update the relevant runbook files in the same change whenever the implementation change adds, removes, invalidates, or clarifies reusable operational knowledge. This review and any required update are part of completing the code change, not optional follow-up documentation.
3. Remove or correct stale guidance that would mislead the next agent.
4. If the review finds no reusable operational knowledge change, leave the runbook untouched. Do not edit files merely to record that they were reviewed.
5. Keep updates concise, factual, and based on the repository state.
6. Do not add speculative documentation or claims that were not established by source, configuration, authoritative documentation, or actual execution.

The runbook should become more accurate after every meaningful agent session. A code change that leaves affected runbook guidance stale is incomplete.

## When to update the runbook

Update the relevant file in `AGENTS/RUNBOOK/` in the same change when any of these change:

* Build modes.
* Build commands.
* Test commands.
* Debugging workflows.
* Runtime requirements.
* Tech stack.
* Major libraries.
* Project layout.
* External documentation sources.
* Official documentation URLs for technologies used by the repository.
* Known authoritative references for frameworks, libraries, tools, APIs, protocols, or services.
* Infrastructure.
* Environment separation.
* Server names or aliases.
* Service endpoints.
* Database addresses.
* File share paths.
* Queue names.
* Storage locations.
* Network ports.
* Configuration keys.
* Data storage.
* Database schema.
* Migrations.
* Authentication.
* Authorization.
* External APIs.
* Internal interfaces.
* Event contracts.
* Request or response schemas.
* Environment variables.
* Required local services.
* Deployment assumptions.
* Operational guardrails.

Also update the runbook when existing guidance is missing, stale, incomplete, misleading, or contradicted by the repository.

Do not update `AGENTS/RUNBOOK/` for changes that do not affect future work.

Examples that usually do not require runbook updates:

* Formatting-only changes.
* Comment-only changes.
* Small local refactors.
* Typo fixes.
* Private implementation details that do not affect build, test, debug, infrastructure, storage, authentication, external documentation, or interfaces.

## After failures

When a command, script, build, test, debug step, environment setup step, infrastructure lookup, file share access, database connection, authentication flow, data access, migration, external documentation lookup, or integration call fails:

1. Identify the failure.
2. Fix the task if possible.
3. Decide whether the failure reveals reusable project knowledge.
4. If yes, record reusable knowledge in the relevant file under `AGENTS/RUNBOOK/`.
5. Include:

   * What failed.
   * Why it failed, if known.
   * The fix or workaround.
   * A guardrail for next time.

Do not document random noise.

Only document a transient or environment-specific failure when it reveals a reusable setup requirement, missing prerequisite, command ordering rule, access requirement, environment distinction, documentation source, or safety guardrail.

Do not leave repeated failures only in chat history.

## Working in this repository

Follow these rules:

* Prefer existing conventions.
* Prefer existing libraries.
* Prefer existing patterns.
* Prefer existing tooling.
* Keep changes limited to the task.
* Avoid unrelated refactors.
* Avoid speculative cleanup.
* Avoid inventing architecture.
* Read source before changing behavior.
* Read tests before changing tested behavior.
* Add or update tests when the task changes behavior.
* Use the smallest reasonable change that solves the task.
* Preserve public contracts unless the user requested a breaking change.
* Do not hide failures.
* Do not claim success unless checks support it.
* Keep the runbook accurate for the parts of the repository you touch.

## Commands

Do not invent commands when the repository contains authoritative tooling.

Before running build, test, migration, format, lint, or run commands, inspect relevant files first:

* `AGENTS/RUNBOOK/COMMANDS.md`
* README files.
* CI configuration.
* Package manifests.
* Solution or project files.
* Build scripts.
* Task runner files.
* Docker files.
* Makefiles.
* Compose files.
* Migration tooling.
* Existing documentation.

If commands are missing or unclear, infer from repository files and update `AGENTS/RUNBOOK/COMMANDS.md` when the result is reusable.

Prefer documented commands over guessed commands.

## Security and secrets

Never commit secrets.

Never write actual secrets to `AGENTS/RUNBOOK/`, source files, tests, examples, logs, comments, or documentation.

Secrets include:

* Passwords.
* Tokens.
* API keys.
* Private keys.
* Certificates.
* Session cookies.
* Personal credentials.
* Production connection strings with credentials.
* Private customer data.
* Sensitive local paths when they expose personal information.

Document where secrets come from instead.

Use placeholders such as:

* Environment variables.
* Secret stores.
* Local user configuration.
* Development-only sample values.
* Test-only fixtures.

Infrastructure documentation may include non-secret identifiers when appropriate, such as hostnames, server aliases, database addresses, file share paths, ports, service URLs, queue names, storage names, and configuration keys.

If a value is sensitive, document where to find it safely instead of writing the value.

Do not weaken authentication or authorization unless the user explicitly requests it and the change is safe for the intended environment.

## Generated and large files

Avoid editing these manually unless the task explicitly requires it or project tooling generated the change:

* Generated files.
* Vendored code.
* Lockfiles.
* Snapshots.
* Migrations.
* Minified assets.
* Build outputs.
* Binary files.

If tooling updates these files, include them only when they are expected results of the task.

## Infrastructure

Do not guess infrastructure.

Before changing environment-specific configuration, deployment assumptions, connection setup, file share access, database connectivity, service endpoints, queues, storage, or network-related behavior:

1. Read `AGENTS/RUNBOOK/INFRASTRUCTURE.md`.
2. Identify the relevant environment: local, development, testing, staging, or production.
3. Inspect configuration files, deployment files, environment variable usage, scripts, CI configuration, and source code.
4. Keep environment details clearly separated.
5. Do not copy production values into development or testing guidance unless the repository explicitly uses shared infrastructure.
6. Do not store secrets.
7. Update the runbook when infrastructure guidance is missing, stale, or changed.

Update `AGENTS/RUNBOOK/INFRASTRUCTURE.md` when infrastructure, environment separation, addresses, endpoints, file shares, ports, service names, or access assumptions change.

## Data storage

Do not guess table names, collection names, keys, relationships, file paths, or migration rules.

Before reading or changing stored data behavior:

1. Read `AGENTS/RUNBOOK/DATASTORAGE.md`.
2. Inspect entities, schemas, migrations, repositories, queries, and configuration.
3. Use safe read/query/seed paths documented by the project.
4. Do not use production data unless explicitly authorized.
5. Do not expose private data in logs, tests, docs, or chat output.

Update `AGENTS/RUNBOOK/DATASTORAGE.md` when storage behavior changes or when a failure reveals missing storage guidance.

## Authentication and authorization

Do not guess authentication behavior.

Before changing authentication or authorization:

1. Read `AGENTS/RUNBOOK/AUTHENTICATION.md`.
2. Inspect middleware, policies, roles, scopes, token handling, session handling, and enforcement points.
3. Preserve existing security boundaries.
4. Add or update tests for changed security behavior.
5. Document reusable guidance.

Update `AGENTS/RUNBOOK/AUTHENTICATION.md` when authentication or authorization behavior changes.

## Interfaces

Do not guess external or internal contracts.

Before changing APIs, events, schemas, or domain contracts:

1. Read `AGENTS/RUNBOOK/INTERFACES.md`.
2. Inspect existing handlers, clients, schemas, validators, tests, and compatibility rules.
3. Preserve backward compatibility unless the task requires a breaking change.
4. Update tests and examples when contracts change.
5. Document changed contracts.

Update `AGENTS/RUNBOOK/INTERFACES.md` when interface behavior changes.

## Debugging

Before debugging:

1. Read `AGENTS/RUNBOOK/DEBUGGING.md`.
2. Use documented run and attach steps.
3. Use documented log locations.
4. Check common failure modes.
5. Prefer repeatable debugging steps.

When a new debugging failure mode is discovered, update `AGENTS/RUNBOOK/DEBUGGING.md`.

## Debug and Release behavior

Before changing behavior that may differ by build mode:

1. Read `AGENTS/RUNBOOK/DEBUGRELEASE.md`.
2. Check compiler flags, optimization settings, logging, feature toggles, conditional compilation, and environment assumptions.
3. Verify behavior in the relevant mode.
4. Document changed behavior.

Update `AGENTS/RUNBOOK/DEBUGRELEASE.md` when build-mode behavior changes.

## Tech stack

Before adding dependencies or changing frameworks:

1. Read `AGENTS/RUNBOOK/TECHSTACK.md`.
2. Prefer libraries already used by the project.
3. Prefer the platform defaults already used by the project.
4. Avoid adding new dependencies for small tasks.
5. Document meaningful stack changes.

Update `AGENTS/RUNBOOK/TECHSTACK.md` when languages, frameworks, runtimes, major libraries, or project layout change.

## External documentation

Do not guess external technology behavior when authoritative documentation is available.

Before answering or changing code related to a framework, library, tool, API, protocol, or service that is not fully clear:

1. Read `AGENTS/RUNBOOK/EXTERNAL_DOCUMENTATION.md` when it exists.
2. Prefer official documentation and vendor-maintained references.
3. Use the documented source links to verify behavior before relying on assumptions.
4. Update `AGENTS/RUNBOOK/EXTERNAL_DOCUMENTATION.md` when a useful authoritative source is discovered.

Do not use this file for random search results, blog posts, outdated articles, or one-off links unless they are clearly important to the repository.

Keep entries concise and focused on sources future agents should check again.

## Definition of done

Before finishing a task, verify:

1. The requested change is complete.
2. The change is limited to the requested scope.
3. Existing conventions were followed.
4. Relevant tests were added or updated when behavior changed.
5. Relevant checks were run when available.
6. Failures are fixed or clearly documented.
7. `AGENTS/RUNBOOK/` was checked when relevant to the task.
8. `AGENTS/RUNBOOK/` was updated when operational knowledge changed.
9. Stale or misleading runbook guidance touched by the task was corrected.
10. No secrets were added.
11. No unrelated changes were introduced.
12. The final response states what changed and what was verified.

If a check could not be run, say so and explain why.

Do not claim that tests, builds, or tools passed unless they were actually run and passed.

## Final response

In the final response to the user:

* Be concise.
* State what changed.
* State what was verified.
* State any checks that could not be run.
* Mention whether `AGENTS/RUNBOOK/` was checked when relevant.
* Mention any `AGENTS/RUNBOOK/` updates.
* Mention any remaining risks or follow-up work.

Do not include unnecessary implementation details unless the user asks for them.
