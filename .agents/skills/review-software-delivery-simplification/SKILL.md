---
name: review-software-delivery-simplification
description: Analyze software projects and propose practical simplifications for build, publish, deployment, installation, updates, configuration, data handling, rollback, and ongoing operation.
---

# Review Software Delivery Simplification

PURPOSE
Analyze a software workspace, solution, project, subproject, service, application,
library, plugin, package, script collection, or similar technical system and identify
how it can be simplified so that building, publishing, deploying, installing,
updating, operating, and recovering it require as few reliable steps as possible.

The skill focuses on the transition from:

    "The software can be built or debugged"

to:

    "The software can be rolled out and maintained repeatedly in real life."

The goal is not to introduce the most advanced deployment architecture.
The goal is to find the simplest dependable delivery and update process that is
appropriate for the actual project.

CORE PRINCIPLE
Minimize the number of decisions, commands, files, manual edits, environment-specific
exceptions, and irreversible operations required to move from source code to a
working real-world installation.

Prefer a boring, explicit, repeatable process over a sophisticated process that is
difficult to understand or maintain.

SUCCESS CRITERIA
A good result should make it possible for a person with reasonable project knowledge
to answer these questions immediately:

1. What is the initial state before the software is installed?
2. What exact artifact is built?
3. Which files belong to the deployable result?
4. Which files or directories must not be overwritten?
5. How is the first installation performed?
6. How is an existing installation updated?
7. How are configuration, secrets, user data, and generated data handled?
8. How is the software started, stopped, verified, and recovered?
9. How can a failed update be rolled back?
10. What is the smallest reliable set of steps required in normal operation?

APPLICABILITY
Apply this skill generically to any combination of:

- workspace
- solution
- repository
- monorepository
- project
- subproject
- executable application
- web application
- background service
- command-line tool
- desktop application
- mobile application
- library
- package
- plugin
- extension
- container
- infrastructure project
- database-backed system
- scripts and automation
- generated artifacts
- mixed technology stacks

Do not assume a specific programming language, operating system, build system,
deployment platform, cloud provider, packaging format, or runtime.

ROLE
Act as a pragmatic software delivery and operability reviewer.

Think like the person who must:

- install the software for the first time
- deploy it without development tools
- update it six months later
- understand which files are safe to replace
- preserve existing data and configuration
- diagnose a failed rollout
- restore the previous working version
- hand the process over to another person

Do not limit the analysis to source code correctness.

PRIMARY OBJECTIVE
Produce an analysis and a prioritized sequence of simplification proposals that
reduce the real-life effort and risk of:

- build
- package
- publish
- installation
- initial startup
- configuration
- deployment
- update
- migration
- verification
- rollback
- routine operation
- handover

The final recommended workflow should contain as few steps as reasonably possible.

OPERATING BOUNDARY
This is a source-first, review-only skill. Inspect the project and return analysis
and proposals in chat. Do not edit source, configuration, documentation, scripts,
dependencies, generated artifacts, or Git state. Do not execute deployment,
migration, installation, update, rollback, or publication actions. Read-only checks
are allowed when they are necessary to verify the current delivery model.

WORKING METHOD

PHASE 1 — IDENTIFY THE SYSTEM BOUNDARY

Determine what is being delivered.

Identify:

- the main runnable or consumable product
- supporting projects and libraries
- build-time-only projects
- runtime dependencies
- external services
- databases
- generated files
- static assets
- configuration files
- secrets
- persistent data
- caches
- logs
- temporary files
- user-created content
- machine-specific files
- development-only files
- test-only files
- deployment scripts
- packaging scripts
- infrastructure definitions

Clarify whether the delivery unit is:

- one artifact
- one directory
- multiple coordinated artifacts
- a package
- an installer
- a container image
- a service bundle
- a set of independently deployable components

If the boundary is unclear, explicitly state the ambiguity and propose the simplest
reasonable boundary.

PHASE 2 — DESCRIBE THE CURRENT DELIVERY FLOW

Reconstruct the current real-life process from source to running system.

Describe the current steps for:

1. obtaining the source
2. restoring dependencies
3. selecting build configuration
4. building
5. testing
6. publishing or packaging
7. transferring artifacts
8. preparing the target environment
9. supplying configuration
10. supplying secrets
11. preparing persistent storage
12. initializing data
13. starting the software
14. verifying successful startup
15. updating an existing installation
16. handling schema or data changes
17. rolling back a failed update

Do not assume that a documented command actually produces a complete deployable result.
Verify conceptually whether the output contains everything needed at runtime.

Highlight every hidden or manual step.

Examples of hidden steps include:

- manually copying an additional file
- editing paths after deployment
- installing an undocumented runtime
- creating a directory with special permissions
- copying a development configuration
- changing a hard-coded hostname
- manually creating the first user
- running a database script in the correct order
- preserving a file that would otherwise be overwritten
- starting components in a specific undocumented sequence
- deleting stale files from an earlier version
- using a developer machine to generate production assets

PHASE 3 — ANALYZE STATES AND TRANSITIONS

Analyze the system as a set of states, not only as source files.

At minimum, evaluate these states:

A. Clean source state
The repository has been checked out, but nothing has been built.

B. Build state
Dependencies are available and the source can be compiled or processed.

C. Publish state
A deployable artifact has been created.

D. Empty target state
The target machine or environment has no previous installation.

E. First-run state
The software is starting for the first time.

F. Initialized state
Required directories, configuration, schemas, users, keys, or initial data exist.

G. Running state
The software is operating normally.

H. Existing installation state
A previous version, configuration, and persistent data are present.

I. Update state
A new version is being applied to the existing installation.

J. Migration state
Persistent formats, schemas, configuration, or data structures are changing.

K. Failed update state
The new version cannot start or does not pass verification.

L. Rollback state
The previous working version must be restored.

M. Decommissioned state
The software is removed while selected data may need to be retained.

For every relevant transition, determine:

- required inputs
- commands or actions
- files that are created
- files that are overwritten
- files that must be preserved
- irreversible operations
- possible failure points
- verification criteria
- recovery procedure

Pay special attention to differences between:

- no existing data
- existing valid data
- existing outdated data
- partially initialized data
- corrupted or incompatible data
- missing configuration
- old configuration
- first user not yet created
- secrets not yet generated
- an interrupted previous update

PHASE 4 — CLASSIFY ALL FILES AND DATA

Classify relevant files and directories into clear ownership categories.

Recommended categories:

1. VERSIONED APPLICATION ARTIFACTS
Files produced by the build or publish process.
These should normally be replaceable as one unit.

2. STATIC DEPLOYMENT ASSETS
Files required at runtime but not modified by the running application.

3. ENVIRONMENT CONFIGURATION
Values that differ between environments but are not secret.

4. SECRETS
Passwords, tokens, private keys, connection credentials, and similar sensitive values.

5. PERSISTENT APPLICATION DATA
Databases, uploaded content, domain data, user-created content, and durable state.

6. GENERATED MACHINE-SPECIFIC STATE
Generated identifiers, local certificates, runtime keys, or machine registration data.

7. CACHE AND TEMPORARY DATA
Files that can safely be recreated.

8. LOGS AND DIAGNOSTICS
Operational output that must not be mixed with versioned application artifacts.

9. DEVELOPMENT-ONLY CONTENT
Source code, test fixtures, IDE files, debug configuration, local tooling, and mock data.

10. OBSOLETE OR UNKNOWN CONTENT
Files whose ownership or necessity is unclear.

The preferred layout should make these categories physically or logically distinct.

Avoid a deployment design in which application binaries, configuration, secrets,
logs, caches, and persistent user data are mixed in the same replaceable directory.

PHASE 5 — EVALUATE THE BUILD

Check whether the build process is:

- documented
- deterministic enough for the project
- non-interactive
- reproducible from a clean checkout
- independent of an individual developer machine
- explicit about required tools and versions
- explicit about build configuration
- able to fail clearly
- able to produce a complete output
- free from machine-specific absolute paths
- free from required manual file edits
- free from undeclared local dependencies

Determine whether one canonical build command can be provided.

Preferred target:

    build

or an equivalent single command that:

- restores required dependencies
- validates prerequisites
- builds the correct targets
- produces the intended deployable output
- returns a non-zero result on failure

Do not require one command when multiple commands are technically necessary, but hide
unnecessary internal complexity behind a single documented entry point.

PHASE 6 — EVALUATE THE PUBLISH OR PACKAGE RESULT

Check whether the publish result is a self-contained and clearly identified delivery unit.

The result should ideally answer:

- Where is the output?
- Is the output complete?
- Can it be copied to another machine?
- Does it contain development-only files?
- Does it contain environment-specific configuration?
- Does it accidentally contain secrets?
- Does it rely on files outside the output directory?
- Does it include architecture- or platform-specific content?
- Can old files remain after an update and cause problems?
- Can the artifact be versioned and archived?
- Can its integrity be verified?

Prefer an immutable versioned artifact, for example:

    product-name-version-platform

The exact format is technology-dependent.

A publish operation should not modify the source tree in surprising ways.

PHASE 7 — EVALUATE FIRST INSTALLATION

Analyze the first installation as a separate operation from an update.

Determine:

- prerequisites on the target
- required runtime or whether it is bundled
- required directories
- required operating-system permissions
- service registration
- port requirements
- external service requirements
- configuration source
- secret source
- database or storage creation
- schema initialization
- initial data creation
- first-user creation
- administrator bootstrap
- generated keys or certificates
- startup behavior
- readiness verification

The preferred first-install experience should be one of:

A. Copy artifact, provide configuration, start.

B. Run one installer or installation command.

C. Start one package or container with explicit external configuration.

D. Run one bootstrap command that performs safe idempotent initialization.

Initialization should be idempotent wherever practical.

Running initialization twice should either:

- succeed without harmful changes, or
- stop with a clear explanation of the existing state.

Avoid requiring users to manually modify internal files in the deployed artifact.

PHASE 8 — EVALUATE CONFIGURATION

Determine how configuration is created, validated, stored, updated, and documented.

Prefer:

- one clear configuration source
- explicit precedence rules
- sensible non-dangerous defaults
- a generated example or template
- validation before normal startup
- actionable error messages
- separation of configuration from versioned artifacts
- preservation of local configuration during updates
- explicit handling of newly introduced settings
- backward-compatible defaults where practical

Check whether configuration supports:

- first installation
- existing installation
- multiple environments
- local paths
- network endpoints
- ports
- feature switches
- logging
- storage
- authentication
- external services

Avoid:

- multiple undocumented configuration locations
- silently ignored invalid settings
- secrets stored in source control
- production configuration copied from development files
- replacing local configuration during every update
- requiring users to compare large configuration files manually

For configuration evolution, prefer one or more of:

- backward-compatible defaults
- explicit configuration versioning
- automatic safe migration
- a validation command
- a small generated migration report
- a documented list of newly required values

PHASE 9 — EVALUATE SECRETS

Identify all secrets and ensure they are not treated like ordinary application files.

Determine:

- who creates the secret
- when it is created
- where it is stored
- how the application receives it
- how permissions are restricted
- how it is rotated
- whether updates preserve it
- whether backups include or exclude it
- whether logs might expose it

Prefer platform-appropriate external secret handling.

Do not propose a complex secret-management system when a simpler secure mechanism is
sufficient for the actual environment.

PHASE 10 — EVALUATE DATA AND MIGRATIONS

Identify all persistent data and its compatibility requirements.

Determine:

- data location
- ownership
- format
- schema version
- backup requirements
- migration mechanism
- migration direction
- whether migration is automatic or explicit
- whether migrations are transactional
- whether migrations are reversible
- whether the previous application version can read migrated data
- how interrupted migrations are handled
- how large datasets affect deployment time
- whether initial example data is optional or mandatory

Separate these concepts clearly:

- empty storage
- schema initialization
- mandatory system data
- optional example data
- user data
- test data
- development seed data
- production bootstrap data

Never silently install development or test data in production.

For migrations, prefer:

- explicit version tracking
- deterministic ordering
- idempotent operations where possible
- backup or snapshot before destructive changes
- a dry-run or compatibility check when practical
- clear failure behavior
- a defined rollback or forward-recovery strategy

Do not claim that deployment rollback is safe when the data migration is not backward-compatible.

PHASE 11 — EVALUATE FIRST USER AND ADMINISTRATIVE BOOTSTRAP

When the system has users, roles, tenants, organizations, or administrators, determine
how the first trusted identity is created.

Evaluate options such as:

- one-time bootstrap command
- temporary setup page with strict expiration
- environment-provided initial administrator
- invitation-based setup
- external identity provider
- automatically generated one-time credential

The process should:

- be explicit
- be secure
- work without editing internal database records
- prevent repeated unauthorized bootstrap
- provide a clear state when initialization is complete
- avoid permanent default passwords
- avoid embedding credentials in deployment artifacts

If the project has no user concept, state that this section is not applicable.

PHASE 12 — EVALUATE UPDATE BEHAVIOR

Treat updating as the most common long-term operation.

Determine the smallest safe update procedure.

The ideal update flow is conceptually:

1. produce a versioned artifact
2. stop or isolate the existing version if required
3. preserve configuration, secrets, and persistent data
4. replace versioned application artifacts
5. apply compatible migrations
6. start the new version
7. verify readiness
8. remove old artifacts only after success

Evaluate whether a simple copy-over update is safe.

A copy-over update is acceptable only when:

- replaceable artifacts are clearly separated from persistent files
- removed files from old versions cannot remain and interfere
- configuration is not overwritten unintentionally
- file locks and running processes are handled
- permissions remain correct
- migrations are handled
- platform-specific files are correct
- partial copies cannot leave an undefined state
- rollback remains possible

Prefer replacing an entire versioned application directory over copying individual
files into a mixed directory.

A simple recommended layout may conceptually resemble:

    application/
        releases/
            version-A/
            version-B/
        current -> releases/version-B
        config/
        data/
        secrets/
        logs/
        backups/

This is only a generic pattern. Adapt it to the platform instead of forcing it.

If direct directory replacement is not appropriate, propose the simplest equivalent
mechanism available on the target platform.

PHASE 13 — EVALUATE REMOVED AND STALE FILES

Check whether an update can leave files from an old version behind.

This is especially important when using:

- recursive copy
- archive extraction over an existing directory
- package restoration
- generated assets
- renamed assemblies or modules
- removed plugins
- changed frontend bundles
- old configuration fragments

Prefer one of:

- deployment into a new empty version directory
- explicit synchronization with deletion
- package-manager ownership
- installer-managed replacement
- container image replacement

Do not recommend blind deletion when the target directory also contains persistent data.

PHASE 14 — EVALUATE STARTUP, SHUTDOWN, AND HEALTH

Determine how the software is controlled in production.

Identify:

- start command
- stop command
- restart command
- graceful shutdown behavior
- startup timeout
- readiness criteria
- liveness criteria
- exit codes
- service manager integration
- dependency startup order
- retry behavior
- crash behavior
- log location

Prefer a single clear operational entry point.

A successful process start is not sufficient proof that the application is ready.

Define a minimal verification procedure, such as:

- health endpoint
- status command
- expected log event
- functional smoke test
- successful connection to required storage
- successful background worker initialization

PHASE 15 — EVALUATE ROLLBACK AND RECOVERY

For every proposed deployment process, define what happens when it fails.

Determine:

- how the previous artifact is retained
- how the active version is selected
- whether configuration remains compatible
- whether data remains compatible
- whether migrations prevent rollback
- how the previous service is restarted
- how a partially copied artifact is avoided
- how failed initialization is detected
- how backups are restored
- who decides that rollback is necessary

Prefer rollback by switching back to a previously known artifact rather than rebuilding
an old version during an incident.

If true rollback is unsafe, explicitly recommend forward recovery and explain why.

PHASE 16 — EVALUATE PLATFORM AND PERMISSIONS

Check assumptions involving:

- operating system
- CPU architecture
- runtime version
- system packages
- filesystem case sensitivity
- path separators
- line endings
- executable permissions
- service account
- file ownership
- writable directories
- privileged ports
- firewall rules
- certificates
- network access
- proxy settings
- clock and timezone behavior
- locale and encoding

Reduce platform assumptions where reasonable, but do not hide necessary constraints.

Document unavoidable prerequisites in a machine-checkable form where practical.

PHASE 17 — EVALUATE DEPENDENCY ON DEVELOPMENT TOOLS

A production installation should not normally require:

- an IDE
- source code
- test projects
- local developer secrets
- a package restore from an untrusted network at startup
- a compiler
- a frontend development server
- a developer-specific directory
- manual generation from a developer workstation

Where production compilation or runtime package restoration is intentional, explain
the reason and operational consequences.

PHASE 18 — EVALUATE VERSIONING AND COMPATIBILITY

Determine whether the system exposes enough version information to support updates.

Evaluate:

- application version
- artifact version
- package version
- schema version
- configuration version
- protocol version
- API compatibility
- plugin compatibility
- data-format compatibility
- minimum supported upgrade path
- direct upgrade from older versions
- downgrade limitations

Prefer a visible runtime version command or status endpoint.

Do not depend only on file timestamps to identify deployed versions.

PHASE 19 — EVALUATE OBSERVABILITY AND SUPPORTABILITY

Keep observability proportionate to the project, but ensure basic diagnosability.

Determine whether an operator can identify:

- which version is running
- whether startup succeeded
- why startup failed
- where logs are located
- whether required dependencies are reachable
- whether storage is writable
- whether migrations completed
- whether configuration is valid
- whether the service is ready

Prefer actionable messages over generic failure messages.

Avoid proposing a large monitoring platform when a structured log and health check
would solve the actual problem.

PHASE 20 — EVALUATE DOCUMENTATION AND HANDOVER

Determine whether the delivery process can be followed by someone other than the
original developer.

The project should ideally contain one concise operational entry point that documents:

- prerequisites
- build command
- publish command
- artifact location
- first installation
- configuration
- secrets
- first startup
- first-user creation
- update
- verification
- rollback
- backup
- important file locations
- version identification

Prefer executable scripts and validation over prose-only instructions.

Documentation should describe the normal path first.
Exceptional recovery procedures should be separate.

SIMPLIFICATION HEURISTICS

Actively look for these simplifications:

1. Replace many commands with one canonical command.
2. Replace manual file selection with a generated artifact.
3. Replace in-place mixed-directory updates with versioned directory replacement.
4. Separate replaceable program files from persistent state.
5. Move environment-specific values out of the artifact.
6. Generate configuration templates instead of requiring manual discovery.
7. Validate configuration before startup.
8. Make initialization idempotent.
9. Detect existing installations automatically.
10. Distinguish install, update, repair, and rollback explicitly.
11. Preserve previous versions until verification succeeds.
12. Provide a health or verification command.
13. Remove dependence on developer machines.
14. Eliminate undocumented ordering requirements.
15. Eliminate default credentials.
16. Eliminate source-tree deployment.
17. Eliminate manual database editing.
18. Eliminate unnecessary platform-specific branches.
19. Eliminate stale files from previous versions.
20. Eliminate steps that exist only because project boundaries are unclear.
21. Prefer generated manifests over human-maintained copy lists.
22. Prefer safe defaults over required optional configuration.
23. Prefer clear failure over silent fallback.
24. Prefer one supported path over several partially maintained paths.
25. Prefer automation of frequent operations over automation of rare theoretical cases.

MINIMUM-STEP TARGET

After analysis, try to reduce the normal workflows toward these conceptual targets.

TARGET BUILD FLOW

    1. Run one build or publish command.
    2. Receive one clearly identified versioned artifact.

TARGET FIRST-INSTALL FLOW

    1. Place or install the artifact.
    2. Provide required external configuration and secrets.
    3. Run one initialization or start command.
    4. Complete the secure first-user bootstrap if applicable.
    5. Run one verification command.

TARGET UPDATE FLOW

    1. Place the new versioned artifact.
    2. Run one update or activation command.
    3. Run one verification command.

TARGET ROLLBACK FLOW

    1. Reactivate the previous known-good version.
    2. Run one verification command.

These are direction targets, not mandatory shapes.
Do not hide unsafe operations merely to reduce the visible step count.

ANALYSIS RULES

- Base conclusions on evidence found in the project.
- Clearly distinguish known facts from assumptions.
- State when a topic is not applicable.
- Do not invent project requirements.
- Do not recommend a cloud, container, orchestrator, installer, package manager, or
  CI/CD platform unless it materially simplifies the actual project.
- Do not introduce infrastructure solely because it is considered standard.
- Do not treat CI/CD as a substitute for a clear local build and deployment model.
- Do not optimize only for the first deployment.
- Give special weight to the hundredth update.
- Consider how the process behaves after the original developer is unavailable.
- Prefer incremental improvements when a full redesign is unnecessary.
- Identify dangerous simplifications separately.
- Consider security, but keep recommendations proportionate.
- Consider both automated and manual deployments.
- A manual process can be acceptable when it is short, explicit, repeatable, and safe.
- Automation is valuable only when it removes decisions or prevents errors.
- Do not automate an undefined process before clarifying it.

PRIORITIZATION MODEL

Classify proposals into:

CRITICAL
Without this change, deployment or updates are unsafe, incomplete, unrecoverable,
or dependent on undocumented knowledge.

HIGH VALUE
The change substantially reduces routine effort, failure risk, or operational ambiguity.

USEFUL
The change improves maintainability or clarity but is not required for a safe rollout.

OPTIONAL
The change is beneficial only at greater scale or under specific future conditions.

Also estimate each proposal as:

- low effort
- medium effort
- high effort

Prefer proposals with high operational value and low implementation effort.

REQUIRED OUTPUT FORMAT

Produce the result in the following structure.

1. DELIVERY SUMMARY

Briefly explain:

- what appears to be delivered
- the current build-to-runtime path
- the current deployment maturity
- the largest source of operational complexity
- the smallest realistic target workflow

2. CURRENT STATE MODEL

Describe the relevant states:

- clean source
- published artifact
- empty installation
- initialized installation
- running installation
- existing installation
- update in progress
- failed update
- rollback

Mention additional project-specific states when necessary.

3. CURRENT WORKFLOW

List the currently required steps for:

- build
- publish
- first installation
- first startup
- update
- verification
- rollback

Mark steps that are:

- manual
- implicit
- environment-specific
- destructive
- irreversible
- undocumented
- dependent on a developer machine

4. FILE AND STATE OWNERSHIP

Provide a classification of:

- replaceable application files
- configuration
- secrets
- persistent data
- generated state
- logs
- caches
- temporary files
- development-only files
- unclear files

Explicitly state what may and may not be copied over during an update.

5. MAIN RISKS

List the most important real-life failure modes.

Examples:

- configuration is overwritten
- stale files remain
- data migration blocks rollback
- first installation is undocumented
- first user requires database manipulation
- publish output is incomplete
- runtime dependency is missing
- update is not atomic
- permissions differ between machines
- failed startup is not detectable

6. SIMPLIFICATION PROPOSALS

Provide multiple numbered proposals.

For each proposal include:

- Title
- Priority
- Effort
- Problem
- Proposed simplification
- Concrete implementation direction
- Effect on first installation
- Effect on updates
- Effect on rollback
- Remaining risks

Proposals must be actionable.
Avoid recommendations such as "improve deployment" without explaining how.

7. RECOMMENDED TARGET WORKFLOWS

Provide the proposed minimal workflows for:

A. Build and publish
B. First installation
C. Normal update
D. Configuration change
E. Data or schema migration
F. Rollback
G. Uninstallation or decommissioning, when relevant

Show the exact conceptual sequence.

Example:

Build and publish:
    1. Run the canonical publish command.
    2. Archive the generated versioned artifact.

First installation:
    1. Extract the artifact into a new version directory.
    2. Create external configuration from the provided template.
    3. Supply secrets.
    4. Run the idempotent initialization command.
    5. Activate the version.
    6. Verify readiness.

Update:
    1. Back up migration-sensitive data.
    2. Extract the new artifact into a new version directory.
    3. Run compatibility validation.
    4. Stop the old version if required.
    5. Apply migrations.
    6. Activate the new version.
    7. Verify readiness.
    8. Keep the previous version until the update is accepted.

8. MINIMUM COMMAND SURFACE

Propose a small consistent command or script surface.

Use technology-appropriate names, but conceptually consider:

    build
    publish
    validate
    install
    initialize
    start
    stop
    status
    update
    migrate
    verify
    rollback
    backup

Do not recommend all commands automatically.
Combine operations when this is safe and makes normal usage simpler.

A small project may need only:

    publish
    install
    update
    verify

9. COPY-OVER DECISION

Explicitly answer:

- Is copying the new output over the old installation currently safe?
- Which paths may be replaced?
- Which paths must be preserved?
- Must removed files be deleted?
- Must the application be stopped?
- Are open files or locks relevant?
- Are migrations required?
- Can a partial copy leave the system unusable?
- Is atomic directory switching preferable?
- What is the rollback path?

When evidence is insufficient, say that copy-over safety is unverified.

10. DEFINITION OF DONE

Provide a project-specific checklist.

The software is operationally ready only when, where applicable:

- a clean build works
- a complete deployable artifact is produced
- the artifact does not contain secrets
- first installation is documented or automated
- configuration is external and validated
- persistent data is separated from replaceable files
- first-user bootstrap is secure
- initialization is repeatable or safely guarded
- updates preserve required state
- stale application files cannot remain
- migrations are versioned
- failed updates are detectable
- rollback or forward recovery is defined
- the running version can be identified
- logs and health information are available
- the routine update process contains no hidden manual step
- another person can perform the process using the project documentation

FINAL RECOMMENDATION STYLE

End with:

A. The three most important changes.
B. The recommended minimal first-install procedure.
C. The recommended minimal update procedure.
D. The recommended rollback procedure.
E. The expected number of routine manual steps after implementation.
F. Any remaining operation that cannot safely be simplified.

QUALITY BAR

The analysis is successful when it converts an unclear development-oriented project
into a clear operational model and proposes a realistic path toward a small number of
repeatable actions.

The best result is not necessarily:

    "Everything is fully automated."

The best result is:

    "The normal process is obvious, short, safe, repeatable, diagnosable, and recoverable."

DEFAULT MINDSET

Always ask internally:

- Can this step be removed?
- Can this decision be made once instead of on every deployment?
- Can this information be generated?
- Can this operation be made idempotent?
- Can replaceable and persistent state be separated?
- Can failure be detected before activation?
- Can the previous version remain available?
- Can the update be completed by replacing one versioned unit?
- Can another person understand the process without tribal knowledge?
- Will this still be manageable after many future updates?

Optimize for the ordinary real-life lifecycle, not only for the successful developer demo.

