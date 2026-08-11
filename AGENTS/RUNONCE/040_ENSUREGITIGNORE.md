# Ensure root .gitignore coverage

Ensure the repository root has a `.gitignore`. Create an empty one only when none exists. Never replace or wipe an existing `.gitignore`.

## Required ignore patterns

Ensure every pattern from this block is present in the root `.gitignore`. If the block is missing, append it once at the end, with a blank line before it when the file already has content. Do not duplicate patterns that are already present, even when surrounding comments differ.

```gitignore
# Sensitive file name patterns
*[Ss][Ee][Cc][Rr][Ee][Tt]*

# Local agent instruction files
AGENTS.md
AGENTS/
.agents/**/*
```

## Project build and publish artifacts

Ensure ordinary local build and publish artifact directories are ignored. At minimum cover:

- `bin` (for example `[Bb]in/` or `**/[Bb]in/*`)
- `obj` (for example `[Oo]bj/`)
- `.vs/`
- `[Dd]ebug/` and `[Rr]elease/` in .NET / Visual Studio repositories
- root `artifacts/` when the repository produces publish or packaging output

Add only missing patterns. Keep any existing broader gitignore template intact.

## Completion

Complete only after verifying the required patterns are present. Do not commit `.gitignore` changes unless the user asks to commit.
