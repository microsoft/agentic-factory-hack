---
applyTo: "evides-leakops/**"
---

# Evides LeakOps: Copilot instructions

These instructions apply to everything under `evides-leakops/`. For this folder they **take precedence over the workshop-mode
rules** in `.github/copilot-instructions.md` (no "don't use solution code", no "complete files only", no "smaller-model" limits).
`evides-leakops/` is a finished demo: read and change the existing code directly, in small, surgical edits.

## Purpose
A demo for **Evides** (Dutch drinking-water company) of what **Microsoft Foundry** can do: six registered Foundry agents, C# **MCP**
tools, **Foundry IQ** knowledge bases, **Foundry Skills** (preview), evaluations, tracing, the content filter and guardrails in code.
One SCADA alarm goes from "is this a real leak?" to "technician dispatched and residents informed on (fake) Waterstoring".

**Core principle, never weaken it:** the model judges and writes; code owns the facts. The rules in
`src/LeakOps.Contracts/Logic/*Rules.cs` rebuild the facts from tool data and re-check every answer before anything is published,
booked or dispatched. When the model and the data disagree, the safer reading wins; when it is uncertain, a person decides
(`uncertain`, `needsReview`, `needs_review`).

## Project map
| Path | What |
|---|---|
| `src/LeakOps.Contracts` | Shared records; `Logic/` holds all rules and shared logic (time, geo, travel, stock, roster, `WorkflowRunner`) |
| `src/LeakOps.Mcp` | Azure Functions app with the 19 `[McpToolTrigger]` tools over Cosmos DB |
| `src/LeakOps.Agents` | Azure Functions app, one HTTP function per agent; `*Agent.cs` holds each `Spec`, `AgentCatalog.cs` lists them |
| `src/LeakOps.AgentRegistry` | Registers the agents in Foundry (instructions, tools, knowledge, skills, answer schema); `SkillStore.cs` publishes skills |
| `src/LeakOps.Workflow` | Workflow host and operator console (SSE); only orchestrates, never touches data |
| `src/LeakOps.Waterstoring` | **Fake** waterstoring.nl: ingest API, public page, technicians and warehouse pages |
| `src/LeakOps.Seeder` | Seeds `LeakOpsDB`; generates sensor readings from `data/simulation.json` |
| `src/LeakOps.AppHost` | Aspire: runs everything locally |
| `tests/LeakOps.Tests` | xunit v3 unit tests (no Azure needed) |
| `scripts/` | `mcp-smoke.cs` (MCP tools) and `e2e.cs` (real model, 48 checks) |
| `data/`, `knowledge/`, `skills/`, `evals/` | Simulated data and scenarios, synthetic documents, `SKILL.md` files, Foundry evaluations |
| `deploy/`, `demo-site/`, `docs/` | Bicep and scripts, static presentation site, architecture diagrams |

`README.md` is the technical reference, `DEMO.md` the runbook, `demo-site/README.md` the site. Read the relevant section before changing behaviour.

## Change an agent
1. Edit the `Spec` in its class (`src/LeakOps.Agents/<Name>Agent.cs`): `Instructions`, `AllowedTools` (least privilege: only the tools
   this agent needs), `Knowledge` (`KnowledgeSpec` with the ops or comms knowledge base from `KnowledgeBases`) and `Skills` (`SkillNames`).
   Communications must never get the ops knowledge base or data tools.
2. A new agent also goes into `AgentCatalog.cs` (pipeline order) with its answer type, and into `WorkflowRunner` if it is a step.
3. Put every fact and check in the matching `Contracts/Logic/<Name>Rules.cs`, not in the prompt. Add unit tests for the cases where
   the model is **wrong** (wrong segment, ineligible technician, unsafe text, invented times) using fakes, not the real model.
4. Re-register: `dotnet run --project evides-leakops/src/LeakOps.AgentRegistry` or `deploy/deploy.ps1 -SkipInfra`. A new agent version
   is created only when the definition changed.

## Add or change an MCP tool
- Add a `[Function]` with `[McpToolTrigger("kebab-name", "description")]` and `[McpToolProperty]` parameters in the right `*Tools.cs`.
  The description is what the model reads: say what it returns and when to use it.
- Validate input and return errors as JSON with `ToolJson.Error(...)` (`{"error": ..., "hint": ...}`) so the agent can correct itself;
  return success with `ToolJson.Ok(...)`. Do not let exceptions reach the caller.
- Shared logic goes in `Contracts/Logic` so tools, rules and tests use the same code. Writing tools must be idempotent (safe to retry).
- Add the tool to the allow-list of the agent(s) that need it, and extend `scripts/mcp-smoke.cs`.

## Knowledge and skills
- `knowledge/ops/` and `knowledge/comms/` are **synthetic** documents. Keep the "SIMULATED DOCUMENT" banner and a document code at the
  start of the title (`# SOP-ISO-01: ...`). Agents cite that code; citation marks like 【…】 are rejected by the rules.
- Documents are guidance, never data: numbers come from tools, and a document can never close an alarm or override a rule.
- `skills/<name>/SKILL.md`: front matter with `name` and `description`; the description is **unquoted and contains no `: `**
  (the Skills preview API rejects it otherwise). The body is injected into the agent instructions at registration.
- Deploy or remove the layer with `deploy/deploy-knowledge.ps1` (`-Remove`). Everything must keep working without it.

## Build and test
```powershell
dotnet build evides-leakops\LeakOps.slnx
dotnet test --project evides-leakops\tests\LeakOps.Tests
```
- Run the unit tests after every code change; they are fast and need nothing.
- `scripts/e2e.cs` calls the real model and **costs tokens**; run it only when asked. Run
  `dotnet run --project evides-leakops\src\LeakOps.Seeder -- --reset-runtime` before and after e2e or smoke runs.
- .NET 10 (`global.json`). Keep the NuGet versions that are in the `.csproj` files; do not upgrade packages unless asked.
- Settings: names from `env.example` (`AZURE_AI_PROJECT_ENDPOINT`, `MODEL_DEPLOYMENT_NAME`, `COSMOS_*`, `MCP_*`, `AGENTS_BASE_URL`,
  `WATERSTORING_*`). Local values go in git-ignored `local.settings.json` or `.env`.

## Demo site and docs stay in sync
- `demo-site/js/technical.js` and `demo-site/js/captured.js` are **generated; never edit them by hand**. After changing anything under
  `src/`, `data/`, `deploy/`, `evals/`, `knowledge/` or `skills/`, run `node evides-leakops/demo-site/tools/build-technical.js`
  (it fails when a referenced line moved: fix the reference in the tool, do not silence it). `captured.js` comes from
  `tools/capture-runs.ps1` against the deployed demo.
- Hand-written texts: `js/slides.js` and `js/tech-text.js` (English and Dutch, keep both).
- Update `README.md`, `DEMO.md` and the diagrams in `docs/` when behaviour, tools, counts or names change. The `.svg` is the source;
  re-export the `.png` from it. `demo-site/img/architecture.svg` is a copy of `docs/architecture.svg`; keep them equal.

## Safety and honesty
- All data is **simulated** and the Waterstoring site is a **fake**. Never present anything as real Evides data, documents or
  systems; new data and documents must be clearly synthetic.
- Never commit secrets, keys or endpoints with keys. Services use managed identity; keys live only in app settings.
- Ground truth in `data/scenarios.json` (`groundTruth`) is for tests and evaluation only; never pass it to an agent or the operator page.
- Public text for residents never contains internal ids, severity, material, costs, unpromised times, blame, customer names or
  health claims; `CommunicationsRules` enforces this, keep it that way.
- Cost awareness: Azure AI Search is about 70 EUR a month while the knowledge layer exists; remind to run
  `deploy/deploy-knowledge.ps1 -Remove` and `deploy/scale-web.ps1 -Sku F1` after a demo. Do not deploy or run paid steps unasked.

## Style
- Code: match the existing C#: records, primary constructors, short `/// <summary>` comments that explain why. A brief comment on a
  C# idiom is fine where the existing code does so; no need to annotate every idiom.
- Docs: plain, factual English, short sentences, numbers with units, no marketing words. **Bold** for products and agents, `code`
  for files, commands, ids and settings (see `.github/STYLE_GUIDE.md`). Public and demo-site texts are Dutch and English.
