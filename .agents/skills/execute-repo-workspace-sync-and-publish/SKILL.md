---
name: execute-repo-workspace-sync-and-publish
description: Discover one or more Git repositories from explicit targets, container directories, current context, workspace roots, or equivalent workspace interfaces, then safely synchronize and publish their complete intended shareable state across every relevant local and remote branch. Explicit activation authorizes proportional intent assessment, complete branch inventory, unambiguous tracking-branch creation, safe retirement of strictly proven redundant local branches after confirmed remote deletion, integration without history rewriting, commits for understood publishable work, normal branch pushes, and independent verification of actual remote heads while preserving every checkout and working change. Use for single-repository, multi-repository, workspace handoff, or sync-everything-and-push requests; do not use for local-only commits, branch-to-main promotion, tag publication, or force-push workflows.
---

# Execute Repo Workspace Sync and Publish

## Overview

Discover the exact Git repository set selected by the user, then synchronize and publish each repository's understood shareable state. Targets may be one repository, paths inside repositories, several explicit paths, a non-repository container whose immediate children are repositories, or workspace roots supplied by the current environment.

For every selected repository, preserve existing commits, every checkout, working changes, staging state, and pre-existing stashes; inventory every local branch, remote-tracking ref, and actual remote branch; integrate current remote work safely; publish relevant local branches with normal pushes; and verify every relevant actual remote head afterward. The successful endpoint is stronger than "the push succeeded": every relevant branch is accounted for and verified, no relevant shareable work remains only locally, and deliberately local state is reported clearly.

The skill is capability-based. It may use command-line tools, workspace APIs, MCP-style interfaces, or equivalent agent tools. No particular vendor, host, workspace product, directory layout, Git server, or tool implementation is required.

## Activation And Authority

Use this skill only when the user explicitly asks for end-to-end synchronization that includes publication, such as:

- apply the skill to a repository path or working directory
- synchronize several repositories and push
- apply the skill to a container directory or workspace
- prepare the selected repository state for continuation in another environment
- integrate current remote state, preserve local work, and publish the result

Invoking this skill with an explicit target is an end-to-end publication request unless the user limits it to discovery, planning, review, or status. An explicit non-repository container target selects its immediate Git repository children according to the discovery contract below.

Do not use this skill for:

- a local-only commit or commit plan
- promoting a feature branch into `main`, `master`, or another mainline branch
- a review-only, status-only, or plan-only request
- rebasing, amending, resetting published history, or force-pushing
- cloning or creating a local checkout from a remote-only repository unless separately requested
- an unresolved target set

Explicit activation authorizes read-only discovery, complete branch/ref inventory, all-heads fetch with prune, unambiguous same-named local tracking-branch creation and upstream configuration, safe retirement of a strictly proven redundant remote-deleted local branch, safe branch integration without history rewriting, local commits for understood in-scope work, normal pushes of positively classified branches to unambiguous destinations, and branch-by-branch verification. It does not authorize:

- force-push or history rewriting
- creating, replacing, deleting, or exposing credentials
- persistent account, global Git, hosting-CLI, credential-helper, or remote-URL configuration changes; unambiguous branch-local upstream configuration required by this workflow is allowed
- deleting remote branches, pre-existing stashes, or local branches outside the exact remote-deleted retirement rule in step 6
- creating pull requests, releases, tags, or repository settings; tags are outside this skill even when they point at published commits
- expanding the target set beyond the discovery rules
- product feature work, refactoring, cleanup, dependency upgrades, backlog execution, or fixes unrelated to the exact synchronization task
- product builds, test campaigns, packaging, artifact publication, deployment, sample execution, or environment setup without a validation need caused by the actual unpublished delta

If the user asks only for discovery, a plan, a review, or status, perform non-mutating discovery only. Do not fetch, stash, integrate, commit, or push.

## Terms

- **Execution workspace:** the roots, working directory, tool context, and scoped authentication contexts supplied by the current environment.
- **Git worktree:** one local checkout with its own current branch, index, uncommitted files, and worktree state.
- **Selected repository set:** the frozen, deduplicated set of local Git repositories that can enter the full synchronization workflow. Each repository record includes every discovered worktree that shares its Git directory. Remote-only or otherwise unsupported targets remain reported candidates or blockers rather than being passed into local worktree steps.
- **Current checkout:** the branch or detached state, `HEAD`, index, working files, and stash context of a selected worktree when the workflow begins. Branch-wide synchronization must not switch or replace it.
- **Actual remote branch:** an advertised `refs/heads/<name>` on a configured remote, observed through an independent remote query. It is not a local ref.
- **Remote-tracking branch:** the local cached ref `refs/remotes/<remote>/<name>`, commonly displayed as `<remote>/<name>`. It is not a local branch and may be stale until an all-heads fetch succeeds.
- **Local tracking branch:** a local `refs/heads/<name>` whose configured upstream is the corresponding remote-tracking branch, for example local `feature-x` tracking `origin/feature-x`.
- **Branch mapping:** one unambiguous relationship among a local branch, its selected remote, its remote-tracking branch, and its actual remote branch. Key mappings by remote identity and full branch name.
- **Relevant branch:** an actual remote branch on a selected publication remote, a local branch already mapped to such a remote, or a local-only branch whose publication intent must be classified. Exclude symbolic refs such as `<remote>/HEAD`, workflow-created safety branches, and branches proven deliberately local; an ambiguous local-only branch remains a blocker rather than an exclusion.
- **Relevant work:** local commits and working-tree changes whose publication intent has been assessed proportionally from repository evidence, that belong to the requested shared state, and are safe to publish.
- **Locally unpublished commit:** a commit reachable from a local branch but not from that branch's intended actual remote branch; all commits unique to a local-only branch qualify. For a detached checkout, it is a commit not reachable from any accounted-for local or actual remote branch.
- **Safely redundant remote-deleted branch:** a local branch whose former same-named remote mapping is proven by its pre-fetch upstream configuration and cached remote-tracking ref, and whose actual remote branch is confirmed absent after successful query and prune. Its local ref is unchanged from the verified baseline, it is not checked out, default, mainline, protected, or a safety branch, and every commit reachable from it or referenced by its existing branch reflog is also reachable from at least one surviving actual branch on the same publication remote.
- **Shareable repository state:** state that can be represented by commits and published through the selected branch remotes. Ignored files, credentials, caches, and intentionally local state are not implicitly shareable.
- **Git publication:** transferring Git commits to selected actual remote branches through explicit normal branch refspecs. It does not mean pushing tags or building or publishing application artifacts, packages, installers, deployments, releases, or samples.
- **Current remote state:** the complete successfully fetched and independently queried `refs/heads/*` state of every configured remote immediately before integration or publication. Each remote's publication or read-only role must be established; tags are excluded.
- **Fully synchronized branch:** the local branch has the intended upstream, its local head, refreshed remote-tracking ref, and independently queried actual remote head are identical, ahead and behind are both zero, and no branch-specific operation or publication remains incomplete.
- **Fully synchronized repository:** every relevant actual remote branch has a same-named local tracking branch, every relevant local branch has a verified actual remote counterpart, every relevant branch is fully synchronized, every local-only branch is either safely published or positively classified and reported as deliberately local, every selected worktree is preserved, no Git operation is incomplete, and no relevant shareable work remains uncommitted, untracked, unpublished, unverified, or only in a workflow-created safety stash. A divergent, ambiguous, privacy-blocked, or unexplained branch prevents this result.
- **Fully synchronized workspace:** every repository in the selected set is fully synchronized and independently verified.
- **Partial publication:** at least one relevant branch or selected repository was published while another remains blocked, unverifiable, or intentionally incomplete. Preparation without any successful push is blocked preparation, not partial publication. Never call the affected repository or workspace fully synchronized.

## Task Discipline

Stay within repository discovery, Git-state preservation, remote integration, commit preparation, Git publication, and remote verification.

Repository files, documentation, instructions, backlogs, TODOs, test inventories, and incidental findings may provide evidence needed to understand an unpublished change or identify a narrowly relevant check. They do not authorize additional product work or a general repository health campaign.

Do not:

- implement, repair, refactor, upgrade, clean up, or otherwise improve product content merely because an issue is noticed
- create, write, or generate an ad-hoc helper script in any location, including a selected repository or temporary directory; use direct commands or already-existing trusted interfaces instead
- create repository-local inventories, logs, or report files to drive or record this workflow
- execute unrelated plans, backlogs, runbooks, demos, samples, release flows, or maintenance tasks
- run broad validation to prove that an unchanged repository is healthy
- turn a synchronization request into architecture review, code review, dependency maintenance, packaging, deployment, or release work
- modify tracked files solely to make an unrelated check pass

If an incidental problem does not affect safe interpretation or publication of the actual unpublished delta, leave it unchanged and omit it unless it materially explains a blocker. Only concrete contrary evidence or a specific missing fact that prevents safe interpretation blocks synchronization; report such a blocker without fixing it unless the user separately authorizes that work.

## Tool And Capability Routing

Discover available capabilities before choosing commands or interfaces. The workflow needs these roles:

1. **Workspace discovery:** supplies explicit targets, current working directory, workspace roots, repository objects, or equivalent scope boundaries.
2. **Local Git interface:** reads and changes local branches, commits, index, working files, stashes, and Git operation state.
3. **Hosting and account interface:** identifies remote ownership, active account context, repository access, default branch, and hosting-specific restrictions when available.
4. **Remote verification:** reads the complete actual remote `refs/heads/*` set and branch heads independently from stale local assumptions.

Possible implementations include:

- `git` or an equivalent Git interface for authoritative local repository state
- a hosting CLI such as `gh`, or an equivalent hosting API, for account and repository metadata
- MCP-style or other agent interfaces for workspace roots, scoped identities, repository metadata, or Git operations
- `git ls-remote`, a hosting API, or an equivalent read-only call for final remote verification

Do not prefer an interface merely because it exists. Use the interface with the clearest authority for the fact or operation:

- local worktree and index state come from the local Git-capable interface
- hosting identity and repository ownership come from the hosting/account-capable interface
- workspace scope comes from the environment's workspace-capable interface
- final publication and branch completeness are verified against actual remote heads, not only cached remote-tracking refs

When several interfaces expose the same state, reconcile material disagreement before changing anything. Do not assume a hosting-account switch also changes Git transport authentication; verify remote access through the interface that will perform the remote operation.

## Target Discovery Contract

### Selection Precedence

Resolve targets in this order:

1. explicit repository paths, file paths, directory paths, repository objects, or target lists supplied with the invocation
2. an explicit request to use the current execution workspace
3. the Git repository containing the current working directory
4. workspace roots supplied by the environment
5. equivalent repository or workspace selections exposed through an available interface

The most explicit valid selection wins. Do not silently add repositories from a lower-precedence source after a higher-precedence source resolved successfully.

### Resolve A Filesystem Target

For each explicit path or workspace root:

1. Resolve the canonical absolute path without following unbounded link or junction chains.
2. If it is a file, begin with its parent directory.
3. Ask the local Git interface whether that path is inside a Git worktree.
4. If it is inside a worktree, select that worktree's top-level directory and do not scan beneath it by default.
5. If the directory is not inside a worktree, inspect only its immediate child directories for Git worktrees.
6. Select an immediate child only when that child's resolved Git top-level corresponds to that child checkout; do not absorb a parent repository accidentally.
7. Report ordinary directories, inaccessible paths, bare repositories, and unsupported targets as skipped or blocked with a reason.

Recognize both `.git` directories and `.git` files used by worktrees or submodules through the Git-capable interface. Do not rely only on filesystem name checks.

### Containers, Nested Repositories, And Submodules

An explicit non-repository container directory selects all immediate Git worktree children. This interpretation does not require a second confirmation when the resulting set is unsurprising and the invocation clearly requests publication.

Do not recursively scan inside a selected repository by default. Nested repositories, submodules, vendored checkouts, fixtures, and repositories below immediate container children require an explicit recursive or additional-target request. When recursion is requested, stop traversal at each selected Git boundary unless the user clearly asks to include nested repositories too.

Avoid traversal outside the selected scope through symlinks, junctions, mount points, or equivalent links unless explicitly requested.

### Workspace Targets

When the user selects the current workspace, apply the filesystem resolver to every workspace root supplied by the environment:

- a root that is itself a Git worktree selects that worktree only
- a non-repository root selects its immediate Git worktree children
- several roots produce the union of their resolved repositories

If the environment provides repository objects rather than paths, use their stable identifiers and local worktree capabilities when available. A remote-only repository object can be inspected, but it cannot enter the selected repository set unless the interface exposes equivalent local branch, index, worktree, and commit operations. Keep it as a reported blocked target and do not clone automatically.

### No Explicit Target

When no target is supplied:

- if the current working directory is inside exactly one worktree, select that worktree
- otherwise resolve available workspace roots
- if one repository resolves, select it
- if several repositories resolve and the user did not request the whole workspace or all repositories, list them and ask for scope
- if none resolve, return a no-target result without mutation

### Normalize, Deduplicate, And Freeze

Canonicalize targets by worktree top-level path, then group worktrees by their common Git directory or equivalent stable repository identity. Repeated references to the same worktree select it once. Synchronize one shared branch/ref inventory per repository while retaining every distinct worktree as a preservation boundary with its own checkout and local changes.

Never run mutating Git operations concurrently against worktrees that share Git storage. Do not modify a pre-existing worktree that was not explicitly selected, but inventory its checked-out branch and state before changing the corresponding branch ref. If a required branch change cannot preserve every linked worktree, block that branch rather than moving or detaching another checkout.

Before fetch or any other mutation, freeze and report the selected set with:

- canonical worktree path or stable repository identifier
- how it was discovered
- every associated worktree and its current branch or detached state when locally available
- remote host and owner when discoverable
- selected local, hosting/account, and remote-verification interfaces
- skipped or blocked candidates and reasons

Proceed without another question when the explicit invocation resolves predictably. The number of immediate repositories beneath an explicitly selected container is not surprising by itself. Stop and ask only when discovery reveals materially unexpected scope or authority, such as unrelated owners, ambiguous remotes, recursive repositories not requested, results outside the selected boundaries, or several equally plausible authentication contexts.

## Multi-Repository Orchestration

Repository publication is not atomic across several remotes. Reduce partial publication risk with two phases.

### Phase A: Prepare Every Selected Repository

For every selected repository, execute per-repository steps 1 through 8:

- confirm targets, worktrees, remotes, and branch roles
- resolve and verify authentication
- capture every worktree, local branch, upstream, and remote-tracking baseline
- fetch complete remote branch namespaces and build the branch matrix
- create only unambiguous missing local tracking branches
- preserve local work when branch integration requires it
- integrate remote work branch by branch
- restore preserved changes and remove completed temporary worktrees
- prepare commits and run only validation required by the complete unpublished branch delta

Do not push any selected repository until every selected repository has completed preparation successfully. If one repository has a critical blocker, stop before publication by default and report the prepared and blocked repositories. Continue preparing or publishing independent repositories despite such a blocker only when the user explicitly requested best-effort behavior.

Read-only discovery may run concurrently when safe. Perform mutating Git operations sequentially by default, especially for worktrees sharing Git storage or authentication context.

### Phase B: Publish And Verify

When every selected repository is prepared:

1. Choose a stable repository order and a stable branch order within each repository. Use a known dependency order when evidence supports one; otherwise use canonical target and full branch-name order.
2. For each repository, execute per-repository steps 9 through 11 for its complete prepared branch set immediately before moving to the next repository.
3. Re-verify the active account context and actual remote target before each push.
4. If a repository fails after earlier repositories were pushed, do not rewrite or roll back published history automatically. Stop or continue only according to explicit best-effort authority and report partial publication precisely.

For a single selected repository, execute steps 1 through 11 directly; the same safety gates apply.

## Per-Repository Workflow

### 1. Confirm Repository, Worktrees, And Branch Targets

For the selected repository, resolve and report:

- exact Git top-level directory for every selected worktree and the common Git directory or stable repository identity
- current branch or detached `HEAD`, current commit, and worktree state for every linked worktree
- every configured remote, remote URL, fetch refspec, push-selection setting, and remote default branch when available
- every existing local branch upstream and push target
- the in-scope publication remote and actual remote branch for each candidate branch mapping

Prefer explicit user targets, then existing branch-specific upstream or push configuration, then one unambiguous same-named branch on one ownership-compatible remote. Use remote ownership and the remote default branch only as supporting evidence. Do not silently change a remote URL or invent a renamed branch mapping.

Multiple remotes are not interchangeable. Resolve each branch by remote identity and full branch name. Stop the affected repository before publication when several remotes or owners remain equally plausible, when two actual remote branches compete for the same local branch name, or when the resolved repository no longer matches the frozen discovery result.

Assign every configured remote an evidenced role: publication target for identified branch mappings, read-only comparison source, or unresolved. Existing upstreams, push configuration, ownership, and explicit user scope are valid evidence. A read-only remote is still fetched completely but its remote-only branches do not require same-named local tracking branches. An unresolved remote role blocks complete synchronization because its branches cannot be classified safely.

A detached current checkout does not prevent read-only branch inventory or unambiguous creation of a missing local tracking branch without checkout. It blocks attributing or committing working changes and any operation that would need to move that checkout. Preserve the detached commit and report the branch-specific limitation.

In this step, record only proposed mappings and branch-local configuration changes. Do not create a branch or change an upstream before authentication succeeds, the complete baseline is captured, and the all-heads branch matrix verifies the proposal. Existing-branch upstream configuration and missing tracking-branch creation occur only in step 4. A relevant local-only branch receives its same-named upstream only as part of a successful explicit-refspec push in step 10.

### 2. Resolve Authentication Context Safely

For every configured remote, determine whether the operation requires a specific existing account, organization, tenant, or hosting context. Derive the expected identity from the remote URL and repository owner, then inspect available contexts and the active identity using read-only checks.

An existing account selection may be performed without another question when the available interface provides a workspace-scoped, sandbox-scoped, session-scoped, per-call, or otherwise isolated context switch. A hosting CLI account switch is also allowed when the execution environment is known to isolate its configuration and the switch only selects an existing account. After switching, verify repository access with a read-only metadata or remote-head query and through the transport that will fetch or push.

Do not:

- perform an interactive login
- create, replace, delete, or expose credentials, tokens, or keys
- change a shared or persistent machine account context without explicit user permission
- change Git configuration, credential helpers, or remote URLs to bypass an authentication problem

If no matching usable context exists, several contexts are equally plausible, or switching would require persistent shared configuration, stop and ask the user. Record the initial context, contexts checked, any isolated switch, and the context ultimately used without exposing credential material.

### 3. Capture The Baseline Before Fetch

Inspect and retain enough information to restore or explain the starting state:

- every worktree path, current `HEAD`, current branch or detached state, and concise status including staged, unstaged, untracked, deleted, and renamed paths
- every local `refs/heads/*` branch and OID
- every local branch upstream, push target, and checked-out worktree association
- every detached worktree `HEAD` and whether its commits are reachable from an accounted-for local branch; reassess actual-remote reachability after the all-heads fetch
- every cached `refs/remotes/<remote>/*` ref and OID, including symbolic refs recorded separately
- every remote, URL, fetch refspec, and branch-related configuration used to resolve publication targets
- ahead/behind information currently recorded locally for every existing branch mapping
- running merge, rebase, cherry-pick, revert, bisect, or other Git operation
- existing stashes and their identifiers

Stop if a Git operation is already incomplete. Do not absorb or overwrite work with concrete evidence of another owner, local-only purpose, or unsafe handling requirements; general uncertainty alone requires proportional repository-local assessment rather than an automatic block.

Inspect candidate changes for generated output, ignored files, local runtime state, and likely secret-bearing paths. Never print secret values. Existing ignored files are not normal publication candidates.

The pre-fetch remote-tracking inventory is required because prune may remove stale refs. Preserve it in memory or the run report so a disappeared remote branch can be distinguished from a branch that was never observed locally. Do not treat a remote query or fetch failure as evidence that a branch was deleted.

### 4. Fetch All Branches And Build The Branch Matrix

For every configured remote, query the actual `refs/heads/*` set independently, then fetch its complete branch namespace with prune and explicit no-tag behavior. Do not rely on a configured fetch refspec until it has been verified to cover all remote heads. A typical command-line implementation is:

```text
git fetch --prune --no-tags <remote> "+refs/heads/*:refs/remotes/<remote>/*"
```

This force marker applies only to updating local remote-tracking observations when the remote rewrote a branch; it does not authorize a force-push or local history rewrite. Never fetch or reconcile tag refs in this workflow.

If the actual remote query or fetch fails, do not create tracking branches, integrate, commit, or push for that remote. Report the transport or authentication error and the captured baseline. Only a successful actual remote query in which a previously observed branch is absent establishes a remote-deleted case.

After all fetches, independently query actual remote heads again and build one branch matrix containing at least:

- remote identity and full branch name
- actual remote branch and OID
- refreshed remote-tracking ref and OID
- same-named local branch and OID
- configured upstream and push target
- worktree in which the local branch is checked out, if any
- ahead and behind counts against the intended upstream
- publication-intent classification
- required action and current blocker, if any

Exclude symbolic refs such as `<remote>/HEAD` from branch counts and actions. Key records by remote identity plus full branch name so same-named branches on different remotes remain distinct.

Classify every row as exactly one of:

- **in sync:** local, remote-tracking, and actual remote heads exist and match; ahead 0, behind 0
- **local ahead:** the mapped local branch is ahead and not behind
- **local behind:** the mapped local branch is behind and not ahead
- **diverged:** both sides contain unique commits
- **local only:** a local branch has no selected actual remote branch
- **remote only:** an actual remote branch has no same-named local branch
- **remote deleted:** a previously observed remote-tracking or upstream branch is absent after a successful actual query and prune, while related local state may remain
- **ambiguous or blocked:** remote, name, upstream, worktree preservation, authentication, privacy, or publication intent is unresolved

For local-only and remote-only states, inspect branch configuration, commit topology, commit lists, and changed paths as needed. For divergence, also inspect the merge base, changes on both sides, same-file edits, renames, deletions, and dependent project or configuration changes. Do not compare feature branches to the default branch as a substitute for ahead/behind against their own mapped upstreams.

Only after the baseline, successful authentication, complete all-heads query and fetch, and branch matrix establish one unambiguous proposal may step 4 change branch-local state. When an actual remote branch and same-named existing local branch are unambiguous and the local branch has no upstream, configure it to track the corresponding remote-tracking branch and verify the result. Create a missing local tracking branch for every unambiguous remote-only row on a selected publication remote exactly at the verified actual remote OID, set its upstream to the corresponding remote-tracking branch, and verify it without checking it out. Keep remote-only branches from evidenced read-only remotes as observed comparison state. Block rather than rename or reconfigure when a local ref already exists with incompatible casing, a prefix collision such as `name` versus `name/topic`, a different upstream, or another remote mapping.

When prune reveals a remote-deleted branch, never republish it automatically. Treat remote deletion as strong evidence that the branch was completed or intentionally closed elsewhere. Preserve its former mapping and local OID for the step 6 retirement decision; do not delete it during fetch or classification.

### 5. Preserve Working Changes When Integration Requires A Clean Tree

Do not switch the selected current checkout merely to process another branch. Use the branch's already selected worktree when it is checked out there and that worktree can pass the same preservation checks. When a non-current branch is not checked out anywhere and needs checkout-based integration, create a uniquely named temporary worktree for that exact branch inside a validated repository-local or temporary location. Record it and remove it after successful verification. If the branch is checked out in another pre-existing worktree that was not selected, or that worktree cannot be preserved safely, block only that branch.

Do not create a stash merely because changes exist. Create one only in the exact worktree where a required fast-forward or merge cannot safely proceed with its current working tree.

Before stashing:

- record all pre-existing stashes
- identify staged, unstaged, and untracked changes
- do not include ignored files unless the user explicitly requests their preservation in this workflow

Create one uniquely named safety stash including untracked files, record its exact stash reference, object ID, and owning worktree, and verify that the tracked working tree is clean. If the stash fails or the tree remains unsafe, stop without integrating that branch.

Never delete or rewrite a pre-existing stash.

### 6. Prepare Every Branch Mapping

Process every branch-matrix row in stable remote and full branch-name order. Re-verify the expected local, remote-tracking, and actual remote OIDs immediately before changing a ref. Apply the branch case without switching the selected current checkout:

#### In Sync

No integration is required.

#### Local Ahead

Do not merge or rebase. The local branch already contains the fetched upstream state.

#### Local Behind

Advance only by fast-forward against the already fetched upstream after verifying that the old local OID is an ancestor of the intended new OID and the actual remote head still matches. In its selected clean worktree, a typical command-line implementation is:

```text
git merge --ff-only <upstream>
```

If fast-forward unexpectedly fails, stop and re-evaluate. Do not substitute a merge, reset, or rebase silently.

When the branch is not checked out in any worktree, an atomic ref update guarded by the expected old OID is allowed instead of a checkout. Verify the branch and upstream afterward. Never update a branch ref underneath an unselected or dirty worktree.

#### Diverged

Create a uniquely named local safety branch at the original local branch head. Keep it unless the user later requests cleanup. Perform any allowed merge only in the branch's selected preserved worktree or a workflow-created temporary worktree; never switch the user's current checkout.

Treat every divergence as a branch-level special case. Proceed with a normal merge only after the overlap analysis shows that the intended combined result is unambiguous, every affected worktree can be preserved, and the merge does not combine different branch identities merely to make them equal. Prefer a merge over rebase so existing commits are not rewritten. Start the merge without automatically finalizing the merge commit when the available interface supports that review point.

Before completing the merge, inspect:

- the full staged result
- unexpected deletion or rename outcomes
- project, dependency, build, data-format, and configuration effects
- source and test changes that must remain consistent

Resolve conflicts only when the correct combined behavior is supported by repository evidence. Do not choose one side merely to make Git clean. If a safe resolution is not possible, abort only the merge started by this workflow, preserve the safety branch, restore any workflow-created stash, and ask the user with the exact conflicted paths and options.

Never use force-push, destructive reset or clean commands, or rebase as an automatic fallback.

#### Local Only

Do not publish a local-only branch merely because it exists. Inspect its complete commits and changed paths and classify its branch-level publication intent separately from working-tree path intent.

- **publish:** positive evidence shows that the branch is intended shared work, its same-named remote target is unambiguous, and it is safe and complete
- **deliberately local:** concrete evidence shows that it is private, temporary, experimental, security-sensitive, generated, a workflow-created safety branch, or otherwise intentionally local
- **blocked:** publication intent, privacy, ownership, remote, or completeness remains unclear after proportional repository-local assessment

Names such as `private`, `tmp`, `wip`, `security`, or `safety` are warning signals, not sufficient evidence by themselves. Never expose a suspected private or security branch to resolve uncertainty. A deliberately local branch remains local and is reported; a blocked local-only branch prevents claiming full synchronization.

#### Remote Only

After the verified local tracking branch has been created in step 4, reclassify the row against its upstream. No push or merge is needed when all three heads match.

#### Remote Deleted

Never recreate the deleted actual remote branch automatically. Determine whether the retained local branch is safely redundant by checking all of the following against the refreshed complete remote branch set:

- the pre-fetch baseline proves a same-named upstream mapping and records its cached remote-tracking OID; do not misstate that cached OID as a previously verified actual remote head
- a successful independent query and prune confirm that exact actual remote branch is absent
- the local branch ref still has its expected OID and is not checked out in any worktree
- it is not the repository default, a mainline or protected branch, or a workflow-created safety branch
- no merge, rebase, cherry-pick, revert, bisect, stash restoration, or other operation depends on it
- inspect the local branch's complete reflog when one exists; every commit reachable from the branch or referenced by that reflog is also reachable from at least one surviving actual branch on the same publication remote

When every condition is proven, remove the local branch with an expected-OID guard and remove only its corresponding stale branch-local upstream configuration. Do not use force deletion to bypass a failed proof. Record the removed branch name and final local OID, then classify the row as **retired**. This is normal cross-computer cleanup of a branch whose work is already preserved by surviving remote history.

If any commit is reachable only from the retained local branch, if it has moved since the baseline, or if any other condition is unresolved, keep it. Report the number and short OIDs of locally unique commits, whether it is checked out, and the exact reason retirement was blocked. Do not publish it merely to restore symmetry. A retained unresolved remote-deleted branch blocks complete synchronization unless concrete evidence classifies it as deliberately local.

Do not perform a broader content-relocation or equivalence investigation by default when the automatic retirement proof fails. When repository evidence suggests that a deeper read-only analysis could determine whether the retained branch's work is preserved elsewhere, offer an optional follow-up action for that branch. If selected, the analysis may inspect commit ancestry, unique commits, complete reflog reachability, and exact Git-blob matches in surviving branches or relocated repository-owned paths. Report a short evidence-based recommendation to remove the branch, keep it, or leave the result unresolved. This follow-up analysis must not switch a checkout, delete a ref, change branch configuration, publish anything, or otherwise mutate state.

#### Ambiguous Or Blocked

Perform no branch mutation or publication. Record the exact ambiguity or safety condition. Under the default all-or-nothing preparation gate, one unresolved relevant branch blocks publication for the repository; best-effort branch publication requires explicit user authority and must be reported as partial.

### 7. Restore Preserved Work And Remove Temporary Worktrees

Restore each exact stash created by this workflow only in its recorded owning worktree. Apply it first with its staging information when possible; do not pop it immediately.

Verify that:

- all prior staged, unstaged, and untracked changes are present
- the staging state is restored as far as the interface supports
- no conflicts were introduced
- the selected stash is the workflow-created stash for that worktree, not a pre-existing one

Drop that stash only after successful verification. If restoration conflicts, keep the stash, preserve the integrated branch state, and investigate without guessing. Report that complete repository synchronization is blocked.

Remove only temporary worktrees created by this workflow, and only after their branch operation is complete, their worktree and index are clean, their branch ref is verified, and no preservation artifact remains inside them. Never remove or relocate a pre-existing worktree. A temporary worktree that cannot be removed safely remains a reported blocker to complete synchronization.

### 8. Prepare Relevant Local Work For Publication

Inspect the complete resulting working-tree and staged diff in every selected worktree. Also inspect every locally unpublished commit on every branch that would be published, including its full changed-path set and relevant diff, rather than assuming an existing commit is safe merely because it is already committed.

For every detached worktree, determine whether its `HEAD` and unique ancestry are reachable from an accounted-for local or actual remote branch. Inspect unreachable commits and classify them from concrete repository evidence. They may be retained as deliberately local, but otherwise they block complete synchronization because this workflow must not invent a publication branch or attach the detached checkout to a guessed mapping.

Commit working-tree changes only to the branch already attached to their owning selected worktree. Never switch the user's checkout, attach detached work to a guessed branch, or move changes between branches merely to publish them. A detached or ambiguous worktree with relevant changes is a blocker.

#### Intent Assessment For Working-Tree Changes

Explicit activation means the agent owns a proportional assessment of whether the actual unpublished changes belong in the requested shared state. Tracked modifications, renames, and deletions are strong positive evidence of publication intent and default to **commit now** after safety and completeness checks. Block or hold them only when concrete contrary evidence shows that they are excluded, local-only, unsafe, incomplete, or outside the requested shared state.

Untracked files require positive project classification before they can be committed. Conventional project-scoped configuration or settings files are positive publication candidates when they are syntactically plausible and contain no secrets, credentials, private keys, personal identifiers, machine-specific absolute paths, or generated/local-only markers. Localhost or development profiles alone are not contrary evidence. The absence of a previous tracked counterpart or an explicit per-file policy is not a blocker; concrete local-only evidence overrides the positive inference.

Before classifying work as **needs review**, exhaust proportional repository-local evidence such as the changed paths and diff, nearby tracked files, project conventions, manifests, and narrowly relevant documentation. State the exact unresolved risk or missing fact. General uncertainty or the lack of per-file user approval is insufficient. Never silently sanitize unsafe values or force-add ignored files.

Classify every changed path as:

- **commit now:** tracked modification, rename, or deletion with no concrete contrary evidence, or an untracked file with positive project classification; in each case understood, in scope, safe, and complete
- **hold:** explicitly excluded, generated, ignored, sensitive, intentionally local, or supported by other concrete local-only evidence
- **needs review:** incomplete intent, suspected secret, risky behavior, or a specific unresolved risk or missing fact that remains after proportional repository-local assessment

Group `commit now` changes into coherent commits that preserve a usable progression. Stage only exact intended paths and use concise messages derived from the changes. Do not force-add ignored files, silently change tracking policy, or combine unrelated work merely to empty the worktree.

Check both newly created and previously existing locally unpublished commits for unintended generated output, secret-bearing material, unrelated work, and changes outside the selected publication intent. Do not rewrite an existing commit automatically when a problem is found; stop and report the exact non-secret concern and safe options.

After commit preparation, classify every local branch with locally unpublished commits as **publish**, **deliberately local**, or **blocked**. An existing unambiguous upstream and an explicit synchronization request are strong positive evidence for a mapped local-ahead branch, but concrete privacy, safety, ownership, completeness, or scope evidence overrides that inference. A local-only branch still requires the stronger positive evidence defined in step 6. Reconfirm any earlier branch classification when its delta changed during preparation.

#### Delta-Based Validation Gate

Determine the complete unpublished delta across every publishable branch against current remote state before running any validation. Deduplicate overlapping validation needs, but do not validate only the current branch when another branch contains a distinct unpublished delta.

- When there are no locally unpublished commits and no relevant working-tree or staged changes, skip product builds, tests, packaging, artifact generation, publish commands, deployments, samples, and runtime exercises. An unchanged repository needs only Git and remote-state verification.
- When the unpublished delta changes only documentation or non-executable metadata, use only a directly relevant lightweight check when one is necessary. Do not run a product build merely because a build command exists.
- When the unpublished delta changes code, build logic, tests, or runtime behavior, choose the smallest targeted check that gives reasonable confidence in those exact changes.
- Expand to a broader build or test suite only when the delta is broad enough to require it or the user explicitly requested broader validation.
- Do not run product publish, packaging, deployment, installer, sample, environment-provisioning, or network-heavy workflows as generic synchronization checks. Such a command requires separate user authority or a direct validation need created by the unpublished delta with no safer narrower check.

Validation is evidence for the unpublished delta, not a new task. Do not inspect unrelated failures, fix pre-existing problems, or continue into additional checks after the smallest sufficient validation has passed.

A failing directly relevant check blocks claiming a fully synchronized publish unless the user explicitly accepts publication with that known failure. A skipped check should be reported only when useful, including the reason it was unnecessary or outside scope.

If relevant work or a relevant branch remains under `needs review` or `blocked`, state the exact unresolved risk or missing fact and stop before claiming complete synchronization. Existing safe commits may be published only when doing so does not misrepresent or break the shared state and the user authorized best-effort publication; report the result as partial. Do not use general uncertainty, lack of a previous tracked counterpart, lack of explicit policy, or lack of per-file user approval as the sole reason to leave working-tree paths under `needs review`; local-only branch publication still requires positive shared-state evidence because an accidental branch push can expose substantially more history than one path.

### 9. Recheck Every Remote Branch Immediately Before Push

Independently query every configured remote's complete actual `refs/heads/*` set and fetch every complete branch namespace again with prune and no tags. Rebuild the full branch matrix rather than checking only branches already known at preparation time.

New actual remote branches discovered here are not ignorable races. Create their local tracking branches only when the mapping remains unambiguous, classify them, and return the repository to preparation. If an existing actual remote branch advanced, do not push its local branch; reclassify and integrate the new state. In a multi-repository run, re-evaluate whether other prepared repositories remain valid. If remote state changes repeatedly or safe integration becomes unclear, stop rather than racing another contributor.

Before any push confirm for every relevant branch:

- target remote and full branch name remain unambiguous
- active authentication context can access the exact target
- local branch, upstream, remote-tracking ref, and actual remote head still match the prepared matrix or have an understood publishable relationship
- no Git operation is incomplete and no unresolved conflict exists in any selected or temporary worktree
- local history contains the current actual remote history
- relevant checks passed or an explicitly accepted limitation is recorded
- no suspected secret, private branch history, or unintended file is staged or committed for publication
- no unresolved relevant branch would make publication falsely appear complete

### 10. Publish Branches Without Rewriting History

Use one explicit normal branch refspec per approved branch mapping in stable order. Never use `--all`, `--mirror`, a tag refspec, implicit tag following, force, force-with-lease, or another history-rewriting path. A typical command-line shape is:

```text
git push --no-follow-tags <remote> refs/heads/<name>:refs/heads/<name>
```

Immediately before each explicit branch push, independently query that exact actual remote ref again. Its OID must still equal the prepared expected OID, or it must still be absent for an approved new branch. If it changed or appeared, do not push; return to all-heads classification. Do not use force-with-lease as a compare-and-swap substitute because force-style pushes remain outside this skill.

Publish only local-ahead or local-only branches whose final branch classification is **publish**. Do not push a newly created local tracking branch whose actual remote branch already has the same OID. For a successfully published local-only branch, set the verified same-named remote branch as upstream as part of that explicit push or immediately afterward through branch-local configuration; do not modify remote URLs or global configuration.

If any push is rejected or authentication fails, do not bypass the rejection. For a non-fast-forward rejection, fetch all heads and classify again. For permission, policy, or protected-branch rejection, report the branch-specific blocker and required user or repository-owner action. Do not roll back branches already published and never imply atomic branch publication.

### 11. Verify The Complete Actual Remote Result

Do not rely only on local remote-tracking refs after push. Query every configured remote's complete actual `refs/heads/*` set independently, fetch the complete branch namespaces once more with prune and no tags, and rebuild the branch matrix from the final state.

Confirm:

- every relevant actual remote branch has the required same-named local tracking branch with the intended upstream
- every relevant local branch has the intended actual remote counterpart
- every confirmed remote-deleted branch was either safely retired, positively classified as deliberately local, or remains an explicit blocker
- for every relevant mapping, local, refreshed remote-tracking, and actual remote OIDs are identical and ahead and behind are both zero
- no unexpected new remote branch, stale unaccounted remote-tracking ref, divergence, ambiguous mapping, or unexplained local-only branch remains
- every deliberately local branch has concrete classification evidence and is reported without publication
- every unreachable detached-checkout commit is positively classified as deliberately local or remains a reported blocker
- no conflict or incomplete Git operation remains in any worktree
- each selected current checkout remains on its original branch or detached commit identity; intended branch-head advancement is allowed, but checkout switching is not
- every original staged, unstaged, and untracked change is present unless it was intentionally committed by this workflow, with staging restored as far as the interface supports
- every workflow-created stash was restored and removed, or remains as a reported blocker
- all pre-existing stashes remain untouched; no pre-existing worktree was removed or switched, selected worktrees changed only through authorized integration or commits, and unselected worktrees remain otherwise untouched
- every temporary worktree was safely removed
- any safety branch created for divergence is named, deliberately local, and reported
- no tag was intentionally fetched, created, changed, pushed, or included in verification

Only then mark this repository fully synchronized. Verification of only the current branch or its upstream is never sufficient.

## Failure And Recovery Rules

- If an unexpected file, repository, worktree, or commit appears during the workflow, stop and re-inventory before continuing.
- If target discovery changes after it was frozen, stop before mutation or publication and resolve the scope again.
- If an actual remote query or all-heads fetch fails, make no tracking-branch, integration, or publication changes for that remote.
- If a remote, owner, account, push target, same-named branch mapping, or ref namespace is ambiguous, block the affected repository before default publication rather than guessing or renaming.
- If a branch is divergent and a normal evidence-supported merge cannot be completed without changing another checkout or guessing at behavior, preserve the original local branch head through the safety branch and block that branch.
- If a required branch is checked out in an unselected or unsafe worktree, do not update its ref underneath that checkout.
- If local-only branch publication intent or privacy is unresolved, do not push it and do not call the repository fully synchronized.
- If a previously remote branch is deleted, never recreate the actual remote branch automatically; retire its local branch only when every step 6 proof succeeds.
- If a remote-deleted local branch changes during retirement checks or expected-OID deletion fails, preserve it, re-inventory, and report the blocker.
- If tracking-branch creation cannot place the exact same-named local branch at the verified actual remote OID with the intended upstream, preserve and report any partially created local ref and its exact state; never delete or overwrite a local branch as recovery from tracking-branch creation.
- If integration fails, preserve the original local branch head through the safety branch and abort only operations started by this workflow when safe.
- If stash restoration fails, keep the stash and do not claim that local work was fully restored.
- If validation fails, report the exact failed check and do not hide it behind a successful push.
- If push fails, never force it; preserve local commits and report the visible reason.
- If account selection is ambiguous or persistent shared configuration would be required, ask before switching.
- If a required operation would need remote-branch deletion, local-branch deletion outside the exact step 6 retirement rule, checkout switching, rebase, destructive reset, history rewriting, force-push, or tag handling, do not perform it under this skill.
- In a multi-repository run, never imply atomic rollback after any repository has been published.

## Final Report

Compose the final report directly from the verified workflow facts and this reporting contract. Do not delegate its structure to a helper script, copy a tool-native table as the result, or let raw command output determine the layout.

For one repository, keep a successful report compact. For a multi-repository run, start with a concise result table containing one row for every selected repository and every exceptional resolved candidate that was skipped or blocked. Group ordinary non-repository or unsupported immediate children by reason and count outside the table instead of adding one row per path; list individual paths only when needed to explain an unexpected, ambiguous, or actionable case. The table must contain at least these columns:

| Repository | Status vorher | Maßnahme | Status nachher | Ergebnis |
| --- | --- | --- | --- | --- |

Use the columns as follows:

- **Repository:** identify the repository unambiguously; add compact branch-coverage counts when useful.
- **Status vorher:** give one compact branch synopsis. Use `L<n>/R<n>` for the counts of relevant local branches and relevant actual remote branches on publication remotes. For a routine repository use `L4/R4; alle 0/0; sauber`, where `alle 0/0` means every mapped branch is zero ahead and zero behind rather than an aggregate commit count. Name only branches that differ, for example `L2/R1; main 0/0; feature/x lokal vorhanden, Remote fehlt (+2)`. Add initial branch or `HEAD` values only when they explain a change.
- **Maßnahme:** state only material changes such as `keine Änderung`, `Tracking-Branch erstellt`, `lokalen Branch entfernt`, `Fast-forward`, `Merge`, `Commit erstellt`, `Push`, or `blockiert`. Routine all-head queries, fetches, authentication checks, and verification are execution evidence, not separate per-repository measures; summarize them once outside the table when useful.
- **Status nachher:** use the same compact branch synopsis and add concise worktree and independent remote-verification state. Name individual branches only when they changed or remain exceptional.
- **Ergebnis:** for an end-to-end execution run, use exactly one of `vollständig synchronisiert`, `teilweise veröffentlicht`, `blockiert`, or `übersprungen`. Use `vollständig synchronisiert` only after the existing verification criteria are satisfied; the other labels summarize incomplete, blocked, or intentionally skipped outcomes without changing those criteria. For an explicitly non-mutating discovery, status, planning, or simulation run, use the following exact meanings:
  - `kein Handlungsbedarf`: an independent live query covered the complete actual `refs/heads/*` namespace of every publication remote; all relevant local branch heads, remote-tracking refs, and actual remote heads match, every mapped branch is zero ahead and zero behind, and no working change or branch classification requires a synchronization action
  - `Handlungsbedarf`: at least one concrete fetch, tracking, integration, commit, publication, retirement, or other synchronization action is required; state it in **Maßnahme**
  - `blockiert`: a required or potentially required synchronization action cannot be selected safely because scope, intent, mapping, privacy, authentication, or another safety condition remains ambiguous
  - `nicht vollständig ermittelbar`: the complete live actual-remote state or another required fact could not be obtained, so the run cannot determine whether action is required

  Cached remote-tracking refs such as `origin/*` record only an earlier observation and cannot by themselves establish `kein Handlungsbedarf`; another machine may have changed the actual remote branches since the last fetch.

When a status value was not obtained because a repository was skipped or blocked before that workflow stage, write `nicht ermittelt` with a concise reason instead of guessing.

The repository table is the primary workspace summary. Add a branch table only for branches that changed, were created or retired, were published, or remain local-only, remote-only, remote-deleted, divergent, ambiguous, held, or blocked. Multiple routine synchronized branches alone never justify a branch table.

| Branch | Local | Upstream | Actual remote | Status vorher | Maßnahme | Status nachher | Ergebnis |
| --- | --- | --- | --- | --- | --- | --- | --- |

Use one row per exceptional or actioned branch, not one row for every mapping. Identify the remote in `Upstream` or `Actual remote`. Distinguish `origin/feature-x` as a remote-tracking ref from local `feature-x` as a local tracking branch. Include retired branches, pruned stale refs, and deliberately local branches with their terminal classification. Keep routine synchronized mappings summarized by count in the repository row.

Use branch-level `Ergebnis` values `synchronisiert`, `entfernt`, `lokal belassen`, `blockiert`, or `übersprungen`. A read-only-remote observation may be `übersprungen` with its established remote role. These branch labels do not replace the required repository-level result.

Across the tables, a short target-resolution preface, and any necessary follow-up details, include:

- requested target and how it resolved
- selected repositories and skipped or blocked candidates
- complete local-branch, upstream, remote-tracking, and actual-remote coverage summarized by counts for each repository
- fetched remotes, newly discovered actual remote branches, and pruned stale remote-tracking refs
- initial authentication context and any isolated context selection
- initial and final OIDs and ahead/behind state for changed, exceptional, or blocked branches; routine branches stay count-compressed
- tracking branches created and whether fast-forward, merge, or no integration was used per branch
- local-only branches published, deliberately retained locally, or blocked, with non-sensitive reasons
- safety stash, safety branch, or temporary worktree created and its final state
- commits created and branches published
- validation commands and results, or that validation was skipped because there was no unpublished delta
- explicit branch refspec pushes and independent all-heads remote-verification result
- remaining local, excluded, incomplete, sensitive, or stashed work
- preservation result for every selected checkout and pre-existing worktree
- remote-deleted local branches retired or retained with reasons, confirmation that no remote branch was deleted by the workflow, and that tags were outside the run
- per-repository result in the table and the overall selected-workspace result outside it

When at least one retained remote-deleted branch qualifies for the optional deeper analysis from step 6, append a concise `## Suggested Follow-up Actions` section. Group the actions dynamically by repository and branch, assign letters and numbers only as convenient references, and describe each action as an analysis that will return a removal recommendation without changing repository state. Do not perform the analysis merely because it is listed.

Accept any unambiguous user selection of those actions. References such as `A1`, branch or repository names, and natural-language replies such as `yes, all`, `both`, or `analyze the first one` are equally valid when their meaning is clear. Do not require the user to reproduce the displayed identifier syntax; ask only when the requested selection is genuinely ambiguous.

Do not repeat facts in prose when the table already communicates them clearly. Do not emit a full branch inventory, repeat identical fetch or verification actions in every repository row, list unchanged OIDs, or create detail rows for routine synchronized branches. Add detail after the table only when it is needed for surprising discovery, branch retirement, divergence, conflicts, safety-stash or safety-branch recovery, partial publication, authentication problems, remaining work, failed validation, failed push, or a concrete next action. Never reproduce secret values in the report.
