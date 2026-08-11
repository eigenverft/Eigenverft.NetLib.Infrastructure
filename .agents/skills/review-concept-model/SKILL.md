---
name: review-concept-model
description: Source-first review guidance for finding weak concepts, flawed underlying models, and costly implementation ideas in an app, then turning them into realistic alternatives and concrete improvement work.
---

# Review Concept Model

## Purpose

Use this softskill to review a real app codebase for conceptual problems, not just messy code.

The goal is to identify the ideas behind the implementation that create unnecessary complexity, weak ownership, awkward flows, fragile extensions, or a model that does not really fit the app.

This softskill should:

1. read the source
2. identify conceptual pain points
3. group them by underlying idea
4. highlight the worst concept cluster
5. judge whether the idea itself is sound
6. show realistic alternatives
7. turn the result into concrete improvement tasks

## Core Goal

Find the concepts, models, and structural assumptions that make the app harder than it should be to:

- understand
- change
- extend
- debug
- reason about
- maintain

Then turn that into useful next actions.

## Use This Softskill When

Use this softskill when the user wants help with:

- finding weak implementation ideas
- reviewing whether the current model makes sense
- identifying concept-level pain points
- spotting where the code fights the product
- understanding why the app feels harder than it should
- getting alternatives, not just criticism
- turning architectural pain into tasks, issues, or working points

## Do Not Use This Softskill When

Do not use this softskill for:

- isolated syntax fixes
- style-only cleanup
- one-off bug fixes
- performance-only tuning
- security-only review
- purely local refactors with no conceptual relevance

## Source First

Always base the review on the actual source.

Useful sources include:

- code files
- repo structure
- feature boundaries
- shared modules
- data models
- state handling
- API contracts
- async flows
- routing
- adapters, wrappers, and mapping layers
- tests
- configs
- docs and comments that reveal intended behavior

Do not give abstract advice unless it clearly maps back to the source.

## What To Look For

Look for signs that the app is built on a concept that creates more cost than value.

Examples:

- an ownership model that is unclear or split too widely
- a data model that does not fit the actual feature behavior
- too many layers for too little responsibility
- flows that are much harder than the problem requires
- repeated adapters or mapping chains caused by a weak shared model
- abstractions that organize files but do not reduce complexity
- a shared layer that centralizes code but weakens meaning
- state transitions spread across too many places
- wrappers that exist because the real boundary is unclear
- implementation patterns that technically work but do not really make sense for the app

## Review Mindset

Do not stop at:

- duplicated code
- ugly files
- too many helpers
- too many wrappers

Ask instead:

- what idea caused this shape
- is that idea actually useful here
- is this pain local or systemic
- is the implementation rough, or is the concept itself weak
- what simpler idea would fit better
- what should become a real improvement task

Be willing to say when:

- the abstraction is not worth its cost
- the model is wrong for the feature
- the structure solves the wrong problem
- the app is paying for unnecessary indirection
- a simpler concept would work better

## What Good Looks Like

Prefer a codebase where:

- feature ownership is clear
- boundaries reflect real responsibilities
- data shapes fit the product model
- flows are traceable and not over-layered
- shared code is shared for a real reason
- abstractions reduce complexity instead of moving it around
- the main implementation ideas are easy to explain
- changes do not require crossing many unrelated modules

## Finding Eligibility And No-Finding Contract

Do not invent conceptual pain to satisfy the output shape.

A concept concern qualifies only when repository evidence shows a material recurring cost, the underlying idea can be identified, and a realistic improvement direction would be worthwhile now.

When no concept concern qualifies, return only:

`Concept model review complete. Material findings: 0.`

In that case, omit the concept clusters, alternatives, task list, and all other required-output sections below.

## Repository-State Contract

This is a read-only review skill. Keep source, tests, configuration, documentation, dependencies, generated files, handoffs, and Git state unchanged. Return findings in chat; do not implement the recommended tasks.

## Required Output Style

When at least one material concept concern qualifies, the output must be practical, direct, source-based, and action-oriented.

It should answer:

- which concepts are creating pain
- which one is the worst
- whether the idea itself makes sense
- what better alternatives exist
- what should become concrete improvement work

## Required Output Shape

Use this shape only when at least one material concept concern qualifies.

### 1. Concept pain point list

List the main concept-level pain points seen in the source.

For each point, include:

- where it appears
- what the underlying idea seems to be
- why that idea creates friction

### 2. Concept clusters

Group the pain points into broader concept clusters.

For each cluster, include:

- what idea connects them
- which source areas feed into it
- why it creates recurring friction
- whether it is mostly local or systemic

### 3. Highest pain-potential concept cluster

Pick exactly one cluster as the highest pain-potential group.

Explain:

- why it is the most costly
- what downstream problems it causes
- why it should be challenged first
- whether it looks like an implementation problem or a flawed underlying model

### 4. Concept judgment

This is mandatory.

For the highest pain-potential cluster, say clearly whether:

- the implementation is rough but the concept is fine
- the implementation is too complicated for a reasonable concept
- the underlying concept itself does not make much sense for this app

Do not soften this unnecessarily.

### 5. Realistic alternatives

This is mandatory.

Provide 2 or 3 realistic alternative approaches.

For each alternative, include:

- what current idea it replaces
- why it may fit the app better
- what tradeoff comes with it

Prefer alternatives that are clear, specific, and plausible in the current codebase.

### 6. Improvement tasks

This is mandatory.

Provide at least 5 concrete tasks, issues, or working points.

Each task should say:

- what should be challenged, simplified, replaced, aligned, collapsed, or rerouted
- why it matters
- what pain it reduces

Tasks must be specific enough to become tickets.

### 7. What not to change yet

Call out anything that may look messy but is not a core concept problem right now.

### 8. Watchouts

Mention only relevant risks, such as:

- fixing symptoms while keeping the weak concept
- replacing one vague abstraction with another
- spreading effort across many local issues instead of the worst cluster
- overcorrecting into a new model that does not fit the app either
- forcing a large redesign where a smaller conceptual correction would do

## Prioritization Rule

Rank concept problems higher when they:

- appear across several parts of the app
- create repeated workarounds
- force awkward code shapes
- slow down feature work
- make debugging harder
- weaken ownership and local reasoning
- reflect a bad core model instead of a local mistake

## Strong Rules

### Be source-based

Every important claim should map to visible code or structure.

### Focus on concepts, not only symptoms

Do not stop at messy files or duplicated helpers.
Explain the underlying idea that keeps producing them.

### Challenge weak ideas directly

If the concept itself is weak, say so plainly.

### Show alternatives

Do not only criticize the current approach.
Show realistic other ways the app could be structured.

### Produce usable outcomes

The result should clearly suggest tasks, issues, or improvement work.

### Prefer root causes

Focus on the patterns generating repeated pain, not only the places where pain becomes visible.

## Minimum Concreteness Rule

This is mandatory only when at least one material concept concern qualifies.

- List multiple concrete concept pain points.
- Group them into meaningful concept clusters.
- Pick exactly one highest pain-potential cluster.
- Clearly judge whether the underlying idea makes sense.
- Provide 2 or 3 realistic alternatives.
- Provide at least 5 concrete improvement tasks.
- Use concrete verbs like:
  - inspect
  - trace
  - challenge
  - simplify
  - collapse
  - replace
  - isolate
  - reroute
  - centralize
  - separate
  - align

Avoid vague advice like:

- improve architecture
- reduce complexity
- rethink this part
- make it cleaner

unless followed by a concrete source-level action.

## Good Task Examples

- `Trace the repeated adapter chain around the same entity and test whether one shared normalized shape can replace it.`
- `Challenge the split between controller, service, and wrapper layers where one feature flow is currently spread across all three.`
- `Move state transition ownership closer to the feature boundary instead of coordinating it through unrelated shared modules.`
- `Replace the generic abstraction that hides different behaviors behind one interface with explicit feature-owned paths.`
- `Inspect whether the shared model is forcing repeated field translation and narrow it to the data that is actually common.`

## Bad Advice Examples

Do not produce advice like:

- `Improve the architecture.`
- `Refactor this area.`
- `Reduce complexity.`
- `Make the code more maintainable.`
- `Rethink the model.`

Those are incomplete until translated into concrete source-based findings and tasks.

## Preferred Tone

Be practical, skeptical, and useful.

The response should feel like:

- this concept is causing pain
- this idea is weak or overcomplicated
- here are better alternatives
- here is what should become actual work

## Typical Invocation Phrases

- `[$review-concept-model] review this app for weak concepts and bad underlying ideas`
- `use the concept model review softskill`
- `read this codebase and tell me which implementation ideas do not make much sense`
- `show me the worst concept-level pain cluster in this app`
- `review this app and suggest better alternatives to the current approach`
- `turn the main concept pain points into concrete improvement work`
