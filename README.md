# FHIR - MCP Server

An MCP (Model Context Protocol) server in C#/.NET that exposes read-only clinical
tools over synthetic FHIR patient data, letting Claude query patient conditions,
medications, and observations.

> ⚠️ **Synthetic data only.** This project uses Synthea-generated synthetic
> patients. It contains no real patient data (PHI). It is **not a medical device**
> and is **not for clinical use** — it is an educational/portfolio project.

## Status
Work in progress. Core server is functional (5 read-only tools, verified against
HAPI FHIR + Synthea, working in Claude Desktop). README, evals, and docs are being
completed.

## Stack
C# / .NET 10 · official `ModelContextProtocol` C# SDK (stdio) · Firely `Hl7.Fhir.R4` ·
HAPI FHIR in Docker · Synthea synthetic patients

## Safety design
- All tools are **read-only** — no clinical writes or mutations
- Responses are summarized text, never raw FHIR JSON
- Missing data returns an explicit "not found" — the server never fabricates