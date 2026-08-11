# Normalize the mainline branch to main

The canonical mainline branch is `main`. At the start of a new chat session, inspect the local repository, every configured remote, and repository-hosting metadata for a remaining `master` mainline. When a safe, unambiguous migration is possible, normalize it to `main`.

## Preserve local work

Never lose, overwrite, discard, or hide staged, unstaged, untracked, or ignored local work while performing this instruction.

Before changing branches or references:

1. Record the current branch, commit, upstream, worktree status, staged changes, unstaged changes, and untracked files.
2. Fetch configured remotes so branch comparisons are current.
3. Do not use `git reset --hard`, `git clean`, `git checkout --`, force-push, rebase, or another history-rewriting operation.
4. Prefer a temporary Git worktree for branch comparison, merging, and remote preparation when the primary worktree is dirty.
5. Use a temporary stash only when a worktree cannot safely solve the problem. Include untracked files, apply rather than pop it, verify the original local work was restored, and only then remove the temporary stash. Never drop a stash after a conflicted or incomplete restore.
6. If ignored files, local changes, branch locks, conflicts, missing permissions, or ambiguous history make preservation uncertain, stop the migration and report the exact blocker. Leave all existing work and branches intact.

## Determine the branch state

Inspect at least:

- local `main` and `master` branches;
- remote `main` and `master` branches on every configured remote;
- upstream tracking relationships;
- the remote default branch such as `origin/HEAD`;
- unique commits on each branch in both directions;
- open pull requests whose base branch is `master`, when repository-hosting access is available;
- branch protections, repository rulesets, Pages configuration, and other hosting settings that explicitly target `master`, when they can be inspected safely.

Do not assume that branches with similar names or matching latest files have matching history. Compare their commit ancestry.

## Normalize safely

Apply the smallest safe case that matches the repository:

- If only `master` exists, rename or create it as `main` while preserving its complete history and current local work.
- If `main` already contains all commits from `master`, use `main` as the canonical branch and treat `master` as obsolete.
- If `master` contains commits missing from `main`, bring them into `main` with a normal history-preserving merge. Do not squash, rebase, cherry-pick a whole branch, or rewrite published history merely to perform the rename.
- If both branches contain unique commits, merge `master` into `main` only when conflicts are absent or small and unambiguous. Stop when a conflict affects behavior or the correct result is unclear.
- Push `main` normally when remote publication is required for the migration and the current instruction has repository write authority.
- Change the hosted repository default branch from `master` to `main` when access is available and the new `main` branch is fully published and verified.
- Retarget open pull requests from `master` to `main` when this is supported and unambiguous.
- Migrate clearly equivalent branch protections, rulesets, Pages branch settings, and automation targets from `master` to `main` before removing the old remote branch.

Delete local or remote `master` only after all of the following are verified:

1. `main` contains every commit that must be preserved from `master`.
2. `main` is published to the intended remote.
3. The remote default branch is `main`.
4. Relevant pull requests and hosting settings no longer depend on `master`.
5. No required tracked configuration still references `master` as the mainline branch.
6. The original uncommitted local work is still present and verifiably preserved.

If any condition is not satisfied, keep `master` and report what remains.

## Find and update branch self-references

Search tracked repository content case-sensitively and contextually for branch references, including:

- CI and workflow branch filters;
- deployment, publish, release, bootstrap, maintenance, and synchronization scripts;
- Git submodule branch configuration;
- repository badges and links containing `/master`, `/blob/master/`, `/tree/master/`, or `raw.githubusercontent.com/.../master/`;
- documentation and contribution instructions;
- package, container, Pages, coverage, documentation, and release configuration;
- Dependabot or Renovate target branches;
- code that invokes Git commands or names the expected default branch;
- tests, fixtures, templates, examples, and generated configuration that encode the mainline name.

Change a match only when it clearly identifies the repository mainline branch. Do not replace unrelated uses of the ordinary word `master`, protocol terminology, domain concepts, credentials, data fields, device roles, test text, or references to external repositories that genuinely still use `master`.

After updates, search again and classify every remaining `master` occurrence as intentional, historical, external, or unresolved. Do not claim normalization is complete while an unexplained mainline reference remains.

## Verification

Complete this instruction only after verifying:

- the active canonical branch is `main`;
- its upstream is the intended remote `main` branch;
- local and remote ahead/behind state is understood;
- `main` preserves all required `master` history;
- the repository host uses `main` as its default branch when hosting access exists;
- no required automation or configuration still targets `master`;
- all pre-existing local work remains present;
- no temporary worktree or migration stash was left behind unintentionally.

Do not commit unrelated changes. Do not create a migration commit solely for branch-name updates unless tracked references actually changed. Report completed actions, preserved local work, remaining intentional `master` references, and any blocker that prevented full normalization.
