---
name: execute-tracked-initial-handoff-creation-and-sync
description: Investigate the current repository and create only justified Initial Implementation Handoffs under TRACKED_HANDOFFS/INITIAL/. Preserve every existing product checkout, author each producing run in a uniquely owned ignored runtime worktree inside the repository root, save it first on tracked-handoffs/initial/run/<hash>, then integrate it append-only into tracked-handoffs/initial/current. Recover open remote and local runs, keep the product branch untouched, and return only synchronized paths, the permitted NO_HANDOFFS_CREATED response, or an exact blocker with the next safe action.
---

# Execute Tracked Initial Handoff Creation and Sync

## Purpose

Use this skill when repository investigation should produce durable Initial Implementation Handoffs that remain available across product branches, independent sessions, clones, and computers.

Many independent top-level agents may run this skill at the same time:

- on the same computer
- on different computers
- from `main`, development, test, feature, release, or detached product states
- directly or with explicitly requested subagents

Each producing run first saves its result on its own remote run branch. The result is then integrated into the shared current Initial collection.

Activating this skill authorizes creation of uniquely owned Initial run branches, append-only updates of `tracked-handoffs/initial/current`, and guarded deletion of Initial run branches already proven fully integrated. It never authorizes a product-branch commit, push, merge, reset, or checkout change.

Reconciliation, implementation planning, implementation, completion, supersession, and later lifecycle interpretation are outside this Creation workflow.

## Operational Summary

A normal successful run does this:

1. identify the selected product repository and preserve its exact checkout state
2. generate a unique session hash and create its session record
3. ensure the local runtime and mirror paths are ignored through local Git exclude rules
4. fetch Initial Handoff refs into a session-owned local ref namespace
5. validate, recover, and integrate safely recoverable open runs
6. create one uniquely owned temporary producing worktree inside the selected repository root
7. inspect the product checkout read-only and write justified Initial Handoffs only in the producing worktree
8. push and verify the producing run on its own remote run branch
9. integrate the append-only union into `tracked-handoffs/initial/current`
10. refresh the local visibility mirror, remove only this run's disposable artifacts, and return the allowed response

The remote run branch is the durable intermediate save. The temporary worktree is not the durable save.

## Core Model

### Remote branches

Creation uses:

```text
tracked-handoffs/initial/current
tracked-handoffs/initial/run/<12-character-hash>
```

The general phase pattern is:

```text
tracked-handoffs/<phase>/current
tracked-handoffs/<phase>/run/<hash>
```

This skill owns only:

```text
TRACKED_HANDOFFS/INITIAL/
tracked-handoffs/initial/current
tracked-handoffs/initial/run/*
```

A later Reconcile workflow may use separate branches such as:

```text
tracked-handoffs/reconciled/current
tracked-handoffs/reconciled/run/<hash>
```

This Creation skill must not create, update, delete, or reinterpret another phase's branches or files.

### Meaning of `current`

`tracked-handoffs/initial/current` means:

> The currently integrated and remotely published Initial Handoff collection.

It does not mean:

- reconciled
- approved
- final
- ready for implementation
- correct for every product branch
- completed or superseded

### Meaning of `run/<hash>`

`tracked-handoffs/initial/run/<hash>` means:

> One producing run's remotely saved Initial Handoff result, which may or may not already be integrated into `initial/current`.

After a run branch has been fetched and verified, another session or computer can recover it even if integration into `current` is still pending.

### Local paths

Resolve the selected product checkout with Git rather than directory-name assumptions:

```text
git rev-parse --show-toplevel
git rev-parse --git-common-dir
```

Use these conceptual paths:

```text
<repository-root>/.tracked-handoff-runtime/worktrees/<session-hash>-produce/
<repository-root>/.tracked-handoff-runtime/worktrees/<session-hash>-integrate-NN/
<repository-root>/.tracked-handoff-runtime/worktrees/<session-hash>-recover-NN/
<repository-root>/TRACKED_HANDOFFS/INITIAL/
<git-common-dir>/tracked-handoff-initial-sessions/<session-hash>.json
<git-common-dir>/tracked-handoff-local-exclude.lock
<git-common-dir>/tracked-handoff-initial-fetch.lock
<git-common-dir>/tracked-handoff-initial-mirror.lock
refs/tracked-handoff-initial-sessions/<session-hash>/remote/current
refs/tracked-handoff-initial-sessions/<session-hash>/remote/runs/<run-hash>
```

The runtime root and mirror are local visibility or working paths. They are never part of a product commit.

## Hard Safety Rules

Always preserve these invariants:

- never force-update, rewrite, or replace remote history; do not use force-push for run publication or current integration
- the only permitted `--force-with-lease` use is an exact expected-object guard that requires a new run ref to be absent, requires the first `initial/current` ref to be absent, or requires an integrated run ref still to equal the previously verified object before deletion; it must never move an existing ref to a different commit
- never switch, checkout, detach, pull, merge, rebase, reset, clean, commit, or push any checkout or worktree that existed when this run started
- never stash or overwrite unrelated local work
- never merge a tracked-handoff branch into a product branch
- never stage, commit, or push product code on a tracked-handoff branch
- never change a path outside `TRACKED_HANDOFFS/INITIAL/` on an Initial storage commit
- never edit, rename, move, or delete an already integrated Initial Handoff
- never delete a remote run branch before its complete contribution is verified in `initial/current` either byte-for-byte or through the explicit path-collision replacement terminal proof
- never claim cross-computer availability before the relevant remote push is fetched and verified
- never create an empty run branch, empty result commit, or placeholder file
- never remove or reuse another active session's runtime directory, worktree, branch, lock, or session record
- never modify a versioned `.gitignore` as a side effect of this workflow

The selected product checkout is read-only source evidence. Handoff authoring and storage commits happen only in new runtime worktrees owned by this run. The only permitted local write areas below the repository root are the locally ignored `.tracked-handoff-runtime/` area and, when eligible, the locally ignored `TRACKED_HANDOFFS/INITIAL/` visibility mirror under its mirror lock. Neither area is product content or may enter a product commit.

Assume other agents may use the same repository concurrently. Existing checkouts and worktrees are shared infrastructure, not disposable scratch space.

## Investigation-Only Execution Boundary

This workflow investigates and writes Handoff documents. It does not execute product workflows merely to validate, initialize, or prepare the repository.

Do not run as part of this skill:

- product builds, tests, dependency or package restores, publishes, application launches, or benchmarks
- product or repository setup, bootstrap, initialization, maintenance, migration execution, formatting, generation, or repository-provided cleanup commands
- repository-provided one-time, startup, runbook, automation, or environment-preparation procedures
- a secondary product-source checkout or detached product worktree merely to run such commands

Existing source, tests, configuration, logs, history, and documented command results may be read as evidence. Do not execute them.

This boundary does not prohibit the fetches, local exclude setup, temporary Handoff worktrees, Handoff commits, remote run publication, current integration, mirror refresh, or cleanup of this run's own Handoff artifacts required by this skill.

If the user explicitly requests command execution, treat it as separate work outside this Creation workflow. Do not silently add it as verification, and do not change an existing product checkout during this workflow.

## Repository and Remote Resolution

### Selected repository

Use the repository containing the designated source document or the repository explicitly named by the user.

Do not assume the execution workspace root is the repository root. Customer workspaces may contain:

- one repository at the workspace root
- repositories in subdirectories
- monorepositories
- nested independent repositories
- submodules
- linked worktrees

Record the selected repository root, Git common directory, current branch or detached commit, exact source commit, upstream, staged state, unstaged state, and relevant untracked state before any Handoff operation.

### Writable remote

Choose the writable remote deterministically:

1. use the writable remote configured as the selected product branch's upstream remote when one exists
2. otherwise use writable `origin`
3. otherwise use the only remaining writable remote
4. if several writable remotes remain and no primary remote can be established, stop and report the ambiguity

Fetch the selected remote before investigation and before every integration attempt. Fetching must not update any product checkout.

### Session-local fetch namespace

Do not make concurrent Handoff sessions compete over the same local remote-tracking refs.

Discover the legacy branch, current branch, and run branches with exact remote queries such as `git ls-remote`. Fetch only the required Handoff refs into this session's unique local namespace:

```text
refs/tracked-handoff-initial-sessions/<session-hash>/remote/current
refs/tracked-handoff-initial-sessions/<session-hash>/remote/runs/<run-hash>
```

Use explicit refspecs. Do not fetch product branches or tags merely for this workflow, and do not update `refs/remotes/<remote>/...` when a session-local destination ref can be used.

After confirming the source refs exist, the intended shapes are:

```text
git fetch --no-write-fetch-head --no-tags <remote> refs/heads/tracked-handoffs/initial/current:refs/tracked-handoff-initial-sessions/<session-hash>/remote/current
git fetch --no-write-fetch-head --no-tags <remote> refs/heads/tracked-handoffs/initial/run/*:refs/tracked-handoff-initial-sessions/<session-hash>/remote/runs/*
```

Use the installed Git's equivalent syntax when necessary, but preserve the source and session-owned destination namespaces.

Use `--no-write-fetch-head` so concurrent sessions do not compete over the shared `FETCH_HEAD` file. If the installed Git does not support that option, serialize only the short Handoff fetch command with `<git-common-dir>/tracked-handoff-initial-fetch.lock`; do not hold that fallback lock during investigation, authoring, publication, integration construction, or retry waits.

Repeated fetches may advance this session's current ref and populate its run refs. A non-fast-forward change to a previously fetched current or immutable run ref is unexpected remote history rewriting and must be reported rather than hidden with a forced local ref update.

Session-local refs are not user branches and are not pushed. Delete only this session's local ref namespace after remote verification and local recovery no longer need it.

The remote query is authoritative. Before classifying open runs, remove only this session's local run refs whose matching remote source ref no longer exists. Never treat a stale session-local ref as proof that a remote run is still open. If `initial/current` was previously observed but disappears remotely, report the unexpected remote deletion instead of silently using the stale local ref.

## Local Exclude Contract

The runtime worktree and local visibility mirror must not appear as product changes.

Use the selected repository's local exclude file:

```text
<git-common-dir>/info/exclude
```

Do not require or modify a versioned `.gitignore`.

### Required selected-repository rules

Ensure these anchored rules exist when their paths are used:

```gitignore
/.tracked-handoff-runtime/
/TRACKED_HANDOFFS/
```

Rules in `info/exclude` are persistent local clone configuration. Do not remove them during normal cleanup.

### Safe exclude update

Before creating the runtime root or writing the mirror:

1. resolve the canonical repository root and Git common directory
2. acquire `<git-common-dir>/tracked-handoff-local-exclude.lock` by atomic directory creation
3. if another active session owns the lock, wait only with short bounded delays such as 1, 2, 4, 8, and 15 seconds
4. preserve every existing line and comment in `info/exclude`
5. append only missing exact rules; do not reorder, normalize, or remove unrelated rules
6. preserve a valid final newline
7. verify the intended paths with `git check-ignore -v` or an equivalent Git check
8. verify that adding the rule did not alter the product index or tracked files
9. release the lock immediately

A stale local-exclude lock may be removed only after its recorded owner is safely classified as inactive.

If `info/exclude` cannot be read or updated safely, stop before creating runtime content and report the exact blocker.

### Existing path checks

Before taking ownership of `.tracked-handoff-runtime/`:

- verify it is not tracked product content
- verify it is not a symlink
- verify it is a directory or absent
- verify existing children can be attributed to this skill by valid session records or recognized hash-scoped layout
- leave unrelated or unreadable content untouched

Use no-follow or `lstat`-equivalent inspection for runtime, hook, staging, backup, transaction, and mirror paths. Recheck each path and its existing parent components immediately before create, rename, replacement, or deletion. If a path changes into a symlink or reparse target, skip that local operation, preserve remote-safe progress, and continue through a fresh uniquely owned path when possible.

If the reserved runtime root is tracked, is a symlink, or is clearly owned by another purpose, do not repurpose it. Report the conflict.

### Enclosing repositories

A selected repository may be physically contained inside another Git worktree.

Do not modify parent repositories preemptively.

Instead:

1. inspect accessible ancestor directories only within the execution environment's workspace boundary
2. identify actual enclosing Git worktree roots, not arbitrary `.git`-named paths
3. test whether the selected runtime or mirror path appears as a change in an enclosing repository
4. only when it actually appears, add the narrow repository-relative path to that enclosing repository's local `info/exclude` under that repository's own short exclude lock
5. verify the enclosing repository no longer reports the path
6. never modify an enclosing repository's index, branch, `.gitignore`, or tracked files

If an enclosing repository sees the path and its local exclude cannot be changed safely, stop before creating the path. Do not knowingly dirty another repository.

For a real submodule, prefer the selected submodule repository's own exclude rules and verify that the parent repository remains clean. Do not alter the parent gitlink.

## Runtime Worktree Contract

Use exactly one producing runtime worktree per direct producing run:

```text
<repository-root>/.tracked-handoff-runtime/worktrees/<12-character-session-hash>-produce/
```

The session directory name must be unique lowercase hexadecimal and must not already belong to an active or unreadable session.

The runtime worktree:

- is created only after the exclude checks succeed
- is a new linked worktree registered by the selected repository
- uses a uniquely named local Handoff branch owned by this run
- is based on the latest fetched `initial/current` when it exists
- uses an orphan Initial storage tree when the phase is empty
- contains only the pure Initial storage tree
- is never used to inspect, build, test, or run product source

Recommended local branch prefix:

```text
tracked-handoff-initial-work/<hash>
```

Integration and recovery work use separate sibling runtime worktrees such as `<session-hash>-integrate-NN` and `<session-hash>-recover-NN` when a worktree is needed. Never nest a new worktree inside another worktree, and never reuse an existing checkout.

A linked worktree inside the ignored runtime root is allowed because it has its own index and `HEAD` while sharing the selected repository's object store. Its location inside the product worktree does not authorize writing product content.

### Controlled Git execution

Repository-local hooks, signing settings, default push refspecs, tag-following, and content filters must not silently expand or alter this storage workflow.

Create one empty, regular, non-symlink hook directory owned by the session under the ignored runtime root. Run every skill-owned command that can invoke local hooks with an explicit absolute `core.hooksPath` pointing to that empty directory. Disable commit, tag, and push signing for skill-owned storage operations. Use `--no-verify` where the Git command supports it, use `--no-follow-tags` for pushes, and always provide one explicit full source and destination refspec. Do not modify the customer's persistent Git configuration.

Conceptually apply equivalent per-command configuration such as:

```text
git -c core.hooksPath=<absolute-empty-session-hook-dir> -c commit.gpgSign=false -c tag.gpgSign=false -c push.gpgSign=false -c push.followTags=false <command>
```

Server-side remote hooks remain authoritative. Treat their rejection as an ordinary publication or integration failure; never attempt to bypass them.

Before creating the result commit, verify that every staged Handoff blob is byte-identical to the intended regular Markdown file. If attributes, line-ending conversion, or a clean filter changed a staged blob, replace only that session-owned index entry with the raw file blob using Git's no-filter object/index plumbing, then verify again before committing. After the commit, read every committed blob back from the commit and compare its bytes again before the first push. Never publish a commit whose stored Markdown bytes differ from the verified authored bytes.

## Pure Storage Branch Contract

`tracked-handoffs/initial/current` and every Initial run branch are pure storage branches. Their trees may contain only:

```text
TRACKED_HANDOFFS/
└── INITIAL/
    └── initial-handoff-<hash>-NN-<topic>.md
```

They must not contain ordinary repository paths such as:

```text
src/
ISSUES/
.agents/
README.md
```

When `initial/current` does not exist, treat the Initial collection as empty. The first producing run uses an orphan result commit containing only its Initial Handoffs.

After that run is verified remotely:

1. query the exact `refs/heads/tracked-handoffs/initial/current` ref again
2. while it remains absent, create it directly from the verified orphan result with an explicit absent-ref lease
3. use the full current ref and full result OID, equivalent to:

```text
git push --force-with-lease=<current-ref>: --no-verify --no-follow-tags <remote> <result-oid>:<current-ref>
```

4. fetch and byte-verify the created current tree
5. if the create-only push is rejected because another agent created current first, fetch that current and integrate the run through the normal append-only integration path

Never overwrite or replace a concurrently created current ref.

Before using any fetched Initial branch, verify that its complete tree contains no path outside `TRACKED_HANDOFFS/INITIAL/`. A malformed branch is not safe input.

## Session Records and Ownership

Store one session record at:

```text
<git-common-dir>/tracked-handoff-initial-sessions/<session-hash>.json
```

Record at least:

- repository identity and canonical root
- Git common directory
- host identifier
- process identifier when available
- session hash and every producing hash owned by the session
- start and heartbeat times
- current workflow phase
- source product branch or detached commit
- exact source commit and publication state
- runtime directory
- owned worktrees and local Handoff branches
- intended remote run branch
- the full Initial base commit OID used by each producing run, or the literal `EMPTY` for an orphan first run
- the full result commit OID after it is created
- whether the remote run push was fetched and verified
- whether current integration was verified

Refresh the heartbeat at major phases and at least every five minutes during a long investigation.

### Atomic session-record updates

Never overwrite a session JSON file in place. For every creation or update:

1. serialize the complete next record to a uniquely named sibling temporary file
2. flush and close it
3. read it back and validate the JSON, repository identity, session hash, phase, and owned paths
4. replace the final session record with a same-directory atomic rename or atomic replace operation
5. keep the previous valid final record until the replacement succeeds

A crash may leave a hash-scoped temporary record, but must not leave a partially written final record. A later session may promote a temporary record only when the final record is absent and the temporary record is complete, valid, and attributable to the same inactive session. Otherwise it ignores or safely removes only an attributable disposable temporary file. Session records are removed last during cleanup.

Ownership is established by the session record plus the expected hash-scoped paths and branch names. A matching name alone is not sufficient permission to delete an artifact.

Every local lock directory created by this skill must contain owner metadata identifying the repository, session hash, host, process when available, acquisition time, and heartbeat. A lock name without readable ownership metadata is uncertain and must not be broken merely because it appears old.

### Session state classification

Classify local sessions and lock owners with these concrete rules:

- **active:** the recorded process is verifiably still the same process on the recorded host, or the readable heartbeat is no older than 15 minutes
- **inactive:** the record belongs to the current host, the recorded process identity can be checked and is verifiably no longer running, and the heartbeat is older than 15 minutes
- **uncertain:** the record is unreadable, the host is different, process identity cannot be checked safely, the process identifier may have been reused, timestamps are implausible, or the evidence does not satisfy either rule above

Record process start identity or an equivalent process discriminator when available, not only a numeric process identifier.

Age alone never makes a session, lock, worktree, or branch safe to delete. Leave uncertain ownership untouched and continue independent safe work. When an uncertain artifact blocks required local setup, report its exact path and the manual ownership check needed rather than guessing.

## Normal Workflow

### Phase A: Establish the synchronized Initial state

1. resolve the selected repository, source context, local state, and writable remote
2. preserve every pre-existing checkout and worktree unchanged
3. generate one unique 12-character lowercase hexadecimal session hash and create its session record
4. establish and verify the local exclude contract
5. reject the legacy `tracked-handoffs` branch when present
6. enumerate the remote current and run refs without changing shared remote-tracking refs
7. fetch the required Handoff refs into the session-local fetch namespace
8. validate current and every visible remote run
9. recover and integrate every safely recoverable valid open run
10. delete only remote runs whose full contribution is verified byte-for-byte in current
11. refresh the local mirror from verified current when the short mirror lock is available

Before the requested investigation starts, the agent must have fetched the complete published current collection and every visible open Initial run. An open remote run must not be ignored merely because integration is temporarily racing.

### Phase B: Investigate and author in isolation

1. for direct execution, use the session hash as the producing hash; delegated producing runs receive their own unique hashes
2. create the uniquely owned producing runtime worktree
3. inspect the designated product checkout read-only
4. read the supplied Markdown completely when the assignment names one
5. compare its claims with current repository evidence
6. apply the Handoff Eligibility Gate to each material implementation boundary
7. write only justified Initial Handoffs under `TRACKED_HANDOFFS/INITIAL/` in the producing worktree

Do not author in the product checkout or the local visibility mirror.

### Source Stability Under Concurrent Local Work

Other agents may change the selected product checkout while this skill is reading it. Do not create a Handoff from a mixture of incompatible repository states.

1. Record the initial `HEAD`, branch or detached state, working-tree summary, and content identity of every repository file materially used as evidence.
2. Immediately before finalizing the Handoff files, re-read `HEAD`, the working-tree summary, and the relevant evidence paths.
3. When relevant evidence changed, re-read and re-evaluate the changed paths, update the Source context, and perform one final stability check.
4. Unrelated changes do not block the run.
5. If relevant evidence keeps changing or a coherent source state cannot be established safely, do not publish a conclusion based on mixed states. Preserve any draft only as local unpublished work and return a precise blocker naming the unstable paths and next safe action.

Do not lock, reset, stash, or otherwise freeze the product checkout to obtain stability.

### Durable run-base provenance

Every producing result must remain independently verifiable after the local session record is gone.

Record the full Initial base in both the session record and the result commit message:

```text
Tracked-Handoff-Run: <producing-hash>
Tracked-Handoff-Base: <full-commit-oid-or-EMPTY>
```

For a normal result, the commit must have exactly one parent and that parent must equal `Tracked-Handoff-Base`. For the first empty phase, the result must be an orphan root commit and the trailer must be `Tracked-Handoff-Base: EMPTY`.

A later computer validates the run from its commit graph and trailers, not from an unavailable local session record. Missing, conflicting, abbreviated, or false base provenance makes the run malformed.

### Phase C: Save the producing result remotely

When one or more Handoffs were created:

1. verify the runtime worktree contains no changed path outside `TRACKED_HANDOFFS/INITIAL/`
2. verify all changes are additions and no integrated file was modified, renamed, or deleted
3. verify filenames, headings, and internal run references use the producing hash
4. verify every staged Handoff blob is byte-identical to the intended authored file, bypassing local content filters for the session-owned index entry when necessary
5. record the full Initial base OID, or `EMPTY`, in the session record
6. create exactly one result commit from that base, with the required `Tracked-Handoff-Run` and `Tracked-Handoff-Base` trailers, local hooks disabled, and commit signing disabled
7. read every committed Handoff blob back from the result commit and verify its exact bytes
8. record the full result commit OID in the session record
9. query the exact intended remote run ref and prepare a create-only explicit-ref push that succeeds only while that ref is absent, with local hooks, signing, and tag-following disabled
10. push the result to:

```text
tracked-handoffs/initial/run/<producing-hash>
```

11. fetch that exact remote branch
12. verify its object ID, commit trailers, parent/base relationship, complete tree, and file bytes
13. update the session record atomically to remote-run-verified

The individual remote run push does not use the mirror lock and does not wait for a shared publication lock. Different producing hashes normally produce independent remote branch names.

After verification, the result is recoverable from another computer even if current integration remains pending.

### Remote run name collision

Treat every published run branch as immutable.

The initial run push must be create-only. Use an explicit lease that requires the exact remote run ref to be absent. If the installed Git cannot express that safely, query the exact ref immediately before a normal create push and treat every rejection as a collision; never retry by updating the existing ref.

The verified command shape is:

```text
git push --force-with-lease=<run-ref>: <remote> <result-oid>:<run-ref>
```

The empty expected value after `<run-ref>:` means the remote ref must not exist. Use full ref names and the full result OID. A rejection is a collision signal, not permission to retry without the lease.

If the intended remote run ref already exists or appears during the push:

1. fetch and validate its exact object ID and bytes
2. when it is exactly this same interrupted producing result, treat publication as idempotently recovered
3. otherwise preserve the original Initial base, generate a fresh producing hash, and create a new sibling replacement worktree from that same base
4. write the renamed files, headings, and internal run references in the replacement worktree
5. create exactly one new result commit from the original base with the new run/base trailers
6. publish and verify the new create-only run ref
7. remove the superseded unpublished local worktree and commit only after the replacement is remotely verified

Do not add a second result commit on top of the colliding commit. Never overwrite, force-update, or delete the different existing remote run.

### Phase D: Integrate open runs into `current`

After the own run is remotely saved, integrate it together with every other safely recoverable open run:

1. fetch the newest current and all open Initial run branches
2. validate every run with the Remote Run Contract
3. build the append-only union on a new sibling integration runtime worktree and local Handoff branch owned by this session
4. preserve every file already in current byte-for-byte
5. add every safe, not-yet-integrated run contribution, including unaffected paths from replacement-pending original runs
6. verify the prospective integration changes only `TRACKED_HANDOFFS/INITIAL/` and contains additions only
7. verify every staged integration blob is byte-identical to its verified source run or current blob
8. create one integration commit that records the included run hashes, with local hooks and signing disabled
9. read the committed integration tree back and byte-verify it before push
10. push through one explicit full refspec to `tracked-handoffs/initial/current`, with local hooks, signing, and tag-following disabled
11. never force-update current
12. fetch and verify the resulting remote current tree
13. delete each integrated remote run only after its entire contribution is byte-identical in verified current or its explicit replacement terminal proof is complete

### Phase E: Refresh visibility, recover leftovers, and return

1. classify all remaining open remote runs again
2. refresh the local visibility mirror when safe
3. remove only this session's worktrees, local Handoff branches, session-local fetch refs, locks, and session record when they contain no unpublished value
4. preserve and report any unpublished local value
5. return only the response allowed by the Return Contract

## Remote Run Contract

A valid Initial run branch must:

- use `tracked-handoffs/initial/run/<12-character-lowercase-hex-hash>`
- contain only `TRACKED_HANDOFFS/INITIAL/`
- have a tip exactly one result commit above the full base OID declared by its `Tracked-Handoff-Base` trailer, or one orphan result commit declaring `Tracked-Handoff-Base: EMPTY` for the first empty phase
- add files only
- never modify or delete an Initial Handoff from its base
- use the same producing hash in branch name, filenames, headings, and internal run references
- contain regular Markdown files only
- contain no symlinks, secrets, credentials, private data, or unsafe artifacts

For an ordinary result, inspect the tip against its first parent. For an orphan result, inspect the complete tree.

Require a full, matching `Tracked-Handoff-Run` trailer and a full `Tracked-Handoff-Base` trailer. For an ordinary result, the declared base must equal the sole first parent. Do not depend on a local session record to recover this provenance.

When replacement trailers are present, require one full original run OID and one or more unambiguous old-to-new path mappings. The original OID must identify the fetched replacement-pending run. Normalize only exact old/new producing hashes, mapped paths, first-heading identity, and internal run/hash references; after that normalization, all remaining bytes must match. Missing, ambiguous, duplicate-conflicting, or unverifiable replacement mappings do not authorize deletion of the original run.

When a remote run is malformed:

- do not integrate, delete, or rewrite it silently
- continue processing independent valid runs when safe
- report the exact branch and reason

## Open Remote Run Recovery

At startup and before final return, classify every remote Initial run.

### Already integrated

All contributed files exist with identical bytes in current.

Action:

- treat the run as integrated
- record the exact verified remote run object ID
- delete the remote run only through the OID-bound deletion contract
- treat deletion failure as cleanup debt, not loss of the integrated result

### Valid and not yet integrated

The run is valid and contributes one or more paths absent from current.

Action:

- include it in the next append-only integration attempt

### Path collision

A contributed path already exists in current or another open run.

Action:

1. read both files completely
2. if bytes are identical, treat the contribution as already integrated
3. if contents differ, never discard one merely because the topics overlap
4. preserve the already integrated path unchanged
5. classify the original run as replacement-pending without modifying it
6. create a fresh replacement run from the newest verified current, containing only the still-unintegrated distinct collided content under a fresh producing hash and fresh path
7. change only identity-dependent fields required by the new run: filename, first heading identity, producing hash, and internal run/hash references
8. add full commit trailers for each replaced source:

```text
Tracked-Handoff-Replaces-Run: <full-original-run-oid>
Tracked-Handoff-Replaces-Path: <old-path> => <new-path>
```

9. verify that each replacement file becomes byte-identical to its source after deterministic normalization of only those permitted identity fields
10. publish and fetch-verify the replacement run normally
11. integrate every unaffected original path together with the mapped replacement paths through the normal append-only integration procedure
12. prove the original run's terminal replacement state: every unaffected original path is byte-identical in current, every mapped replacement path is integrated and normalization-equivalent, and the original remote ref still has the recorded OID
13. delete the original run with the OID-bound deletion contract

A replacement run has exactly one result commit above its own newest-current base. It does not add a commit to the original run and does not rewrite the original ref.

If an automatic replacement proof cannot be established, preserve the original run and continue integrating independent valid runs. Treat it as isolated cleanup debt unless it prevents safe completion of the current assignment; do not ask the user merely to choose between two preserved contents.

Semantic combination, supersession, or deduplication of different Initial Handoffs belongs to a later Reconcile workflow.

## OID-Bound Remote Run Deletion

Remote run deletion is a compare-and-delete operation, not an unconditional ref deletion.

1. Fetch the exact run ref and record its full verified object ID.
2. Verify either that every contributed file from that exact object is byte-identical in verified current, or that the exact object has completed the explicit path-collision replacement terminal proof.
3. Delete only with an explicit expected-object lease requiring the remote run ref still to equal that verified object ID.

Use the verified command shape:

```text
git push --force-with-lease=<run-ref>:<verified-oid> <remote> :<run-ref>
```

Use the full run ref and full verified OID. Never substitute an implicit tracking value or an abbreviated OID.
4. If the ref is already absent, treat cleanup as complete after confirming the remote query.
5. If the lease fails because the ref changed, fetch and reclassify the new object. Do not delete it based on the earlier verification.
6. If the installed Git or remote cannot perform the expected-object deletion safely, leave the branch as cleanup debt rather than using an unconditional delete.

This narrow lease is permitted only for deletion of a proven integrated run. It never authorizes updating a run or current ref to another commit.

## Integration Retry Contract

Updating `initial/current` is the shared remote step. Normal non-fast-forward push rejection provides concurrency control.

For a rejected current push:

1. fetch the newest current and all open Initial runs
2. first check whether another agent already integrated the intended contributions
3. if work remains, rebuild the append-only union from the newest current tip
4. retry with increasing waits of approximately 10, 20, 30, 45, 60, 75, and 90 seconds
5. add a small random variation when practical so many agents do not retry in lockstep
6. stop after the eighth integration attempt and no more than about six minutes total
7. never force-push

If integration still fails, every producing result already verified on a remote run branch must remain there.

Return a concise pending-integration blocker containing:

- the remote run branch or branches
- the target `tracked-handoffs/initial/current`
- the exact integration failure
- confirmation that the Handoffs are already saved remotely
- that the same request can be retried after concurrent integrations settle
- that a later run on this or another computer will recover the open remote run automatically

Do not report a remote-verified run as existing only locally.

## Local Runtime Recovery

At startup inspect only artifacts using this skill's reserved paths and prefixes.

For each session record and runtime directory:

1. classify the owner as active, inactive, or uncertain
2. leave active and uncertain sessions untouched
3. inspect inactive sessions for unpublished Initial Handoffs
4. when a matching remote run is already verified, prefer remote recovery and remove disposable local artifacts when safe
5. when a remote run was not verified, recover valid unpublished Initial Handoffs into a fresh valid run before deleting their source
6. never delete a runtime directory containing unrelated, unreadable, or unattributed content
7. do not let an unrelated abandoned runtime directory block a new uniquely owned session

Recovery priority:

> Preserve unpublished Initial Handoffs, save them remotely, integrate everything safe, then clean only proven disposable artifacts.

Do not use broad cleanup commands that may affect other worktrees. In particular, do not use global worktree cleanup as a substitute for ownership checks.

### Canonical cleanup for an inactive owned session

Cleanup is idempotent and runs only for artifacts attributed to one proven inactive session:

1. atomically mark the session record `cleanup-started`
2. establish whether every local Handoff value is remote-verified, integrated, or successfully recovered
3. remove each exact registered owned worktree through `git worktree remove <exact-path>` using the controlled Git configuration
4. verify that the exact worktree is no longer registered; do not run global `git worktree prune`
5. remove an owned local Handoff branch only when it is not checked out and its tip has no local-only value beyond verified remote run or current state
6. delete only the exact session-local fetch-ref namespace with exact ref operations
7. remove attributable temporary session files, mirror staging or backup directories, and the empty session hook directory
8. delete the final session record last

If the physical worktree is already absent but Git retains uncertain administration metadata, leave that metadata as cleanup debt rather than deleting shared Git internals manually. A new session uses a new hash, path, branch, and ref namespace and continues normally. Failure to remove disposable local metadata must not downgrade a remote-verified or integrated result into a lost-result blocker.

## Artifact Lifecycle and Cleanup

No artifact is deleted solely because it is old. The lifecycle is evidence-based:

| Artifact | Keep while | Safe cleanup condition |
| --- | --- | --- |
| `initial/current` | always | never deleted by this Creation skill |
| valid unintegrated remote run | any contribution is absent from current | delete only after every contributed file is byte-identical in verified current |
| replacement-pending original run | a colliding contribution has not reached the proven replacement terminal state | delete only after unaffected paths and every normalized replacement mapping are verified in current and the original OID still matches |
| integrated remote run | OID-bound remote deletion has not succeeded yet | retry only after repeating byte verification and recording the current exact run OID |
| malformed remote run | ownership or content is unsafe | do not delete automatically; report it |
| runtime worktree and local Handoff branch | unpublished or unattributed value remains | remove only after remote verification or successful recovery proves no local-only value remains |
| session-local fetch refs | remote verification or local recovery still needs them | delete only this session's namespace when no recovery value remains |
| session record | any owned artifact still needs classification or recovery | remove after all owned artifacts are safely resolved |
| local lock | protected operation is active | release immediately after the operation; break later only when the owner is proven inactive |
| `info/exclude` rules | the clone may use runtime or mirror paths | keep as persistent local configuration |
| local mirror | local visibility is useful | rebuild from verified current; never treat it as the durable source |

A valid unintegrated remote run is a durable pending item, not garbage. If no later Creation run ever occurs, it may remain as harmless remote cleanup debt rather than risk data loss. A future maintenance workflow may list or clean only already integrated runs, but this skill must remain independently safe without such a workflow.

## Local Visibility Mirror

The selected product checkout may expose:

```text
TRACKED_HANDOFFS/INITIAL/
```

This is a human and agent visibility mirror of verified `tracked-handoffs/initial/current`.

It is not an authoring workspace.

### Mirror eligibility

Use the root mirror only when:

- `TRACKED_HANDOFFS/` is absent or untracked local content reserved for this workflow
- it is not a symlink
- it is ignored in the selected repository and every actually affected enclosing repository
- it contains no unrelated or unreadable content

If `TRACKED_HANDOFFS/` is tracked product content, do not overwrite, remove, or reinterpret it. Continue remote synchronization and use the verified storage worktree for reading the collection; skip the root mirror and report only if the missing mirror materially affects the requested result.

### Mirror update

Use an atomically created phase-specific lock:

```text
<git-common-dir>/tracked-handoff-initial-mirror.lock
```

The lock protects only short mirror inspection, copy, and cleanup operations. It must not guard investigation, remote run publication, or current integration.

When another active session owns the lock:

- do not read or write the mirror concurrently
- continue using verified remote branches and runtime worktrees
- retry briefly with waits such as 1, 2, 4, 8, and 15 seconds
- if still busy, defer mirror refresh without invalidating a verified remote result

Refresh the mirror only from verified current. Never copy unintegrated run branches into it.

Build every refresh completely in a session-owned staging directory under `.tracked-handoff-runtime/`, not in the visible mirror. Byte-verify the staged tree against the verified current tree before acquiring the mirror lock. Under the short mirror lock:

1. write an atomic session-owned mirror transaction record containing the verified current OID, target path, staging path, and backup path
2. recheck that current and the staging bytes still match the recorded OID
3. move an existing complete mirror to the recorded hash-scoped backup path without deleting it
4. move the complete staging directory into the visible mirror target
5. verify the installed mirror against the recorded current OID
6. on failure, restore the complete backup before releasing the lock when possible
7. after success, remove the backup and transaction record

On startup, recover only attributable inactive mirror transactions under the mirror lock. If the visible target is missing, restore the complete backup or rebuild from verified current. Never treat a partial staging directory as the authoritative mirror. Since the mirror is only visibility data, an abandoned refresh must not block remote run publication or current integration.
Do not stage or commit mirror changes on a product branch.

## Local Unpublished Initial Handoffs

A person or an earlier failed process may deliberately place a publication-ready candidate Markdown file directly under the eligible local mirror. Placement alone is not sufficient proof of publication intent.

When the mirror lock is available:

1. compare direct Markdown files with verified current and open remote runs
2. read each local-only candidate completely
3. require a valid `initial-handoff-<hash>-NN-<topic>.md` filename, an `Initial Implementation Handoff` first heading, and substantive `Source context` and `Intended outcome` sections
4. reject files marked draft, private, temporary, do-not-publish, or equivalent
5. exclude ordinary notes that do not clearly present repository-specific implementation preparation
6. reject symlinks, unreadable files, non-Markdown files, secrets, credentials, or sensitive content
7. for a valid publication-ready local-only Initial Handoff, preserve the author's content and apply only minimum contract normalization
8. assign a fresh hash when needed
9. create, push, and verify a recovery run before changing the mirror source
10. integrate the recovery run into current
11. refresh the mirror from verified current

Only deliberate placement together with the publication-ready Handoff structure above indicates publication intent. Ambiguous files remain untouched and are reported only when they block a safe mirror refresh. Private drafts belong elsewhere.

If the mirror lock is temporarily unavailable, defer this local-only scan rather than blocking remote investigation or remote publication.

## Concurrent Top-Level Sessions

Independent top-level sessions are not subagents of one parent.

Each session must have:

- its own session record
- its own producing hash
- its own ignored producing and integration runtime directories
- its own linked runtime worktrees and local Handoff branches
- its own local fetch-ref namespace
- its own remote `tracked-handoffs/initial/run/<hash>` branch when it produces Handoffs
- hash-scoped filenames

Sessions may investigate and publish their own run branches concurrently.

They do not wait for a shared publication lock before remotely saving their results.

Only current integration is shared, and it is resolved through fetch, append-only rebuild, normal push rejection, and bounded retry.

## Direct Execution

Direct execution is the default.

1. establish local exclude and remote synchronization
2. recover safe remote and local runs
3. inspect the requested product source directly and read-only
4. apply the Eligibility Gate
5. write complete Initial Handoffs for justified concerns
6. push and verify one remote run branch when Handoffs exist
7. integrate all valid open runs into current
8. verify current
9. refresh the mirror when possible
10. return only the allowed response

Do not launch subagents merely because a task is broad.

## Delegated Execution

Use subagents only when the user explicitly requests them.

The parent agent owns all Git synchronization and publication.

The parent must:

1. complete startup synchronization and recovery once
2. record the shared product source context
3. create a separate writable temporary Handoff output area for each subagent
4. assign a unique 12-character producing hash to each delegated producing run
5. give each subagent a bounded objective and minimum sufficient context
6. require read-only product-source inspection
7. prohibit product commands, branch switching, fetching, committing, pushing, and writing outside the assigned temporary Handoff area
8. collect each successful subagent result
9. publish one remote run branch per successful delegated producing run
10. integrate all valid open runs into current
11. return all newly integrated paths, or the top-level no-Handoff response when none qualified

Each subagent returns only created paths or exactly:

```text
NO_HANDOFFS_CREATED
```

Subagents do not add the explanatory second line. The parent applies the top-level Return Contract.

When several subagents are used for the same work set, each prompt must directly include the objective, scope, output path, safety rules, filename contract, Eligibility Gate, and response contract. Do not rely on repository-local instructions to convey essential constraints.

Delegation is one level only. A delegated subagent must not launch, request, or coordinate further subagents. The parent prompt must state this prohibition explicitly so delegated work cannot fan out recursively.

## Handoff Eligibility Gate

Create an Initial Implementation Handoff only when current repository evidence supports concrete unfinished implementation work worth preserving for later reconciliation, planning, or implementation.

Before creating a file, answer all four questions:

1. what current behavior, defect, gap, duplication, risk, or required change creates real implementation work?
2. what concrete target direction is supported by repository evidence?
3. is the concern substantial enough for a later planning agent to act on?
4. does preserving this source-grounded Handoff add useful input for later reconciliation, planning, or implementation?

If any answer is no, uncertain, speculative, stylistic, or only optional cleanup, do not create the Handoff.

These are not sufficient reasons:

- the user asked for a review
- an alternative exists but is not recommended
- the current implementation is already appropriate
- the result is keep-as-is, no action, or insufficient evidence
- a possible future requirement may make a change useful
- the output format appears to expect a file

Repository comparison determines whether supplied source material still describes real unfinished implementation work. It is not a semantic reconciliation or deduplication pass over the Initial collection.

An existing Initial Handoff with similar or overlapping coverage is not by itself a reason to return `NO_HANDOFFS_CREATED`. When the supplied source and current repository still confirm unfinished work, create a new source-grounded Initial Handoff and record relevant overlap, dependency, or prior Handoff paths where useful.

Only reuse already created paths when recovering the same producing run or completing its interrupted publication. Do not suppress a new independent producing run merely because another Initial Handoff discusses the same topic.

Finding nothing is valid.

## Markdown Source Validation

Treat every supplied or discovered Markdown document as advisory input, not proof that work remains open.

Compare it with current:

- source
- tests
- configuration
- manifests
- schemas and migrations
- workflows
- public contracts
- Git state and history

Create a Handoff only when the current repository still confirms concrete unfinished implementation work.

Do not execute builds or tests to make this determination.

A source document may be historically useful even when no new Handoff is justified. Do not automatically edit or delete it.

## Handoff Hash and Filename Contract

Use one unique 12-character lowercase hexadecimal producing hash per direct or delegated producing run.

Do not use sequential placeholders, agent numbers, model names, repeated characters, or timestamps alone.

Use:

```text
initial-handoff-<handoffhash>-NN-<topic>.md
```

Examples:

```text
initial-handoff-7f3a91c2d4e6-01-domain-contracts.md
initial-handoff-7f3a91c2d4e6-02-storage-transition.md
```

Rules:

- `NN` begins at `01`
- use two digits
- order by dependency or implementation-preparation sequence
- use short lowercase hyphen-case topics
- every file in one producing run uses the same hash
- branch name, filenames, headings, and internal run references use the same hash
- never overwrite an existing path

## Source Context Contract

Every Handoff must record:

- repository identity or normal remote identifier
- product branch name or detached source commit
- exact source commit at investigation time
- source publication state: remote, local-only, or unknown
- relevant working-tree state
- intended applicability: repository-wide, mainline-oriented, or branch/environment-specific

When evidence depends on uncommitted or unpushed product work:

- state the limitation
- identify relevant paths without copying secrets or large diffs
- preserve enough evidence for later revalidation
- do not claim another computer can reproduce the exact source state
- do not create the Handoff when unavailable source details make the conclusion unsafe

## Topic Splitting and Ordering

Create separate Handoffs for materially different implementation boundaries, independent subsystems, foundation-versus-dependent work, or separately plannable data, API, UI, infrastructure, migration, rollout, or operational concerns.

Do not create one file per minor observation.

A useful fallback order is:

1. shared concepts and contracts
2. data and persistence foundations
3. core application behavior
4. external interfaces and integrations
5. UI or consumer adaptation
6. migration, rollout, observability, and cleanup

Repository-specific dependencies take precedence.

## Required Handoff Structure

Use this structure when relevant and omit empty sections:

```markdown
# Initial Implementation Handoff <handoffhash> NN: Topic

## Assignment
What the producing agent was asked to determine.

## Source context
Repository identity, product branch, exact source commit, publication state, working-tree state, and intended applicability.

## Intended outcome
The concrete implementation outcome this Handoff prepares for.

## Scope
Covered and excluded work.

## Source inspected
Repository-relative files, symbols, tests, configuration, workflows, and other evidence.

## Current state
How the relevant code currently works.

## Concrete direction
The recommended implementation-near target direction.

## Technical approach
Likely responsibility changes, contracts, data or control flow, integration seams, error handling, compatibility, and verification surfaces without writing the implementation or a complete execution plan.

## Alternatives and recommendation
Realistic options, repository-specific trade-offs, and the recommended direction.

## Affected boundaries
Files, modules, APIs, schemas, configuration, tests, integrations, and ownership boundaries.

## Compatibility and constraints
Behavior and operational properties that must remain stable.

## Dependencies and ordering
Prerequisites and enabled follow-up work.

## Planning inputs
Decisions, acceptance concerns, and verification surfaces for a later planning agent.

## Risks and unresolved questions
Only genuine unresolved matters.
```

## Concreteness and Plain Language

Name actual repository-relative paths, symbols, responsibilities, data flows, configuration keys, contracts, tests, and observed behavior whenever practical.

Do not replace repository evidence with generic advice such as:

- improve separation of concerns
- make the architecture cleaner
- use a more scalable approach
- follow modern best practices

Use simple, direct language. Explain uncommon technical terms when they matter. Remove repeated framing and analysis narration.

## No-Implementation Contract

This skill must not:

- run product builds, tests, restores, publishes, applications, setup, initialization, maintenance, migrations, formatting, generation, or unrelated repository automation
- create a secondary product-source worktree for command execution or validation
- modify product source
- create migrations
- change product configuration behavior
- add product tests
- write production patches
- produce a complete coding-agent execution plan
- commit or push a product branch
- change any path outside `TRACKED_HANDOFFS/INITIAL/` on Initial storage branches

It may provide implementation-near technical direction inside Handoffs, but it stops before implementation.

## Return Contract

Path-only and `NO_HANDOFFS_CREATED` responses apply only when synchronization succeeded.

### One or more new Handoffs integrated

Return only paths produced by this top-level assignment, including its explicitly requested delegated producers. Do not include unrelated older runs merely because this session recovered or integrated them. If another concurrent session integrated this assignment's verified run first, return its paths after confirming identical bytes in current.

Return repository-relative paths, one per line:

```text
TRACKED_HANDOFFS/INITIAL/initial-handoff-7f3a91c2d4e6-01-domain-contracts.md
TRACKED_HANDOFFS/INITIAL/initial-handoff-7f3a91c2d4e6-02-storage-transition.md
```

### No new Handoff qualifies

Return `NO_HANDOFFS_CREATED` on the first line.

Use this clean response only when no independent synchronization problem still requires user attention. If no new Handoff qualifies but a malformed, unsafe, or unresolved run remains, return a blocker that states both facts: no new Handoff was justified for this assignment, and the named synchronization problem remains with its next safe action.

Do not create an own remote run branch, result commit, or placeholder file for a no-Handoff result. Startup recovery and integration of older open runs must still complete as far as safely possible, and the mirror should still be refreshed when available.

Integrating unrelated older runs does not turn a no-Handoff result for the current assignment into a path response.

When the assignment was based on one or more supplied or named Markdown documents, add exactly one concise second line:

```text
NO_HANDOFFS_CREATED
The supplied Markdown was checked against the current repository and does not currently justify a separate unfinished implementation handoff; consider closing, archiving, or deleting it if it has no remaining documentation value.
```

This is only a recommendation. Do not modify the source document. Do not claim it is obsolete unless repository evidence specifically proves that.

### Assignment integrated but an independent synchronization problem remains

Do not return a clean path-only success when another malformed, unsafe, or otherwise unresolved Initial run still requires user attention.

Return one concise blocker that:

- states that this assignment's Handoffs were successfully integrated
- lists those integrated paths
- names each independent unresolved run or artifact and its exact reason
- confirms that the integrated paths are safely stored and were not rolled back
- states the next safe action

Do not hide the independent problem, and do not describe the successfully integrated assignment as lost or pending.

### Remote run saved but current integration pending

Do not return created paths as fully synchronized.

Return one concise blocker stating:

- the Handoffs are saved on the named remote run branch
- integration into current remains pending
- the exact cause
- the same request may be retried
- another session or computer can recover the open run

### Failure before remote run verification

Return:

- intended Handoff paths when known
- the exact blocker
- the preserved runtime worktree path when unpublished work remains
- the next safe user action

Do not claim the result was saved remotely.

## Legacy Branch Blocker

The legacy remote branch:

```text
tracked-handoffs
```

cannot coexist with:

```text
tracked-handoffs/initial/current
tracked-handoffs/initial/run/<hash>
```

If the writable remote still contains the legacy branch, stop before creating new work and report that the one-time migration is required.

Do not delete or migrate the legacy branch automatically during an ordinary Creation run.

## Failure Handling

Stop before investigation when:

- the selected repository cannot be established safely
- the local runtime path cannot be reserved and ignored safely
- an enclosing repository would be dirtied and cannot be protected through a local exclude rule
- the writable remote or credentials are unavailable
- the legacy branch blocks the namespace
- current is malformed
- the already published Initial collection cannot be established safely

Continue safe work and report the remaining problem when one malformed or colliding open run does not prevent independent valid runs from being integrated.

Preserve useful unpublished local work before cleanup.

After a verified remote run push, prefer remote recovery and do not retain an unnecessary runtime worktree merely because current integration is pending.

## Security and Privacy

Never publish:

- secrets
- credentials
- private keys
- tokens
- cookies
- private customer data
- sensitive personal data
- unsafe linked content
- large raw logs or data dumps

Name configuration keys and safe source locations without copying secret values.

## Completion Checklist

Before returning success, verify:

- the selected repository and writable remote were resolved deterministically
- no pre-existing checkout or worktree was switched, detached, repurposed, committed, merged, rebased, reset, cleaned, or pushed
- no product or repository execution workflow was run
- the runtime path and mirror were locally ignored without modifying a versioned `.gitignore`
- no selected or enclosing repository was dirtied by runtime or mirror paths
- every created runtime worktree is uniquely owned by this session
- skill-owned worktree, commit, and push commands used the session-owned empty hooks path, disabled signing, explicit refspecs, and no tag-following
- every committed Handoff blob was byte-identical to the intended authored bytes before the first push
- session records were updated through validated same-directory temporary files and atomic replacement
- local sessions and locks were classified with the concrete active/inactive/uncertain rules, and no artifact was deleted because of age alone
- Handoff fetches did not contend on shared `FETCH_HEAD`, or used only the short fallback fetch lock
- Handoff fetches used this session's local ref namespace and did not overwrite another session's refs
- this session's local fetch-ref namespace was removed when it no longer carried recovery value
- the legacy branch does not block the Initial namespace
- current and all consumed runs contain only `TRACKED_HANDOFFS/INITIAL/`
- every remote run branch was treated as immutable and any run-name collision was resolved without overwriting the existing ref
- every producing result was first fetched and verified on its own remote run branch
- every result commit contains full, matching run and base provenance that can be validated without its local session record
- every run ref was created without replacing an existing remote ref
- every deleted remote run was removed only while its remote object ID still matched the previously verified object
- every returned path exists with identical bytes in verified current
- materially used product evidence was rechecked before publication and represents one coherent recorded source state
- no integrated Initial Handoff was overwritten, renamed, moved, or deleted
- no storage commit changed a path outside `TRACKED_HANDOFFS/INITIAL/`
- all safely recoverable open runs were processed
- integrated remote run branches were deleted when possible
- the mirror was refreshed when eligible and available
- any mirror refresh was built and verified in staging before the short transaction-style swap
- the first `initial/current` creation, when needed, used an absent-ref lease and never replaced a concurrent current branch
- path-collision replacements reached the explicit replacement terminal state before the original run was deleted
- retained local artifacts contain unpublished or unattributed value that must not be deleted
- delegated subagents, when used, did not delegate further
- the response follows the Return Contract exactly
- path-only success was not returned while an independent unresolved synchronization problem still required user attention
