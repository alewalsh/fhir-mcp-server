---
name: agents
description: Always-loaded project anchor. Read this first. Contains project identity, non-negotiables, commands, and pointer to ROUTER.md for full context.
last_updated: 2026-07-20
---

# FHIR MCP Server

## What This Is

A C#/.NET MCP server that exposes read-only tools over local HAPI FHIR + Synthea synthetic patient data for Claude (educational / portfolio).

## Non-Negotiables

- Never commit patient payloads or Synthea dumps (even synthetic) — they are gitignored under `data/`
- Never add MCP tools that write or mutate clinical state — the tool surface is read-only (`ReadOnly=true`, `Destructive=false`)
- Context source of truth: `AGENTS.md` + `.mex/ROUTER.md` + `.cursor/rules/project-harness.mdc` (do not add a parallel `.cursorrules`)
- Never present this project or tool output as clinical advice or a medical device
- Never add an external drug API without an explicit referential / not-medical-advice disclaimer on the tool Description
- Prefer `http://127.0.0.1:8080/fhir` over `localhost` for HAPI (IPv6 hang risk)

## Commands

- Dev / MCP stdio: `dotnet run`
- Smoke: `dotnet run -- --smoke [nameHint]`
- Test: `dotnet test`
- Build: `dotnet build`
- HAPI: `docker compose up -d`
- Load Synthea (Windows): `powershell -ExecutionPolicy Bypass -File .\data\load-synthea.ps1`
- Load Synthea (macOS/Linux): `./data/load-synthea.sh`

## After Every Task

After meaningful work, run GROW:

- Ground: what changed in reality?
- Record: update `.mex/ROUTER.md` and relevant `.mex/context/` files
- Orient: create or update a `.mex/patterns/` runbook if this can recur
- Write: bump `last_updated` on changed scaffold files and run `mex log` when rationale matters

## Navigation

At the start of every session, read `.mex/ROUTER.md` before doing anything else.
For full project context, patterns, and task guidance — everything is there.
