---
name: execute-repo-sync-and-merge-branch-into-main
description: Verify a branch is fully synced, switch to mainline, refresh mainline, and merge the branch into main locally. Leave source branches intact unless cleanup is explicitly requested.
---

# Execute Repo Sync and Merge Branch into Main

## Overview

Use this skill when the goal is to bring the current non-main branch onto the repository's mainline branch. The skill verifies that the source branch is fully synced with its remote upstream first, switches to the detected mainline branch, refreshes mainline from remote again, and then merges the source branch into mainline locally. The default outcome is a local mainline merge result that can be reviewed and pushed afterward. Source branches remain unchanged unless the user explicitly requests local or remote cleanup.

## When To Use

- You want to bring the current feature, fix, or release branch onto `main` or `master`.
- You want a repeatable promotion flow with explicit upstream checks before touching mainline.
- You want mainline refreshed from remote immediately before merging the source branch.
- You want the branch promotion captured locally on mainline, typically as an explicit merge commit.

## When Not To Use

- Do not use this skill for rebases, cherry-picks, or history rewrites.
- Do not use this skill when the source branch still needs local commits, push, or pull work before promotion.
- Do not use this skill when you want automatic mainline push as part of the default flow.
- Do not use this skill to guess a source branch while already on `main` or `master` unless the user explicitly names the branch to merge.

## Inputs Needed

- Current branch name.
- Upstream tracking state for the current branch.
- Detected mainline branch, preferring `main`, then `master`, then the remote default branch when discoverable.
- Optional explicit source branch if the current branch is already the detected mainline branch.

## Workflow

### 0. Detect Branch Roles

- Detect the mainline branch in this order:
  - local `main`
  - local `master`
  - the branch pointed to by `origin/HEAD`, if available
- If no mainline branch can be determined, stop and report that blocker.
- If the current branch is not the detected mainline branch, treat the current branch as the source branch.
- If the current branch is already the detected mainline branch, require the user to explicitly name the source branch to merge. Do not guess from checkout history or reflog.

### 1. Source Branch Sync Gate

- Fetch remotes first so branch status is current.
- Verify that the source branch has an upstream tracking branch.
- Verify that the source branch is fully synced with upstream: ahead `0` and behind `0`.
- If the source branch is ahead, behind, or diverged, stop and report that the branch must be fully synced before promotion.
- Verify the worktree has no tracked modifications or staged changes before switching branches.
- Ignored local-only files may remain as long as they do not block checkout or merge.
- If checkout would be blocked by local files, stop and report the exact blocker instead of stashing automatically.

### 2. Switch To Mainline And Refresh It

- Checkout the detected mainline branch.
- Fetch again after switching so mainline state is current at the moment of promotion.
- If mainline has an upstream and is behind but not diverged, sync it with a normal non-interactive fast-forward pull such as `git pull --ff-only`.
- If mainline is ahead of upstream, leave those local mainline commits intact and continue unless the user explicitly wants a different policy.
- If mainline has diverged from upstream, stop and report it instead of choosing merge or rebase automatically.

### 3. Bring Source Branch Onto Mainline

- Merge the source branch into mainline locally.
- Default to an explicit local merge commit by using a non-interactive merge such as `git merge --no-ff --no-edit <source-branch>`.
- If the source branch is already fully contained in mainline, report that mainline is already up to date and stop without creating a new commit.
- If Git reports merge conflicts, stop and report the conflicted state clearly. Do not auto-resolve conflicts.

### 4. Post-Merge State

- After a successful merge, report the resulting mainline commit state and whether the branch is now ahead of remote.
- Do not push mainline by default. The default endpoint of this skill is a local mainline merge result ready for review or a later explicit push.
- If the user explicitly asks to push after the merge, only then perform a normal push and report the result.

### 5. Source Branch Cleanup

- Skip source-branch cleanup by default.
- Consider cleanup only when the user explicitly requests local deletion, remote deletion, or both, and only after a successful merge while currently checked out on the detected mainline branch.
- Before deleting the source branch anywhere, verify that the source branch tip is fully contained in local mainline.
- If the source tip is not contained in local mainline, stop and report that cleanup is unsafe.
- For explicitly requested local cleanup, delete the local source branch only with a normal safe delete such as `git branch -d <source-branch>`.
- For explicitly requested remote cleanup, first verify that a durable remote mainline ref contains the source tip. A merge that exists only on local mainline is not sufficient protection.
- Delete the remote source branch only after that remote-containment check succeeds, using a normal delete such as `git push origin --delete <source-branch>`.
- If the source branch was already fully contained in mainline before the merge step, the same explicit opt-in and remote-containment rules still apply.

## Final Reporting

- For a clean successful run, keep the summary short:
  - source branch
  - detected mainline branch
  - whether source was already fully synced
  - whether mainline was refreshed from remote
  - resulting merge commit or "already up to date"
  - whether mainline is now ahead of remote locally
  - that the source branch was left unchanged by default, or which explicitly requested cleanup was completed
- Expand only when there are blockers, sync mismatches, conflicts, or the user asks for more detail.

## Execution Guardrails

- Do not use destructive commands such as `git reset --hard`, `git checkout --`, force-push, or history rewriting unless explicitly requested.
- Do not auto-stash or auto-commit local work to make the branch switch succeed.
- Do not promote a source branch that is not fully synced with its remote upstream.
- Do not guess a merge strategy beyond the default local merge commit flow.
- Do not auto-push mainline unless the user explicitly asks for it.
- Do not delete a local source branch unless the user explicitly requests local cleanup and the source tip is safely present on local mainline.
- Do not delete a remote source branch unless the user explicitly requests remote cleanup and the source tip is safely present on a durable remote mainline ref.
- Do not suppress checkout, pull, or merge blockers; report them clearly.
