---
name: review-architecture-responsibility-boundaries
description: Review implementation architecture for evidence-based responsibility, ownership, separation-of-concerns, and lifecycle-boundary concerns. Use when the user explicitly requests an architecture review focused on where responsibilities should remain together or be separated. Inspect actual source and behavior, report only concrete concerns, and keep repository state unchanged.
---

# Review Architecture Responsibility Boundaries

## Overview

Review the architecture with primary focus on responsibility boundaries, separation of concerns, ownership, and lifecycle design. Determine whether responsibilities are separated along meaningful boundaries while logic that protects one shared invariant remains together.

This is a read-only review skill. Inspect the selected implementation and return the required report in chat. Do not edit files, change dependencies, modify Git state, or perform a refactoring.

## Activation And Scope

Use this skill only when the user explicitly requests an architecture review focused on responsibility, ownership, separation of concerns, or lifecycle boundaries.

Resolve the requested project, subsystem, diff, or repository scope before reviewing. Read the actual implementation first. Consult tests, configuration, documentation, and history only where they materially establish an architectural contract, lifecycle, dependency, or behavior. State important exclusions instead of silently implying complete coverage.

Do not assume that the current decomposition is either too large or too small. Do not invent missing abstractions, responsibilities, or architectural problems merely because they are common in similar systems.

Only report a concern when it is supported by concrete code structure, state ownership, control flow, lifecycle behavior, or dependency relationships in the implementation. Zero findings is a valid result.

## Review Principles

The main question is:

> Are responsibilities separated along meaningful boundaries, while logic that protects one shared invariant remains together?

Useful criteria include:

- separation of concerns
- clear ownership boundaries
- layered responsibilities
- composition instead of unnecessary monolithic orchestration
- distinct lifecycle ownership

A class does not need to perform literally only one operation. It should instead have a clear responsibility and a coherent reason to change.

As general heuristics:

> Separate components when they have meaningfully different lifecycles, failure semantics, ownership, consumers, or reasons to change.

> Keep components together when they jointly implement or protect one invariant and separating them would mainly create forwarding layers or distributed state.

When reviewing, look for concrete evidence in either direction.

Examples of potentially meaningful separation:

- independently owned state
- independent lifecycle or disposal
- different failure semantics
- different consumers
- reusable lower-level functionality
- persistence concerns separated from runtime behavior
- observation separated from mutation
- orchestration separated from underlying technical primitives

Examples of potentially unnecessary separation:

- classes that only forward calls without adding a real boundary
- state ownership spread across several components without a clear owner
- abstractions that always change together and cannot meaningfully be used independently
- multiple layers representing the same concept under different names
- coordination logic fragmented so that one invariant must be understood across many unrelated classes

Also check the opposite problem: a component may contain several responsibilities that only happen to be used together today but have different lifecycle, failure, or ownership requirements.

Do not recommend a refactoring solely because a different architecture is possible.

Do not recommend introducing patterns, interfaces, managers, coordinators, factories, state machines, event buses, or other abstractions unless the current implementation provides concrete evidence that such a boundary is needed.

Do not treat file count, class count, or line count as evidence by itself.

For every architectural concern, explain:

1. the concrete code or behavior that creates the concern;
2. which responsibilities or ownership boundaries are involved;
3. why the current boundary causes an actual maintenance, correctness, lifecycle, or usability problem; and
4. whether changing it is important or merely optional.

If the current separation is appropriate, say so explicitly. If no meaningful responsibility-boundary issue exists, return no findings without inventing work.

## Required Review Output

### Review Context

Begin the report with a compact review context.

| Item | Value |
| --- | --- |
| Project / subsystem | What was reviewed |
| Branch | Current branch, if available |
| Revision | Commit / HEAD reviewed, if available |
| Comparison base | Base branch or revision for a diff review; otherwise `Current state` |
| Review mode | For example `Read-only architecture review` |
| Scope exclusions | Important areas intentionally not reviewed in depth, or `None` |

Keep this factual and short. Do not list every reviewed file, command, tool invocation, or repository path unless needed to understand the review. If branch or revision information is unavailable, state `Unknown` rather than guessing.

Then add one or two sentences describing the actual review scope in plain language.

Example:

> The review focused on the runtime architecture of the configuration subsystem, especially responsibility boundaries, state ownership, and lifecycle interactions. Tests were consulted only where necessary to confirm an architectural contract; detailed test-quality review was outside scope.

### 0. Architecture Verdict

Start the substantive review with the overall architecture before reporting individual findings.

| Question | Assessment |
| --- | --- |
| Are the responsibility boundaries fundamentally appropriate? | Yes / Mostly / No |
| Is significant restructuring recommended? | Yes / No |
| Is the current layering understandable and maintainable? | Yes / Mostly / No |
| Are ownership and lifecycle boundaries clear? | Yes / Mostly / No |

Then provide a short explanation.

If significant restructuring is recommended, describe that first. Do not spend substantial effort on local findings that would become irrelevant under the recommended restructuring.

The architecture verdict takes precedence over individual findings. If the current architecture is fundamentally sound, say so clearly before discussing local defects.

### 1. Responsibility Map

Briefly describe the major components and the responsibility each one owns.

| Component | Primary responsibility | Lifecycle / state owned | Assessment |
| --- | --- | --- | --- |
| ... | ... | ... | Appropriate / Questionable / Unclear |

Keep this section concise. Its purpose is to demonstrate that the reviewer understood the actual architecture before criticizing it.

Do not list every class merely because it exists. Focus on components that represent meaningful responsibility or lifecycle boundaries.

### 2. Findings

Only report findings supported by concrete implementation evidence.

| ID | Severity | Area | Finding | Concrete impact | Architecture-dependent |
| --- | --- | --- | --- | --- | --- |
| F1 | Important | ... | ... | ... | Yes / No |

Allowed severity levels:

- **Blocker** - architecture or correctness issue that should prevent merge or release.
- **Important** - real issue that should normally be corrected before merge or release.
- **Moderate** - real issue worth correcting, but not necessarily release-blocking.
- **Minor** - small concrete improvement; omit these unless they meaningfully improve the reviewed responsibility boundaries.

Do not create findings solely to populate every severity level. Zero findings is a valid result.

For each finding, explain:

#### F1 - Short Descriptive Title

**Evidence**

Describe the concrete implementation behavior, ownership relationship, state transition, dependency, or lifecycle sequence that demonstrates the issue. Reference specific classes, methods, fields, or control flow where useful.

**Responsibility boundary**

Explain which responsibilities or ownership boundaries are involved. State whether the problem is caused by:

- responsibilities that should be separated but are mixed;
- responsibilities that should remain together but are fragmented;
- unclear ownership;
- incomplete lifecycle ownership;
- inappropriate layer coupling; or
- another concrete boundary problem.

**Concrete impact**

Explain the real consequence, such as:

- correctness failure
- invalid runtime state
- lifecycle gap
- inconsistent ownership
- difficult independent evolution
- consumer confusion
- inability to replace one concern independently
- unnecessary cross-layer coupling

Do not use hypothetical impact without a concrete path from the current implementation.

**Smallest reasonable correction**

Recommend the smallest change that repairs the responsibility or lifecycle boundary. Do not propose broader restructuring when a local correction is sufficient.

If the finding depends on keeping the current architecture, explicitly mark it as architecture-dependent.

### 3. Positive Architectural Observations

Briefly identify responsibility boundaries that are particularly well chosen. Only include observations that are useful for future maintenance, such as:

- responsibilities that should remain together because they protect one invariant;
- components that should remain separate because they have distinct lifecycles;
- abstractions that may look small but own a meaningful lifecycle boundary;
- lower-level primitives that are reusable independently of higher-level orchestration; or
- clean separation between persistence, mutation, observation, and runtime control.

For every positive observation, briefly explain why the boundary is meaningful. This section should help prevent a later reviewer from accidentally undoing intentional architecture. Do not add generic praise.

### 4. Final Architecture Assessment

Classify the overall architecture using exactly one of:

- **Appropriately separated**
- **Mostly appropriately separated**
- **Under-separated**
- **Over-separated**
- **Mixed**
- **Insufficient evidence**

Base this classification only on evidence present in the reviewed implementation and briefly explain it. The number of classes, interfaces, files, or lines of code is not evidence by itself.

### 5. Final Decision

Finish with exactly one overall decision:

- **Architecture sound - no restructuring needed**
- **Architecture sound - local fixes recommended**
- **Architecture mostly sound - targeted boundary changes recommended**
- **Architecture requires restructuring before local fixes**
- **Insufficient evidence**

Then state:

**Merge / release recommendation:**  
`Ready` / `Ready after findings F...` / `Not ready`

Keep this final section short.

If significant restructuring is recommended, explain which findings would become invalid or need to be re-evaluated after that restructuring.

If no restructuring is needed, explicitly distinguish local lifecycle or correctness fixes from architectural redesign.

Do not present a local defect as evidence that the whole architecture should be redesigned unless the implementation demonstrates that the defect originates from the responsibility boundaries themselves.
