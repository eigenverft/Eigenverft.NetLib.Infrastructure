---
name: execute-local-implementation-handoff-creation
description: Create repository-local implementation handoffs from source-based investigation. By default, the current agent performs the review and writes ordered hash-scoped Markdown handoffs. When the user explicitly requests one or more subagents, delegate the investigation with minimally biased prompts and collect only the created paths. In every execution strategy, create files only for concrete and worthwhile implementation concerns, or return exactly NO_HANDOFFS_CREATED.
---

# Execute Local Implementation Handoff Creation

## Purpose

Use this softskill to investigate a repository and preserve concrete implementation concerns as local Markdown handoffs.

The handoff logic is the core behavior. Subagents are optional execution workers, not a prerequisite.

Default behavior:

- the current agent performs the requested investigation directly
- the current agent writes every justified handoff itself
- the current agent returns only created repository-relative paths, or exactly `NO_HANDOFFS_CREATED`

Delegated behavior applies only when the user explicitly asks to use one or more subagents. In that case, the current agent orchestrates the requested number of subagents and collects their handoff paths.

Do not launch subagents merely because an assignment is broad. A request such as `/execute-local-implementation-handoff-creation mach einen generellen Code-Review` runs directly unless the user also requests subagents.

An **Implementation Handoff** is:

- grounded in the actual repository source
- more concrete than a general review observation or recommendation
- close enough to implementation that a later planning agent can turn it into an implementation plan
- organized around one coherent, meaningful chunk of work
- explicit about likely code areas, contracts, dependencies, constraints, and unresolved decisions
- not itself a coding-agent implementation plan

The workflow is complete when every justified finding is safely written to local handoff files, or when no finding qualifies and the result is exactly `NO_HANDOFFS_CREATED`.

## Execution Strategy

### Direct execution

Direct execution is the default.

The current agent must:

1. Interpret the requested review or investigation scope.
2. Inspect the repository directly.
3. Generate one short hash token for the logical handoff run.
4. Apply the Handoff Eligibility Gate to every finding.
5. Write the complete useful result for each justified concern into ordered local handoff files.
6. Return only the created repository-relative paths, one per line, or exactly `NO_HANDOFFS_CREATED`.

A broad request such as a general code review is valid. The current agent should inspect the repository systematically, prioritize high-signal implementation concerns, and split justified findings by implementation boundary. It must not invent findings merely to make a broad review appear productive.

### Delegated execution

Use delegated execution only when the user explicitly requests a subagent or multiple subagents.

There is no separate named single-subagent mode:

- if the user asks for one subagent, launch one
- if the user asks for a specific number, launch that number
- if the user clearly asks for several subagents without a count, choose a small bounded number appropriate to the repository and task

The orchestrating agent must:

1. Define a bounded objective for each subagent.
2. Brief each subagent with minimum sufficient context.
3. Generate and assign a unique short hash token to each delegated run.
4. Include the eligibility, file-writing, filename, no-handoff, and return contracts in every prompt.
5. Require each subagent to inspect the repository and derive its own findings.
6. Require each subagent not to manufacture a finding or file merely to complete the assignment.
7. Require each subagent to preserve its complete useful result in handoff files and return only paths, or exactly `NO_HANDOFFS_CREATED`.
8. Aggregate only successful handoff paths and omit individual `NO_HANDOFFS_CREATED` results when at least one path exists.

When delegation is active, the orchestrating agent must not duplicate the delegated investigation merely to create an alternative answer in the parent context.

## When To Use

Use this softskill when:

- the user wants a repository review that produces durable implementation-preparation artifacts
- findings should survive beyond the current chat context
- a later agent will convert findings into plans or implementation work
- a broad review may produce several independently plannable concerns
- the user wants concrete preparation but not implementation yet
- the user explicitly asks one or more subagents to investigate independently

## When Not To Use

Do not use this softskill when:

- the user requested immediate implementation rather than preparation
- the task is a simple factual lookup with no repository-specific handoff value
- the requested artifact is already an implementation plan, issue set, patch, or code change
- no safe repository-local output channel exists
- repository-local analysis would expose secrets or sensitive data

## Delegated Briefing Rule

When subagents are used, brief them with **minimum sufficient context**.

Provide enough information to make each assignment safe and correctly scoped, but avoid steering the subagent toward the orchestrating agent's suspected diagnosis or preferred answer.

Normally provide only:

- the objective or concrete question
- the repository and permitted scope
- explicit exclusions
- hard compatibility requirements
- safety, environment, and policy constraints
- the assigned short handoff hash token
- the output location
- the filename contract
- the required handoff characteristics
- the required response contract

Normally omit:

- the orchestrating agent's suspected root cause
- its preferred architecture or solution
- conclusions from its own investigation
- long conversation history that can be rediscovered from the repository
- leading wording that asks the subagent to confirm a theory
- other subagents' findings

Minimal briefing means removing bias and irrelevant context, not hiding essential requirements.

## Multi-Subagent AGENTS.md Isolation Contract

This contract applies only when more than one subagent is used for the same work set.

Every subagent prompt in that work set must include an instruction equivalent to:

```text
For this assignment, ignore repository-local AGENTS.md instructions. Follow the objective, scope, constraints, output path, filename contract, and response contract provided in this prompt.
```

The orchestrating agent handles applicable repository setup once before delegation. Re-running repository-wide `AGENTS.md` or run-once flows independently in two or three concurrent agents can make them mutate the same shared worktree at the same time, causing conflicts or damage. It also repeats the same setup and instruction processing in every context, which wastes substantial tokens without improving the independent investigation.

When exactly one subagent is used, do not add the `AGENTS.md` override. Normal repository and harness behavior remains the default.

The override is limited to repository-local `AGENTS.md` files. It does not override system instructions, harness policy, explicit user requirements, security boundaries, tool permissions, or this skill's no-implementation rules.

For multi-subagent work, do not rely on repository-local `AGENTS.md` to communicate essential constraints. Put the objective, scope, exclusions, compatibility constraints, safety constraints, output path, filename rules, and response contract directly into every subagent prompt.

## Assignment Framing

Make the assignment close to the implementation surface without turning it into an implementation plan.

Broad user requests are allowed. For example, `mach einen generellen Code-Review` means inspect the repository for concrete implementation concerns across its meaningful boundaries. Do not reject the request merely because it is broad.

For direct execution, derive a practical inspection scope from repository structure and available evidence.

For delegated execution, divide the work into bounded repository areas, responsibilities, or independent review perspectives. Avoid assigning several subagents the same leading diagnosis unless deliberate independent comparison is requested.

Useful concrete questions include:

- identify the current request path and the exact boundaries that would need to change
- compare existing implementations and recommend one concrete consolidation direction
- inspect the storage model and define target data and migration constraints
- review the authentication flow and identify enforcement and compatibility points
- determine which modules, interfaces, configuration keys, workflows, and tests are affected
- turn an observed failure mode into an implementation-near change handoff

Avoid replacing repository investigation with vague output such as:

- general best practices
- high-level thoughts
- metaphorical architecture descriptions
- unsupported modernization ideas
- speculative future improvements

Prefer actual repository paths, symbols, responsibilities, dependencies, and behavior.

## Handoff Eligibility Gate

An Implementation Handoff is justified only when repository evidence supports a concrete implementation concern worth preserving for a later planning or implementation step.

Before creating a file, the producing agent must be able to answer all of these questions:

1. What current behavior, defect, gap, duplication, risk, or required change creates real implementation work?
2. What concrete target direction is supported by repository evidence?
3. Is the concern substantial enough for a later planning agent to act on?
4. Would creating and processing this handoff add more value than leaving the current state unchanged?

If any answer is no, uncertain, purely speculative, or only a matter of taste, do not create that handoff.

The following are not sufficient reasons to create a handoff:

- the agent was asked to review an area
- an alternative exists but is not recommended
- the current implementation is already appropriate
- the only result is **keep as is**, **no action**, or **insufficient evidence**
- cleanup could be done opportunistically but is not worth planning now
- a possible future requirement might make a change useful later
- the agent wants to demonstrate that it inspected the repository
- the output format appears to expect at least one file

Finding nothing is a valid result. Quality and usefulness take precedence over file production.

When no concern passes this gate, the producing agent must:

- create no handoff files
- leave existing handoff files unchanged
- return exactly `NO_HANDOFFS_CREATED`
- add no explanation, summary, reviewed-area list, or fallback report

## Handoff Hash Contract

Every logical handoff-producing run must use a short filename-safe hash token.

Use a lowercase hexadecimal token with 8 to 12 characters. It does not need to be cryptographically derived or globally unique; it only needs enough practical uniqueness to distinguish concurrent and logically separate runs in the local workspace.

Examples:

```text
7f3a91c2
c84d2e6b
91af07d4c3e2
```

Do not use repeated-character placeholders, sequential tokens, agent numbers, model names, timestamps alone, or long globally unique identifiers when a short hash is sufficient.

In direct execution, the current agent generates one token before writing handoffs for the logical review run.

In delegated execution, the orchestrating agent generates and assigns a different token for every concurrent or logically separate subagent run. Each subagent must use the exact assigned token and must not invent, replace, shorten, expand, or normalize it.

## Output Location Contract

Use a safe repository-local location explicitly designated for local agent work. Unless another safe path is supplied or established by repository guidance, use:

```text
AGENTS/HANDOFF/
```

Apply these rules:

- resolve the repository root before writing
- prefer a path that is already ignored or intentionally local-only
- in multi-subagent work, state the output path directly in every prompt instead of relying on `AGENTS.md`
- do not stage, commit, or push generated handoffs
- do not modify `.gitignore` merely to support this workflow
- do not write to an arbitrary external directory
- if a concern qualifies but no safe repository-local output location can be determined, return the blocker instead of claiming success
- if no concern qualifies, return `NO_HANDOFFS_CREATED` even when no output directory exists

## Filename Contract

Use this exact filename pattern:

```text
handoff-<handoffhash>-NN-<topic>.md
```

Example using `7f3a91c2`:

```text
handoff-7f3a91c2-01-domain-contracts.md
handoff-7f3a91c2-02-storage-transition.md
handoff-7f3a91c2-03-api-compatibility.md
```

Rules:

- `<handoffhash>` is the exact short token for the producing run
- `NN` is a two-digit order number such as `01`, `02`, or `03`
- the order number appears immediately after the hash so filesystem sorting preserves sequence within the run
- `<topic>` is a short lowercase hyphen-case name for the actual implementation concern
- numbering starts at `01` for each hash-scoped run
- ordering follows dependency or implementation-preparation order
- every file from the same run uses the same token
- an existing file must never be overwritten
- a filename collision must be returned as a blocker


## Complete-Result Persistence Contract

When one or more concerns pass the Handoff Eligibility Gate, the handoff files are the complete useful result. The chat response is only a path handoff.

The producing agent must:

- preserve all useful source-based findings that pass the gate in the files
- avoid shortening the result merely to keep the response small
- move supporting detail into the appropriate handoff or a concise appendix inside it
- avoid leaving important reasoning only in the response
- avoid returning the complete report after the files were written successfully

When no concern qualifies, `NO_HANDOFFS_CREATED` is the complete successful response.

## Topic Splitting Contract

After at least one concern passes the gate, create multiple handoffs when the justified result contains:

- materially different implementation concerns
- independent subsystems or ownership boundaries
- a foundation followed by dependent work
- separate data, API, UI, infrastructure, migration, or rollout concerns
- more detail than one planning agent should process as one coherent work package

Split by implementation boundary or dependency, not arbitrary text length.

Each handoff should be:

- large enough to represent a meaningful chunk of future implementation work
- small enough for one planning session to understand and convert into a plan
- internally coherent
- independently nameable
- ordered relative to other handoffs from the same hash-scoped run

Do not create one file for every minor observation. Merge closely related findings that would naturally be planned and implemented together.

## Default Handoff Ordering

Use the repository's real dependency graph when available.

A useful fallback order is:

1. shared concepts, contracts, and compatibility decisions
2. data model, persistence, or state foundations
3. core application or domain behavior
4. external interfaces and integrations
5. UI, presentation, or consumer adaptation
6. migration, rollout, observability, and cleanup

The actual repository dependency order takes precedence.

## Handoff Size Standard

Create **medium to near-long** handoffs rather than tiny notes or exhaustive unbounded reports.

Include:

- enough source evidence to establish the current state
- enough concrete detail to define the implementation concern
- enough boundaries and constraints to prevent a later planning agent from repeating the full investigation
- enough dependency and risk information to order the work

Split a handoff when it contains multiple independently plannable outcomes or becomes difficult to navigate. Merge it when it contains only a few shallow observations without a substantial implementation boundary.

## Source-First Contract

Inspect the actual repository before writing conclusions.

Relevant evidence may include:

- source files
- project and package manifests
- entrypoints and composition roots
- configuration
- tests
- schemas and migrations
- workflows and deployment files
- existing runbooks and project notes
- public contracts and interfaces
- current Git state when relevant

Requirements:

- do not fill handoffs with generic practice disconnected from the repository
- make supporting source locations or observed behavior identifiable
- use repository-relative paths and symbol names whenever practical
- reduce confidence when source access is incomplete
- do not fill missing evidence with assumptions

## Required Handoff Structure

Use this structure unless the concern clearly requires a small variation:

```markdown
# Implementation Handoff <handoffhash> NN: Topic

## Assignment
What the producing agent was asked to determine.

## Intended outcome
The concrete implementation outcome this handoff prepares for.

## Scope
What is covered and what is intentionally excluded.

## Source inspected
Repository-relative files, symbols, configuration, tests, and other evidence.

## Current state
How the relevant code currently works and where responsibility lives.

## Concrete direction
The recommended target direction, stated in implementation-near terms.

## Technical approach
How the change could be realized technically: likely responsibility shifts, affected symbols and contracts, data or control flow, configuration or persistence changes, integration seams, error handling, and compatibility mechanics. Describe the implementation shape without writing the implementation or a step-by-step execution plan.

## Alternatives and recommendation
When more than one credible approach exists, compare realistic options, explain repository-specific trade-offs, recommend one, and state when another option would be preferable.

## Affected boundaries
Files, modules, APIs, schemas, configuration, tests, integrations, or ownership boundaries likely to matter.

## Compatibility and constraints
Behavior, contracts, environments, data, or operational properties that must be preserved.

## Dependencies and ordering
What must happen before this topic and what it enables afterward.

## Planning inputs
Concrete decisions, acceptance concerns, and verification surfaces the later planning agent must include.

## Risks and unresolved questions
Only genuine risks or decisions not safely derivable from the source.
```

Optional sections may include:

- `## Data and migration considerations`
- `## Interface examples`
- `## Candidate test surfaces`
- `## Rejected directions`
- `## Supporting evidence`

Do not add empty sections.

## Concreteness Contract

Handoffs must be concrete enough that a later planning agent can begin without repeating the entire investigation.

Good content resembles:

- `src/Orders/OrderController.cs` currently owns request mapping and payment orchestration; move orchestration behind an application-level boundary while preserving the controller contract.
- `PackageResolver` and `DepotResolver` duplicate version selection; consolidate the selection rule before changing acquisition behavior.
- The production route is configured in `ReverseProxySettings.Production.json`; development settings are outside the intended deployment change.

Avoid content such as:

- improve separation of concerns
- make the architecture cleaner
- use a more scalable approach
- consider modern best practices

Name actual files, symbols, responsibilities, data flows, contracts, and observed behavior whenever possible.

## Technical Direction Contract

Go beyond identifying affected areas and provide useful technical guidance about how the change could be realized.

When supported by repository evidence, describe:

- where responsibilities should remain, move, split, or consolidate
- which existing types, interfaces, methods, modules, configuration keys, schemas, or workflows are likely to change
- which new boundary or contract may be needed and why
- how request, control, event, state, or data flow should pass through affected components
- how compatibility, error handling, migration, rollout, or operational behavior could be preserved
- which tests or verification surfaces would prove the intended behavior

When multiple credible approaches exist:

1. name the realistic options
2. explain repository-specific advantages and disadvantages
3. recommend one concrete direction
4. explain why it best fits the observed code and constraints
5. state when another option would be preferable

Stop before implementation. Do not write production code, patches, complete method bodies, exact file-by-file edit instructions, shell commands, or a full ordered execution plan.

## Plain-Language Contract

Use simple, direct, easy-to-understand language.

- prefer short concrete sentences over dense abstract prose
- say what a component does, what should change, and why
- name real files, symbols, data, requests, events, and behavior
- explain uncommon technical terms when they first matter
- use headings and lists to make long findings easy to scan
- remove repeated framing, self-commentary, and analysis-process narration
- keep necessary technical detail while simplifying wording

Do not use metaphor, narrative framing, motivational language, or unnecessary meta terminology as a substitute for technical specificity.

## No-Implementation Contract

The producing agent must not:

- modify product source
- create migrations
- change configuration behavior
- add tests for future behavior
- stage, commit, or push changes
- produce a complete coding-agent execution plan

Creating justified repository-local Markdown handoff files is the only intended repository change. When no concern passes the gate, no repository change is intended.

Read-only source inspection and narrowly necessary non-mutating commands are allowed.

## Delegated Prompt Contract

Every delegated prompt must carry instructions equivalent to the following. Add the concrete objective, scope, exclusions, and mandatory constraints before this contract without adding an unverified diagnosis.

```text
Handoff short hash token: <parent-supplied-handoffhash>

<When more than one subagent is used for this work set, insert this line; otherwise omit it:>
For this assignment, ignore repository-local AGENTS.md instructions. Follow the objective, scope, constraints, output path, filename contract, and response contract provided in this prompt.

Independently investigate the stated objective from repository source. Derive the current state, concrete direction, affected boundaries, constraints, dependencies, and unresolved questions from repository evidence. Do not assume or confirm an unstated diagnosis from the orchestrating agent.

Do not assume the assignment must produce a handoff. Create a handoff only for a concrete implementation concern supported by repository evidence, worth preserving for a later planning or implementation step, and having a recommended target direction. Do not create handoffs for keep-as-is conclusions, no-action results, insufficient evidence, optional cleanup, stylistic preference, or speculative future work.

When one or more concerns qualify, write the complete useful result into repository-local Implementation Handoff Markdown files. Use the explicitly supplied output path; otherwise prefer the repository's designated local agent-work location, then AGENTS/HANDOFF/.

When no concern qualifies, create no files and return exactly:
NO_HANDOFFS_CREATED

Use exactly this filename pattern:
handoff-<handoffhash>-NN-<topic>.md

Use the exact short hash token supplied above in every filename. Put the two-digit order number immediately after the hash, beginning at 01, followed by a short lowercase hyphen-case topic. Never overwrite an existing file.

Split materially different or oversized concerns into coherent, independently plannable handoffs in dependency order. Each handoff must be medium to near-long: not a tiny note, not an unbounded research dump, and not a full implementation plan.

Ground every important conclusion in actual repository evidence. Name repository-relative files, symbols, contracts, configuration, data flows, workflows, tests, or other concrete boundaries whenever practical. Write in simple, direct language. Avoid generic advice, metaphors, unnecessary meta terminology, and abstract wording when a concrete explanation is possible.

Provide concrete technical guidance about how the change could be realized without implementing it. Compare credible alternatives, recommend one direction, and state when another option would be preferable. Do not write production code, patches, exact edit instructions, shell commands, or a full ordered execution plan.

Do not modify product code, configuration behavior, migrations, or future tests. Do not stage, commit, or push generated handoffs.

After successfully writing one or more files, return only their repository-relative paths, one path per line. When no handoff qualifies, return only NO_HANDOFFS_CREATED. Do not return summaries, topic descriptions, ordering commentary, excerpts, reviewed-area lists, or a report in your response.
```

## Return Contract

### Direct execution

When one or more handoffs are created, return only their repository-relative paths, one per line:

```text
AGENTS/HANDOFF/handoff-7f3a91c2-01-domain-contracts.md
AGENTS/HANDOFF/handoff-7f3a91c2-02-storage-transition.md
AGENTS/HANDOFF/handoff-7f3a91c2-03-api-adaptation.md
```

When none qualify, return exactly:

`NO_HANDOFFS_CREATED`

### Delegated execution

Each successful subagent must return only created paths or exactly `NO_HANDOFFS_CREATED`.

After all requested subagents complete:

- if at least one handoff path exists, return only all created repository-relative paths, one per line
- omit individual `NO_HANDOFFS_CREATED` responses when paths exist
- if no subagent created a handoff, return only `NO_HANDOFFS_CREATED`

Do not paste complete handoff contents or no-finding explanations into the main conversation unless the user explicitly asks to read them there.

## Failure Handling

If a handoff cannot be written:

- do not claim it was saved
- return the intended path and exact blocker
- include only the minimum fallback detail needed to avoid losing all useful work

In delegated execution, the orchestrating agent may provide another safe repository-local path or perform the file write itself using returned fallback content.

When source access is incomplete:

- state the missing source inside a handoff when a handoff can still be written
- reduce confidence
- avoid filling gaps with generic assumptions

A broad review with no justified finding still returns `NO_HANDOFFS_CREATED`.

## Security and Privacy Contract

Never place secrets, credentials, private keys, tokens, cookies, private customer data, or sensitive personal data into a handoff.

Document secret sources and configuration key names without copying secret values. Do not include large raw logs or data dumps. Summarize technically relevant evidence and point to the safe source location.

## Quality Checklist

Before direct execution:

- the requested scope is understood
- broad review scope has been mapped to practical repository inspection
- one unique handoff hash was generated
- output location and filename pattern are known
- eligibility, source-first, concreteness, technical-direction, plain-language, and no-implementation rules are active

Before delegated execution:

- the user explicitly requested one or more subagents
- each assignment is bounded and concrete
- each prompt contains only minimum sufficient context
- when more than one subagent runs, every prompt includes the `AGENTS.md` isolation instruction
- when exactly one subagent runs, no `AGENTS.md` override is added
- every delegated prompt repeats all essential task constraints directly instead of relying on repository guidance
- every delegated run has a unique assigned handoff hash
- output path, filename pattern, eligibility gate, and return contract are explicit

Before returning success:

- the response contains only created paths or exactly `NO_HANDOFFS_CREATED`
- when `NO_HANDOFFS_CREATED` is returned, no new handoff file exists for the run
- every new filename follows `handoff-<handoffhash>-NN-<topic>.md`
- every returned file exists when the harness can verify it
- no generated handoff was staged, committed, or pushed

## Typical Invocation Phrases

- `/execute-local-implementation-handoff-creation mach einen generellen Code-Review`
- `/execute-local-implementation-handoff-creation mach einen generellen Code-Review mit drei Subagenten`
- `Use $execute-local-implementation-handoff-creation to review this repository and create only justified implementation handoffs.`
- `Use one subagent and preserve only concrete repository-supported findings as handoffs.`
- `Use three independent subagents, keep the parent context small, and return only handoff paths or NO_HANDOFFS_CREATED.`
