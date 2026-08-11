---
name: review-reuse-before-build
description: Review a repository for custom mechanisms that should be replaced or materially reduced now by an existing project, platform, framework, standard-library, current-dependency, or established ecosystem solution. Emit only current-state actionable recommendations; no finding is a valid result. Verify realistic reuse options, project health, maintenance status, compatibility, and package-manager availability without modifying product code or dependencies.
---

# Review Reuse Before Build

## Purpose

Use this softskill to review whether a repository builds technical mechanisms that should instead reuse an existing solution.

The review is not limited to command-line parsing or any specific technology. Apply it to any language, runtime, framework, application type, or infrastructure stack.

The core question is:

> Before this mechanism is built or expanded, is there already a suitable, established, maintained, and easy-to-consume solution?

This is a review-only softskill.

Its job is not to produce a repository report. Its job is to identify only those cases where replacing or materially reducing the current mechanism is recommended for the repository as it exists now.

A review that finds no such case is successful. Do not manufacture output to prove that the review happened.

Do not change product code, dependencies, manifests, configuration, documentation, lock files, generated files, or Git state.

## Core Principle

Reuse before building, but do not reuse blindly.

Prefer, in order of practical suitability:

1. a correct existing mechanism already used in the repository
2. a standard-library, runtime, operating-system, or platform capability
3. a feature already provided by the project’s framework
4. a suitable dependency already present in the project
5. an established and maintained ecosystem package
6. a small local implementation when the problem is genuinely small and external reuse would cost more than it saves
7. a new general-purpose subsystem only when current requirements justify it

This order is a review guide, not an automatic verdict. A poor existing mechanism does not become good merely because it already exists.

## Actionable Finding Gate

A reviewed mechanism becomes an output finding only when all of the following are true:

1. the repository currently owns meaningful custom behavior for a reusable technical concern
2. a specific existing repository, platform, framework, current-dependency, or established external solution covers the actual required behavior
3. the replacement or reduction is compatible with current constraints
4. adoption cost and migration risk are lower than the expected correctness, maintenance, security, or complexity benefit
5. the reviewer recommends making the change now, not merely considering it later
6. the recommendation is concrete enough to describe the target mechanism and affected boundary

Apply this practical test:

> Would it be responsible to open an implementation issue or pull request for this change against the current repository?

If the answer is no, do not emit a finding.

The following do **not** qualify as findings:

- a package or API exists, but the local implementation is already proportionate
- an alternative is technically possible but not preferable
- the existing code already uses the correct platform or framework primitive
- a modernization is idiomatic but has no meaningful current benefit
- a tiny cleanup could be done opportunistically while touching the code for another reason
- a solution might become useful if the feature grows later
- the reviewer can only conclude **Keep the local implementation**
- the reviewer has **Insufficient evidence**
- the change would mostly reduce line count while adding dependency or migration cost

Do not output keep-as-is confirmations, reviewed-area inventories, rejected-package catalogs, or architecture praise.

## Current-State Recommendation Threshold

Recommendations must be for the current repository state. Avoid conditional output such as:

- adopt this if requirements grow
- consider this if maintenance becomes painful
- modernize this when the file is touched
- this could be cleaner with another abstraction

Such observations may guide private reasoning, but they are not output findings.

A reduction qualifies only when it removes meaningful duplicated or risky mechanism now. Cosmetic extraction, speculative consolidation, and optional helper cleanup do not qualify.

A small change may still qualify when it has a clear present correctness or safety benefit, such as replacing hand-written argument quoting with an official argument-list API.

## Finding Selectivity

Prefer a few high-confidence findings over broad coverage.

Do not split one weak observation into several findings. Do not create one finding per reviewed subsystem. Group only changes that naturally belong to the same implementation decision.

The presence of many reviewed areas does not imply that any output is required.

## Review Scope

Look for custom implementations of common technical capabilities, including but not limited to:

- command-line parsing
- configuration loading and precedence
- validation
- serialization and deserialization
- retry, timeout, and resilience behavior
- scheduling and background work
- caching
- dependency injection or service location
- logging and structured diagnostics
- object mapping
- event dispatch and messaging
- routing
- authentication and authorization plumbing
- cryptographic helpers
- identifiers and token generation
- file watching
- archive and compression handling
- templating
- data access helpers
- migrations
- state machines
- plugin loading
- process execution
- HTTP clients and protocol handling
- parsing of standard formats
- date, time, locale, and unit handling
- collection, graph, queue, and concurrency primitives
- test infrastructure and mocking helpers
- build, packaging, and release utilities

Do not assume that every local helper is unnecessary. Focus on mechanisms with meaningful implementation, maintenance, correctness, security, compatibility, or extension cost.

## When To Use

Use this softskill when:

- a feature appears to have introduced a complete subsystem for a small requirement
- the repository contains custom infrastructure for a common problem
- a review should check whether the project is reinventing an established solution
- a proposed implementation adds significant code, abstraction, or maintenance surface
- a dependency decision needs an evidence-based reuse review
- the user wants only actionable replacement or reduction findings without changing the repository

## When Not To Use

Do not use this softskill when:

- the user explicitly requested implementation rather than review
- the work is purely domain-specific and no general reusable mechanism is involved
- the question is only whether code style is clean
- the user asked for a dependency upgrade execution
- repository access is unavailable and source-based review is required

## Source-First Contract

Read the relevant source before recommending reuse.

Inspect, where applicable:

- the custom mechanism itself
- call sites and real usage patterns
- public and internal contracts
- tests
- dependency manifests and lock files
- framework and runtime versions
- repository-local utilities with similar responsibility
- documentation and comments that explain constraints
- deployment or portability requirements

Determine the actual required capability, not the capability implied by the abstraction’s size.

A large implementation may support only a small real use case. A small implementation may hide difficult compatibility or security requirements.

Do not recommend replacement from the class name, folder name, or package category alone.

## Existing Repository Reuse Check

Before searching externally, determine whether the repository already contains a suitable mechanism.

Check for:

- another implementation of the same concern
- an existing shared utility
- a framework service already configured elsewhere
- a dependency already used by another project or module
- a wrapper around an established package that could be reused
- an existing convention that the new mechanism bypasses

When repository-internal duplication produces an actionable consolidation, prefer that finding over an external-library recommendation. Otherwise keep the observation internal.

Do not propose a new dependency when the repository already has a suitable, maintained solution unless there is a concrete reason not to reuse it.

## External Research Contract

Search externally only after repository inspection identifies a plausible actionable replacement. Do not research packages merely to populate an alternatives section.

When a plausible established solution may exist and current external information would materially affect the recommendation, search the internet.

Do not rely only on remembered package names or historical popularity.

Research should verify:

- the official project or package source
- current maintenance status
- recent releases or meaningful maintenance activity
- compatibility with the project’s current technology versions
- supported platforms and deployment model
- package-manager availability
- documentation quality
- licensing at a useful summary level
- security advisories or clear abandonment signals when relevant
- migration and adoption cost

Prefer primary sources:

- official project documentation
- official package registries
- official source repositories
- official release notes
- language, runtime, framework, or platform documentation

Use third-party comparisons only as supporting evidence, not as the sole basis for a recommendation.

## Established-Solution Standard

Do not recommend obscure software merely because it technically matches the requirement.

A preferred external solution should normally show several of these qualities:

- recognized or widely used in its ecosystem
- maintained by a credible organization or stable maintainer group
- recent releases or active maintenance appropriate to its maturity
- clear documentation and examples
- stable package-manager distribution
- transparent license
- issue and security handling that does not appear abandoned
- compatibility with supported project versions
- a user base large enough that common problems are likely to be known
- an adoption path simpler than maintaining the custom solution

Do not use download counts or repository stars as proof by themselves.

A small focused package can be valid. An unknown, unmaintained, single-purpose package with unclear provenance should not replace a simple local implementation.

## Project Health Check

During analysis, classify each external candidate that remains plausible for an actionable recommendation as one of:

1. **Healthy** — maintained, compatible, documented, and practical to adopt.
2. **Mature and stable** — low release frequency is reasonable because the problem and API are stable; no clear abandonment signs.
3. **Usable with caution** — useful but has maintenance, compatibility, governance, or adoption concerns.
4. **Outdated** — stale, abandoned, insecure, or incompatible because maintenance has not kept pace.
5. **Healthy but unsuitable** — maintained software that does not fit this repository’s actual requirement, constraints, deployment model, or proportionality threshold.
6. **Unverified** — evidence is insufficient for a responsible recommendation.

Do not label a project outdated only because its last release is old. Consider whether the domain is stable and whether maintenance activity, issue handling, compatibility, and ecosystem usage remain healthy.

Conversely, do not label a project healthy merely because it published a recent version.

## Consumption and Update Contract

Prefer the technology-appropriate package manager or official distribution mechanism.

Examples include package managers, module registries, operating-system package systems, framework package mechanisms, plugin registries, container registries, or official binary distribution channels.

The exact tool is technology-specific, but the principle is not.

Prefer solutions that can be:

- declared in the normal dependency manifest
- restored reproducibly
- versioned clearly
- updated through ordinary dependency-management workflows
- checked by existing security and dependency tooling
- removed without manual source cleanup

Treat these as strong warning signs unless clearly justified:

- downloading source archives manually
- copying third-party source files into the repository without a deliberate vendoring policy
- extracting individual files from another project
- building an external library from source only to consume normal functionality
- committing opaque binaries from unofficial sources
- maintaining custom update scripts for a dependency available through a normal package channel
- bypassing lock files or reproducible dependency resolution

Manual vendoring or source builds may be valid for offline, regulated, patched, unsupported-platform, reproducibility, or supply-chain reasons. Require concrete repository evidence before recommending them.

## Proportionality Check

Do not turn “reuse before build” into “always add a library.”

Compare the complete cost of each option:

- current required behavior
- likely near-term variation supported by evidence
- correctness difficulty
- security sensitivity
- compatibility surface
- maintenance burden
- dependency size and transitive dependencies
- upgrade effort
- operational impact
- API complexity
- testing burden
- portability requirements
- licensing constraints
- removal or migration cost

A small local implementation may be the best answer when:

- the requirement is genuinely narrow
- behavior is obvious and easy to test
- edge cases are limited and controlled
- the code is not security-sensitive
- the external dependency would be larger or harder to maintain
- the project would use only a trivial fraction of a large package

An established solution is more likely appropriate when:

- parsing or protocol rules have many edge cases
- compatibility must remain stable
- the feature is public or user-facing
- security or correctness failures are costly
- help, diagnostics, localization, completion, migration, or extensibility matter
- the custom code is already growing
- several modules need the same capability
- mature ecosystem behavior would otherwise need to be recreated

## No Speculative Architecture Rule

Judge against current requirements and evidence-backed near-term needs.

Do not justify a large subsystem with vague future possibilities such as:

- “we may need more options later”
- “this could become extensible”
- “other implementations might appear”
- “it is more enterprise-ready”

Future extensibility counts only when supported by product requirements, existing variation, a public compatibility commitment, or a clear repository direction.

## Replacement Judgment

Use these judgments during analysis:

1. **Reuse existing repository mechanism**
2. **Use standard platform or framework capability**
3. **Use existing project dependency**
4. **Adopt an established external solution**
5. **Keep the local implementation**
6. **Reduce the local implementation instead of replacing it**
7. **Insufficient evidence**

Only judgments 1, 2, 3, 4, and 6 are eligible for output, and only when they pass the Actionable Finding Gate.

Judgments 5 and 7 are internal review outcomes. Do not report them as findings.

Judgment 6 is eligible only when the reduction is recommended now and has meaningful benefit. An optional cleanup, future possibility, or opportunistic refactor is not eligible.

Do not recommend replacement solely to reduce line count. Account for migration risk, behavior compatibility, dependency policy, and long-term ownership.

## No-Change Contract

This softskill is analysis-only. It reviews the repository and returns findings; it does not authorize repository changes.

As part of this skill, do not:

- edit source files
- add, remove, or update dependencies
- modify manifests or lock files
- change configuration
- run automatic upgrade or migration commands
- stage, commit, or push

Read-only inspection, dependency metadata inspection, and internet research are allowed.

## Required Output Shape

### No actionable finding

Do not produce a report, reviewed-area list, keep-as-is section, alternatives table, priority list, or final recommendation.

In direct chat, return only:

`No actionable reuse opportunity found.`


### One or more actionable findings

Return only the actionable findings. Omit all reviewed mechanisms that do not warrant a current change.

For each finding include:

- **Location** — relevant files and symbols
- **Replace or reduce** — the current mechanism and the recommended target
- **Why now** — concrete present-day correctness, maintenance, security, compatibility, or complexity benefit
- **Evidence** — repository behavior and constraints that support the recommendation
- **Adoption path** — existing API, framework feature, dependency already present, or normal package-manager path
- **Migration boundary** — the main callers, contracts, tests, or behavior that must be preserved
- **Net judgment** — why the benefit exceeds adoption and migration cost
- **Confidence** — high or medium; do not emit low-confidence findings

Keep the result narrow. Do not add a summary section unless needed to distinguish several independent actionable changes.

Do not include:

- keep-as-is findings
- no-action items
- speculative future opportunities
- optional cleanups
- rejected candidates unless directly necessary to explain the recommended target
- candidate-health tables for packages that are not being recommended
- general repository review commentary

## Output Style

Use simple, direct, technically concrete language.

Prefer real file names, symbols, behaviors, package names, maintenance facts, and adoption paths over abstract phrases.

Do not fill the response with general statements about avoiding reinvention. Every significant recommendation must connect to repository evidence.

Keep internet-derived facts clearly distinguishable from source-derived findings and cite them when the environment supports citations.

## Quality Bar

A successful review does not merely say “use a library.”

It determines:

- what the project actually needs
- what it already has
- which established alternatives are plausible for a current actionable replacement
- whether those alternatives are healthy and compatible
- whether they can be consumed and updated normally
- whether reuse is genuinely simpler and safer than local ownership
- whether any recommendation is strong enough to justify a current change

The best result may be no output. When output exists, it should describe a change worth making now and reduce unnecessary custom infrastructure without replacing it with unnecessary dependency infrastructure.

## Typical Invocation Phrases

- `Use $review-reuse-before-build to find only custom mechanisms that should be replaced or materially reduced now.`
- `Check whether this project is reinventing common infrastructure. Return only current-state actionable recommendations; no finding is acceptable.`
- `Review this subsystem for existing repository, platform, framework, or maintained-library replacements. Do not report keep-as-is results.`
- `Find custom mechanisms that are worth replacing now, but reject obscure, outdated, disproportionate, or merely optional candidates.`
- `Check whether normal package-manager dependencies would be simpler than the custom implementation. Produce no report when the answer is no.`
