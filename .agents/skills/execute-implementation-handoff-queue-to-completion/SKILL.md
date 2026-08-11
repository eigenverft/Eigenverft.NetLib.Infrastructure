---
name: execute-implementation-handoff-queue-to-completion
description: Use when an agent should consume one or more active implementation handoff files from AGENTS/HANDOFF/, make all remaining technical and optional-scope decisions, plan, implement, and verify each actionable handoff, move completed actionable handoffs into AGENTS/HANDOFF/DONE/, archive valid fully absorbed top-level redirects in AGENTS/HANDOFF/SUPERSEDED/, and continue until the selected root queue is clean or only unavoidable blockers remain. This is an execution skill, not a review-only or plan-only skill.
---

# Execute Implementation Handoff Queue to Completion

## Purpose

Use this skill as the implementation agent responsible for turning repository-local handoff files into completed, verified code changes.

The normal flow is:

1. Select the active handoff files.
2. Read and revalidate each handoff against the current repository.
3. Create an actionable implementation plan.
4. Resolve all remaining technical and optional-scope decisions.
5. Implement the accepted work.
6. Verify the complete outcome.
7. Move the completed actionable handoff into `AGENTS/HANDOFF/DONE/`, or archive a valid fully absorbed redirect in `AGENTS/HANDOFF/SUPERSEDED/`.
8. Continue with the next top-level handoff until the selected root queue is clean.

Do not stop after analysis or planning when implementation is possible.

## Direct Execution Contract

This skill controls the agent that performs the loop. The executing agent must plan, decide, implement, verify, and move the handoffs itself.

Do not delegate the queue, an individual handoff, its planning, its technical decisions, or its implementation to subagents. Repository tools, build tools, test runners, and the harness's own planning state may be used, but responsibility for the complete selected queue remains with the executing agent.

## Full-Queue Completion Mandate

The selected active queue is one continuous execution assignment. The agent's job is to process it to its terminal state, not to provide an interim checkpoint after one or several handoffs.

Do not:

- ask whether to continue with the current or next handoff
- stop after reporting partial queue progress
- treat a natural file, phase, test, or context boundary as a reason to return control
- wait for approval of ordinary technical or optional-scope decisions
- end the run while independently processable selected handoffs remain active

Continue until every selected top-level actionable handoff has moved to `AGENTS/HANDOFF/DONE/` and every selected valid top-level superseded redirect has moved to `AGENTS/HANDOFF/SUPERSEDED/`. A clean selected root queue is the terminal lifecycle goal.

A genuinely unavoidable blocker on one handoff does not end the queue run. Record it, keep that handoff active, and continue with every later handoff that can be processed safely and independently. Return before the active queue is empty only when no remaining selected work can proceed safely.

Produce a work set that is ready for later independent verification. Favor a complete, verifiable queue result with clear final accounting over incremental approval checkpoints. This does not lower implementation or verification quality.

## Handoff Location and Naming

The default handoff directory is:

```text
AGENTS/HANDOFF/
```

Active handoff filenames follow:

```text
handoff-<handoffhash>-NN-<topic>.md
```


Examples:

```text
AGENTS/HANDOFF/handoff-7f3a91c2-01-domain-contracts.md
AGENTS/HANDOFF/handoff-7f3a91c2-02-storage-transition.md
AGENTS/HANDOFF/handoff-7f3a91c2-03-api-adaptation.md
```

Treat each file as a durable implementation input, not as executable truth. The current repository remains the source of truth.

Completed handoffs are stored under:

```text
AGENTS/HANDOFF/DONE/
```

Keep the original filename when moving a completed actionable handoff. Files in `DONE/` are completed implementation history and are not part of the default active queue.

Fully absorbed redirect archives are stored under:

```text
AGENTS/HANDOFF/SUPERSEDED/
```

Files in `SUPERSEDED/` are non-actionable provenance. They are terminal archive records, are not part of the default active queue, and must never be moved to `DONE/` merely because their canonical destinations were completed.

## When To Use

Use this skill when:

- the user asks an agent to work on one or more handoff files
- a review has already produced implementation-near handoffs
- the desired result is actual implementation rather than another review
- the agent should make technical decisions instead of returning options for user lock-in
- several handoffs should be processed as an ordered queue
- the harness has a planning capability that should guide implementation

## When Not To Use

Do not use this skill when:

- the user requested review, synthesis, or planning only
- the handoff is being created rather than consumed
- the requested work must remain analysis-only
- the agent lacks permission to modify the repository
- the referenced handoff files do not exist and cannot be recovered
- all selected work depends on external business, legal, security, or production authorization and no handoff can proceed safely without it

## Selection Contract

Determine the work set in this order:

1. Use explicitly named handoff paths in the order supplied by the user.
2. If the user names a handoff hash, select matching `handoff-<hash>-*.md` files and sort them lexically.
3. If the user requests all handoffs or does not identify a narrower selection, select all top-level Markdown files matching the handoff naming convention in `AGENTS/HANDOFF/` and sort them lexically.

Ignore unrelated Markdown files and nested directories by default. In particular, do not select files already inside `AGENTS/HANDOFF/DONE/` or `AGENTS/HANDOFF/SUPERSEDED/` as active work. An explicitly named archived file may be inspected, but its archive lifecycle must remain unchanged.

Before building the actionable queue, detect top-level superseded handoff redirects. This supports both the normal Create → Reconcile → Execute flow and the shorter Create → Execute flow when a valid redirect already exists in the root queue.

A redirect is a handoff file that contains the exact line:

```text
Status: superseded
```

A valid redirect must list one or more canonical handoff paths and state that it is not an implementation task. Resolve each canonical path first at its listed top-level location and then, when it has already been completed, by the same filename under `AGENTS/HANDOFF/DONE/`.

For a valid top-level redirect:

- do not plan or implement the redirect file
- read each canonical destination that is still active
- treat a canonical destination already present in `DONE/` as completed
- when the redirect was explicitly named, replace it in the actionable queue with active canonical destinations in the same position
- when processing a broad selection, include each active canonical destination once and skip duplicate queue entries
- keep the redirect at the top level while any canonical destination is still active or blocked
- after every canonical destination is completed or already in `DONE/`, create `AGENTS/HANDOFF/SUPERSEDED/` when needed and move the redirect there with its original filename
- never overwrite an existing archive file; a destination collision is a blocker and leaves the redirect at the top level
- record the redirect as archived superseded provenance rather than as implementation work

For an explicitly named redirect already inside `SUPERSEDED/`, use it only to locate canonical destinations. Do not move or rewrite the archived redirect.

If a top-level redirect is malformed, points to a missing file, still contains competing implementation instructions, or cannot be archived safely, treat it as a required-work blocker instead of guessing or deleting it. Keep it active, record the exact problem, and continue with every other selected handoff that can proceed safely and independently. Return early only when this or another blocker prevents all remaining selected work.

Lexical sorting is the default because the filename places the hash and two-digit sequence before the topic. Reorder files only when a handoff or current source proves a dependency that requires a different order. Record that decision in the implementation plan.

## Repository Safety Gate

Before planning or editing:

- resolve the repository root
- read repository guidance, including `AGENTS.md`, relevant runbooks, and local instructions
- inspect the current branch, upstream, and working-tree state
- preserve staged, unstaged, untracked, and ignored local work
- fetch or synchronize remote state only when repository guidance or the user requests it
- never discard unrelated local changes
- never use destructive cleanup, hard reset, history rewrite, or force push as part of this skill

If existing local work overlaps the selected handoff, integrate with it when safe. When proceeding would overwrite or invalidate work that cannot be preserved, mark that handoff blocked and continue with independent selected work. Return early only when no remaining selected work can proceed safely.

## Read the Complete Handoff

Read every actionable selected handoff completely before implementing it. Read a superseded redirect completely only to resolve and validate its canonical destinations; never treat the redirect itself as implementation work.

For each handoff, identify:

- assignment and intended outcome
- scope and exclusions
- source originally inspected
- described current state
- recommended direction
- technical approach
- alternatives and recommendation
- affected boundaries
- compatibility constraints
- dependencies and ordering
- planning inputs
- risks and unresolved questions

Do not implement from headings, filenames, or excerpts alone.

## Revalidation Contract

Before relying on a handoff conclusion, inspect the current repository areas it references.

A handoff may be stale because source, configuration, dependencies, tests, or neighboring work changed after it was written. Therefore:

- confirm referenced files and symbols still exist
- confirm the described behavior still matches current source
- inspect related code needed to implement safely
- preserve current repository conventions
- adapt the approach when the original recommendation no longer fits
- do not repeat the entire original review when the handoff remains accurate

When the handoff and current source disagree, prefer current source and the user’s present objective. Record the adjusted decision in the plan and continue.

## Overlap and Remaining-Work Contract

Assume that selected handoffs may overlap with earlier handoffs, previous tasks, local uncommitted work, or changes already present in the current codebase.

Before planning each handoff, compare its requested outcome and affected boundaries against:

- the current implementation and tests
- changes already completed for earlier handoffs in the active queue
- relevant existing local changes
- behavior or infrastructure introduced by previous work
- current documentation and runbooks when they describe implemented behavior

Classify the handoff scope as:

1. **Not implemented** — the relevant outcome is still missing.
2. **Partially implemented** — some requirements or boundaries are already satisfied.
3. **Already implemented** — the intended outcome is present and consistent with current requirements.
4. **Satisfied by an alternative implementation** — current code already solves the underlying concern through a different, valid design.

The agent must plan and implement only the remaining delta.

When a handoff is partially implemented:

- remove already satisfied work from the active plan
- preserve valid existing behavior and design choices
- implement only missing, inconsistent, or insufficient parts
- avoid replacing working code merely to match the handoff’s suggested shape
- update tests or documentation only where the remaining delta requires it

When an actionable handoff is already implemented or satisfied by an alternative implementation:

- do not manufacture changes to make the handoff appear processed
- run the smallest meaningful verification that supports the conclusion
- record the handoff as completed with no implementation changes required
- continue to the next selected handoff

Overlap is not itself a blocker. When overlapping required work is materially incompatible and resolution depends on an external decision that cannot be inferred safely, mark the affected handoff blocked and continue with independent selected work. Do not interrupt the queue with a confirmation question.

## Mandatory Planning Behavior

Plan before editing each handoff.

If the agent harness exposes a dedicated plan function, planning tool, task tracker, or editable plan state, use it. Keep it updated as work progresses.

The plan must be an execution plan, not another broad review. It should include:

- the intended outcome
- the current-state classification: not implemented, partially implemented, already implemented, or satisfied by an alternative implementation
- the remaining implementation delta after removing already satisfied scope
- the chosen technical direction
- decisions resolved from alternatives in the handoff
- every optional item and the decision to implement now, not implement, or defer it
- concrete repository areas to change
- compatibility and migration handling
- tests and verification
- documentation or runbook updates when knowledge changes
- dependencies on earlier or later handoffs
- the final move into `AGENTS/HANDOFF/DONE/` after completion

When processing multiple files, maintain one visible top-level queue with one active handoff at a time. Expand the active handoff into implementation steps, complete and verify it, move it to `DONE/`, and advance immediately. Do not turn queue progress into a user-facing confirmation checkpoint.

If no plan function exists, maintain a concise explicit plan in the current working context and execute it immediately. Do not create a separate plan Markdown file unless the user explicitly requests one.

Do not merely present the plan and stop. Planning is a required implementation step, not the final deliverable.

## Decision Authority

This mode authorizes the agent to make ordinary technical implementation decisions.

Resolve choices using this priority:

1. explicit user requirements
2. current repository behavior and compatibility obligations
3. repository-local guidance and conventions
4. the handoff’s recommended direction and evidence
5. the smallest coherent design that completes the outcome
6. maintainability, operability, and verification quality

When several credible approaches remain:

- compare them against current source and constraints
- choose one
- record the choice and rationale in the plan
- implement it

Do not ask the user to choose between normal technical options merely because more than one implementation is possible.

Do not defer decisions with vague wording such as “consider X or Y.” Select the best-supported direction and proceed.

Treat a required decision as a blocker only when it is genuinely external to the repository and cannot be safely inferred, such as:

- a product or business policy with materially different user-visible outcomes
- credentials, secrets, or inaccessible infrastructure
- destructive or irreversible production actions
- legal, privacy, or security authorization
- mutually incompatible explicit requirements
- risk of losing existing local work

When a safe reversible default exists, choose it instead of blocking the handoff. When a required external decision remains, record the blocker and continue with independent selected work. Request input only in the final response when no remaining selected work can proceed safely.

## Optional Work Decision Contract

A handoff may contain optional changes, alternatives, follow-up suggestions, nice-to-have cleanup, or recommendations that are useful only under certain conditions.

Do not pause the queue to ask the user about these items, and do not automatically implement all of them. The agent must make a run-local decision for each item.

For every optional item, the agent must decide one of these outcomes:

1. **Implement now** — the item is useful, fits the handoff outcome, follows current repository direction, and can be completed and verified safely in this work set.
2. **Do not implement** — the item is unnecessary, speculative, already satisfied, outside the coherent scope, lower value than its cost or risk, or conflicts with the chosen direction.
3. **Defer** — the item may be useful, but is not required for the handoff's coherent outcome or cannot be justified safely within the current work set. Deferral is a completed decision for this loop run, not a reason to pause.

Make ordinary optional-scope decisions without asking the user. Use current source, repository guidance, compatibility, maintenance cost, operational value, testability, and scope coherence as evidence.

Record each decision and a short concrete reason in the plan. For items accepted for implementation, include their work and verification in the active plan. For rejected or deferred items, do not create TODOs, placeholders, follow-up files, or incomplete scaffolding merely to preserve the suggestion. Include important deferred items in the final queue report so later review can see the decision.

An optional item never blocks queue continuation by itself. When it depends on external product, business, legal, privacy, security, or production input, defer it rather than stopping the loop, unless the same missing input also blocks required handoff behavior.

A handoff may move to `DONE/` after every optional item has been classified as implement now, do not implement, or defer, and every item accepted for implementation has been implemented and verified. Deferred optional work may remain unimplemented.

## One-Handoff-at-a-Time Rule

Implement one handoff at a time.

For the active handoff:

1. Revalidate it against current source and earlier completed work.
2. Detect overlap and classify how much remains.
3. Reduce the plan to the remaining delta.
4. Resolve every required decision and classify every optional item as implement now, do not implement, or defer.
5. Implement the remaining coherent required outcome and every optional item accepted for implementation.
6. Update tests and documentation where required.
7. Verify the implemented, already-satisfied, or alternative-implementation outcome.
8. Mark all plan and optional-decision steps complete.
9. Move the handoff into `AGENTS/HANDOFF/DONE/`.
10. Continue to the next handoff.

Do not mix unrelated changes from later handoffs into the active one unless a shared prerequisite is necessary. If a prerequisite belongs to a later file, reorder the queue deliberately and record why.

Do not run parallel implementation agents or delegate work under this skill. Process the queue directly, one handoff at a time, in the current execution context.

## Implementation Contract

Implement the selected handoff rather than restating it.

The implementation may include, when required by the handoff:

- production source changes
- interface and contract changes
- configuration changes
- schemas and migrations
- tests
- workflows and deployment files
- documentation and runbooks
- compatibility adapters
- cleanup made obsolete by the completed change

Follow existing repository patterns unless the handoff and evidence justify a deliberate new boundary.

Prefer complete vertical outcomes over partial scaffolding. Do not leave TODO-only implementations, fake placeholders, dead branches, or unused abstractions merely to mirror the handoff structure.

Keep scope aligned with the active handoff. Make adjacent fixes only when they are necessary for correctness, verification, or removal of directly obsolete code.

## Handoff Is Input, Not a Checklist to Obey Blindly

The handoff may contain recommendations, alternatives, uncertainty, or outdated assumptions.

The agent must:

- use the handoff to avoid repeating expensive discovery
- preserve its useful evidence and constraints
- independently verify implementation-sensitive claims
- choose the final implementation shape
- correct stale or unsafe recommendations
- avoid implementing an option merely because it appears first

The implementation result, not literal adherence to every sentence, defines success.

## Test and Verification Contract

Verification is required for every completed handoff.

Use the smallest meaningful verification first, then broaden when risk warrants it:

- targeted unit or integration tests
- type checking or compilation
- lint or static analysis
- project build
- migration validation
- focused runtime checks
- broader test suites when shared contracts changed

Add or update tests for behavior changed by the handoff.

Do not claim completion when relevant verification fails because of the implementation.

If a verification failure is clearly pre-existing or unrelated:

- confirm that distinction with evidence
- record it accurately
- continue only when the active change is still safely verified by other relevant checks

Do not proceed to the next handoff while the active handoff leaves the repository in a knowingly broken state.

## Runbook and Documentation Contract

Apply the repository’s normal documentation rules during implementation.

When code, configuration, workflows, commands, architecture, interfaces, storage, authentication, infrastructure, or debugging knowledge changes, update the relevant runbook or documentation in the same work set.

Do not modify runbooks merely to record that they were checked. Leave them unchanged when no reusable knowledge changed.

## Handoff Completion and Archive Lifecycle

Treat the content of a handoff as immutable implementation input. Do not rewrite its recommendations or append completion notes during execution.

After an actionable handoff meets every completion criterion, move the original file to:

```text
AGENTS/HANDOFF/DONE/<original-filename>
```

Lifecycle rules:

- create `AGENTS/HANDOFF/DONE/` when the first completed handoff needs it
- preserve the original filename
- move each handoff immediately after its own successful completion rather than waiting for the whole queue
- do not move a handoff while required work, required decisions, accepted optional work, verification, or required documentation remains open
- deferred optional work does not prevent the move when the required outcome is complete and verified
- do not move a blocked or knowingly broken handoff
- move an already-implemented or alternative-implementation-satisfied actionable handoff to `DONE/` after the supporting verification succeeds
- move a valid top-level superseded redirect to `SUPERSEDED/` only after all canonical destinations are completed or already in `DONE/`
- leave redirects already archived in `SUPERSEDED/` unchanged
- do not overwrite an existing destination file
- treat a destination collision as a blocker unless repository guidance provides an explicit safe rule
- do not delete the handoff after implementation; the move preserves it as completed history
- do not stage, commit, or push the move unless explicitly requested

The top-level `AGENTS/HANDOFF/` directory is the active queue. `AGENTS/HANDOFF/DONE/` is completed implementation history. `AGENTS/HANDOFF/SUPERSEDED/` is terminal non-actionable redirect history.

## Git Contract

This skill changes working-tree files but does not automatically publish them.

Unless the user explicitly requests otherwise:

- do not commit
- do not push
- do not create or merge branches
- do not delete branches
- do not modify remote repository settings

Keep unrelated existing changes intact and report the final working-tree state accurately.

## Failure Handling

If an individual handoff cannot be completed because of a genuine required-work blocker:

- keep completed earlier handoffs in `DONE/`
- leave the blocked handoff in the active top-level directory
- identify the exact blocker in the plan
- preserve work already completed
- defer optional items rather than treating them as blockers
- record verification already performed
- continue with every later handoff that is demonstrably independent and safe to process
- postpone only dependent handoffs whose execution would hide, worsen, or rely on the blocker

Do not return a progress-only response while independent selected work remains. Do not silently skip a handoff. A blocked file remains evidence that the full-queue target has not yet been reached.

## Completion Criteria

An actionable handoff is complete only when:

- the current repository was inspected
- stale assumptions and overlap with existing work were reconciled
- already satisfied scope was removed instead of reimplemented
- a concrete remaining-delta plan was created and maintained
- ordinary technical decisions were resolved by the agent
- every optional item was classified as implement now, do not implement, or defer
- every optional item accepted for implementation was implemented and verified
- the intended behavior was implemented, proven already satisfied, or proven satisfied by an alternative implementation
- relevant tests and documentation were updated when required
- meaningful verification passed
- no known implementation defect remains in scope
- the original handoff file was moved into `AGENTS/HANDOFF/DONE/`

A queue is complete only when every selected actionable handoff is in `DONE/` and every selected valid top-level superseded redirect is in `SUPERSEDED/`. Any blocked, active, or unprocessed selected top-level file means the full-queue target was not reached. Do not describe partial progress as queue completion.

## Final Response

After execution, report concisely:

- processed handoff paths in execution order
- final `AGENTS/HANDOFF/DONE/` paths for completed handoffs
- top-level superseded redirects archived in `SUPERSEDED/` and their canonical destinations
- completion, already-satisfied, alternative-implementation-satisfied, archived-superseded, or blocker status for each selected file
- important optional items implemented, rejected, or deferred
- scope reduced because of overlap or existing implementation
- major technical decisions made
- implementation files changed
- tests and verification run with results
- any active actionable handoff that could not reach `DONE/` or top-level redirect that could not reach `SUPERSEDED/`, with the unavoidable blocker and why no remaining safe work could continue
- current Git status at a useful summary level

Do not send the final response merely to ask whether the user wants the loop to continue. The normal final response is emitted after the selected active queue is empty; an earlier response is an exception caused by an unavoidable blocker that prevents all remaining safe work.

Do not paste the complete handoff contents or the full internal plan into the response unless the user asks for them.

## Typical Invocation Phrases

- `Use the work on handoff files softskill and implement all files in AGENTS/HANDOFF/.`
- `Work through these handoffs in order, plan each one, make the decisions, and implement them.`
- `Use the harness plan function, then complete every selected handoff.`
- `Implement the handoffs for hash 7f3a91c2 and verify each before continuing.`
- `Consume AGENTS/HANDOFF/handoff-7f3a91c2-01-domain-contracts.md and finish only the work that is not already present in the codebase.`
