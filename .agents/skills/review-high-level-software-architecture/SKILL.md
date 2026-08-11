---
name: review-high-level-software-architecture
description: Source-first software architecture guidance that turns actual codebase evidence into concrete, incremental refactoring directions and task lists.
---

# Review High-Level Software Architecture

## Purpose

Use this softskill to review a real software codebase or project source and turn that review into concrete architectural guidance.

This softskill should not stop at meta commentary such as “improve layering,” “reduce coupling,” or “clarify boundaries.”
It should produce actionable architecture advice based on the actual source material in front of it.

The output should feel like:

- here are the 2 or 3 most plausible architectural directions for this codebase
- here is what each direction optimizes for
- here are the first concrete refactoring steps for each direction
- here is the direction I recommend most
- here is the exact task list to start with

The emphasis is on **real source-driven advice**, not generic architecture talk.

The output is not complete unless the recommended steps are concrete enough to be copied into issues, TODOs, or a refactoring checklist with minimal rewriting.

If a recommendation cannot be turned into a concrete refactoring task, do not present it as advice yet.
First refine it until it names an actual change to code structure, dependency direction, module ownership, or system boundaries.

A review with no material architectural recommendation is complete and successful. Do not manufacture direction options or task lists merely to satisfy the format.

This is a read-only review skill. Keep source, tests, configuration, documentation, dependencies, generated files, handoffs, and Git state unchanged. Return architectural guidance in chat; do not implement the proposed refactorings.

## Scope

This softskill can be used for many project types, including:

- small utilities and scripts
- npm, pip, NuGet, Maven, Gradle, Cargo, and similar package-based projects
- MSBuild and solution-based projects
- libraries and SDKs
- backend services and APIs
- monoliths and modular monoliths
- microservices
- frontend apps
- full web applications
- minimal apps and prototypes
- internal tools
- desktop and mobile apps

The source can be anything useful for architectural inference:

- source code
- repo tree
- package manifests
- build files
- project files
- MSBuild files
- entrypoints
- dependency wiring
- docs
- plans
- tickets
- test structure
- CI config
- deployment config

## Core Rule

Always anchor the advice in the actual source.

Do not produce abstract architecture recommendations unless they are directly tied to observed code structure, dependency patterns, build setup, or system shape.

Bad:
- “Consider layered architecture.”
- “Reduce coupling.”
- “Improve module boundaries.”

Good:
- “`services/order.ts` mixes HTTP mapping, validation, and payment orchestration. Extract payment orchestration into `application/orders/process_order.ts` first.”
- “The `src/utils` folder acts as a shared dumping ground across 11 features. Split it into feature-local helpers, then keep only true cross-cutting utilities.”
- “Three projects reference the infrastructure package directly. Introduce interfaces in the core package, then move the current implementations behind adapters.”

## Primary Goal

The primary goal is to help the user move the architecture in a better direction through concrete, incremental, source-based changes.

Prefer:

- specific direction options
- concrete refactoring tasks
- small safe steps
- explicit tradeoffs
- practical sequencing
- evidence from the source
- recommendations sized to the project reality

## Use This Softskill When

Use this softskill when the user wants:

- architecture review
- high-level codebase guidance
- restructuring advice
- refactoring direction
- modularization guidance
- service boundary guidance
- package boundary guidance
- maintainability improvement planning
- a realistic architectural next-step list

## Do Not Use This Softskill When

Do not use this softskill for:

- isolated syntax fixes
- single bug fixes with no architecture relevance
- low-level implementation tasks only
- unrelated code edits
- generic brainstorming without source material, unless the user explicitly wants a hypothetical design

## Inputs

Review whatever source material is available.

Possible inputs:

- repository structure
- code files
- package manifests
- build files
- solution files
- project files
- docs and plans
- startup wiring
- dependency injection setup
- imports and references
- tests
- deployment config

## What To Extract From The Source

Before recommending anything, infer the current shape of the system from the source.

Look for:

- major modules, packages, projects, or services
- dependency direction
- repeated responsibility patterns
- feature boundaries
- cross-cutting concerns
- framework leakage
- shared utility dumping grounds
- cycles and tangles
- oversized modules
- unstable public API surfaces
- places where business logic, I/O, and wiring are mixed
- signs of accidental complexity
- mismatch between project structure and actual responsibilities

Treat repo structure, package boundaries, and build references as architectural evidence.

## Working Style

This softskill should behave like an experienced architecture reviewer who looks at the actual codebase and says:

- these are the realistic architectural directions from here
- this one probably fits best
- these are the first small refactorings to start tomorrow

It should not behave like a textbook.

## Required Output Shape

When at least one material architecture concern qualifies, the response must be structured around **direction options with concrete task lists**.

Use this shape unless the user asks for another format.

When no material architecture concern qualifies, return only:

`Architecture review complete. Material findings: 0.`

### 1. What the source suggests today

Describe the current architecture briefly, based on evidence.

Keep this concrete.
Mention actual modules, layers, packages, folders, projects, or dependency patterns where possible.

### 2. Main architectural pressure points

List only the most important issues visible from the source.

Each one must be tied to a concrete observation.

Examples:

- “Controllers are coordinating domain rules directly.”
- “Feature code is split across `components`, `services`, `hooks`, and `utils` with no feature boundary.”
- “The `Core` project references infrastructure-specific packages, so dependency direction is inverted.”
- “The shared package has become a dependency magnet.”

### 3. Viable direction options

Provide 2 to 4 realistic architectural directions.

Each option must include:

- what this direction is trying to optimize
- when this direction fits
- why the current source suggests this could work
- what tradeoff comes with it

### 4. Concrete first steps for each direction

For each direction, provide a small task list.

This is mandatory.

Each step should be phrased as a real refactoring action, not a vague principle.

Good task wording:

- “Create `src/features/orders/` and move order-specific validation there.”
- “Extract database calls out of `UserService` into a repository or data-access module.”
- “Split `shared/utils.ts` into `date`, `string`, and `http` modules, then move feature-only helpers back into their owning features.”
- “Introduce an interface in the core module for payment processing and move the Stripe implementation behind it.”
- “Move application startup and dependency wiring into a single composition root.”
- “Rename `common` to something responsibility-based, then shrink it by moving code back to feature modules.”
- “Separate the public package API from internal helpers by creating an explicit `index` surface.”
- “Break the 900-line service into command-oriented application functions before changing package structure.”

Bad task wording:

- “Improve architecture.”
- “Refactor for maintainability.”
- “Adopt clean architecture.”
- “Revisit module boundaries.”

### 5. Recommended direction

Choose one direction.

Explain why it is the best improvement-to-disruption tradeoff for this codebase.

### 6. Ordered task list to start with

After recommending a direction, provide the concrete starting sequence.

This is the most important section.

Format it as a practical refactoring task list.

Each task should say:

- what to change
- why this is first
- what it unlocks next

### 7. Risks and watchouts

Mention only the risks that matter for the chosen path.

### 8. Next checkpoint

State what should be re-evaluated after the first batch of changes.

For example:

- after one feature extraction
- after dependency inversion at one boundary
- after shrinking the shared module
- after introducing a composition root
- after separating public API from internals

## Minimum Concreteness Rule

This rule is mandatory only when at least one material architecture concern qualifies.

- For every direction option, provide at least 3 concrete, source-based refactoring actions.
- For the recommended direction, provide at least 5 ordered tasks.
- Phrase tasks like issues or TODO items that someone could put straight into a backlog.
- Use concrete verbs such as:
  - move
  - split
  - extract
  - isolate
  - merge
  - rename
  - delete
  - introduce
  - replace
  - collapse
  - invert
  - group
- Avoid vague verbs such as:
  - improve
  - consider
  - revisit
  - evaluate
  - rethink
  - clean up
  unless they are immediately followed by a specific codebase action.
- If the source is detailed enough, mention actual folders, files, modules, packages, projects, services, or dependency edges.
- If the source is incomplete, still make the tasks concrete by naming the target responsibility or boundary.

## Issue / TODO Task Style Rule

When listing actions, prefer issue-style or TODO-style wording.

Good examples:

- “Extract payment orchestration from `OrderController` into an application service.”
- “Split `shared/utils.ts` into `date.ts`, `string.ts`, and `http.ts`.”
- “Move feature-specific helpers out of `common/` back into `features/billing/`.”
- “Introduce a `PaymentGateway` interface in core and move Stripe integration behind it.”
- “Stop `Web` from referencing `Infrastructure` directly; route access through application interfaces.”
- “Create `src/features/users/` and co-locate handlers, validation, and mapping for that feature.”
- “Merge `UserHelpers` and `UserManager` if they are a fake split around the same responsibility.”
- “Delete dead abstraction layer around repository wrappers if it only forwards calls.”

Bad examples:

- “Improve separation of concerns.”
- “Refactor toward better architecture.”
- “Review module boundaries.”
- “Adopt a cleaner structure.”

## Strong Behavioral Rules

### Source-first rule

Always derive the recommendations from the actual source material.
The advice should visibly map back to what was observed.

### Concrete-action rule

Every direction option must include concrete refactoring actions.

### Small-step rule

Prefer steps that can realistically be done incrementally.

### No-big-bang-by-default rule

Do not default to rewrites or sweeping restructures.

### Recommendation rule

Do not just present options when material findings qualify.
Choose one direction unless the evidence is too weak. When evidence is too weak to support a worthwhile current change, use the zero-finding result instead.

### Refactoring-task rule

The final section must read like a task list the user could execute.

## Direction Option Template

Use a template close to this:

#### Direction A — [name the direction]

Best when:
- [context where this path makes sense]

Optimizes for:
- [main benefit]

Why it fits this codebase:
- [source-based reasoning]

Tradeoff:
- [main downside or cost]

Start with:
1. [concrete source-based refactoring step]
2. [concrete source-based refactoring step]
3. [concrete source-based refactoring step]

#### Direction B — [name the direction]

Best when:
- [context]

Optimizes for:
- [benefit]

Why it fits this codebase:
- [source-based reasoning]

Tradeoff:
- [downside]

Start with:
1. [concrete source-based refactoring step]
2. [concrete source-based refactoring step]
3. [concrete source-based refactoring step]

## How Concrete The Advice Should Be

The softskill should be comfortable saying things like:

- move X out of Y
- split module A into A1 and A2
- stop referencing package B from package C
- create a composition root
- extract one feature slice
- isolate framework code
- move business rules into a domain or application layer
- collapse over-engineered abstractions
- introduce an adapter around external dependency Z
- shrink or delete a shared module
- separate public API from internal implementation
- merge two artificial layers if they add no value
- defer service extraction and first modularize the monolith internally

It should not hide behind generic language.

## Project-Type Guidance

### Libraries and packages

Focus on:

- public API surface
- internal vs public separation
- dependency weight
- module clarity
- versioning impact
- extension points

Concrete examples:

- separate `public exports` from internal helpers
- move package-private logic under internal modules
- reduce transitive dependency exposure
- stabilize the entry surface before reorganizing internals

### Web applications

Focus on:

- feature boundaries
- state ownership
- UI vs application logic separation
- API integration boundaries
- framework conventions

Concrete examples:

- group code by feature instead of scattering it by file type
- move orchestration out of components
- isolate data fetching and mapping layers
- stop using cross-feature shared folders as dumping grounds

### Backend systems

Focus on:

- business logic placement
- persistence boundaries
- integration boundaries
- application flow
- observability and failure surfaces

Concrete examples:

- extract use-case orchestration out of controllers
- isolate persistence and external API calls
- introduce ports at unstable boundaries
- split oversized services by capability

### Monoliths

Focus on:

- internal modularity
- dependency direction
- explicit boundaries
- reducing hidden coupling

Concrete examples:

- create modules around business capabilities
- stop shared-everything access patterns
- remove cycles
- add clear ownership per module before any service split

### Microservices

Focus on:

- whether service boundaries are valid
- data ownership
- communication and coupling cost
- operational complexity

Concrete examples:

- merge services that are split only technically, not by business boundary
- move cross-service shared logic back behind contracts
- reduce chatty request chains
- make ownership and data boundaries explicit

### Small apps and scripts

Focus on:

- proportionate structure
- separating growth points from stable logic
- keeping the codebase easy to change

Concrete examples:

- split I/O from logic
- create one or two modules with clear responsibilities
- avoid introducing enterprise layering too early

## Guardrails

### Avoid generic advice

Do not output generic architecture principles without tying them to source observations.

### Avoid fake precision

If the source is partial, be explicit about that.
Still give useful direction, but reduce confidence.

### Avoid pattern forcing

Do not force clean architecture, hexagonal architecture, CQRS, microservices, DDD, or any other named style unless the source and problem justify it.

### Avoid architecture theater

Renaming folders is not enough.
Boundary changes should improve responsibilities, dependency direction, or changeability.

### Avoid overreach

Do not recommend major decomposition when a smaller refactoring would solve the pressure point.

## Preferred Tone

The tone should be practical, direct, and specific.

The user should feel that the softskill is saying:

- here are the realistic architectural choices
- here is what each choice buys you
- here is the first batch of refactoring tasks
- here is the one I would do first

## Example Response Skeleton

### What the source suggests today
[brief source-based read of the current architecture]

### Main pressure points
- [concrete issue tied to source]
- [concrete issue tied to source]
- [concrete issue tied to source]

### Direction A — [name]
Best when:
- [...]

Optimizes for:
- [...]

Why it fits this codebase:
- [...]

Tradeoff:
- [...]

Start with:
1. [...]
2. [...]
3. [...]

### Direction B — [name]
Best when:
- [...]

Optimizes for:
- [...]

Why it fits this codebase:
- [...]

Tradeoff:
- [...]

Start with:
1. [...]
2. [...]
3. [...]

### Recommended direction
[choose one and explain why]

### Ordered task list to start with
1. [concrete task]
2. [concrete task]
3. [concrete task]
4. [concrete task]
5. [concrete task]

### Risks and watchouts
- [...]
- [...]

### Next checkpoint
[what to review after the first wave of refactoring]

## Typical Invocation Phrases

- `[$review-high-level-software-architecture] review this codebase and give me concrete architecture directions`
- `use the high-level architecture softskill and tell me the first refactoring steps`
- `look at this repo and propose 2 or 3 architecture directions with task lists`
- `review this project from a software architecture perspective and tell me what to move, split, extract, or simplify first`
- `give me source-based architecture options and concrete next steps`
- `suggest the next architectural refactors for this codebase`
