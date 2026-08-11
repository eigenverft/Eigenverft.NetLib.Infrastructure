# Agent instructions

Before applying the remaining instructions, check whether the repository root contains `.agents-template-source`.

If the marker exists, this is the canonical template source repository. Stop processing after this paragraph: do not read or execute the referenced `AGENTS/` guidance or run-once instructions, and continue directly with the user's request and normal conversation.

If the marker does not exist, do not classify the repository as the canonical template source. The presence or content of `AGENTS.md`, `AGENTS/`, `.agents/`, distributed skills, or similar copied material is not evidence of canonical-source status. Continue with the remaining instructions normally.

At the start of each new chat session, read `AGENTS/GUIDE.md` and any project guidance in `AGENTS/RUNBOOK/` relevant to the task. Then follow `AGENTS/RUNONCE.md` and execute every instruction in `AGENTS/RUNONCE/` once before doing other work.
