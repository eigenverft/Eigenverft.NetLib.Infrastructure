---
name: review-independent-change-boundaries
description: Review a repository for team-scalable change boundaries: clear responsibility ownership, local implementation freedom, stable contracts, low shared-hotspot pressure, independent verification, and incremental paths that let developer groups work in parallel. Use qualitative source-based scenario analysis rather than invented precision. Return only material chat findings and keep repository state unchanged.
---

# Review Independent Change Boundaries

## Purpose

Use this softskill to review how well a codebase supports several developer groups working in parallel.

The central question is:

> How far can a realistic change stay inside one clearly owned area before another area must coordinate, edit shared internals, or wait for an integration decision?

This is a design lens rather than a mechanical metric. Future changes are partly theoretical, so the reviewer combines repository evidence with representative change scenarios and gives a reasoned qualitative judgment.

The target is a codebase where one coherent responsibility can usually be designed, implemented, tested, reviewed, and evolved inside its own boundary while neighboring areas depend on small, understandable contracts.

This is a source-first, read-only review softskill. Return material findings in chat and keep repository state unchanged.

## Core Principle

Organize the system around independent reasons to change.

A strong boundary gives one owning group:

- a clear responsibility
- an implementation area it can understand and maintain
- ownership of the relevant rules and state
- a small interaction surface
- freedom to change internals behind that surface
- focused verification
- a compatible contract-evolution path

Team names and reporting lines may change. Base boundaries on enduring capabilities, state ownership, and change patterns, then map current groups onto them.

Files and folders support the boundary, while dependency direction, visibility, contracts, data ownership, tests, build structure, and integration behavior make it real.

## Evidence Model

Use the strongest evidence available:

- implementation and call sites
- repository and module structure
- public and internal APIs
- project, package, and build references
- composition and registration
- schemas, persistence, and migrations
- tests, fixtures, and test commands
- configuration and operational scripts
- ownership documentation such as `CODEOWNERS`
- current feature documentation, roadmap items, issues, and recent change descriptions
- architecture rules or dependency tests
- Git co-change patterns, repeated merge hotspots, or conflict history when available

Historical co-change can reveal real coordination pressure. It remains supporting evidence because frequent co-change may reflect one temporary initiative, while a quiet history may simply mean the area has changed rarely.

When history or ownership information is unavailable, use source structure and scenario tracing and state the uncertainty.

## Representative Change Scenarios

Choose a small set of realistic scenarios from the repository. Prefer common or likely changes over exotic possibilities.

Useful scenarios include:

- add one capability or operation
- change one business rule
- add another implementation behind a capability
- extend an input or output contract
- replace one external integration
- change persistence for one owned concept
- fix a defect inside one responsibility
- add focused verification for one behavior
- change one operational or deployment concern

Trace each scenario through the likely change path:

- implementation
- contracts
- shared models or schemas
- state and persistence
- composition and registration
- tests and fixtures
- build references
- configuration
- integration and release steps where relevant

Classify the scenario qualitatively:

1. **Local** — one owning area can complete and verify the change through existing contracts.
2. **Mostly local** — one area owns the work and needs a small additive contract or integration adjustment.
3. **Coordinated** — several ownership areas must change together or agree on shared internals.
4. **System-wide** — the change crosses central models, schemas, registries, build structure, or operational paths used throughout the system.

Use the classification to explain the boundary, not to create a numeric score.

## Boundary Quality Model

### 1. Ownership and boundary size

Identify who naturally owns each capability, rule, state transition, integration, and data concept.

A useful ownership area is large enough to contain a coherent reason to change and small enough for one group to understand, test, and operate.

Strong ownership means:

- new work has an obvious home
- internal decisions stay with the owning area
- neighboring areas consume a contract
- state and cleanup responsibilities are explicit
- operational failures have an understandable owner

Review both extremes:

- a broad area that combines unrelated reasons to change creates internal coordination
- very small areas turn one feature into a chain of handoffs and contracts

Choose the smallest boundary that contains the full responsibility and its normal change path.

### 2. Change locality and shared hotspots

Trace which ownership areas a typical change must enter. Count changes in responsibility, not files: ten files inside one coherent area can be more independent than two files owned by different groups.

Look for recurring edits to shared coordination points such as:

- central switch statements
- global registries
- shared enums and discriminators
- broad composition roots
- common DTOs and schemas
- shared mapping tables
- universal options models
- cross-feature utility modules
- central fixtures
- monolithic build or deployment definitions
- dependency cycles or bidirectional project and package references

A useful improvement moves feature variation toward the owning area while preserving one clear composition view of the system.

Examples include feature-owned registration, narrow module descriptors, generated metadata, or a stable root that composes modules without knowing their internals.

### 3. Contracts and compatible evolution

A boundary contract states what one area provides while preserving freedom over how it works.

Contracts may be interfaces, functions, APIs, commands, events, messages, schemas, package exports, protocols, or executable examples.

Review contracts for:

- capability-oriented names
- consumer-relevant inputs and outputs
- explicit error and lifecycle behavior
- clear ownership
- limited exposure of internal models
- stable semantics
- focused compatibility verification

Use the lightest contract form that creates real independence. A direct function or concrete type can be enough inside one ownership area. A formal interface or protocol becomes valuable when teams, implementations, processes, packages, or release cadences genuinely vary.

Support different development speeds through clear evolution mechanisms:

- additive fields with explicit defaults
- additive operations or capabilities
- overloads for the same semantic operation with another input form
- a new named contract when semantics or guarantees change
- adapters for staged migration
- deprecation windows for widely consumed surfaces
- versioned messages or APIs for independently released consumers
- provider and consumer compatibility tests

Contract ownership normally follows the providing capability. Consumers contribute requirements and compatibility evidence, while the provider protects meaning and evolution.

### 4. Data and decision ownership

Independent code boundaries need clear ownership of mutable state and behavior-driving decisions.

Review:

- which area is authoritative for each data concept
- which area may write it
- how other areas read or react to it
- where validation and invariants live
- where implementation selection happens
- how errors cross boundaries
- how cached or copied views define freshness
- whether migrations can be owned incrementally

Prefer one authoritative owner with explicit read, command, event, or replication contracts. Shared write access creates strong coordination because several groups must understand the same invariants and release changes together.

When two concepts must satisfy one atomic business invariant, treat that as evidence for one ownership boundary or one explicit orchestration owner. Preserve the invariant instead of separating state only to mirror team structure.

Repeated branching on another area’s internal types, flags, modes, or states indicates shared decision ownership. Move the decision to its natural owner and expose the result or capability required by consumers.

### 5. Verification and boundary enforcement

A group should be able to prove its change with focused feedback.

Review whether an ownership area has:

- focused unit or component tests
- integration tests for its real dependencies
- contract tests at important boundaries
- test data it can understand and maintain
- a practical build or test command
- clear failure ownership

System-level tests remain useful for end-to-end behavior. Local verification becomes weak when those tests are the only reliable proof for ordinary feature changes.

Use lightweight enforcement where it protects a meaningful boundary:

- internal versus public visibility
- package or project references
- restricted exports
- dependency direction rules
- architecture tests
- schema validation
- contract test suites
- build targets scoped to an area

Enforcement should make the intended path easier and accidental cross-boundary access visible. Match its weight to the repository’s scale and risk.

### 6. Physical topology and integration timing

Modules, packages, projects, services, repositories, and deployable units can strengthen independence when they reflect real responsibility.

Evaluate whether physical separation provides:

- intentional public surfaces
- useful dependency enforcement
- focused builds and tests
- appropriate release independence
- clear operational ownership

Balance those gains against versioning, deployment, observability, latency, transaction, support, and tooling cost.

A well-structured monolith can support many groups through strong internal modules. A separate service or repository is an enforcement and release choice, not the definition of a good boundary.

Independent groups still need explicit integration moments. Useful practices include:

- agreeing on a small contract early
- provider and consumer tests
- stable examples or test doubles
- generated clients or schemas where appropriate
- adapters that support staged migration
- temporary compatibility paths with a clear completion point

The goal is less continuous coordination and more predictable contract and integration checkpoints.

## Legitimate Cross-Cutting Change

Some changes are naturally broad:

- runtime or language upgrades
- security policy changes
- organization-wide observability
- shared design-system changes
- legal or compliance requirements
- foundational build and deployment changes

Treat a system-wide scenario as a boundary problem only when ordinary capability changes are also forced through the same shared surfaces or when ownership remains unclear.

Optimize the common product and maintenance changes for locality while keeping genuine platform-wide work explicit and well owned.

## Practical Boundary Shapes

Choose the simplest shape that supports the responsibility.

### Capability module

One area owns its workflow, rules, internal model, state or persistence contract, outward contract, and focused tests.

### Provider-consumer boundary

One area provides a capability through a narrow contract while consumers remain independent of provider internals.

### Platform boundary

A shared technical group provides a stable service such as identity, logging, configuration, storage infrastructure, messaging, or deployment tooling. Product-specific variation stays with product areas.

### Integration adapter

External systems, frameworks, protocols, and vendor models stay at the edge while the owning capability uses an internal contract.

### Composition boundary

A small explicit root assembles modules and cross-cutting policy. Owned areas contribute narrow registrations or descriptors where that keeps ordinary extensions local.

## Incremental Improvement Model

Recommend the smallest coherent change that creates observable practical independence.

Useful slices include:

- move one complete responsibility and its tests into one owning module
- introduce one narrow contract at a proven coordination seam
- reroute one consumer family away from another area’s internals
- give one state concept a single authoritative owner
- replace one central feature switch with owned registration
- separate one public contract from internal implementation models
- add provider-consumer tests before groups evolve independently
- introduce an adapter for staged migration from a shared model
- split one shared hotspot while retaining one clear composition point
- add one lightweight dependency rule that protects an established boundary

A complete slice leaves a working path, clear ownership, compatibility evidence, and a simpler next change. Moving files alone is useful only when dependency and ownership paths move with them.

## Finding Eligibility

A concern becomes an output finding when:

1. repository evidence supports recurring or likely coordination pressure
2. a representative change scenario exposes the pressure
3. unrelated ownership areas currently edit, decide, verify, or release together
4. a concrete ownership boundary and interaction contract can be described
5. the improvement preserves important behavior and compatibility
6. the independence benefit exceeds migration and operational cost
7. confidence is high or medium

Apply this test:

> Would the proposed change let one owning group complete a meaningful future change with less cross-group knowledge or synchronization?

Use zero findings when the current structure fits its scale or the evidence supports no worthwhile boundary change.

## Required Output Shape

Return only material findings in chat.

For each finding include:

- **Scenario and evidence** — the representative change and repository locations that expose the pressure
- **Coordination pressure** — which ownership areas must currently synchronize and why
- **Proposed ownership** — the capability, rules, and state that belong together
- **Target contract** — the smallest useful interaction surface and its evolution approach
- **Incremental slice** — the focused source-level change that establishes the boundary
- **Verification and preservation** — focused proof plus behavior, data, API, and operational constraints
- **Expected independence** — how the scenario becomes more local, with high or medium confidence

Order findings by expected reduction in recurring coordination and shared-hotspot pressure.

When zero material findings qualify, return only:

`Boundary review complete. Material findings: 0.`

## Output Style

Use simple, direct, source-based language.

Name real files, modules, symbols, schemas, tests, build edges, and responsibilities.

Describe contracts through actual capabilities and behavior.

Prefer statements such as:

- adding a new operation currently edits the global command table and three unrelated fixtures
- this module reads another capability’s internal state instead of using its published result
- several groups must modify one shared DTO even though one capability owns the new field
- the provider can expose one additive request contract while keeping its storage model private
- feature-owned registration keeps new handlers inside their module and leaves the root composition file stable

One strong boundary finding is more useful than a broad restructuring program.

## Repository-State Contract

Operate through read-only repository inspection and narrowly necessary non-mutating commands.

Keep source, tests, scripts, configuration, dependencies, documentation, generated files, plans, reports, handoffs, issues, and Git state unchanged.

Use external research only when a current platform or framework fact is essential for an accurate recommendation.

## Quality Bar

A strong review connects theoretical change independence to concrete repository evidence.

It uses representative scenarios, ownership reasoning, traced change paths, and optional history without pretending that boundary quality is perfectly measurable.

It values:

- clear responsibility ownership
- appropriate boundary size
- local freedom behind stable contracts
- explicit data and decision ownership
- low shared-hotspot pressure
- independent verification
- lightweight enforcement
- compatible evolution
- proportional physical separation
- incremental migration

The best recommendation makes parallel work simpler while keeping the architecture understandable and proportionate.

## Typical Invocation Phrases

- `Use $review-independent-change-boundaries to review this repository for team-scalable change boundaries.`
- `Trace representative changes and show where developer groups must coordinate unnecessarily.`
- `Review ownership, contracts, state, tests, and shared hotspots for parallel development.`
- `Find incremental boundary changes that let capability groups work more independently.`
- `Evaluate whether this modular structure supports several groups changing different parts at the same time.`
