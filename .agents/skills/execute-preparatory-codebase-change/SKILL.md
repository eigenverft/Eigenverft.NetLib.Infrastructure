---
name: execute-preparatory-codebase-change
description: Use when asked to prepare a codebase for a later task, prompt, implementation, feature, refactor, fix, or investigation without performing the main requested work yet. The skill analyzes the current codebase, identifies safe preparatory changes, and performs only unambiguous preparation. If there are multiple reasonable preparation options, output the options first and make no changes until the user locks one in.
---

# Execute Preparatory Codebase Change

You prepare the repository for a later task without doing the main task itself.

The user may attach this skill to a larger prompt. Treat that prompt as the target task. Your job is to make the codebase easier, safer, and better aligned for the later execution of that target task.

Do not complete the target task.

## Core Behavior

Analyze the target task and the current codebase.

Then decide whether there is:

1. Exactly one clearly appropriate preparatory change.
2. Multiple reasonable preparatory options.
3. No safe preparatory change.

Act according to the decision rules below.

## Lock-In Rule

If there are multiple reasonable preparation options, stop before changing files.

Output the options first.

Do not edit files, refactor code, add tests, or modify documentation until the user chooses one option.

The response should clearly say that changes were skipped because the preparation direction needs to be locked in.

## Allowed Work

You may:

- Inspect the repository structure.
- Analyze relevant files, modules, services, routes, components, tests, configs, and scripts.
- Identify existing codebase patterns.
- Check current behavior.
- Run targeted tests, type checks, linters, builds, or static analysis.
- Identify risks, blockers, unclear areas, and likely future touchpoints.
- Make one safe preparatory change when the correct change is obvious.
- Add or improve documentation for existing behavior.
- Add characterization tests for existing behavior.
- Remove unused imports or dead code in the affected area.
- Improve naming, formatting, or structure when behavior is unchanged.
- Extract existing duplicated logic when the refactor is clearly behavior-preserving and useful for the target task.
- Prepare fixtures, helpers, or organization only when they do not implement the target task.

## Disallowed Work

Do not:

- Implement the target task.
- Add the requested feature.
- Fix the requested bug.
- Add future behavior.
- Add new user-facing UI for the target task.
- Add new API behavior for the target task.
- Add database migrations for the target task.
- Add business logic for the target task.
- Add placeholders that imply future behavior.
- Add TODO-heavy scaffolding.
- Add feature flags for the target task.
- Write tests that assert the future behavior unless the user explicitly asks for test-first preparation.
- Make broad unrelated refactors.
- Choose between multiple viable preparation strategies without user lock-in.

## Workflow

### 1. Understand the Target Task

Read the user's prompt and identify what later work is being prepared for.

Summarize:

- The target task.
- What preparation means in this context.
- What is out of scope.
- Any assumptions.

Use neutral wording. Do not assume the work is an issue, ticket, or user story.

### 2. Inspect the Codebase

Before changing anything, inspect the repository.

Look for:

- Project structure.
- Frameworks and runtime.
- Existing architecture.
- Similar implementations.
- Naming conventions.
- Test conventions.
- Build, lint, and type-check commands.
- Data flow.
- API boundaries.
- UI/component structure.
- Service/model/helper patterns.
- Configuration patterns.

Prefer existing codebase conventions over introducing new patterns.

### 3. Identify Preparatory Opportunities

List possible preparation moves.

Examples:

- Add characterization coverage for current behavior.
- Extract duplicated existing logic.
- Clarify confusing current behavior in docs.
- Simplify an existing helper before later changes.
- Normalize naming around the affected area.
- Move existing code to match surrounding structure.
- Remove dead code that would confuse later work.
- Add missing test fixtures for current behavior.
- Document relevant architectural constraints.

Each option should include:

- What would change.
- Why it helps the later target task.
- Whether behavior should remain unchanged.
- Risk level.
- Files likely affected.

### 4. Decide Whether to Change Files

Apply this rule:

- If exactly one safe preparatory change is clearly best, perform it.
- If multiple reasonable options exist, do not change files.
- If no safe preparatory change exists, do not change files.

When multiple options exist, output them first and wait for user lock-in.

### 5. Perform Only Safe Preparatory Changes

A preparatory change is allowed only if all are true:

- It does not implement the target task.
- It does not partially complete the target task.
- It does not change user-facing behavior.
- It is small and reviewable.
- It follows existing codebase style.
- It reduces future implementation risk.
- It can be clearly explained as preparation.

If editing code, preserve behavior.

Prefer small, local changes over broad restructuring.

### 6. Verify

After making a change, run the smallest useful verification.

Examples:

- Targeted tests.
- Type check.
- Lint.
- Build.
- Existing test file related to the changed area.

If verification cannot be run, explain why.

## Output Format

### If Multiple Options Exist

Do not change files.

Respond with this structure:

Multiple preparation directions are reasonable, so I did not make changes yet.

Option A: [short name]
- Change:
- Why it helps:
- Behavior impact:
- Risk:
- Files likely affected:

Option B: [short name]
- Change:
- Why it helps:
- Behavior impact:
- Risk:
- Files likely affected:

Recommended lock-in:
[state which option seems safest or most useful, without changing files]

### If One Preparatory Change Was Made

Respond with this structure:

Preparatory change completed.

Target task:
[brief summary]

What changed:
[brief summary]

Why this is preparation only:
[explain why the target task was not implemented]

Files changed:
[list files]

Verification:
[commands run and results]

Suggested next implementation step:
[next logical step for the main task]

### If No Safe Preparatory Change Exists

Respond with this structure:

No safe preparatory change was made.

Target task:
[brief summary]

Reason:
[why no change was safe or useful]

Current findings:
[relevant findings]

Suggested next step:
[next useful step]

## Decision Examples

### Safe

The target task will likely touch payment validation.

Allowed preparation:

- Add characterization tests for the current payment validation behavior.
- Extract duplicated existing validation formatting logic.
- Document the existing validation flow.

Not allowed:

- Add the new payment rule requested by the target task.

### Safe

The target task will later add a new UI state.

Allowed preparation:

- Identify the existing state-rendering pattern.
- Clean up unused imports in the component.
- Add tests for current state rendering.

Not allowed:

- Add the new UI state.

### Multiple Options

The target task could reasonably be prepared by any of these:

- Refactoring the service layer.
- Adding characterization tests.
- Updating documentation.

If more than one is reasonable, output options and stop.

Do not pick one unless the user explicitly chose it or the codebase makes one clearly best.

## Final Boundary

Your role is to make the repository more ready for later work.

Do useful preparation, but never do the main task.

When there is a choice, pause and ask for lock-in before making changes.
