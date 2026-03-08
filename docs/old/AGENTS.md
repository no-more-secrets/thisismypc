# Codex BMAD Command Bridge

## Purpose

This repository includes BMAD workflows, Claude-style slash command files in `.claude/commands/`, and Codex-usable BMAD skills in `.agents/skills/`.

For Codex sessions in this repo, treat BMAD slash commands as aliases for the same-named installed skill or workflow.

## Command Mapping

- If the user types `/bmad-help`, invoke the `bmad-help` skill.
- If the user types `/bmad-...`, strip the leading slash and treat the remaining token as the BMAD skill name when a matching installed skill exists.
- If the user types `/bmad-agent-...`, load the matching BMAD agent skill and follow its activation instructions exactly.
- If the user types a BMAD command plus extra text, treat the command token as the skill invocation and the remaining text as the user request for that workflow.

Examples:

- `/bmad-bmm-create-prd`
- `/bmad-bmm-create-architecture`
- `/bmad-bmm-code-review review the latest startup module changes`
- `/bmad-agent-bmad-master`

## Fallback Rules

- If a slash command has no exact matching skill, search the BMAD manifests under `_bmad/_config/` and execute the referenced workflow directly when feasible.
- If both a skill and a Claude command file exist, prefer the installed skill because it is already exposed to Codex through the workspace instructions.
- Do not tell the user BMAD commands are unsupported just because they are written with a leading slash.

## Notes

- `.claude/commands/` is a Claude integration layer, not the source of truth.
- `_bmad/` and `.agents/skills/` are the source of truth for Codex execution in this repo.
