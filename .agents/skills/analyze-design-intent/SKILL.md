---
name: analyze-design-intent
description: Read-only reconstruction of why a selected technical target likely exists, what situation or need makes it useful, what role and consequences it has, which assumptions and trade-offs shaped it, and what broader or future uses the same design could plausibly support. Use any relevant evidence without privileging code, treat the selected target as the question boundary rather than the evidence boundary, abstract away from local vocabulary, and clearly separate observed use, inferred rationale, plausible wider use, and historical fact.
---

# Analyze Design Intent

## Purpose

Use this softskill when a user points at something they do not yet understand and wants to get behind the surface description.

The central question is:

> Why did someone likely consider this worth creating in the first place?

That does **not** mean inventing a developer's private thought process. It means reconstructing the smallest plausible design rationale supported by the available evidence.

The target can be represented by any technical material: behavior, source, configuration, documentation, data, schemas, workflows, automation, metadata, tests, examples, history, or combinations of these.

Do not begin by assigning the target to a predefined artifact category. First discover what role it appears to play.

The analysis should try to answer:

- What is this really for?
- What situation makes it useful or necessary?
- What problem, repeated burden, risk, ambiguity, coordination need, or desired property does it appear to address?
- Who or what benefits from it?
- What becomes easier, safer, clearer, more predictable, more reusable, more recoverable, or more controllable because it exists?
- What would likely be worse, harder, or impossible without it?
- Why might this shape have been chosen instead of an obvious simpler alternative?
- What assumptions and trade-offs does the design reveal?
- How does it fit into a larger process, workflow, lifecycle, or body of work?
- What broader or future situations could the same design plausibly support, even if those uses are not yet directly observed?

The internal reasoning may look like:

`target -> role -> situation -> need / pressure -> design choice -> consequence -> trade-off -> broader intent`

This is an internal reasoning path, not a required report structure.

## Read-Only Contract

This is a read-only analysis skill.

- Keep all available material and repository/workspace state unchanged.
- Do not create, edit, delete, rename, stage, commit, merge, rebase, push, restore, or format files.
- Do not run commands whose purpose is to mutate application, repository, workspace, or external state.
- Build, test, or execute only when the user explicitly asks for runtime evidence and the action is known to be non-destructive.
- Return the analysis in chat by default.
- Do not create report files unless the user explicitly asks for an artifact.

If a useful probe would require mutation, describe the probe instead of performing it.

## Core Principle: Explain Existence, Not Inventory

Finding a mechanism is not the same as understanding why it exists.

Examples:

- finding three retries is not yet an insight;
- finding persisted state is not yet an insight;
- finding an index, cache, lock, watcher, queue, timeout, schema, or validation stage is not yet an insight.

These are clues.

The useful questions are:

- Why is retry needed at all?
- Why must this information survive beyond the immediate moment?
- Why is this distinction stored explicitly?
- Why is this value versioned?
- Why is this decision centralized?
- Why does this process retain history?
- Why is this failure tolerated while another is terminal?
- Why is this operation split into stages?
- Why is this information deliberately not retained or exposed?

Only surface a low-level mechanism when it materially helps explain the design intent, situation, trade-off, or consequence.

Prefer:

`This retains the last accepted state because temporary bad input is treated as recoverable and should not destroy a working situation.`

Over:

`This has a cache and three retries.`

## Scope Principle

### The selected target is the question boundary, not the evidence boundary

The user's target defines what the answer is about.

It does not mean all evidence must come from inside that target.

Use the narrowest answer scope that satisfies the question, but inspect surrounding material when it can materially change the explanation.

Useful surrounding evidence may reveal:

- who or what relies on the target;
- what happens before or after it;
- what larger process it participates in;
- what information it creates, preserves, consumes, or transforms;
- what assumptions or promises exist around it;
- how it is documented, configured, generated, tested, automated, delivered, maintained, or evolved;
- whether the apparent local purpose is actually part of a larger design;
- whether an intended use is visible only outside the selected files or fragment.

### Scope inheritance

If the target was established in the immediately preceding conversation, inherit it automatically.

Do not ask the user to restate something that was just located, pasted, named, or selected.

Only resolve ambiguity when multiple plausible targets remain after considering the immediate conversation.

### Reflect on the scope itself

Before deep analysis, ask internally:

- What exactly is the user trying to understand?
- Is the visible boundary also the meaningful boundary?
- Is the local name a real concept or just project vocabulary?
- Does this target make sense on its own?
- What nearby evidence could change the answer to “why does this exist?”
- Is there evidence about intended use, maintenance, lifecycle, delivery, future evolution, or surrounding people/processes that matters more than the local mechanics?

Do not dump this checklist in the final answer.

## Context Expansion By Information Gain

Do not zoom out merely because more material exists.

Expand context only when the next probe could plausibly change at least one of these:

- what the target is for;
- when it becomes useful;
- who or what benefits;
- the problem it addresses;
- the consequences of using it;
- the assumptions behind it;
- the trade-off it embodies;
- the meaning of its information or state;
- whether an apparent purpose is local, shared, incomplete, or part of something broader;
- how it is expected to be used, maintained, changed, delivered, operated, or evolved.

Use this test:

> If this surrounding evidence came out differently, would I explain why the target exists differently?

If not, do not inspect it merely for completeness.

Prefer a few high-information probes over exhaustive traversal.

## Vocabulary Independence

The common use case is unfamiliarity. Therefore the target's own terminology must not become the explanation.

Bad:

`The FooCoordinator coordinates Foo state.`

Better:

`This creates one authority for a shared decision so several independent participants do not drift into incompatible states.`

Move through:

`local wording -> observed role -> neutral meaning -> situation -> purpose`

Rules:

- Explain role, consequence, and purpose before relying on local labels.
- Prefer ordinary or transferable concepts over project-specific nouns.
- Introduce the local term only when it helps reconnect the explanation to the material.
- Treat naming as evidence, not proof.
- Documentation repeating the same local wording is not independent understanding.
- The explanation should still make sense if the local names were renamed tomorrow.

## Use-Situation Reconstruction

For every material topic, try to reconstruct the situation in which the design earns its complexity.

Ask:

- Under what conditions does this become valuable?
- What event, change, decision, failure, scale, coordination problem, handoff, or lifecycle transition makes it relevant?
- Who or what experiences the benefit?
- What task becomes easier?
- What risk or confusion becomes less likely?
- What repeated manual work might disappear?
- What future change or operating condition appears anticipated?
- What would happen if this target did not exist?
- What simpler arrangement would probably be used instead, and what would its limitations be?

Useful internal form:

`When <situation>, this helps <beneficiary / surrounding process> achieve <outcome> without <problem / risk / repeated burden>.`

Do not force this exact wording into the final response.

### Beneficiaries are discovered, not predefined

Do not force every interpretation into fixed personas such as user, developer, operator, administrator, or system.

The beneficiary may be:

- a person performing a task;
- another part of the system;
- an automated process;
- a maintenance workflow;
- a future migration;
- a support/debugging situation;
- a deployment or operation process;
- a team maintaining the system later;
- several of these at once.

Infer the relevant beneficiary from evidence.

## Think Beyond The Immediately Observed Use

Do not stop at the first directly visible usage.

Once the present role is understood, ask whether the same design naturally generalizes to other situations.

Explore, when useful:

- adjacent use cases that require the same property;
- future changes the design seems prepared to accommodate;
- broader classes of problems the same mechanism solves;
- situations that are not currently exercised but are made possible by the design;
- combinations with other parts that could produce a higher-level capability;
- whether the design seems intentionally more general than the currently observed consumer needs.

Examples of the level of reasoning:

- a mechanism currently used for one configuration switch may really be a generic way to coordinate several state changes;
- retained history used for recovery may also support auditing or explainability;
- a stable boundary used by one consumer may indicate intended reuse by other consumers;
- explicit versioning may suggest anticipated evolution even if only one version currently exists.

### Keep speculative expansion disciplined

Separate:

- **observed use**: directly shown by surrounding evidence;
- **strongly implied use**: follows closely from the current design;
- **plausible wider use**: a reasonable application of the same idea that is not demonstrated;
- **historical intent**: requires historical evidence.

Never present a plausible wider use as an existing requirement or as proof of what the original author intended.

The purpose of wider thinking is to understand the **generality and design space** of the target, not to invent a product roadmap.

## Information And Data As Intent Evidence

When information is created, stored, transformed, copied, synchronized, versioned, or deleted, ask why that information needs to exist in that form and lifecycle at all.

Do not stop at “how is it stored?”.

Ask:

- What later decision or behavior needs this information?
- Why must it survive beyond the current operation?
- Who or what relies on it later?
- What is authoritative, derived, temporary, reconstructable, or historical?
- Why are absence, uncertainty, conflict, history, versions, or transitions represented explicitly?
- What becomes impossible if the information is not retained?
- Why are some values retained while others are recalculated or discarded?
- What does migration, expiry, retention, deletion, recovery, synchronization, or conflict handling suggest about expected use?
- Which information is intentionally not stored or exposed, and why might that matter?

Storage syntax, field counts, table layouts, formats, and serialization details are normally internal evidence unless they materially explain the purpose.

## Process And Interaction As Intent Evidence

When the meaning lives in a sequence or relationship, reconstruct the meaningful flow.

Ask:

- What starts the situation?
- What participants, decisions, sources, or handoffs matter?
- What changes from before to after?
- Where is authority located?
- Where can the process fail, wait, retry, branch, recover, or fall back?
- What does another participant observe?
- Why might the sequence be arranged this way?

Prefer describing the meaningful process over listing components.

## Design Choice And Trade-off Reconstruction

Reconstruct the smallest defensible rationale for the observed shape.

Ask:

- What does the design appear to favor?
- What cost, complexity, restriction, delay, duplication, storage, or maintenance burden does it accept to gain that benefit?
- What simpler alternative seems possible but was not chosen?
- Which failure or inconvenience is tolerated, and which is actively prevented?
- Where is flexibility deliberately limited?
- What assumption is made explicit instead of being left implicit?

Useful language:

- `The design appears to favor ... over ...`
- `This makes sense if ... was an important condition.`
- `A plausible rationale is ...`
- `The extra complexity buys ...`
- `A simpler design could have ..., but would lose ...`

Do not claim access to a developer's private chain of thought.

## Present Meaning Versus Historical Origin

Current material can strongly support why a design makes sense **now** without proving why it was originally introduced.

Distinguish:

- present role;
- present design rationale;
- observed or implied use;
- plausible wider use;
- historical origin.

When historical origin matters, inspect evidence such as introduction changes, history, old documentation, discussions, or migration notes if available.

It is valid to conclude:

`The current design rationale is clear, but the original historical motivation is not established.`

## Relationships And Higher-Level Ideas

When several topics are present, look for relationships only when they add understanding.

Ask whether:

- several local choices solve different parts of the same larger problem;
- one idea is a prerequisite for another;
- two explanations compete;
- several independently observed choices suggest a broader design principle;
- several apparent findings merely repeat the same underlying evidence.

A broader concept should emerge from supported local interpretations, not be imposed first.

## Evidence Selection

Any material that can change the interpretation may be useful evidence.

Possible evidence includes, without priority:

- implementation;
- documentation and READMEs;
- examples;
- configuration;
- data representations;
- schemas and contracts;
- tests;
- comments;
- scripts and workflows;
- automation definitions;
- metadata;
- dependency declarations;
- generated material;
- usage sites;
- surrounding processes;
- history and diffs;
- neighboring technical material.

Choose evidence by information value, not file type.

Do not privilege source code merely because it is available.

### Evidence discipline

- Actual use can outweigh naming.
- Documentation can reveal intended role that local mechanics do not show.
- Data retention can reveal future needs, lifecycle, auditability, recovery, or coordination intent.
- Automation and metadata can reveal how something is expected to be delivered, maintained, or evolved.
- Tests can reveal intended semantics but are not automatically independent confirmation.
- Historical evidence is stronger for historical claims than current material alone.
- Missing expected evidence can weaken a hypothesis.
- Repetition of the same idea across files is not necessarily independent support.

## Internal Workflow

This workflow is for reasoning. Do not use it as the default report structure.

### Step 1: Resolve the target

Use the current request and immediate conversation context.

### Step 2: Reflect on the meaningful scope

Determine what the user actually wants explained and which surrounding evidence could change that explanation.

### Step 3: Establish the target's role in neutral language

Avoid explaining local vocabulary with more local vocabulary.

### Step 4: Reconstruct the use situation

Ask when this becomes valuable, what problem exists without it, and who or what benefits.

### Step 5: Follow high-information context

Inspect only evidence likely to change the role, situation, purpose, trade-offs, surrounding use, information meaning, or historical understanding.

### Step 6: Reconstruct relevant information and process flows

Use data and interactions as evidence when they reveal why the target exists.

### Step 7: Infer design choices and trade-offs

Explain the smallest defensible rationale and compare plausible simpler alternatives.

### Step 8: Think one level wider

Ask what adjacent, future, or more general situations the same design could plausibly serve.

Keep these as wider-use hypotheses unless observed.

### Step 9: Test the interpretation

Look for supporting, contradictory, missing, or independent evidence.

### Step 10: Organize by the user's topic

Present the explanation around what the user wants to understand, not around Steps 1-9.

## Topic-Centric Output

The default response is an explanation, not an analysis report.

### One-topic request

If the user asks about one thing, keep the answer centered on that thing even if many surrounding materials were inspected.

Use the target or a clearer neutral concept as the main heading.

Start with a concise 1-3 line **Essence** answering:

- what it is really for;
- the situation or need it addresses;
- why that matters.

Then include only dimensions that add explanatory value. Useful possibilities include:

- **Why it likely exists**
- **When it becomes useful**
- **What problem it removes or reduces**
- **How it is used or fits into a larger process**
- **Why relevant information is retained**
- **What design choice / trade-off is visible**
- **What this enables**
- **Plausible wider or future uses**
- **What remains uncertain**

These are optional dimensions, not mandatory headings.

### Multi-topic request

When the target contains several materially different things the user is trying to understand, organize by those topics:

`# Topic A`

`# Topic B`

`# Topic C`

Keep all relevant purpose, use situation, reasoning, data meaning, interactions, utility, and uncertainty together under the topic they explain.

Afterward, add a short connections section only when shared design ideas materially improve understanding.

### Broad target

For a broad target, begin with a 1-3 line overall Essence.

Optionally provide a compact topic index when useful, then explain the important themes one by one.

Do not make a capability table, observation ledger, coverage matrix, or fixed analysis report mandatory.

Coverage belongs near the end only when breadth itself matters or meaningful areas were intentionally left unexamined.

## Output Relevance Filter

Before including a discovered detail, ask:

> Does this help explain why the target exists, when it is useful, what problem it addresses, what decision shaped it, or what it enables?

If not, keep it as internal evidence and omit it.

Examples:

- `there are three retries` -> normally omit;
- `temporary failure is treated as recoverable because the surrounding operation should continue without manual intervention` -> useful when supported;
- `the value is stored in JSON` -> normally omit;
- `the value is persisted because a later start must remember a previously chosen intention` -> useful when supported;
- `there are six fields` -> normally omit;
- `one field preserves compatibility with older stored records` -> useful when it explains the design.

The goal is explanatory compression: retain the meaning, not every mechanism that led to it.

## Anti-Patterns

### Do not answer “what is there?” when the real question is “why is it there?”

Inventory is evidence, not intent.

### Do not stay trapped inside the selected folder or fragment

The selected target is the question boundary. Follow high-information evidence outside it when necessary.

### Do not zoom out indiscriminately

Context expansion must earn its way by changing the interpretation.

### Do not privilege code

Documentation, data, configuration, automation, metadata, examples, usage, or history may explain intent more clearly.

### Do not mirror project vocabulary

A useful explanation should be understandable to someone who does not already know the local glossary.

### Do not stop at the currently observed use

After establishing the current role, consider what broader class of situations the same design plausibly serves.

### Do not confuse plausible wider use with known intent

Clearly label inference distance.

### Do not invent exact private reasoning

Reconstruct bounded design rationale and trade-offs, not a verbatim chain of thought.

### Do not force fixed personas or artifact types

Discover beneficiaries, role, and context from evidence.

### Do not force every mechanism into the output

Only retain details that explain purpose or consequence.

### Do not write a grand story first

Broader ideas must emerge from supported local interpretations.

### Do not confuse present design meaning with historical origin

Historical claims need historical evidence.

## Optional Evidence Appendix

Only add raw evidence when:

- the user asks for traceability;
- the interpretation is contested;
- conclusions depend on subtle evidence;
- stable references would help follow-up work.

Place evidence after the human-readable explanation, not before it.

## Source Referencing

Prefer precise references directly next to the claims they support.

Use whatever locator fits the material: path, symbol, line, section, key, record shape, workflow step, commit, or another precise reference.

Do not fabricate locations.

## Preferred Tone

Be investigative, explanatory, concept-oriented, and focused on purpose.

Prefer language such as:

- `This appears to exist because ...`
- `The situation it seems designed for is ...`
- `Without this, ...`
- `This becomes useful when ...`
- `The design appears to favor ... over ...`
- `The extra complexity buys ...`
- `A plausible wider use is ...`
- `That wider use is not directly observed.`
- `The present rationale is clear; historical origin remains uncertain.`

The skill succeeds when the reader can answer not just **what the target does**, but **why someone would have bothered to create it, in which situations it pays off, what assumptions and trade-offs it embodies, and what broader possibilities the same idea opens up**.

## Typical Invocation Phrases

- `[$analyze-design-intent] why does this exist?`
- `what problem was someone probably trying to solve with this?`
- `help me understand why somebody wrote this and when it is useful`
- `look beyond the local implementation and reconstruct the design intent`
- `why is this information stored at all?`
- `what situations does this design appear intended for?`
- `what does this make easier for whoever has to use or maintain it?`
- `what other plausible uses does the underlying idea support?`
- `analyze this broader area, but explain it by the topics and purposes you discover`
