---
name: execute-handoff-set-reconciliation
description: Use when multiple independently produced handoff sets in AGENTS/HANDOFF/ may overlap, duplicate work, or prescribe incompatible technical directions and must be reconciled before planning or implementation. The fixer reads the complete selected handoff collection, detects producing-run sets by filename hash, preserves distinct implementation topics, performs a strict no-op when only one set exists or when multiple sets are already compatible, rechecks disputed areas against the current repository, and repairs only existing handoff files. A file is superseded only when it is fully absorbed, contains no remaining implementation task, and has been reduced to a verified redirect before being moved to AGENTS/HANDOFF/SUPERSEDED/. It never creates new handoff files and never implements product changes.
---

# Execute Handoff Set Reconciliation

## Purpose

Use this skill to reconcile independently produced handoff sets before another agent plans and implements them.

Separate direct or delegated review runs may investigate the same or related assignment and produce handoffs that:

- duplicate the same future work
- partially overlap in responsibility or scope
- prescribe incompatible architecture, ownership, contracts, data flow, or migration approaches
- disagree about dependencies or implementation order
- use different names for the same concern
- split or group the same work differently
- contain stale conclusions after the repository changed

The desired result is one coherent implementation-preparation collection of distinct, topic-bounded actionable handoffs inside `AGENTS/HANDOFF/`. Fully absorbed redirects belong in `AGENTS/HANDOFF/SUPERSEDED/`, not in the active collection.

The desired result is **not** a separate synthesis report and **not** a new generation of handoff files. The fixer changes only the existing handoffs when a real reconciliation problem exists.

## Core Principle

Do not optimize, rewrite, merge, rename, or delete handoffs merely because this skill was invoked.

A successful run may make no changes.

The fixer exists to resolve harmful set-level conflicts and overlap, not to restyle valid handoffs or minimize file count. Reconcile by coherent implementation topic and boundary. Do not collapse distinct independently plannable topics into one handoff merely because they are related or because fewer files would look simpler.

## Handoff Location and Naming

The active handoff directory is:

```text
AGENTS/HANDOFF/
```

Fully absorbed superseded redirects are archived under:

```text
AGENTS/HANDOFF/SUPERSEDED/
```

`SUPERSEDED/` is not an active implementation queue. Create that directory only when at least one fully absorbed handoff must be archived.

Active handoff filenames follow:

```text
handoff-<handoffhash>-NN-<topic>.md
```


The short hash identifies the originating handoff-producing run. All matching files with the same hash form one **producing-run handoff set**, regardless of whether the run was direct or delegated.

Examples:

```text
handoff-7f3a91c2-01-domain-contracts.md
handoff-7f3a91c2-02-storage-transition.md
handoff-c84d2e6b-01-engine-boundaries.md
handoff-c84d2e6b-02-storage-transition.md
```

The hash records provenance only. It does not make one recommendation more authoritative than another.

## Strict No-Op Gate

Before editing anything, group the selected handoffs by handoff hash.

### One producing-run set

When all selected handoffs share one hash, make no changes by default.

A single agent may intentionally create several ordered handoffs for one investigation. Their related topics, shared source areas, or dependencies are not evidence of problematic cross-agent overlap.

In this case:

- read every selected handoff completely, then confirm that the files form one producing-run set
- do not rewrite for style
- do not merge merely because scopes are related
- do not rename or renumber
- do not delete files
- do not perform a general quality review under this skill
- report that reconciliation was unnecessary because only one producing-run set was present

Only repair a single set when the user explicitly asks for intra-set repair rather than cross-agent reconciliation.

### Multiple producing-run sets

Multiple hashes are only a reason to inspect for conflicts, not a reason to change files.

If the sets are complementary, compatible, or already clearly separated:

- make no changes
- preserve all existing files
- report that no problematic overlap or contradiction was found

Apply changes only when there is concrete evidence that the current collection would cause a later planning or implementation agent to duplicate work, make incompatible changes, or receive materially misleading instructions.

## When To Use

Use this skill when:

- two or more direct or delegated review runs investigated the same or related task
- several hash-scoped handoff sets exist for the same repository area
- handoffs appear to recommend conflicting designs
- the same implementation outcome appears in several files
- a later agent should loop through the handoffs without avoidable contradictions
- the user asks to reconcile, fix, normalize, consolidate, deduplicate, or clean up a multi-agent handoff collection

## When Not To Use

Do not use this skill when:

- only one producing-run set exists and no explicit intra-set repair was requested
- the task is to generate handoffs
- the task is to plan or implement handoffs
- the user requested only a comparison or summary without file changes
- the agent cannot read the complete relevant handoff collection
- the files are not repository-local implementation handoffs

## Selection Contract

Use explicitly named handoffs when the user supplies a selection.

Otherwise inspect all top-level Markdown files matching the handoff naming convention in `AGENTS/HANDOFF/`.

When the user narrows the mutable selection:

- inspect other handoffs when needed to detect cross-set conflicts
- modify only the explicitly mutable files
- do not silently change an unselected file
- report when full reconciliation requires a wider mutable set

Ignore unrelated Markdown files and nested archive directories unless explicitly included.

## Whole-Collection-First Rule

Read the complete inspection collection before editing any handoff.

Do not repair files incrementally while later files remain unread. A locally sensible edit may conflict with another producing-run set.

For each file, identify:

- originating hash and sequence
- assignment and intended outcome
- scope and exclusions
- referenced files, symbols, modules, schemas, configuration, workflows, and tests
- current-state claims
- recommended technical direction
- alternatives and recommendation
- affected boundaries
- compatibility constraints
- dependencies and ordering
- risks and unresolved questions

Build a cross-file model of equivalent, overlapping, complementary, and conflicting concerns.

If the harness exposes a plan or task-tracking capability, use it for the fixer's reconciliation work. Do not create a separate plan Markdown file.

## Repository Source-of-Truth Contract

Handoffs are source-based proposals, not executable truth.

Reinspect the current repository when needed to resolve:

- contradictory current-state descriptions
- competing ownership or architecture proposals
- disputed contracts, data models, or control flow
- stale paths and symbols
- compatibility or migration disagreements
- dependency and ordering questions
- work already completed since a handoff was written

Inspect the smallest sufficient set of current source, tests, configuration, schemas, workflows, runbooks, and Git state.

Resolve decisions using this priority:

1. explicit current user requirements
2. current repository behavior and compatibility obligations
3. repository-local guidance and conventions
4. evidence and constraints in the handoffs
5. the smallest coherent direction that satisfies the intended outcome
6. maintainability, operability, migration safety, and verification quality

Do not prefer a handoff because it appears first, is longer, sounds more confident, or came from a particular hash.

## Problematic Overlap Test

Overlap is problematic only when leaving it unchanged would likely cause one or more of these outcomes:

- the same work is planned or implemented twice
- one implementation would undo or conflict with another
- two files claim incompatible ownership of the same responsibility
- contracts, schemas, data flow, or migration strategies cannot coexist
- a later agent cannot determine which direction is authoritative
- dependency order is circular, contradictory, or materially misleading
- stale instructions would cause unnecessary or unsafe work

Shared source files, related topics, similar terminology, or a common objective alone are not sufficient evidence.

## Reconciliation Taxonomy

### Exact duplication

Several files prepare substantially the same outcome with the same boundaries and compatible direction.

Resolution:

- choose one existing canonical file
- preserve the best useful content there
- after all useful content is retained in the canonical file, replace each fully redundant file with a short superseded-handoff redirect and move it to `AGENTS/HANDOFF/SUPERSEDED/`
- delete a redundant file only when the user explicitly requests deletion; inability to archive safely is a blocker, not a reason to delete

### Partial overlap

Files contain distinct useful work but claim some of the same responsibilities, source areas, or outcomes.

Resolution:

- assign each shared concern to one authoritative existing handoff
- rewrite affected files so their remaining scopes are explicit and non-overlapping
- preserve distinct useful work in its proper existing file

### Contradictory technical direction

Files prescribe incompatible ownership, contracts, data models, control flow, integration patterns, or migration approaches for the same concern.

Resolution:

- recheck current repository evidence
- choose one direction
- make that direction authoritative in one or more remaining existing handoffs
- remove contradictory prescriptive guidance elsewhere
- retain rejected alternatives only when their trade-offs remain useful planning context

### Different valid scopes mistaken for conflict

Recommendations appear incompatible but apply to different environments, consumers, phases, or boundaries.

Resolution:

- retain both
- clarify the distinct scopes only when the current files would otherwise mislead a later agent
- do not merge valid independent work

### Fragmentation

Several handoffs are individually too small and naturally form one planning and implementation unit.

Resolution:

- merge their useful content into one chosen existing canonical file
- when a fragment is fully absorbed and has no remaining implementation task, replace it with a superseded-handoff redirect and move it to `AGENTS/HANDOFF/SUPERSEDED/`
- delete a fragment only when the user explicitly requests deletion; inability to archive safely is a blocker, not a reason to delete

Do not use fragmentation as a reason to merge normal ordered handoffs from one producing-run set under the default no-op rule.

### Oversized mixed scope

One file contains multiple independently plannable concerns that overlap existing destinations.

Resolution:

- redistribute content among suitable existing files
- narrow the source file accordingly

When no suitable existing destination exists, keep the concern in the current file and clarify internal boundaries. Do not create a new file.

### Stale or already satisfied work

Current source already implements all or part of a handoff, or the proposed direction no longer fits.

Resolution:

- remove satisfied scope
- update the remaining delta
- when another existing handoff now carries all remaining or authoritative work and this file has no independent implementation task, replace it with a superseded-handoff redirect and move it to `AGENTS/HANDOFF/SUPERSEDED/`
- when no future work and no canonical destination remain, keep a short completed/no-action marker or delete only when the user explicitly requests deletion

Do not turn this skill into a complete current-state audit when no multi-set reconciliation issue exists.

### Ordering conflict

Handoffs disagree about prerequisites or imply an impossible order.

Resolution:

- derive the real dependency order from current source
- update dependency sections
- rename existing files only when their current ordering is materially misleading

## Decision Authority

The fixer must make ordinary technical reconciliation decisions itself.

When credible competing directions remain:

1. compare them against current source and constraints
2. choose the best-supported direction
3. make it authoritative in the remaining handoffs
4. preserve useful trade-off context
5. remove contradictory prescriptions

Do not leave avoidable statements such as "choose X or Y later" when repository evidence is sufficient to decide.

Stop for user input only when the unresolved decision is genuinely external to the repository and materially changes product, business, legal, privacy, security, or irreversible production behavior.

When such an external decision remains, place one precise unresolved question in the most relevant existing handoff. Do not preserve competing files that prescribe incompatible answers.

## Existing-Files-Only Contract

The fixer must not create any new handoff file.

Allowed transformations:

- keep an existing file unchanged
- rewrite an existing file in place
- move useful content from one existing file into another
- merge several files into one chosen existing canonical file
- redistribute content across existing files
- rename an existing file when topic or order is materially misleading
- replace a fully absorbed existing file with a superseded-handoff redirect and move it to `AGENTS/HANDOFF/SUPERSEDED/`
- delete an existing file only after useful content is preserved elsewhere and the user explicitly requests deletion; an archive blocker must not trigger deletion

Disallowed transformations:

- create an additional handoff
- create a synthesis handoff
- create a conflict report beside the handoffs
- create a replacement actionable set in another directory; moving verified fully superseded redirects to `AGENTS/HANDOFF/SUPERSEDED/` is the only archive exception
- copy backup handoffs into the repository
- preserve the old full proposal after its actionable content was absorbed; replace it with a short redirect instead

The final total number of handoff files across the active directory and `SUPERSEDED/` must be less than or equal to the initial total. Reconciliation must not manufacture files to improve organization.

Use Git or surrounding workspace history for recovery when available. Do not create backup handoff files.

## Superseded Handoff Archive Contract

Mark and archive a handoff as superseded only when **all** of these conditions are true:

1. the file's entire useful actionable scope has been incorporated into one or more existing canonical handoffs
2. the file contains no remaining independent implementation task, decision, constraint, or verification responsibility
3. every canonical destination is complete and understandable without reading the source file
4. no useful content would be lost by replacing the source with a redirect

A partial overlap is not superseded. If any independently plannable topic remains, keep the file active in `AGENTS/HANDOFF/`, narrow its scope, and reconcile only the overlapping content.

For a fully absorbed file, use this sequence:

1. move all useful content into the appropriate existing topic-bounded canonical handoff or handoffs
2. reread the canonical destinations without relying on the source file
3. rewrite the absorbed file in place as a short redirect
4. verify that the redirect contains no implementation task or competing technical direction
5. create `AGENTS/HANDOFF/SUPERSEDED/` only when it does not exist and at least one file qualifies
6. move the verified redirect into `AGENTS/HANDOFF/SUPERSEDED/`, preserving its original filename

Use a short, clear redirect like:

```markdown
# Superseded Handoff

Status: superseded

This file is not an implementation task. Its useful proposals were incorporated into:

- `AGENTS/HANDOFF/handoff-<hash>-NN-<topic>.md`

Read and implement the listed canonical handoff instead. Do not plan or implement this file.
```

A superseded redirect may list more than one canonical destination when its former scope was split across existing files.

The redirect must:

- use the exact line `Status: superseded`
- name every canonical destination needed to find the absorbed work
- state that the file is not an implementation task
- contain no remaining competing recommendation, technical direction, plan, unresolved option, or independent work
- stay short and easy to understand
- preserve the original filename

Never overwrite an existing file in `SUPERSEDED/`. Treat a destination collision as a blocker and leave the source file active until the collision is resolved safely.

Do not use a superseded redirect as a substitute for moving useful content. Do not write `Status: superseded` or move the file when any meaningful implementation scope remains.

## Standalone Canonical Handoff Contract

Every remaining actionable handoff must be understandable and usable on its own.

After merging or redistributing content, each canonical handoff must include all information needed to plan its own scope, including:

- current-state evidence
- authoritative technical direction
- affected files, symbols, contracts, and flows
- compatibility constraints
- dependencies and ordering
- useful risks, decisions, and verification inputs

A canonical handoff may reference neighboring actionable handoffs for real prerequisites, but it must not depend on a superseded redirect for missing explanation, evidence, or decisions.

Before replacing or deleting an absorbed file, reread the canonical destinations without the source file. If they are not complete and clear by themselves, move the missing content first.

## Canonical File Selection

When several files become one, choose an existing canonical file using:

- best match between filename topic and final scope
- strongest existing structure and source evidence
- best fit with real dependency order
- least misleading topic and provenance
- least unnecessary renaming

Keep the canonical file's existing hash. Do not invent a new hash for merged content.

Before replacing another file with a superseded redirect or deleting it, ensure each still-useful fact, constraint, source reference, risk, and planning input has been:

- incorporated into a remaining file
- assigned to another remaining file
- or intentionally rejected as duplicate, stale, incorrect, incompatible, or out of scope

## Rename and Ordering Contract

Renaming an existing handoff is allowed.

Preserve:

```text
handoff-<existinghash>-NN-<topic>.md
```

When renaming:

- retain the file's existing hash
- use a two-digit sequence
- use a short lowercase hyphen-case topic
- avoid collisions
- change order only when it reflects a real dependency improvement
- do not renumber merely for cosmetic continuity
- allow sequence gaps after deletions

Because different hashes do not define one global lexical order, every remaining handoff must state real prerequisites clearly.

## Rewrite Quality Contract

Every changed handoff must remain implementation-near without becoming a coding-agent execution plan.

Preserve or reconstruct useful sections such as:

- Assignment
- Intended outcome
- Scope
- Source inspected
- Current state
- Concrete direction
- Technical approach
- Alternatives and recommendation
- Affected boundaries
- Compatibility and constraints
- Dependencies and ordering
- Planning inputs
- Risks and unresolved questions

Changed files must:

- identify concrete repository paths, symbols, responsibilities, contracts, and flows
- contain one authoritative technical direction per concern
- distinguish scope from neighboring handoffs
- retain useful compatibility and verification information
- avoid generic advice, metaphors, and empty sections
- avoid production code, patches, exact edit scripts, and full implementation plans

## No Product Implementation Contract

This skill repairs handoff files only.

Do not:

- modify product source
- modify tests for product behavior
- change runtime configuration
- create schemas or migrations
- change workflows or deployment behavior
- implement any handoff recommendation
- stage, commit, or push changes unless explicitly requested

Read-only repository inspection and narrowly necessary non-mutating commands are allowed.

## Safety Contract

Before editing:

- inspect Git status
- preserve staged, unstaged, untracked, and ignored user work
- do not overwrite unrelated local changes
- do not use hard reset, clean, history rewrite, or force push
- do not copy secrets or sensitive data into handoffs

If a selected handoff has unrelated local edits that cannot be safely preserved, stop rather than overwrite them.

## Execution Workflow

1. Resolve repository root and guidance.
2. Select and fully read the inspection collection.
3. Group files by handoff hash.
4. Apply the strict no-op gate.
5. Build a cross-set overlap and conflict map only when multiple sets exist.
6. Reinspect current source for material disputes.
7. Decide the authoritative direction and final existing-file allocation.
8. Plan all rewrites, topic-preserving redistributions, merges, renames, superseded archives, and exceptional deletions before applying them.
9. Apply the minimal coherent transformation without collapsing distinct independently plannable topics.
10. Reread every actionable canonical handoff both alone and as one collection.
11. For each fully absorbed file, verify the full-supersession gate, write the redirect, and move it to `AGENTS/HANDOFF/SUPERSEDED/`.
12. Verify every file in `SUPERSEDED/` contains only a valid redirect and is not an implementation task.
13. Verify there are no unresolved harmful overlaps or contradictions.
14. Verify no new handoff was created and the final total count did not increase.
15. Report changes or the no-op result accurately.

## Completion Criteria

A reconciliation with changes is complete only when:

- all relevant producing-run sets were read
- real conflicts and overlaps were resolved against current source
- scopes are coherent, non-duplicative, and separated by meaningful implementation topic and boundary
- distinct independently plannable topics were not collapsed merely to reduce file count
- dependencies are consistent
- useful evidence and constraints were preserved
- only fully absorbed files with no remaining implementation task were rewritten as valid superseded redirects and moved to `AGENTS/HANDOFF/SUPERSEDED/`
- partially overlapping files remain active with a clear independent scope
- every actionable canonical handoff is complete and understandable without reading superseded files
- no new handoff file was created
- no product implementation was performed

A no-op reconciliation is complete when:

- only one producing-run set exists, or multiple sets were inspected
- no harmful cross-set duplication, overlap, contradiction, or misleading ordering was found
- no files were changed merely for stylistic improvement

## Final Response

When no changes were needed, report concisely:

- inspected handoff paths or set hashes
- that reconciliation was unnecessary
- why the no-op gate applied
- current Git status at a useful summary level

When changes were made, report concisely:

- inspected producing-run set hashes
- files kept, rewritten, or renamed
- files merged or redistributed by topic
- fully absorbed redirects moved to `AGENTS/HANDOFF/SUPERSEDED/`
- files deleted only when explicitly requested
- major technical conflicts resolved
- remaining unresolved external decisions, if any
- confirmation that no new handoff files were created
- current Git status at a useful summary level

Do not paste the complete handoff contents into the response unless explicitly requested.

## Typical Invocation Phrases

- `Use the handoff set fixer softskill to reconcile all multi-agent handoffs in AGENTS/HANDOFF/.`
- `Check whether these handoff sets conflict; change nothing when they are already coherent.`
- `Resolve overlapping recommendations across the handoff hashes without creating new files.`
- `Make the existing handoff collection safe to process as an implementation queue.`
- `Deduplicate and reconcile the existing handoffs, deciding disputed technical directions from the current codebase.`
