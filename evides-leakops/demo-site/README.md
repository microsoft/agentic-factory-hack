# Evides LeakOps: interactive demo website

A static presentation site (plain HTML, CSS and JavaScript, no build step, no backend, no keys) that tells the LeakOps story slide by slide.
Every step slide first shows the **functional need** (what a duty manager faces today) and, on reveal, the **technical solution**: the Foundry
agent, the MCP tools it called, the guardrails in code, where it runs in the [architecture](../docs/architecture.svg), and the **AI value**.
The workflow slide (11) replays the complete workflow over the architecture diagram (including the knowledge and skills lanes, which light up when an agent
calls its knowledge base) and ends with the result, the token usage of the run, the residents' notice and a short before/after. Slide 12,
"Before vs after AI", is the detailed version: the same 16 steps as slide 1, each marked as done by an agent, AI-assisted or still done by people.
The last slide (13), "Questions", closes the demo: "Thank you", a QR code to this site and ten common questions; a click on a question card flips it to the answer.

Live: https://white-field-03bd1f003.5.azurestaticapps.net (Azure Static Web App `leakops-demo-88546e`, Free tier, resource group `rg-evides-ai-demo`).

## Use it
| Key | Action |
|---|---|
| `Space`, `Enter`, `→` | Reveal the solution, then go to the next slide (on the workflow slide: play / pause, and after the replay on to slide 12; nothing on the last slide) |
| `←` | Previous slide |
| `L` | English / Dutch |
| `F` | Fullscreen |
| `R` | Replay the workflow (workflow slide, 11) |
| `1`–`6` | Technical tabs in the solution card (see below) |
| `1`–`9`, `0` | Show or hide the answer to question 1-10 (questions slide, 13) |

Slide 1, "Before AI", shows today's drinking-water incident process on one page: the 16 steps from pipe break to closed ticket with roles, system types and people involved, why it is so complex (with public facts from evides.nl, waterstoring.nl and Wikipedia) and where it hurts. The steps, roles and system types are the Evides team's description of a typical process and may differ in detail at Evides; the system names are typical for utilities, not a confirmed list. Slide 12, "Before vs after AI", shows the same 16 steps side by side with what happens now: 7 done by an agent, 3 AI-assisted (an agent prepares, a person acts or approves) and 6 still done by people or unchanged (field work stays with people), with before/after KPIs on top. Its values with AI (44 s to the public notice, 66 s to the work order, technician, times, tool calls and tokens) come from `js/captured.js`. Slide 3 has a switch between the burst and the false alarm. The workflow replay has 1x, 3x and 10x speed. `#/4` in the URL opens slide 4.

Four step slides link to the live fake pages of the Waterstoring app (simulated data): slide 4 "Diagnostic" to the network map (`/gis`, a simulated
demo GIS, not the Evides GIS), slide 5 "Residents" to the residents' page, slide 6 "Technician" to `/technicians` and slide 7 "Parts" to `/warehouse`.

Slide 13, "Questions", opens with a water wave that rises from the bottom and ripples on its surface. The ten question cards (English and Dutch) flip
in 3D to show the answer; flipped cards stay flipped when you switch the language, and the footer button "Hide the answers" turns them all back.
With reduced motion the water stands still and the answer fades in instead of flipping. The QR code is a static file, `img/qr-demo.svg`, because the
CSP allows no runtime libraries; it encodes the live URL. To regenerate it (any static SVG QR code works), for example: `npx qrcode -t svg -d 064f57 -o evides-leakops/demo-site/img/qr-demo.svg <url>`.

## Technical tabs
After "Reveal the solution", the card has six tabs: **Overview** (the story) | **Instructions** | **Tools & MCP** | **Data & knowledge** | **Code rules** | **Source**.
Each agent slide shows its full real instruction text, answer schema, the allowed MCP tools with their descriptions, why MCP, the Cosmos DB
containers behind the tools, the code rules and checks with their constants, and `path:line` references into `src/` (copyable). The Alarm slide
has Contract | Host & API | Data | Code rules | Source; the Trust slide has Foundry | Security | Observability | Code rules | Source.
The slide before Trust, "Tools, knowledge, skills", is a matrix of what each agent is connected to (model, tools, Foundry IQ knowledge base,
skills, memory = none) and which layer answers which need.

**Knowledge and skills (Foundry IQ and Foundry Skills, preview):** the agent tabs show the knowledge paragraph of the instructions, the skills the
agent applies (with their text and why), the `knowledge_base_retrieve` tool, the knowledge base and its documents with `path:line`, and what the agent
really asked the knowledge base in the captured run. The Trust slide's first tab explains why Foundry IQ, how a question reaches a document, what it
costs, and which routes were tried for skills. The knowledge documents are synthetic and say so.

`js/technical.js` is **generated from the source** by `tools/build-technical.js` (instructions, tool descriptions, rule constants and messages, schemas,
data files and line numbers). Run it again after changing anything under `src/`, `data/`, `deploy/`, `evals/`, `knowledge/` or `skills/`:
```powershell
node evides-leakops/demo-site/tools/build-technical.js
```
It fails when a referenced piece of source moved, instead of showing stale references. The short bilingual "why" texts are in `js/tech-text.js`.

## Where the content comes from
`js/captured.js` is **generated from a real run of the deployed demo** (the burst and the false-alarm scenario): the agents' answers, tool calls,
code re-checks, durations and token counts. Nothing in it is typed in by hand. All data is simulated.
To refresh it, run `tools/capture-runs.ps1` (PowerShell 7, Azure CLI, Node.js). It records both scenarios on the deployed workflow (about 0.03 EUR of model use
each; it books a real incident, so clear the runtime data afterwards) and rebuilds the file with `tools/build-captured.js`. Do this with the knowledge layer deployed,
otherwise the knowledge lookups disappear from the slides. Then run `build-technical.js` again, because the "what the agent asked" texts come from the capture.
`js/art.js` holds the illustrations reused from [functional-flow.html](../docs/functional-flow.html); `img/architecture.svg` is a copy of
[docs/architecture.svg](../docs/architecture.svg). Slide texts (English and Dutch) are in `js/slides.js`.

## Run locally
Any static file server works, for example `npx http-server evides-leakops/demo-site -p 8080`, then open http://localhost:8080.

## Deploy
```powershell
$env:AZURE_CONFIG_DIR = "$HOME\.azure-macaw"                  # when your default az login is another tenant
./evides-leakops/deploy/deploy-demosite.ps1 -WhatIfOnly        # shows that one resource would be created
./evides-leakops/deploy/deploy-demosite.ps1                    # Static Web App (deploy/demosite.bicep) + upload with the SWA CLI + health check
./evides-leakops/deploy/deploy-demosite.ps1 -SkipInfra         # only upload changed content
```
Needs PowerShell 7, the Azure CLI and Node.js (the script runs `npx @azure/static-web-apps-cli`). The deployment token is read for that run only
and never written to a file. `staticwebapp.config.json` sets the security headers (CSP, no framing). `deploy/teardown.ps1` removes the site with the rest of the resource group.
