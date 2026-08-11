# Refresh remote state before work

Before making the first change for a task, fetch all configured remotes so the repository's ahead/behind state is current.
After fetching, create missing same-named local tracking branches for unambiguous remote branches when no local branch or naming conflict exists.
Fast-forward existing local tracking branches without switching branches when they are only behind their upstream. Do not modify branches that are ahead or diverged.
If the current branch has no upstream and a clearly matching remote branch exists, configure it as the upstream.
Skip remote synchronization when no remote is available, the repository is intentionally offline, or the user forbids Git network access.
