# Initialize a minimal source-derived runbook

Ensure the repository has a small, useful `AGENTS/RUNBOOK/` based on facts that can be read directly from the current repository.

This is an initialization pass, not a full documentation project and not a proof-of-working exercise.

## Preserve existing knowledge

If `AGENTS/RUNBOOK/` already contains useful project guidance, keep it. Do not replace the directory, rewrite files for style, or remove project-specific knowledge merely to normalize structure.

Read existing runbook files before adding or changing anything. Correct an existing statement only when current source or configuration clearly contradicts it.

## Source inspection

Inspect the smallest useful set of repository files needed to understand the project, such as:

- solution, project, package, module, and dependency manifests
- source entrypoints and composition roots
- build, publish, deployment, migration, and maintenance scripts
- CI and workflow definitions
- application and environment configuration files
- container, orchestration, and infrastructure definitions
- API routes, handlers, contracts, schemas, events, and clients
- authentication and authorization registration and enforcement code
- entities, persistence configuration, migrations, and storage adapters
- existing README and permanent project documentation

Prefer tracked project files as the source of truth. Do not infer project behavior from filenames alone when the relevant file can be read.

## Minimal initial content

Create or update only the runbook files for which concise, reusable facts are directly supported by the repository.

Usually initialize these when applicable:

- `AGENTS/RUNBOOK/TECHSTACK.md`: languages, target runtimes, frameworks, major libraries, project or solution layout, executable entrypoints, and the main responsibility of each clearly identifiable component.
- `AGENTS/RUNBOOK/COMMANDS.md`: canonical commands explicitly declared by repository scripts, workflows, manifests, or existing documentation.

Also create a topic file from the standard runbook set only when the repository clearly exposes useful facts for it:

- `INTERFACES.md`
- `AUTHENTICATION.md`
- `DATASTORAGE.md`
- `INFRASTRUCTURE.md`
- `DEBUGGING.md`
- `DEBUGRELEASE.md`
- `EXTERNAL_DOCUMENTATION.md`

Do not create empty files, empty headings, generic checklists, guessed sections, or placeholders such as `TBD` merely to complete the standard file list.

Keep the initial runbook short. Record the stable facts that would save a future agent from rescanning obvious project structure; omit implementation trivia that is easy to rediscover and unlikely to affect future work.

## Static-fact boundary

Record only what the repository establishes directly. Examples include:

- a project targets a runtime declared in its project file
- an application entrypoint registers a specific framework or service
- a workflow invokes a particular command
- a script publishes to a particular relative output path
- configuration declares a key, endpoint shape, port, provider, or environment distinction
- source code defines an API route, event contract, storage adapter, authentication scheme, or authorization policy

Use precise source-derived wording such as `The project targets ...`, `The workflow invokes ...`, or `The application registers ...`.

Do not convert static evidence into runtime claims. For example, a command present in a workflow may be documented as the command used by that workflow, but not as a command that currently succeeds.

## No proof of working

Do not run builds, tests, applications, deployments, migrations, network calls, or environment checks solely to initialize this runbook.

Do not add:

- proof-of-working sections
- command output or logs
- test or build result summaries
- timestamps for verification runs
- claims such as `works`, `passes`, `healthy`, `reachable`, or `verified`
- environment-specific observations that are not established by repository files

Actual execution evidence discovered during a later user task may be documented when it becomes reusable operational knowledge, but this run-once initialization must remain source-derived.

## Commands

Add a command to `COMMANDS.md` only when it is explicitly present or unambiguously composed from authoritative repository files, for example a checked-in script, workflow step, package script, solution file, project file, Makefile, or existing project documentation.

Preserve required working directories and important arguments when they are visible from the source.

Do not invent convenience commands, assume globally installed tools, or state that a command succeeds.

## Secrets and environment data

Never copy secret values, credentials, tokens, certificates, private keys, cookies, or credential-bearing connection strings into the runbook.

Configuration key names, non-secret provider names, environment names, port numbers, relative paths, and public or non-sensitive endpoint shapes may be recorded when useful. Describe where sensitive values are supplied instead of recording them.

## Completion

Complete this instruction after:

1. existing runbook content has been preserved,
2. the relevant repository structure has been inspected,
3. the smallest useful source-derived runbook files have been created or corrected,
4. no empty or speculative documentation has been added, and
5. no proof-of-working claims or execution evidence have been introduced.

If the repository does not contain enough information to support a useful statement for a topic, omit that topic rather than guessing.

Do not commit runbook changes unless the user asks for a commit.
