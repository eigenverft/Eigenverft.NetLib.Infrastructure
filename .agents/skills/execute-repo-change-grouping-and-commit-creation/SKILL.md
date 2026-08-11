---
name: execute-repo-change-grouping-and-commit-creation
description: Analyze repository differences, group understood changes into logical commits, and execute safe local staging and commit steps with clear messages. Remote synchronization, push, and untracking remain explicit opt-in actions.
---

# Execute Repo Change Grouping and Commit Creation

## Overview

Use this skill to inspect current repository differences, decide how to group understood changes, and then execute local git staging and commit steps. The default endpoint is a clean local commit sequence with no push, pull, fetch, or tracking-policy change. Preserve unexplained, sensitive, generated, ignored, or otherwise uncertain changes until their intent is established. Ignored local-only files are not candidates for normal commits.

## When To Use

- You have mixed local changes and need a clean commit strategy.
- You want to split one large diff into several distinct commits and actually create them.
- You need help deciding what should be committed now versus later and want the commit-ready groups executed.
- You want concise and descriptive commit messages per group and a clear execution report.
- You want a plan-only grouping review; when the user says plan, review, or report only, stop before staging or committing.

## When Not To Use

- Do not use this skill for history rewriting such as `git rebase`, `git commit --amend`, or force-push workflows unless explicitly requested.
- Do not use this skill when the working tree contains risky or unexplained changes that cannot be safely grouped.

## Inputs Needed

- Current branch and repository context.
- Upstream tracking and remote status for the current branch when available.
- `git status` and diff summaries for changed files.
- Any known constraints, such as release urgency or risky files.
- Build or test command when verification is expected.

## Workflow

### 0. Local Branch Context

- At the start, inspect the current branch and its locally recorded upstream tracking state when available.
- Do not fetch, pull, merge, rebase, or otherwise synchronize remote state unless the user explicitly requests synchronization as part of this run.
- When the locally recorded state shows that the branch is behind or diverged, report that caveat. It does not by itself authorize a remote operation or prevent safe local commits.
- If the user explicitly requests synchronization, treat it as a separate pre-commit phase. Fetch first, require a clean tracked worktree before pulling, use only a normal non-interactive fast-forward pull, and stop on divergence or any overwrite risk.
- If there is no upstream tracking branch or no reachable remote, continue with the local repository state and mention that assumption in the final report.

### 1. Inventory Repository Differences

- List changed, untracked, deleted, and renamed files.
- Summarize change scope by area (feature, bugfix, docs, config, tests, refactor).
- Identify potentially sensitive or generated files that should not be committed.
- Check whether any candidate file is ignored by `.gitignore`, `.git/info/exclude`, or a global excludes file before staging it.

### 2. Commit-Readiness Analysis

- Mark each change as `commit-now`, `hold`, or `needs-review`.
- Check dependency links between files to avoid broken intermediate commits.
- Highlight risky changes needing explicit confirmation.
- Use `commit-now` only when the change's intent is understood, it belongs to the user's requested commit scope, and it is safe to stage.
- Use `hold` when the user excludes a path or when a change is clearly local-only, generated, sensitive, or unrelated.
- Use `needs-review` when ownership or intent is unclear, the change may contain a secret, or committing it would require a non-trivial policy decision.
- Ignored untracked files stay local-only by default and do not need to be committed.
- A tracked file that also matches an ignore rule remains tracked. Do not run `git rm --cached` or otherwise change its tracking state unless the user explicitly requests that policy change.
- Aim to finish with no leftover tracked modified or staged files in the understood commit scope. Report excluded or unresolved changes without absorbing them into a commit.

### 3. Logical Grouping

- Build groups by single responsibility and coherent intent.
- Keep unrelated concerns in separate commit groups.
- Prefer small, reviewable groups that preserve a buildable progression.

### 4. Commit Sequencing

- Order groups so foundational changes land first.
- Place follow-up refactors or cleanup after behavior-changing commits.
- Keep test updates with the commit they validate when possible.

### 5. Commit Message Drafting

- Draft one strong subject line per group in imperative mood.
- Prefix each commit subject with the current local execution date in ISO format for easier later searching and lightweight date-based filtering.
- Default subject format:
  - `[YYYY-MM-DD] Imperative subject`
- Use the actual current local date of execution, unless the user explicitly requests a different commit-message format.
- Add a short body when context, risk, or migration notes are needed.
- Ensure message text reflects what changed and why.

### 6. Execute Commits

- Skip this phase entirely for an explicit plan-only, review-only, or report-only request.
- Stage only the files that belong to the current group.
- Commit each group with the drafted message using non-interactive git commands.
- If `git add` reports that a path is ignored, do not use `git add -f` or any force-add switch as a shortcut.
- When an ignored file seems to be showing up in status anyway, diagnose whether it is already tracked with `git ls-files` and confirm the matching ignore rule with `git check-ignore -v --no-index` before changing anything.
- If an ignored file is already tracked, preserve its current tracking state. Commit an intended content change normally when it belongs to the requested scope, or mark it `needs-review` when the tracking policy is unclear.
- If an ignored file is untracked, leave it local-only and ignored. Do not treat it as a commit blocker unless the user explicitly asks to version it or to change ignore policy.
- If the user explicitly asks to stop tracking an ignored tracked file, show the exact target and resulting repository change before using `git rm --cached`.
- If a git command fails because of `.git/index.lock` or a likely concurrent git process, wait a few seconds and retry before treating it as a blocker.
- Preferred retry behavior for transient git locking:
  - wait about 2 to 5 seconds
  - retry up to 3 times
  - only report a blocker if the lock condition remains
- Do not delete `index.lock` blindly as part of the normal workflow.
- After each commit, verify the worktree and confirm the next group is still valid.
- If build or tests are part of the expected verification, run them at the appropriate point.
- Continue until all `commit-now` groups are committed or a real blocker is reached.
- Do not stop early just because some changes look secondary; if they are still modified and not explicitly excluded, they must be grouped and committed before finishing.

### 7. Final Reporting

- After committing all `commit-now` groups, stop with local commits by default.
- Push only when the user explicitly requests it. Use a normal push; never force-push or rewrite history.
- If an explicitly requested push fails, give a short diagnosis based on the visible git error, for example:
  - authentication/credential failure
  - remote rejected update
  - non-fast-forward because remote is ahead
  - missing upstream tracking
- Report which groups were committed and in what order.
- Report any deferred `hold` or `needs-review` items left in the worktree.
- State any verification commands that were run and their result.
- State that no push was attempted by default, or report the result of an explicitly requested push.
- If anything remains tracked and modified or staged at the end, explain exactly why it was not committed and whether that came from an explicit user instruction or a blocking constraint.
- If ignored local-only files remain, mention them only when they are relevant to explain an untrack action or a policy conflict.
- Prefer a short close-out when the run is clean: brief repository summary, compact list of created commits, publication result, and any truly relevant leftover note.
- Expand to the fuller report structure only when there are deferred items, blockers, non-obvious grouping decisions, risky files, sync problems, or a failed push.

## Output Format

Keep the final report concise by default. Compress file-by-file detail unless it is needed to explain grouping or a deferred risk.

For clean runs, prefer a compact format such as:

- short repository change summary
- compact execution results like `<hash> [YYYY-MM-DD] <subject>`
- push result
- deferred items only if any remain

Use the fuller structure below only when the run is not clean or when the user explicitly asks for more detail:

1. Repository change summary
2. Commit readiness table (`commit-now` / `hold` / `needs-review`)
3. Proposed commit groups with short rationale
4. Ordered commit plan
5. Draft commit messages when they add value
6. Execution result per committed group
7. Push result
8. Deferred items and assumptions

Prefer:
- short grouped file summaries over long file inventories
- one-line rationale per group
- compact execution results like `<hash> [YYYY-MM-DD] <subject>`
- compact publication result like `not pushed (default)` or `push failed: non-fast-forward`
- brief deferred-item notes
- omit sections such as draft messages or readiness tables when they add no signal in a clean run

Only expand beyond that when:
- a group boundary is non-obvious
- a risky file is being held back
- the run was blocked or partially complete
- the user explicitly asks for a detailed breakdown

## Execution Guardrails

- Do not use destructive commands such as `git reset --hard`, `git checkout --`, or force-push unless explicitly requested.
- Do not use `git add -f`, `git add --force`, or similar overrides to stage ignored files unless the user has explicitly confirmed that bypassing ignore rules is intended.
- Do not amend commits unless explicitly requested.
- Do not leave understood in-scope tracked modifications behind by default; preserve and report unexplained or excluded work.
- Do not force-add ignored untracked files as normal repository content.
- Do not change the tracking state of a tracked file merely because it is covered by an ignore rule.
- Do not commit files marked `hold` or `needs-review` just to empty the worktree.
- If unexpected changes appear during execution, stop and report the issue before continuing.
- Keep commits scoped, reviewable, and logically ordered.
- Treat transient git lock errors as retryable first, not as immediate hard failures.
- Do not fetch, pull, or push unless the user explicitly requests the corresponding remote action.
- If an explicitly requested push fails, report the likely cause from the git output and stop there.

## References

- `references/commit_grouping_playbook.md`
