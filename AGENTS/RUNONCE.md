# Run-once instructions

`AGENTS/RUNONCE/` contains repository instructions that must be applied once at the start of each new chat session.

On the first read of `AGENTS.md` in a chat session, first read every Markdown file in `AGENTS/RUNONCE/` fully before applying any of them.

## Execution order

Classify the files by name:

1. **Numbered instructions:** filenames beginning with a three-digit prefix followed by an underscore, for example `010_EXAMPLE.md`.
2. **Unnumbered instructions:** every other Markdown filename in `AGENTS/RUNONCE/`.

Apply numbered instructions first in ascending filename order. The three-digit prefixes define their intended execution order.

After all numbered instructions have been processed, consider unnumbered instructions in ascending filename order. Before applying an unnumbered instruction, compare its subject, intended repository changes, and intended outcome with the numbered instructions already processed in the current session.

Skip an unnumbered instruction when it collides with a numbered instruction that completed successfully. A collision exists when both instructions address the same task area or would cause duplicate, competing, or contradictory work in the same repository state. The numbered instruction takes precedence even when the wording or exact scope differs. A similar filename alone is not a collision. When no collision is identifiable, apply the unnumbered instruction normally.

Do not rename, delete, or rewrite unnumbered instruction files merely because they appear outdated or duplicated unless the user explicitly asks for that repository change.

## Session completion

Do not repeat an instruction after it has completed successfully in the same chat session. Process every instruction again when a new chat session starts.

A failed, blocked, or incomplete instruction is not completed for the current chat session. Retry it if the blocker is resolved, but do not claim completion without verifying the intended outcome.

After the run-once phase, include a concise report in the next user-facing response showing:

- the instructions applied, in their actual execution order;
- the unnumbered instructions skipped because they collided with a successfully completed numbered instruction;
- for every skipped instruction, the numbered instruction it collided with.

Omit the skipped section when nothing was skipped. Do not report an instruction as applied when it failed, remained blocked, or was incomplete.

## Suggested follow-up actions

A run-once instruction may declare one or more suggested follow-up actions. Include only actions whose source instruction produced the finding or state that makes them applicable.

After all other run-once output, collect applicable actions under `## Suggested Follow-up Actions`. Group their source instructions in actual execution order using `A)`, `B)`, and so on. Number the applicable actions within each source starting at `1`. Do not add alternatives merely to increase the number of choices. Vary or add actions only when the source instruction's actual findings or resulting state justify distinct, useful follow-up actions. Do not execute suggested follow-up actions automatically or include sources without applicable actions. Omit the section when no actions apply.

```markdown
## Suggested Follow-up Actions

<letter>) Source: `<run-once-filename>`

1. <short imperative action>
```

Run-once instructions do not override higher-priority instructions, required approvals, or safety constraints. Stop and ask the user before destructive, externally visible, or unclear actions unless the instruction or current request already provides the necessary authority.
