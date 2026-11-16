/* Short explanations for the technical tabs ("why"), in English and Dutch. Facts (instructions, tool descriptions, rule constants, schemas,
   file:line) are never written here: they come from technical.js, which is generated from the source. */
window.TT = {
  tabs: {
    step: [
      { en: 'Overview', nl: 'Overzicht' }, { en: 'Instructions', nl: 'Instructies' }, { en: 'Tools & MCP', nl: 'Tools & MCP' },
      { en: 'Data & knowledge', nl: 'Data & kennis' }, { en: 'Code rules', nl: 'Coderegels' }, { en: 'Source', nl: 'Bron' }
    ],
    alarm: [
      { en: 'Overview', nl: 'Overzicht' }, { en: 'Contract', nl: 'Contract' }, { en: 'Host & API', nl: 'Host & API' },
      { en: 'Data', nl: 'Data' }, { en: 'Code rules', nl: 'Coderegels' }, { en: 'Source', nl: 'Bron' }
    ],
    trust: [
      { en: 'Overview', nl: 'Overzicht' }, { en: 'Foundry', nl: 'Foundry' }, { en: 'Security', nl: 'Beveiliging' },
      { en: 'Observability', nl: 'Observability' }, { en: 'Code rules', nl: 'Coderegels' }, { en: 'Source', nl: 'Bron' }
    ]
  },

  ui: {
    copy: { en: 'Copy', nl: 'Kopieer' }, copied: { en: 'Copied', nl: 'Gekopieerd' }, copyAll: { en: 'Copy all references', nl: 'Kopieer alle verwijzingen' },
    full: { en: 'Full text of the agent instructions', nl: 'Volledige tekst van de agent-instructies' },
    lines: { en: 'lines', nl: 'regels' },
    whyInstr: { en: 'Why the instructions look like this', nl: 'Waarom de instructies zo zijn opgebouwd' },
    schema: { en: 'Answer schema (strict JSON, registered in Foundry)', nl: 'Antwoordschema (strikte JSON, geregistreerd in Foundry)' },
    schemaNote: { en: 'The model must answer in exactly this shape, so code can check every field.', nl: 'Het model moet precies in deze vorm antwoorden, zodat code elk veld kan controleren.' },
    resultNote: { en: 'After the rules: code turns it into', nl: 'Na de regels: code maakt er dit van' },
    allowed: { en: 'Allowed tools (allow-list in the Foundry agent)', nl: 'Toegestane tools (allow-list in de Foundry-agent)' },
    of: { en: 'of', nl: 'van' },
    noTools: { en: 'No MCP tools at all', nl: 'Helemaal geen MCP-tools' },
    flow: { en: 'How a tool call travels', nl: 'Hoe een tool-aanroep reist' },
    whyMcp: { en: 'Why MCP?', nl: 'Waarom MCP?' },
    reads: { en: 'Reads from (Cosmos DB containers)', nl: 'Leest uit (Cosmos DB-containers)' },
    writes: { en: 'Writes at run time', nl: 'Schrijft tijdens een run' },
    container: { en: 'Container', nl: 'Container' }, pk: { en: 'Partition key', nl: 'Partitiesleutel' }, records: { en: 'Records', nl: 'Records' },
    kind: { seed: { en: 'seed file', nl: 'seedbestand' }, generated: { en: 'generated', nl: 'gegenereerd' }, runtime: { en: 'written at run time', nl: 'tijdens run geschreven' } },
    knowledge: { en: 'From data, through tools', nl: 'Uit data, via tools' },
    legend: { en: 'What happens when a check fails', nl: 'Wat er gebeurt als een controle faalt' },
    where: { en: 'Where the rules are applied to the model\u2019s answer', nl: 'Waar de regels op het antwoord van het model worden toegepast' },
    consts: { en: 'Constants the rules use', nl: 'Constanten die de regels gebruiken' },
    checks: { en: 'Checks in code (messages as written in the source)', nl: 'Controles in code (meldingen zoals in de bron)' },
    forbidden: { en: 'The notice may never contain', nl: 'Het bericht mag nooit bevatten' },
    methods: { en: 'Public rule functions', nl: 'Publieke regelfuncties' },
    sources: { en: 'Where to look in the repository (path:line, relative to the repo root)', nl: 'Waar je kijkt in de repository (pad:regel, relatief aan de repo-root)' },
    simulated: { en: 'All data is simulated and seeded by LeakOps.Seeder.', nl: 'Alle data is gesimuleerd en gezaaid door LeakOps.Seeder.' },
    inRun: { en: 'In the captured run', nl: 'In de vastgelegde run' },
    reChecks: { en: 'code re-checks (the code calls the same tools itself)', nl: 'code-controles (de code roept dezelfde tools zelf aan)' }
  },

  kinds: {
    rejected: { label: { en: 'rejected', nl: 'afgekeurd' }, cls: 'r', desc: { en: 'The draft is not used. The model is asked again once; then a safe template built from the data replaces it.', nl: 'Het concept wordt niet gebruikt. Het model wordt nog een keer gevraagd; daarna vervangt een veilig sjabloon uit de data het.' } },
    corrected: { label: { en: 'corrected', nl: 'gecorrigeerd' }, cls: 'y', desc: { en: 'Code fixes the answer with the fact from a tool and records a warning, so the trace shows what was changed.', nl: 'Code herstelt het antwoord met het feit uit een tool en legt een waarschuwing vast, zodat de trace laat zien wat er is aangepast.' } },
    needs_review: { label: { en: 'needs review', nl: 'needs review' }, cls: 'b', desc: { en: 'The workflow stops at this step with outcome needs_review. A person decides.', nl: 'De workflow stopt bij deze stap met uitkomst needs_review. Een mens beslist.' } },
    downgraded: { label: { en: 'downgraded', nl: 'teruggezet' }, cls: 'g', desc: { en: 'The verdict becomes uncertain with confidence at most 0.4, so a person looks at it.', nl: 'Het oordeel wordt uncertain met een zekerheid van hooguit 0,4, zodat een mens ernaar kijkt.' } }
  },

  mcpFlow: [
    { en: 'Foundry agent', nl: 'Foundry-agent' }, { en: 'project connection leakops-mcp', nl: 'projectverbinding leakops-mcp' },
    { en: 'MCP endpoint /runtime/webhooks/mcp', nl: 'MCP-endpoint /runtime/webhooks/mcp' }, { en: '[McpToolTrigger] C# tool', nl: '[McpToolTrigger] C#-tool' },
    { en: 'LeakOpsStore', nl: 'LeakOpsStore' }, { en: 'Cosmos DB (managed identity)', nl: 'Cosmos DB (managed identity)' }
  ],
  mcpWhy: [
    { h: { en: 'One tool contract for six agents', nl: 'Eén toolcontract voor zes agents' }, p: { en: 'A tool is written once in C# with a plain-language description. Every agent, and the code itself, uses the same definition.', nl: 'Een tool wordt één keer in C# geschreven met een beschrijving in gewone taal. Elke agent, en de code zelf, gebruikt dezelfde definitie.' } },
    { h: { en: 'Least privilege per agent', nl: 'Minste rechten per agent' }, p: { en: 'Each agent only sees its own allow-list (set in its Foundry definition). Communications and Scheduling have none and cannot act at all.', nl: 'Elke agent ziet alleen zijn eigen allow-list (ingesteld in zijn Foundry-definitie). Communications en Scheduling hebben er geen en kunnen helemaal niet handelen.' } },
    { h: { en: 'Facts stay in code and data', nl: 'Feiten blijven in code en data' }, p: { en: 'Tools read Cosmos DB with a managed identity. The model never sees a connection string or key; Foundry reaches the server through a project connection that holds the key.', nl: 'Tools lezen Cosmos DB met een managed identity. Het model ziet nooit een connection string of sleutel; Foundry bereikt de server via een projectverbinding die de sleutel bewaart.' } },
    { h: { en: 'The code can check the model', nl: 'De code kan het model controleren' }, p: { en: 'The Agents app calls the same tools itself and compares them with what the model said: one source of truth.', nl: 'De Agents-app roept dezelfde tools zelf aan en vergelijkt ze met wat het model zei: \u00E9\u00E9n bron van waarheid.' } },
    { h: { en: 'Swap the data, keep the agents', nl: 'Wissel de data, houd de agents' }, p: { en: 'A real system (for example a PI Web API tool for SCADA) can replace the simulation behind the same tool. Instructions and agents stay the same, and any MCP client can use the tools.', nl: 'Een echt systeem (bijvoorbeeld een PI Web API-tool voor SCADA) kan de simulatie achter dezelfde tool vervangen. Instructies en agents blijven gelijk en elke MCP-client kan de tools gebruiken.' } }
  ],
  endpoints: {
    title: { en: 'Which endpoint is which', nl: 'Welk endpoint is welk' },
    intro: { en: 'Several HTTP endpoints take part in one run (4 and 5 are the same URL). They are easy to mix up: only number 4 (data tools) and number 6 (knowledge base) are the MCP URLs you see on the agent in the Foundry portal.', nl: 'Meerdere HTTP-endpoints doen mee in één run (4 en 5 zijn dezelfde URL). Ze zijn makkelijk te verwarren: alleen nummer 4 (datatools) en nummer 6 (kennisbank) zijn de MCP-URL\u2019s die je bij de agent in het Foundry-portal ziet.' },
    cols: { caller: { en: 'Who calls whom', nl: 'Wie roept wie aan' }, endpoint: { en: 'Endpoint', nl: 'Endpoint' }, auth: { en: 'Sign-in', nl: 'Aanmelding' } },
    rows: [
      { n: 1, ref: 'host', from: { en: 'Operator console \u2192 Workflow host', nl: 'Operatorconsole \u2192 Workflow-host' }, endpoint: 'POST /api/runs  (text/event-stream)', auth: { en: 'X-Operator-Key header', nl: 'X-Operator-Key header' } },
      { n: 2, ref: 'gateway', from: { en: 'Workflow host \u2192 Agents app', nl: 'Workflow-host \u2192 Agents-app' }, endpoint: 'POST /api/agents/{step}', auth: { en: 'function key', nl: 'functiesleutel' } },
      { n: 3, ref: 'foundry', from: { en: 'Agents app \u2192 Foundry (agent by name)', nl: 'Agents-app \u2192 Foundry (agent op naam)' }, endpoint: 'https://evides-foundry-<suffix>.services.ai.azure.com/api/projects/evides-leakops', auth: { en: 'Entra ID, managed identity (role Foundry User)', nl: 'Entra ID, managed identity (rol Foundry User)' } },
      { n: 4, ref: 'mcp', mcp: true, from: { en: 'Foundry \u2192 MCP server (the agent\u2019s tools, called by Foundry itself)', nl: 'Foundry \u2192 MCP-server (de tools van de agent, door Foundry zelf aangeroepen)' }, endpoint: 'https://leakops-mcp-<suffix>.azurewebsites.net/runtime/webhooks/mcp', auth: { en: 'key held in project connection leakops-mcp', nl: 'sleutel in projectverbinding leakops-mcp' } },
      { n: 5, ref: 'client', from: { en: 'Agents app \u2192 MCP server (code re-checks, same URL as 4)', nl: 'Agents-app \u2192 MCP-server (code-controles, zelfde URL als 4)' }, endpoint: 'https://leakops-mcp-<suffix>.azurewebsites.net/runtime/webhooks/mcp', auth: { en: 'mcp_extension key (MCP_AUTH_KEY)', nl: 'mcp_extension-sleutel (MCP_AUTH_KEY)' } },
      { n: 6, ref: 'kb', from: { en: 'Foundry \u2192 Azure AI Search knowledge base (agents with knowledge; Foundry calls it itself)', nl: 'Foundry \u2192 Azure AI Search-kennisbank (agents met kennis; Foundry roept die zelf aan)' }, endpoint: 'https://evides-search-<suffix>.search.windows.net/knowledgebases/<knowledge base>/mcp?api-version=2026-08-01-preview', auth: { en: 'project managed identity (role Search Index Data Reader) via connection leakops-kb-ops or leakops-kb-comms', nl: 'managed identity van het project (rol Search Index Data Reader) via verbinding leakops-kb-ops of leakops-kb-comms' } }
    ],
    suffix: { en: '<suffix> is derived from the subscription and resource group (88546e in the current deployment).', nl: '<suffix> is afgeleid van het abonnement en de resourcegroep (88546e in de huidige deployment).' },
    portalTitle: { en: 'What you see on the agent in the Foundry portal', nl: 'Wat je bij de agent in het Foundry-portal ziet' },
    portalP: { en: 'The agent definition holds one MCP tool with server label leakops_mcp and the URL of endpoint 4. An agent with knowledge holds a second MCP tool, leakops_kb, with the URL of endpoint 6 and one allowed tool: knowledge_base_retrieve. The allowed-tools filters are the allow-lists on this tab; the keys and sign-in are not in the definition but in the project connections.', nl: 'De agentdefinitie bevat één MCP-tool met serverlabel leakops_mcp en de URL van endpoint 4. Een agent met kennis heeft een tweede MCP-tool, leakops_kb, met de URL van endpoint 6 en één toegestane tool: knowledge_base_retrieve. De filters voor toegestane tools zijn de allow-lists op deze tab; de sleutels en aanmelding staan niet in de definitie maar in de projectverbindingen.' },
    portalLabel: { en: 'Server label', nl: 'Serverlabel' },
    portalLabelKb: { en: 'Second server label (knowledge)', nl: 'Tweede serverlabel (kennis)' },
    thisTab: { en: 'Host & API tab', nl: 'Tab Host & API' },
    thisTabNote: { en: '1 and 2 are the workflow host and agents app, described on the Alarm slide. The MCP endpoints (4 and 6) belong to the Tools & MCP tab of every agent.', nl: '1 en 2 zijn de workflow-host en de agents-app, beschreven op de Alarm-slide. De MCP-endpoints (4 en 6) horen bij de tab Tools & MCP van elke agent.' }
  },
  // Knowledge (Foundry IQ): why documents next to data tools, how the pipeline works, and what it costs. The facts (names, documents,
  // versions, lines) come from technical.js; this is the explanation.
  knowledge: {
    whyTitle: { en: 'Why Foundry IQ next to the data tools?', nl: 'Waarom Foundry IQ naast de datatools?' },
    why: [
      { h: { en: 'Documents are not data', nl: 'Documenten zijn geen data' }, p: { en: 'Tools answer \u201Cwhat is the number?\u201D with an exact lookup. Documents answer \u201Cwhat is our rule?\u201D and \u201Cwhat happened last time?\u201D. That is text, written for people, and a tool with parameters cannot search it.', nl: 'Tools beantwoorden \u201Cwat is het getal?\u201D met een exacte opvraging. Documenten beantwoorden \u201Cwat is onze regel?\u201D en \u201Cwat gebeurde de vorige keer?\u201D. Dat is tekst, voor mensen geschreven, en een tool met parameters kan die niet doorzoeken.' } },
      { h: { en: 'One knowledge base, many agents', nl: 'Eén kennisbank, meerdere agents' }, p: { en: 'Triage, Diagnostic and Procurement share the ops knowledge base. Nobody connects each agent to each document set by hand.', nl: 'Triage, Diagnostic en Procurement delen de ops-kennisbank. Niemand koppelt elke agent met de hand aan elke documentenset.' } },
      { h: { en: 'Least privilege applies to knowledge too', nl: 'Minste rechten gelden ook voor kennis' }, p: { en: 'Communications gets its own knowledge base with only the public guidelines. It can never retrieve an internal procedure or an incident review, because its connection cannot reach them.', nl: 'Communications krijgt een eigen kennisbank met alleen de publieke richtlijnen. Die agent kan nooit een interne procedure of incidentreview ophalen, omdat zijn verbinding daar niet bij kan.' } },
      { h: { en: 'Agentic retrieval', nl: 'Agentic retrieval' }, p: { en: 'gpt-5.4 plans the search: it rewrites the question into subqueries, runs them as hybrid (keyword and vector) searches, reranks semantically and returns the passages with references.', nl: 'gpt-5.4 plant de zoektocht: het herschrijft de vraag naar deelvragen, voert ze uit als hybride (trefwoord en vector) zoekopdrachten, rangschikt semantisch en geeft de passages met bronverwijzing terug.' } },
      { h: { en: 'Citable and bounded', nl: 'Citeerbaar en begrensd' }, p: { en: 'Agents cite the document code in their evidence. A document informs judgement; numbers still come from tools and rules still run in code.', nl: 'Agents citeren de documentcode in hun bewijs. Een document informeert het oordeel; getallen komen nog steeds uit tools en regels draaien nog steeds in code.' } }
    ],
    pipelineTitle: { en: 'How a question reaches a document', nl: 'Hoe een vraag een document bereikt' },
    pipeline: [
      { en: 'Documents in a blob container (ops/ and comms/)', nl: 'Documenten in een blobcontainer (ops/ en comms/)' },
      { en: 'Knowledge source: indexer, chunking, embeddings', nl: 'Kennisbron: indexer, chunking, embeddings' },
      { en: 'Search index with semantic ranker', nl: 'Zoekindex met semantische ranker' },
      { en: 'Knowledge base: gpt-5.4 plans the subqueries', nl: 'Kennisbank: gpt-5.4 plant de deelvragen' },
      { en: 'MCP tool knowledge_base_retrieve', nl: 'MCP-tool knowledge_base_retrieve' },
      { en: 'Agent cites the document code', nl: 'Agent citeert de documentcode' }
    ],
    asked: { en: 'What the agent asked in the captured run', nl: 'Wat de agent vroeg in de vastgelegde run' },
    docs: { en: 'Documents behind this knowledge base', nl: 'Documenten achter deze kennisbank' },
    internal: { en: 'internal', nl: 'intern' },
    config: { en: 'Configuration', nl: 'Configuratie' },
    configRows: {
      planner: { en: 'Query planning', nl: 'Query planning' }, embedding: { en: 'Embeddings', nl: 'Embeddings' }, effort: { en: 'Retrieval reasoning effort', nl: 'Retrieval reasoning effort' },
      api: { en: 'Search API version (preview)', nl: 'Search API-versie (preview)' }, output: { en: 'Output', nl: 'Uitvoer' }, identity: { en: 'Sign-in', nl: 'Aanmelding' }
    },
    outputValue: { en: 'extractive data with references; the agent writes the answer', nl: 'extractieve data met bronverwijzingen; de agent schrijft het antwoord' },
    identityValue: { en: 'Entra ID only, no keys: search identity reads the blobs and calls the models; the project identity queries the index', nl: 'Alleen Entra ID, geen sleutels: de search-identiteit leest de blobs en roept de modellen aan; de projectidentiteit bevraagt de index' },
    tradeoffTitle: { en: 'What it costs', nl: 'Wat het kost' },
    tradeoff: [
      { en: 'Time: each knowledge lookup adds a model round trip. A full burst run went from about 47 s to about 66 s.', nl: 'Tijd: elke kennisopvraging voegt een modelronde toe. Een complete burst-run ging van ongeveer 47 s naar ongeveer 66 s.' },
      { en: 'Money: Azure AI Search Basic cannot be paused (about 70 EUR a month), so deploy-knowledge.ps1 has -Remove for between demos. Tokens for planning and embeddings cost cents.', nl: 'Geld: Azure AI Search Basic kan niet worden gepauzeerd (ongeveer 70 EUR per maand), dus deploy-knowledge.ps1 heeft -Remove voor tussen demo\u2019s door. Tokens voor planning en embeddings kosten centen.' },
      { en: 'Maturity: LLM query planning and the Search API version used here are preview features.', nl: 'Rijpheid: LLM-queryplanning en de hier gebruikte Search API-versie zijn previewfuncties.' }
    ],
    toolDesc: { en: 'Foundry IQ knowledge base: agentic retrieval over the documents of this agent\u2019s knowledge base. Returns passages with references. It is the only tool the knowledge connection allows.', nl: 'Foundry IQ-kennisbank: agentic retrieval over de documenten van de kennisbank van deze agent. Geeft passages met bronverwijzing. Het is de enige tool die de kennisverbinding toestaat.' },
    noKb: { en: 'This agent has no knowledge base.', nl: 'Deze agent heeft geen kennisbank.' },
    vsTitle: { en: 'Tools, knowledge and skills', nl: 'Tools, kennis en skills' },
    vs: [
      { k: { en: 'Tools (MCP)', nl: 'Tools (MCP)' }, cls: 't', a: { en: 'Exact data and actions', nl: 'Exacte data en acties' }, b: { en: 'Sensor readings, pipe data, stock, work orders. Cosmos DB behind code.', nl: 'Sensormetingen, leidingdata, voorraad, werkbonnen. Cosmos DB achter code.' } },
      { k: { en: 'Knowledge (Foundry IQ)', nl: 'Kennis (Foundry IQ)' }, cls: 'k', a: { en: 'What is true in our documents', nl: 'Wat waar is in onze documenten' }, b: { en: 'SOPs, policy, playbook, past incident reviews. Retrieved when needed, cited.', nl: 'SOP\u2019s, beleid, draaiboek, eerdere incidentreviews. Opgehaald als nodig, met bronvermelding.' } },
      { k: { en: 'Skills (preview)', nl: 'Skills (preview)' }, cls: 's', a: { en: 'How to do something', nl: 'Hoe je iets doet' }, b: { en: 'Versioned how-to\u2019s: notice checklist, isolation method, how to cite evidence.', nl: 'Geversioneerde werkwijzen: berichtchecklist, isolatiemethode, hoe je bewijs citeert.' } }
    ]
  },

  // Foundry Skills (preview): published from the repository, injected at registration.
  skills: {
    title: { en: 'Skills applied by this agent (Foundry Skills, preview)', nl: 'Skills die deze agent toepast (Foundry Skills, preview)' },
    p: { en: 'A skill is a versioned folder with a SKILL.md in the Foundry project. The registry tool publishes a new version when the text in git changed, then injects the default version into the agent\u2019s instructions (the \u201Cdirect injection\u201D mode of the docs). A changed skill therefore also gives the agent a new version.', nl: 'Een skill is een geversioneerde map met een SKILL.md in het Foundry-project. De registry-tool publiceert een nieuwe versie als de tekst in git veranderde en injecteert daarna de standaardversie in de instructies van de agent (de modus \u201Cdirect injection\u201D uit de documentatie). Een gewijzigde skill geeft de agent dus ook een nieuwe versie.' },
    showBody: { en: 'Show the skill text', nl: 'Toon de skilltekst' },
    knowledgeParagraph: { en: 'Knowledge paragraph (added when the knowledge layer is on)', nl: 'Kennisparagraaf (toegevoegd als de kennislaag aan staat)' },
    none: { en: 'This agent applies no skills.', nl: 'Deze agent past geen skills toe.' },
    routesTitle: { en: 'What we tried for Skills (preview)', nl: 'Wat we probeerden voor Skills (preview)' },
    routes: [
      { ok: true, en: 'Skills API: skills created from a SKILL.md, versions tracked, default version promoted, content read back. Works.', nl: 'Skills API: skills aangemaakt uit een SKILL.md, versies bijgehouden, standaardversie gepromoveerd, inhoud teruggelezen. Werkt.' },
      { ok: true, en: 'Direct injection: the registry puts the default version into the instructions. This is what the agents use.', nl: 'Direct injection: de registry zet de standaardversie in de instructies. Dit gebruiken de agents.' },
      { ok: false, en: 'Toolbox: skills are exposed as MCP resources, and the client must support them (for example Agent Framework\u2019s UseMcpSkills). Foundry\u2019s own MCP tool on a registered agent is not documented to do that, so it is not used here.', nl: 'Toolbox: skills worden aangeboden als MCP-resources en de client moet die ondersteunen (bijvoorbeeld UseMcpSkills van Agent Framework). De MCP-tool van Foundry zelf op een geregistreerde agent is daarvoor niet gedocumenteerd, dus hier niet gebruikt.' },
      { ok: false, en: 'Copilot harness prompt agent: a different hosting model (azd), not tried for these six agents.', nl: 'Copilot harness-promptagent: een ander hostingmodel (azd), niet geprobeerd voor deze zes agents.' }
    ],
    why: {
      'evides-notice-style': { en: 'The Communications agent applies the same checklist every time, and a communications specialist can improve it without touching code.', nl: 'De Communications-agent past elke keer dezelfde checklist toe en een communicatiespecialist kan die verbeteren zonder code aan te raken.' },
      'isolation-playbook': { en: 'The isolation method is a procedure (steps), unlike the SOP document (reference). Skills carry the steps; the knowledge base carries the facts about this network.', nl: 'De isolatiemethode is een procedure (stappen), anders dan het SOP-document (naslag). Skills dragen de stappen; de kennisbank draagt de feiten over dit netwerk.' },
      'evidence-citation': { en: 'Shared by two agents: one place to say how evidence is written, so Triage and Diagnostic cite in the same way.', nl: 'Gedeeld door twee agents: één plek om te zeggen hoe bewijs wordt geschreven, zodat Triage en Diagnostic op dezelfde manier citeren.' }
    }
  },

  kbAgent: {
    triage: { en: 'Triage asks one question after the data tools: how does this pattern compare with the false-alarm playbook and with earlier incidents? In the captured run it found the 2024 Dorpsweg burst. The answer is neutral evidence: only the planned-works register can close an alarm.', nl: 'Triage stelt na de datatools één vraag: hoe verhoudt dit patroon zich tot het draaiboek voor vals alarm en tot eerdere incidenten? In de vastgelegde run vond het de Dorpsweg-breuk van 2024. Het antwoord is neutraal bewijs: alleen het register van geplande werken kan een alarm sluiten.' },
    diagnostic: { en: 'Diagnostic asks about isolating and repairing this kind of pipe and about earlier incidents on the same segment. The SOP and the 2024 review are why the plan names a backup valve for the stiff V-0412-B and informs the care home first.', nl: 'Diagnostic vraagt naar het isoleren en repareren van dit type leiding en naar eerdere incidenten op hetzelfde segment. De SOP en de review van 2024 zijn waarom het plan een reserveafsluiter noemt voor de stroeve V-0412-B en eerst de zorginstelling informeert.' },
    communications: { en: 'Communications asks, before writing, how to write a notice. It is connected to a separate knowledge base that holds only the public guidelines, so internal documents (like the flushing procedure with times) are out of reach.', nl: 'Communications vraagt, voor het schrijven, hoe je een bericht schrijft. Het is verbonden met een aparte kennisbank met alleen de publieke richtlijnen, dus interne documenten (zoals de spoelprocedure met tijden) zijn buiten bereik.' },
    matchmaker: { en: 'No knowledge base. Who can go is a live fact (roster, certifications, position) and is answered by tools. A document would only add the risk of an outdated answer.', nl: 'Geen kennisbank. Wie kan gaan is een live feit (rooster, certificeringen, positie) en wordt door tools beantwoord. Een document zou alleen het risico op een verouderd antwoord toevoegen.' },
    procurement: { en: 'Procurement asks which purchasing policy applies (reserve first, express, approval, contingency stock). The rationale cites POL-PROC-03. The EUR 2,500 approval limit stays a constant in code.', nl: 'Procurement vraagt welk inkoopbeleid geldt (eerst reserveren, express, goedkeuring, reservevoorraad). De onderbouwing citeert POL-PROC-03. De goedkeuringsgrens van EUR 2.500 blijft een constante in code.' },
    scheduling: { en: 'No knowledge base. The schedule is computed by code from tool facts; the model only writes the briefing from finished facts. Nothing to look up.', nl: 'Geen kennisbank. De planning wordt door code berekend uit toolfeiten; het model schrijft alleen de briefing uit afgeronde feiten. Niets op te zoeken.' }
  },

  stack: {
    title: { en: 'Four layers around one model, scoped per agent', nl: 'Vier lagen rond één model, per agent afgebakend' },
    matrix: { en: 'What each agent is connected to (from the registered definitions and the captured run)', nl: 'Waarmee elke agent verbonden is (uit de geregistreerde definities en de vastgelegde run)' },
    memory: { en: 'Memory: none by design. The agents keep no conversation between calls. The workflow runner passes each result into the next agent\u2019s input, and anything that must last lives in Cosmos DB and Waterstoring. That is also why a repeated run books nothing twice.', nl: 'Geheugen: bewust geen. De agents bewaren geen gesprek tussen aanroepen. De workflow-runner geeft elk resultaat door in de invoer van de volgende agent en alles wat moet blijven staat in Cosmos DB en Waterstoring. Daarom boekt een herhaalde run ook niets dubbel.' },
    layers: { en: 'The layers', nl: 'De lagen' },
    model: { h: { en: 'Model: gpt-5.4', nl: 'Model: gpt-5.4' }, p: { en: 'One Foundry deployment (Global Standard) for all six agents. It judges and writes. It also plans the knowledge searches.', nl: 'Eén Foundry-deployment (Global Standard) voor alle zes agents. Het oordeelt en schrijft. Het plant ook de kennis-zoekopdrachten.' } },
    tools: { h: { en: 'Tools: MCP, exact data', nl: 'Tools: MCP, exacte data' }, p: { en: '19 C# tools on an Azure Functions MCP server, reading Cosmos DB. Per-agent allow-lists. The code calls the same tools to check the model.', nl: '19 C#-tools op een Azure Functions MCP-server, lezend uit Cosmos DB. Allow-lists per agent. De code roept dezelfde tools aan om het model te controleren.' } },
    knowledge: { h: { en: 'Knowledge: Foundry IQ, documents', nl: 'Kennis: Foundry IQ, documenten' }, p: { en: 'Two knowledge bases on Azure AI Search with 8 simulated documents. Agentic retrieval, citations, one connection per knowledge base.', nl: 'Twee kennisbanken op Azure AI Search met 8 gesimuleerde documenten. Agentic retrieval, bronverwijzingen, één verbinding per kennisbank.' } },
    skills: { h: { en: 'Skills: Foundry Skills (preview), how-to', nl: 'Skills: Foundry Skills (preview), werkwijze' }, p: { en: '3 versioned skills, published from git and injected into the instructions at registration. Honest note: injection, not native loading.', nl: '3 geversioneerde skills, gepubliceerd uit git en bij registratie in de instructies geïnjecteerd. Eerlijke kanttekening: injectie, niet native laden.' } },
    which: { en: 'Which layer answers which need', nl: 'Welke laag beantwoordt welke behoefte' },
    needs: [
      { need: { en: 'What was the pressure at PS-101?', nl: 'Wat was de druk bij PS-101?' }, cls: 't', layer: { en: 'Tool', nl: 'Tool' }, example: { en: 'get-sensor-readings', nl: 'get-sensor-readings' } },
      { need: { en: 'What does our SOP say about a stiff valve?', nl: 'Wat zegt onze SOP over een stroeve afsluiter?' }, cls: 'k', layer: { en: 'Knowledge', nl: 'Kennis' }, example: { en: 'SOP-ISO-01, INC-2024-0217', nl: 'SOP-ISO-01, INC-2024-0217' } },
      { need: { en: 'How do we write a resident notice?', nl: 'Hoe schrijven we een bericht voor bewoners?' }, cls: 's', layer: { en: 'Skill (and a guideline)', nl: 'Skill (en een richtlijn)' }, example: { en: 'evides-notice-style, COMM-STYLE-01', nl: 'evides-notice-style, COMM-STYLE-01' } },
      { need: { en: 'Book the technician', nl: 'Boek de monteur' }, cls: 'r', layer: { en: 'Code, never the model', nl: 'Code, nooit het model' }, example: { en: 'create-work-order', nl: 'create-work-order' } }
    ],
    value: { en: 'The same model becomes useful for a water utility because it gets live facts, the company\u2019s documents and the company\u2019s way of working, each scoped per agent, with code owning the numbers and the rules.', nl: 'Hetzelfde model wordt bruikbaar voor een waterbedrijf doordat het live feiten, de documenten van het bedrijf en de werkwijze van het bedrijf krijgt, elk per agent afgebakend, waarbij code de getallen en regels beheert.' },
    inputs: {
      triage: { en: 'the alarm', nl: 'het alarm' }, diagnostic: { en: 'alarm + triage verdict', nl: 'alarm + triage-oordeel' },
      communications: { en: 'public facts only', nl: 'alleen publieke feiten' }, matchmaker: { en: 'the diagnosis', nl: 'de diagnose' },
      procurement: { en: 'the diagnosis', nl: 'de diagnose' }, scheduling: { en: 'diagnosis + match + parts package', nl: 'diagnose + match + onderdelenpakket' }
    }
  },

  agent: {
    triage: {
      instr: [
        { en: 'Numbered steps make every investigation the same: sensors, readings, planned works, weather, usage history. Runs stay comparable.', nl: 'Genummerde stappen maken elk onderzoek gelijk: sensoren, metingen, geplande werken, weer, verbruikshistorie. Runs blijven vergelijkbaar.' },
        { en: 'Both signatures are spelled out, with the key physical clue: inflow rises in a leak, and falls when valves are closed for planned work.', nl: 'Beide handtekeningen staan uitgeschreven, met de fysieke aanwijzing: instroom stijgt bij een lek en daalt als kleppen voor gepland werk dicht zijn.' },
        { en: '\u201CNever guess a number\u201D and uncertain is an allowed answer: conflicting evidence or a missing sensor means the model says it does not know.', nl: '\u201CNever guess a number\u201D en uncertain is een toegestaan antwoord: tegenstrijdig bewijs of een ontbrekende sensor betekent dat het model zegt het niet te weten.' },
        { en: 'The alarm text is data: instructions inside an alarm are ignored (prompt injection). Foundry prompt shields add a second layer.', nl: 'De alarmtekst is data: instructies in een alarm worden genegeerd (prompt-injectie). Foundry prompt shields voegen een tweede laag toe.' },
        { en: 'Evidence must name the alarm sensor and quote readings with units, so a person can verify each claim.', nl: 'Bewijs moet de alarmsensor noemen en metingen met eenheden citeren, zodat een mens elke bewering kan nalopen.' }
      ],
      tools: { en: 'Read-only. The five tools answer exactly the five questions of the procedure; no tool can change anything.', nl: 'Alleen-lezen. De vijf tools beantwoorden precies de vijf vragen van de procedure; geen enkele tool kan iets wijzigen.' },
      knowledge: { en: 'Triage knows what normal looks like from get-usage-history (expected flow and pressure per hour and day type, plus known non-leak deviations), the planned-works register and the weather. These are facts with numbers; the model reasons over them but does not remember them.', nl: 'Triage weet hoe normaal eruitziet via get-usage-history (verwachte instroom en druk per uur en dagtype, plus bekende afwijkingen die geen lek waren), het register van geplande werken en het weer. Dat zijn feiten met getallen; het model redeneert erover maar onthoudt ze niet.' }
    },
    diagnostic: {
      instr: [
        { en: 'The Triage suspect is a hypothesis: step 1 ranks all segments by pressure drop and switches segment when another drops at least twice as much.', nl: 'De verdachte van Triage is een hypothese: stap 1 rangschikt alle segmenten op drukval en wisselt van segment als een ander minstens twee keer zoveel zakt.' },
        { en: '\u201CNever calculate a position or a severity yourself\u201D: the locator and the severity rubric are tools, so numbers are reproducible and testable.', nl: '\u201CNever calculate a position or a severity yourself\u201D: de locator en de ernstrubriek zijn tools, dus getallen zijn reproduceerbaar en testbaar.' },
        { en: 'The burst rule (inflow 40 m\u00B3/h above expected, or a 1.0 bar drop) is in the prompt and as constants in DiagnosticRules, so both agree.', nl: 'De burst-regel (instroom 40 m\u00B3/h boven verwacht, of 1,0 bar drukval) staat in de prompt en als constanten in DiagnosticRules, dus beide komen overeen.' },
        { en: 'Isolation covers both sides of the segment, nearest valve first; a valve not operated for two years or not open is flagged with a backup.', nl: 'Isolatie omvat beide zijden van het segment, dichtstbijzijnde afsluiter eerst; een afsluiter die twee jaar niet is bediend of niet open is wordt gemeld met een reserve.' },
        { en: 'The answer is strict JSON with evidence that quotes the actual numbers.', nl: 'Het antwoord is strikte JSON met bewijs dat de echte getallen citeert.' }
      ],
      tools: { en: 'Eight read-only tools: GIS data, the pressure-gradient locator, the severity rubric and the repair-procedure catalogue. Calculations live in tools, not in the model.', nl: 'Acht alleen-lezen tools: GIS-data, de drukgradi\u00EBnt-locator, de ernstrubriek en de catalogus met reparatieprocedures. Berekeningen zitten in tools, niet in het model.' },
      knowledge: { en: 'The pipe network (segments, valves, sensors), the severity rubric (S1 to S4 from breach type, customers, critical customers and diameter) and the repair procedures per material, diameter and breach type. Structured data where an exact lookup beats semantic search.', nl: 'Het leidingnet (segmenten, afsluiters, sensoren), de ernstrubriek (S1 tot S4 uit breuktype, klanten, kritieke klanten en diameter) en de reparatieprocedures per materiaal, diameter en breuktype. Gestructureerde data waar een exacte opvraging het wint van semantisch zoeken.' }
    },
    communications: {
      instr: [
        { en: 'The model only receives public facts. Internal ids, costs and severity never reach it, so it cannot leak them.', nl: 'Het model krijgt alleen publieke feiten. Interne id\u2019s, kosten en ernst bereiken het nooit, dus het kan ze niet lekken.' },
        { en: 'Plain language: Dutch at B1 level addressing people as \u201Cu\u201D, then English. Say honestly what is known: a suspected burst, Evides is working on it.', nl: 'Gewone taal: Nederlands op B1-niveau met \u201Cu\u201D, daarna Engels. Zeg eerlijk wat bekend is: een vermoedelijke breuk, Evides werkt eraan.' },
        { en: 'Hard limits: no times or durations, no blame, no customer names, nothing about whether the water is safe. Health advice is not for a model.', nl: 'Harde grenzen: geen tijden of duren, geen schuldvraag, geen klantnamen, niets over of het water veilig is. Gezondheidsadvies is niet aan een model.' },
        { en: 'Advice is left empty on purpose: the system adds the standard advice and the help line itself.', nl: 'Het advies blijft bewust leeg: het systeem voegt zelf het standaardadvies en het servicenummer toe.' },
        { en: 'Length limits (90 / 500 characters) are in the prompt and enforced again in code.', nl: 'Lengtegrenzen (90 / 500 tekens) staan in de prompt en worden in code opnieuw afgedwongen.' }
      ],
      tools: { en: 'No data tools, on purpose. The agent can only answer; it cannot read data or publish. Its one tool is the knowledge base with the public guidelines. Publishing is done by code through the Waterstoring ingest API, after the check.', nl: 'Geen datatools, bewust. De agent kan alleen antwoorden; hij kan geen data lezen of publiceren. Zijn enige tool is de kennisbank met de publieke richtlijnen. Publiceren doet code via de Waterstoring ingest-API, na de controle.' },
      knowledge: { en: 'It knows only what the prompt contains: the public facts from CommunicationsRules.PublicFactsFor (place, streets, postal codes, number of connections, a yes/no for vulnerable customers, the help line). Less knowledge means less to leak.', nl: 'Het weet alleen wat de prompt bevat: de publieke feiten uit CommunicationsRules.PublicFactsFor (plaats, straten, postcodes, aantal aansluitingen, een ja/nee voor kwetsbare klanten, het servicenummer). Minder kennis betekent minder om te lekken.' }
    },
    matchmaker: {
      instr: [
        { en: 'Step 1 fetches the repair procedure, so the required certifications and repair hours come from data, not from the model.', nl: 'Stap 1 haalt de reparatieprocedure op, dus de vereiste certificeringen en reparatie-uren komen uit data, niet uit het model.' },
        { en: 'availableOnly stays false on purpose: the model sees who was ruled out and why, and can explain it to the dispatcher.', nl: 'availableOnly blijft bewust false: het model ziet wie is uitgesloten en waarom, en kan dat aan de planner uitleggen.' },
        { en: 'Eligible means on shift, not busy and holding all certifications; only then the shortest travel time counts. Closer is not better if not certified.', nl: 'Geschikt betekent in dienst, niet bezet en alle certificeringen; pas dan telt de kortste reistijd. Dichterbij is niet beter als iemand niet gecertificeerd is.' },
        { en: 'Shift end versus repair time and excavator towing must be mentioned, so a handover is planned early.', nl: 'Einde dienst versus reparatietijd en het slepen van een graafmachine moeten worden genoemd, zodat een overdracht vroeg wordt gepland.' },
        { en: 'If nobody is eligible the model leaves the primary empty and says who comes closest: a person decides.', nl: 'Als niemand geschikt is laat het model de primaire leeg en zegt wie het dichtst komt: een mens beslist.' }
      ],
      tools: { en: 'Two read-only tools: the repair procedure and the live roster. The agent books nobody; the work order is created later by code.', nl: 'Twee alleen-lezen tools: de reparatieprocedure en het live rooster. De agent boekt niemand; de werkbon maakt later de code aan.' },
      knowledge: { en: 'Technician certifications, shifts, running jobs and last known positions (the roster), plus the repair procedure that says which certifications a repair needs. Availability changes by the hour, so it must be read live, never remembered.', nl: 'Certificeringen, diensten, lopende klussen en laatst bekende posities van monteurs (het rooster), plus de reparatieprocedure die zegt welke certificeringen een reparatie nodig heeft. Beschikbaarheid verandert per uur, dus moet live worden gelezen en nooit onthouden.' }
    },
    procurement: {
      instr: [
        { en: 'The agent only decides what is needed. Reserving and ordering is done afterwards by code, after it has checked the answer.', nl: 'De agent beslist alleen wat nodig is. Reserveren en bestellen doet de code achteraf, nadat het antwoord is gecontroleerd.' },
        { en: 'Parts marked always are mandatory; conditional parts are included only when the diagnosis meets the condition (severity, breach type, valve state).', nl: 'Onderdelen met always zijn verplicht; voorwaardelijke onderdelen alleen als de diagnose aan de voorwaarde voldoet (ernst, breuktype, afsluiterstatus).' },
        { en: 'Equipment: the smallest item that meets the capacity and is available at the alarm time (equipment not yet back shows 0).', nl: 'Materieel: het kleinste item dat aan de capaciteit voldoet en beschikbaar is op het alarmmoment (materieel dat nog niet terug is toont 0).' },
        { en: 'The model must not claim the stock is sufficient or the package complete: the real outcome comes from the booking.', nl: 'Het model mag niet beweren dat de voorraad volstaat of het pakket compleet is: de echte uitkomst komt uit de boeking.' },
        { en: '\u201CNever guess a SKU, a stock level or a lead time\u201D: all come from tools.', nl: '\u201CNever guess a SKU, a stock level or a lead time\u201D: alles komt uit tools.' }
      ],
      tools: { en: 'Two read-only tools: the repair procedure and the inventory. Writes (reserve-stock, create-purchase-order) are MCP tools too, but only the code calls them, never the model.', nl: 'Twee alleen-lezen tools: de reparatieprocedure en de voorraad. Schrijven (reserve-stock, create-purchase-order) zijn ook MCP-tools, maar alleen de code roept ze aan, nooit het model.' },
      knowledge: { en: 'The repair procedure (parts with conditions, equipment types), the inventory with reorder levels and equipment availability, warehouses and supplier lead times. Exact SKUs and quantities cannot be approximated, so this is a lookup, not a search.', nl: 'De reparatieprocedure (onderdelen met voorwaarden, materieeltypes), de voorraad met bestelniveaus en materieelbeschikbaarheid, magazijnen en levertijden van leveranciers. Exacte SKU\u2019s en aantallen kun je niet benaderen, dus dit is opzoeken, geen zoeken.' }
    },
    scheduling: {
      instr: [
        { en: 'The schedule is computed by code (SchedulingRules). The model only writes the phone briefing for the technician from the finished facts.', nl: 'De planning wordt door code berekend (SchedulingRules). Het model schrijft alleen de telefoonbriefing voor de monteur uit de afgeronde feiten.' },
        { en: '\u201CUse ONLY times that appear in the facts\u201D: the model may not calculate or invent a time, and never writes a date.', nl: '\u201CUse ONLY times that appear in the facts\u201D: het model mag geen tijd berekenen of verzinnen en schrijft nooit een datum.' },
        { en: 'Valves are named in the plan\u2019s order and only as things to close; telling the technician to skip something is forbidden.', nl: 'Afsluiters worden genoemd in de volgorde van het plan en alleen als iets om te sluiten; de monteur zeggen iets over te slaan is verboden.' },
        { en: 'Warnings that matter on site are mentioned: a stiff valve, a part not in the van yet, a shift that ends before the work is done.', nl: 'Waarschuwingen die ter plaatse tellen worden genoemd: een stroeve afsluiter, een onderdeel dat nog niet in de bus ligt, een dienst die eindigt voor het werk klaar is.' },
        { en: 'No costs, severity codes or customer names on a technician\u2019s phone.', nl: 'Geen kosten, ernstcodes of klantnamen op de telefoon van een monteur.' }
      ],
      tools: { en: 'None for the model. The code calls get-travel-estimate and create-work-order itself, so travel time and dispatch are deterministic.', nl: 'Geen voor het model. De code roept get-travel-estimate en create-work-order zelf aan, dus reistijd en inzet zijn deterministisch.' },
      knowledge: { en: 'It knows only the finished schedule that is put in its prompt: route with times, valves in order, what to collect, what is still missing. Road speeds (night, normal, rush hour) live in the travel tool; the time constants live in SchedulingRules.', nl: 'Het weet alleen het afgeronde schema dat in de prompt staat: route met tijden, afsluiters in volgorde, wat op te halen, wat nog ontbreekt. Wegsnelheden (nacht, normaal, spits) zitten in de reistool; de tijdconstanten in SchedulingRules.' }
    }
  },

  alarm: {
    contract: {
      h: { en: 'The contract: one alarm in, six steps out', nl: 'Het contract: één alarm erin, zes stappen eruit' },
      p: { en: 'The workflow host accepts either a scenario id or a complete alarm. The alarm is the only input of the whole chain.', nl: 'De workflow-host accepteert een scenario-id of een compleet alarm. Het alarm is de enige invoer van de hele keten.' },
      steps: { en: 'The six steps, always in this order (route = name of the step)', nl: 'De zes stappen, altijd in deze volgorde (route = naam van de stap)' },
      textIsData: { en: 'The alarm text is data, never an instruction: it is passed to Triage with that warning and checked again by the rules.', nl: 'De alarmtekst is data, nooit een instructie: hij gaat met die waarschuwing naar Triage en wordt door de regels opnieuw gecontroleerd.' }
    },
    host: [
      { h: { en: 'Endpoints', nl: 'Endpoints' }, items: [
        { en: 'POST /api/runs with {scenarioId} or {alarm}: a text/event-stream with one event per step', nl: 'POST /api/runs met {scenarioId} of {alarm}: een text/event-stream met één event per stap' },
        { en: 'POST /api/runs?wait=true waits for the run and returns the summary as JSON (handy for scripts)', nl: 'POST /api/runs?wait=true wacht op de run en geeft de samenvatting als JSON (handig voor scripts)' },
        { en: 'GET /api/scenarios, GET /api/runs, GET /api/runs/{runId}, GET /health', nl: 'GET /api/scenarios, GET /api/runs, GET /api/runs/{runId}, GET /health' }] },
      { h: { en: 'How it runs', nl: 'Hoe het draait' }, items: [
        { en: 'WorkflowRunner calls the agents in order through AgentGateway: HTTP POST /api/agents/{step} on the Agents function app, with a function key', nl: 'WorkflowRunner roept de agents in volgorde aan via AgentGateway: HTTP POST /api/agents/{step} op de Agents-functie-app, met een functiesleutel' },
        { en: 'ASP.NET on App Service (Linux, .NET 10), managed identity, health check on /health', nl: 'ASP.NET op App Service (Linux, .NET 10), managed identity, health check op /health' },
        { en: 'Progress events: run_started, step_started, step_completed, step_failed, run_completed', nl: 'Voortgangsevents: run_started, step_started, step_completed, step_failed, run_completed' }] }
    ],
    data: {
      p: { en: 'Two scenarios ship with the demo: a burst on a cast-iron main during frost, and a pressure dip caused by planned valve work (a false alarm). The alarm is what a SCADA system would send; here it is simulated.', nl: 'Twee scenario\u2019s horen bij de demo: een breuk in een gietijzeren hoofdleiding tijdens vorst, en een drukdaling door gepland afsluiterwerk (vals alarm). Het alarm is wat een SCADA-systeem zou sturen; hier is het gesimuleerd.' },
      real: { en: 'Real data would come through a tool behind the same MCP contract, for example a PI Web API tool; the agents do not change.', nl: 'Echte data komt via een tool achter hetzelfde MCP-contract, bijvoorbeeld een PI Web API-tool; de agents veranderen niet.' }
    },
    rules: [
      { en: 'The workflow API refuses calls without the operator key (X-Operator-Key, compared in fixed time). It is required outside Development.', nl: 'De workflow-API weigert aanroepen zonder operator-sleutel (X-Operator-Key, in vaste tijd vergeleken). Buiten Development is die verplicht.' },
      { en: 'RunGuard allows one run per alarm id at a time (HTTP 409 otherwise).', nl: 'RunGuard staat één run per alarm-id tegelijk toe (anders HTTP 409).' },
      { en: 'A run is not tied to the browser: closing the page cannot leave a half-booked incident behind.', nl: 'Een run is niet aan de browser gekoppeld: de pagina sluiten kan geen half geboekt incident achterlaten.' },
      { en: 'Four outcomes: completed, closed_false_alarm, needs_review, failed. Anything but completed stops the chain and says why.', nl: 'Vier uitkomsten: completed, closed_false_alarm, needs_review, failed. Alles behalve completed stopt de keten en zegt waarom.' }
    ]
  },

  trust: {
    foundry: [
      { h: { en: 'Registered, not hidden in code', nl: 'Geregistreerd, niet verstopt in code' }, items: [
        { en: 'LeakOps.AgentRegistry creates each agent in the Foundry project: model, instructions, strict JSON answer schema and the MCP tool with its allow-list.', nl: 'LeakOps.AgentRegistry maakt elke agent aan in het Foundry-project: model, instructies, strikt JSON-antwoordschema en de MCP-tool met zijn allow-list.' },
        { en: 'A hash of the whole definition decides whether a new version is needed, so versions only change when something really changed.', nl: 'Een hash van de hele definitie bepaalt of een nieuwe versie nodig is, dus versies veranderen alleen als er echt iets veranderde.' },
        { en: 'At run time the code refers to the agent by name (the latest version). The same definition is what you see, test and trace in the portal.', nl: 'Tijdens een run verwijst de code naar de agent op naam (de nieuwste versie). Dezelfde definitie zie, test en trace je in het portal.' }] },
      { h: { en: 'Model', nl: 'Model' }, items: [
        { en: 'One gpt-5.4 deployment (Global Standard, pay per token, 150k tokens a minute) for all six agents.', nl: 'Eén gpt-5.4-deployment (Global Standard, betalen per token, 150k tokens per minuut) voor alle zes agents.' },
        { en: 'Output is capped per call so runs do not hit the tokens-per-minute limit.', nl: 'De uitvoer wordt per aanroep begrensd zodat runs de tokens-per-minuut-limiet niet raken.' }] },
      { h: { en: 'Knowledge and skills', nl: 'Kennis en skills' }, items: [
        { en: 'Foundry IQ: two knowledge bases on Azure AI Search, reached through two project connections. Each agent gets only its own (Communications never sees internal documents).', nl: 'Foundry IQ: twee kennisbanken op Azure AI Search, bereikt via twee projectverbindingen. Elke agent krijgt alleen de eigen (Communications ziet nooit interne documenten).' },
        { en: 'Foundry Skills (preview): three versioned skills, published from git and injected into the instructions at registration. A changed skill gives the agent a new version.', nl: 'Foundry Skills (preview): drie geversioneerde skills, uit git gepubliceerd en bij registratie in de instructies geïnjecteerd. Een gewijzigde skill geeft de agent een nieuwe versie.' }] }
    ],
    security: [
      { h: { en: 'Identity', nl: 'Identiteit' }, items: [
        { en: 'Entra ID only on Foundry: local (key) authentication is switched off.', nl: 'Alleen Entra ID op Foundry: lokale (sleutel)authenticatie staat uit.' },
        { en: 'Managed identities between Azure services: the MCP app and Waterstoring reach Cosmos DB with a data role; the Agents app reaches Foundry with the Foundry User role; storage has no shared keys.', nl: 'Managed identities tussen Azure-diensten: de MCP-app en Waterstoring bereiken Cosmos DB met een datarol; de Agents-app bereikt Foundry met de rol Foundry User; opslag heeft geen gedeelde sleutels.' },
        { en: 'Azure AI Search is Entra ID only too: its identity reads the blobs and calls the models, and the Foundry project identity (Search Index Data Reader) queries the knowledge bases.', nl: 'Ook Azure AI Search werkt alleen met Entra ID: zijn identiteit leest de blobs en roept de modellen aan en de projectidentiteit van Foundry (Search Index Data Reader) bevraagt de kennisbanken.' }] },
      { h: { en: 'Keys only where a key is needed', nl: 'Sleutels alleen waar een sleutel nodig is' }, items: [
        { en: 'The MCP key lives in the Foundry project connection leakops-mcp, never in an agent definition.', nl: 'De MCP-sleutel staat in de Foundry-projectverbinding leakops-mcp, nooit in een agentdefinitie.' },
        { en: 'A function key from the workflow host to the agents, an API key to the Waterstoring ingest API, and the operator key for the console.', nl: 'Een functiesleutel van de workflow-host naar de agents, een API-sleutel naar de Waterstoring ingest-API en de operator-sleutel voor de console.' }] },
      { h: { en: 'Content filter', nl: 'Contentfilter' }, items: [
        { en: 'The model deployment uses the Microsoft.DefaultV2 filter: jailbreak and injection prompts are blocked before the model sees them (prompt shields).', nl: 'De modeldeployment gebruikt het Microsoft.DefaultV2-filter: jailbreak- en injectieprompts worden geblokkeerd voordat het model ze ziet (prompt shields).' },
        { en: 'A softer attack that passes the filter, such as an operator note inside an alarm, is still harmless because alarm text is data.', nl: 'Een zachtere aanval die het filter passeert, zoals een operatornotitie in een alarm, is nog steeds onschadelijk omdat alarmtekst data is.' }] }
    ],
    observe: [
      { h: { en: 'Tracing', nl: 'Tracing' }, items: [
        { en: 'A project connection to Application Insights switches tracing on: every run shows as spans, for example invoke_agent, chat gpt-5.4 and execute_tool leakops_mcp.<tool>, with duration and tokens.', nl: 'Een projectverbinding met Application Insights zet tracing aan: elke run verschijnt als spans, bijvoorbeeld invoke_agent, chat gpt-5.4 en execute_tool leakops_mcp.<tool>, met duur en tokens.' },
        { en: 'The runtime also keeps its own trace per step (tools, tokens, duration): the numbers you saw on the previous slides.', nl: 'De runtime houdt ook een eigen trace per stap bij (tools, tokens, duur): de getallen die je op de vorige slides zag.' }] },
      { h: { en: 'Evaluations', nl: 'Evaluaties' }, items: [
        { en: 'Test questions with expected answers run against the registered agents: leakops-triage-scenarios (4 of 4 pass), leakops-diagnostic-burst (1 of 1) and three knowledge evaluations for Diagnostic, Communications and Procurement (each 1 of 1, including the check that the knowledge base tool calls work).', nl: 'Testvragen met verwachte antwoorden draaien tegen de geregistreerde agents: leakops-triage-scenarios (4 van 4 geslaagd), leakops-diagnostic-burst (1 van 1) en drie kennisevaluaties voor Diagnostic, Communications en Procurement (elk 1 van 1, inclusief de controle dat de tool-aanroepen naar de kennisbank werken).' },
        { en: 'Checks: the exact expected value, plus AI judges task_adherence and tool_call_success.', nl: 'Controles: de exacte verwachte waarde, plus AI-beoordelaars task_adherence en tool_call_success.' },
        { en: 'This is how a change to an instruction is tested before it goes live.', nl: 'Zo test je een wijziging in een instructie voordat die live gaat.' }] }
    ]
  }
};
