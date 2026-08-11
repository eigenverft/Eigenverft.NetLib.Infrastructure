---
name: review-beauty-of-code
description: Perform a source-first, repository-bound beauty-of-code review focused on honest naming, local readability, consistency, quiet surfaces, proportional complexity, and readable tests or scripts. Return only concrete, worthwhile chat findings while keeping repository state unchanged.
---

# Review Beauty of Code

## Purpose

Use this softskill to review a real repository for code that can become clearer, calmer, more honest, and easier to hold in the mind.

Beauty means that the visible shape of the code matches the real shape of the problem.

A beautiful codebase helps a maintainer see:

- what a component owns
- what a name promises
- where data and control flow
- which rules matter
- which behavior is stable
- where variation is real
- how tests and scripts support the product

This is a source-first, read-only review softskill.

Return material findings in chat and keep source code, tests, scripts, configuration, documentation, dependencies, generated files, and Git state unchanged.

## Core Objective

Review the repository independently under this question:

> Which focused changes would make the current code more truthful, locally understandable, consistent, quiet, and proportional to the real problem?

Recommend improvements that strengthen the current product and fit its existing language, constraints, and behavior.

A review with zero material findings is complete and successful.

## Beauty Definition

Judge beauty through repository evidence.

Beauty in this skill consists of six connected qualities:

1. **Honesty** — names, types, APIs, and structure describe the behavior they actually own.
2. **Local clarity** — a reader can understand a type, function, or flow with a manageable amount of nearby context.
3. **Consistency** — similar responsibilities use the repository’s strongest established patterns, while real differences remain explicit.
4. **Quietness** — the visible surface contains meaningful concepts, useful boundaries, and purposeful compatibility points.
5. **Proportionality** — the implementation carries the amount of structure required by the current problem and its evidenced variation.
6. **Readable support code** — tests, fixtures, scripts, and tooling explain behavior and operating assumptions clearly.

Beauty favors clarity over brevity, purpose over symmetry, and repository fit over novelty.

Direct code can be elegant when it expresses a direct problem. Abstraction can be elegant when it removes repeated reasoning and gives a real concept a stable home.

## Truthfulness Principle

Prefer code whose visible shape matches its real responsibility.

Review relationships such as:

- a type name and the capability it truly provides
- a method name and its side effects, I/O, failure behavior, and state changes
- a property name and the authority or freshness of its value
- an abstraction and the real variation behind it
- a result model and the outcomes callers actually support
- a configuration option and its observable effect
- an interface and the boundary it protects
- a helper and the policy or transformation it contributes

Strong beauty improvements often come from an honest rename, a narrower contract, a clearer responsibility boundary, or removal of obsolete visible surface.

## Source-First Contract

Read the actual source before judging beauty.

Inspect, where relevant:

- implementation files
- call sites
- public and internal contracts
- neighboring types with similar responsibility
- composition roots and dependency wiring
- tests and fixtures
- scripts and automation
- configuration and defaults
- error paths and diagnostics
- comments that explain constraints
- compatibility and legacy surfaces
- framework, serialization, reflection, plugin, and deployment boundaries

Understand why a shape exists before recommending a change.

Treat file size, method count, inheritance depth, and naming style as signals that require repository context rather than conclusions by themselves.

## Repository Language Contract

Every repository develops a local language through names, patterns, error handling, testing style, composition, and ownership boundaries.

Use the repository’s strongest clear patterns as the primary reference point.

For each apparent inconsistency, determine:

- which pattern is dominant
- which pattern communicates intent most clearly
- whether the difference represents real behavior
- whether framework or compatibility constraints shape the area
- whether alignment would reduce everyday reading and maintenance cost

Recommend alignment where one pattern is clearly stronger and the current difference adds recurring friction.

Preserve explicit differences where they communicate meaningful behavior.

## Review Lenses

### 1. Naming and semantic honesty

Review names for precision, honesty, and consistency with actual use.

Strong candidates include:

- broad names such as `Manager`, `Handler`, `Processor`, `Helper`, or `Service` where a more concrete responsibility is visible
- boolean names whose true and false meanings require repeated interpretation
- one concept represented by several terms
- several concepts sharing one overloaded term
- abbreviations that obscure repository language
- method names that omit important effects
- legacy names whose behavior has changed
- public names whose scope is larger than their implementation

A naming finding qualifies when a clearer name materially improves reading, searchability, review, or safe change.

### 2. Local readability and mental load

Review how much context a maintainer must retain to understand one coherent action.

Useful signals include:

- methods containing several independent decisions
- control flow spread across many forwarding calls
- state read or changed far from the visible action
- important invariants distributed across unrelated helpers
- methods with several mode flags
- broad context objects carrying loosely related inputs
- repeated conversions that hide the primary data flow
- conditionals whose domain meaning can be named directly
- setup and cleanup paths that obscure the central operation

Prefer boundaries that let one local unit explain one coherent responsibility.

Extract code where the resulting name and boundary reduce mental load. Keep related steps together where sequence and shared state make the flow easier to understand as one unit.

### 3. Pattern consistency

Review equivalent work for a shared clear expression.

Examples include:

- result and error handling within the same layer
- configuration precedence across neighboring features
- dependency registration for equivalent components
- naming and lifecycle rules for similar services
- test setup for the same class of behavior
- scripts that perform the same operational task
- serialization, validation, and mapping conventions

Recommend consolidation where one pattern expresses the repository’s intent more clearly and the variation carries little semantic value.

### 4. Quiet and meaningful surface

Review whether the visible code surface represents active, useful concepts.

Useful candidates include:

- public members with obsolete responsibility
- enums, result branches, flags, aliases, or extension points whose supported behavior has narrowed
- forwarding wrappers whose policy can live at the real boundary
- compatibility helpers whose lifecycle is complete
- option types whose fields carry little meaningful behavior
- comments whose explanation has drifted from the code
- interfaces that can express a clearer boundary or contract
- factories whose construction policy can be named more directly
- convenience APIs that represent the same operation in several forms
- tests that preserve obsolete surface

Account for reflection, serialization, plugins, public APIs, scripts, external consumers, and compatibility commitments while assessing the surface.

A quiet codebase keeps meaningful choices visible and lets completed history recede.

### 5. Proportional complexity

Compare implementation weight with the real current problem.

Review structures such as:

- general mechanisms serving one fixed behavior
- layers that carry values unchanged
- extensibility backed by limited current variation
- configurable behavior with one operationally supported mode
- state machinery relative to the number and difficulty of states
- policy objects relative to the decisions that genuinely vary
- abstraction trees around a local workflow
- repeated defensive machinery relative to platform guarantees

Evaluate correctness, compatibility, testability, operational behavior, and evidenced near-term variation.

Recommend the smallest coherent structure that fully expresses those needs.

### 6. Tests and scripts as readable companions

Treat tests and scripts as part of the repository’s explanation.

Review:

- whether test setup keeps the behavior under test visible
- whether fixture builders reduce repeated noise while preserving meaningful values
- whether assertions communicate the expected outcome
- whether test names state behavior and conditions
- whether integration and focused tests express complementary contracts
- whether repeated setup makes important differences easy to spot
- whether scripts expose phases, inputs, environment assumptions, and failure points
- whether test-only APIs strengthen or weaken product clarity
- whether support code matches the current product surface

Recommend changes that make intent, behavior, and failure conditions easier to read.

Choose the test or script structure that best explains the behavior it protects.

## Complexity Reading Model

Classify observed complexity as:

- **necessary complexity** — required by the problem, platform, compatibility, or operation
- **accidental complexity** — introduced by unclear ownership, duplication, unnecessary generality, or code shape
- **visible complexity** — explicit code that shows an important difficult rule
- **hidden complexity** — indirection that makes an important rule harder to find or understand

Visible necessary complexity can be highly elegant because it tells the truth.

Beauty work focuses on reducing accidental and hidden complexity while preserving the information carried by necessary complexity.

## Architecture Fit Contract

Choose structures that fit the repository’s actual behavior.

Use layers, mediators, repositories, factories, strategies, events, plugins, domain wrappers, builders, and generalized mechanisms when they give real variation or responsibility a clearer stable home.

Use direct calls, concrete types, local functions, and explicit flows when the problem is direct and locally owned.

Let current product behavior, repository language, compatibility, and operational needs determine the shape.

Prefer the smallest coherent direction that improves understanding and keeps future change safe.

## Behavior-Preservation Contract

Beauty work normally preserves externally observable behavior.

For each finding, identify the relevant stable surface, such as:

- public API shape
- persisted data
- configuration keys and precedence
- command behavior and exit codes
- UI behavior
- error semantics
- timing and ordering guarantees
- supported scripts and automation
- test-visible contracts

When the review reveals a probable behavior defect, label that fact clearly and separate the product decision from the readability improvement.

## Beauty Debt Eligibility Gate

A concern becomes an output finding when all of these are true:

1. repository evidence shows a material clarity, honesty, consistency, surface, or proportionality issue
2. the issue affects understanding, safe change, debugging, reviewability, or maintenance
3. a concrete improvement direction is available
4. current behavior and compatibility boundaries are identifiable
5. the expected clarity benefit exceeds migration, churn, and compatibility cost
6. the change is worthwhile for the current repository state
7. confidence is high or medium

Apply this practical test:

> Would an experienced maintainer reasonably accept a focused change because the code becomes easier to understand or safer to change?

Output the finding when the answer is yes.

Keep formatting preferences, harmless differences, speculative cleanup, optional micro-improvements, and low-confidence observations inside the reviewer’s internal reasoning.

## Finding Selectivity

Prefer a small number of strong findings over an inventory of every imperfection.

Group symptoms that share one cause and one improvement direction.

Size each finding as one coherent review and implementation decision.

Use zero findings when the repository evidence supports the current shape.

When zero material findings qualify, return only:

`Beauty review complete. Material findings: 0.`

## Required Output Shape

Return only material findings.

For each finding include:

- **Location** — repository-relative files, symbols, tests, or scripts
- **Beauty debt** — the clarity, honesty, consistency, surface, or proportionality issue
- **Reader cost** — what a maintainer must remember, infer, trace, or verify repeatedly
- **Concrete direction** — the smallest useful change that improves truthfulness or clarity
- **Why this direction** — why it fits the repository and current behavior
- **Preserve** — behavior, contracts, and constraints that remain stable
- **Scope boundary** — the focused boundary of the improvement
- **Confidence** — high or medium

Order multiple findings by expected improvement to everyday understanding and safe change.

Keep the response focused on actionable findings. Use repository paths, symbols, behavior, and concrete flows as evidence.

## Output Style

Use simple, direct, technically concrete language.

Name real files, symbols, flows, tests, and scripts.

Explain the reader cost and how the proposed direction reduces it.

Prefer precise statements such as:

- the method name hides that it writes state and can fail
- these wrappers carry the same values and the same responsibility
- neighboring features encode the same result through different contracts
- this public option leaves observable behavior unchanged
- the test setup hides the value that changes the behavior

Use “beauty” as the review theme and technical reasoning as the basis for every finding.

## Repository-State Contract

Operate through read-only repository inspection and narrowly necessary non-mutating commands.

Keep product source, tests, scripts, configuration, documentation, dependencies, generated files, handoffs, reports, plans, issues, and Git state unchanged.

Use external research only when a current platform or framework fact is essential for an accurate repository recommendation.

## Quality Bar

A successful review identifies focused changes that make code calmer, more truthful, and easier to understand.

It respects necessary complexity, repository language, behavior, and compatibility.

It favors the smallest coherent improvement that removes meaningful beauty debt.

A zero-finding result is a complete result.

## Typical Invocation Phrases

- `Use $review-beauty-of-code to review this repository for material beauty debt.`
- `Review this codebase for honest naming, local readability, quiet surfaces, and proportional complexity.`
- `Find focused changes that make the code easier to understand and safer to change.`
- `Perform an independent beauty-of-code review and return material chat findings.`
- `Review tests and scripts as part of the repository’s readable explanation.`
