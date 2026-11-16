// Generates demo-site/js/technical.js from the real source of the demo (src/, data/, deploy/, evals/).
// Run from anywhere:  node evides-leakops/demo-site/tools/build-technical.js
// Nothing is typed in by hand: instructions, tool descriptions, rule constants, schemas and line numbers are read from the files,
// so the site always shows what the code really says. If the source moves, this script fails instead of printing stale references.
'use strict';
const fs = require('fs');
const path = require('path');

const root = path.resolve(__dirname, '..', '..');            // evides-leakops
const prefix = 'evides-leakops/';
const read = rel => fs.readFileSync(path.join(root, rel), 'utf8').split(/\r?\n/);
const ref = (rel, line) => ({ path: prefix + rel, line });
const fail = msg => { throw new Error('build-technical: ' + msg); };

function find(rel, re, from = 0) {
  const lines = read(rel);
  for (let i = from; i < lines.length; i++) if (re.test(lines[i])) return i + 1;
  return fail(`${rel}: no line matches ${re}`);
}
const lineRef = (rel, re) => ref(rel, find(rel, re));

// ---------- agents: spec, instructions, allowed tools ----------
const STEPS = [
  ['triage', 'TriageAgent', 'src/LeakOps.Agents/TriageAgent.cs'],
  ['diagnostic', 'DiagnosticAgent', 'src/LeakOps.Agents/DiagnosticAgent.cs'],
  ['communications', 'CommunicationsAgent', 'src/LeakOps.Agents/CommunicationsAgent.cs'],
  ['matchmaker', 'ResourceMatchmakerAgent', 'src/LeakOps.Agents/ResourceMatchmakerAgent.cs'],
  ['procurement', 'ProcurementAgent', 'src/LeakOps.Agents/ProcurementAgent.cs'],
  ['scheduling', 'SchedulingAgent', 'src/LeakOps.Agents/SchedulingAgent.cs'],
];
// Which rule files check which agent (the rule classes are named after the agents they guard).
const RULES = {
  triage: ['src/LeakOps.Contracts/Logic/TriageRules.cs'],
  diagnostic: ['src/LeakOps.Contracts/Logic/DiagnosticRules.cs', 'src/LeakOps.Contracts/Logic/SeverityRubric.cs', 'src/LeakOps.Contracts/Logic/LeakLocator.cs'],
  communications: ['src/LeakOps.Contracts/Logic/CommunicationsRules.cs'],
  matchmaker: ['src/LeakOps.Contracts/Logic/MatchmakerRules.cs', 'src/LeakOps.Contracts/Logic/WorkforceAvailability.cs'],
  procurement: ['src/LeakOps.Contracts/Logic/ProcurementRules.cs', 'src/LeakOps.Contracts/Logic/Stock.cs'],
  scheduling: ['src/LeakOps.Contracts/Logic/SchedulingRules.cs', 'src/LeakOps.Contracts/Logic/TravelEstimator.cs'],
};

// The string constants of KnowledgeBases and SkillNames (names of connections, knowledge bases and skills).
function runtimeConstants() {
  const out = {};
  for (const l of read('src/LeakOps.Agents/LeakOpsAgentRuntime.cs')) { const m = l.match(/public const string (\w+) = "([^"]+)";/); if (m) out[m[1]] = m[2]; }
  return out;
}

function parseSpec(rel) {
  const lines = read(rel);
  const specLine = lines.findIndex(l => /public static readonly AgentSpec Spec = new\(/.test(l));
  if (specLine < 0) fail(`${rel}: no AgentSpec`);
  const start = lines.findIndex((l, i) => i > specLine && /Instructions: """/.test(l));
  const end = lines.findIndex((l, i) => i > start && /^\s*""",?\s*$/.test(l));
  if (start < 0 || end < 0) fail(`${rel}: instructions block not found`);
  const body = lines.slice(start + 1, end);
  const indent = Math.min(...body.filter(l => l.trim()).map(l => l.match(/^ */)[0].length));
  const instructions = body.map(l => l.slice(indent)).join('\n').trim();
  const toolsStart = lines.findIndex((l, i) => i > end && /AllowedTools:/.test(l));
  // The allow-list is the array right after "AllowedTools:"; Knowledge and Skills follow it in the same AgentSpec.
  let toolsText = '', depth = 0, opened = false, toolsEnd = toolsStart;
  for (let i = toolsStart; i < lines.length && !(opened && depth === 0); i++) {
    toolsText += lines[i] + '\n'; toolsEnd = i;
    for (const ch of lines[i].slice(i === toolsStart ? lines[i].indexOf('AllowedTools:') : 0)) { if (ch === '[') { depth++; opened = true; } else if (ch === ']') depth--; }
  }
  const allowedTools = [...toolsText.matchAll(/"([\w-]+)"/g)].map(m => m[1]);

  // Knowledge: new KnowledgeSpec(KnowledgeBases.<connection>, KnowledgeBases.<knowledge base>, """ ... """)
  const consts = runtimeConstants();
  let knowledge = null;
  const kLine = lines.findIndex((l, i) => i > toolsEnd && /Knowledge: new KnowledgeSpec\(/.test(l));
  if (kLine >= 0) {
    const m = lines[kLine].match(/KnowledgeBases\.(\w+),\s*KnowledgeBases\.(\w+),\s*"""/);
    if (!m) fail(`${rel}: Knowledge line not understood`);
    const kEnd = lines.findIndex((l, i) => i > kLine && /^\s*"""\)[,;)]*\s*$/.test(l));
    const kBody = lines.slice(kLine + 1, kEnd);
    const kIndent = Math.min(...kBody.filter(l => l.trim()).map(l => l.match(/^ */)[0].length));
    knowledge = {
      connection: consts[m[1]] || fail('unknown constant ' + m[1]), knowledgeBase: consts[m[2]] || fail('unknown constant ' + m[2]),
      instructions: { ...ref(rel, kLine + 2), lineCount: kBody.length, text: kBody.map(l => l.slice(kIndent)).join('\n').trim() },
      spec: ref(rel, kLine + 1),
    };
  }
  // Skills: [SkillNames.<name>, ...]
  const sLine = lines.findIndex((l, i) => i > toolsEnd && /Skills:\s*\[/.test(l));
  const skills = sLine < 0 ? [] : [...lines[sLine].matchAll(/SkillNames\.(\w+)/g)].map(m => consts[m[1]] || fail('unknown skill constant ' + m[1]));
  return {
    spec: ref(rel, specLine + 1),
    instructions: { ...ref(rel, start + 2), lineCount: body.length, text: instructions },
    allowedTools,
    allowedToolsRef: ref(rel, toolsStart + 1),
    knowledge,
    skills,
    skillsRef: sLine < 0 ? null : ref(rel, sLine + 1),
  };
}

// Where the agent class applies the rules to the model's answer (for example TriageRules.Normalize).
function ruleCalls(rel) {
  const out = []; const seen = new Set();
  read(rel).forEach((l, i) => {
    for (const m of l.matchAll(/\b(\w+Rules|SeverityRubric|LeakLocator|WorkforceAvailability|StockViews|TravelEstimator)\.(\w+)\(/g)) {
      const k = m[1] + '.' + m[2]; if (seen.has(k)) continue; seen.add(k);
      out.push({ call: k, ...ref(rel, i + 1) });
    }
  });
  return out;
}

// ---------- records (answer schemas) ----------
function splitTop(text) {
  const parts = []; let depth = 0, cur = '';
  for (const ch of text) {
    if ('<([{'.includes(ch)) depth++; else if ('>)]}'.includes(ch)) depth--;
    if (ch === ',' && depth === 0) { parts.push(cur); cur = ''; } else cur += ch;
  }
  if (cur.trim()) parts.push(cur);
  return parts.map(s => s.trim()).filter(Boolean);
}
function parseRecord(rel, name) {
  const lines = read(rel);
  const at = lines.findIndex(l => new RegExp(`^public record ${name}\\(`).test(l));
  if (at < 0) fail(`${rel}: record ${name} not found`);
  const fields = []; const docs = [];
  for (let i = at - 1; i >= 0 && /^\s*\/\/\//.test(lines[i]); i--) docs.unshift(lines[i]);
  for (let i = at; i < lines.length; i++) {
    let code = lines[i], comment = '';
    const c = code.indexOf('//'); if (c >= 0) { comment = code.slice(c + 2).trim(); code = code.slice(0, c); }
    if (i === at) code = code.slice(code.indexOf('(') + 1);
    const closes = /\)\s*;\s*$/.test(code);
    const inner = closes ? code.replace(/\)\s*;\s*$/, '') : code;
    const params = splitTop(inner).map(p => p.replace(/=.*$/, '').trim());
    params.forEach((p, k) => {
      const m = p.match(/^(.*\S)\s+(\w+)$/); if (m) fields.push({ name: m[2], type: m[1], comment: k === params.length - 1 ? comment : '' });
    });
    if (closes) break;
  }
  const doc = docs.join(' ').replace(/\/\/\/|<\/?summary>|<[^>]+>/g, '').replace(/\s+/g, ' ').trim();
  return { name, summary: doc, ...ref(rel, at + 1), fields };
}

// ---------- rule files: summaries, constants, public methods ----------
const clean = s => s.replace(/\/\/\/|<\/?summary>|<\/?see[^>]*>|<\/?c>|<[^>]+>/g, '').replace(/\s+/g, ' ').trim();
function parseRules(rel) {
  const lines = read(rel);
  const out = { file: ref(rel, 1), classes: [], consts: [], methods: [], checks: [], forbidden: [] };
  // What a failed check does, from the name of the list the message is added to.
  const KIND = { issues: 'rejected', warnings: 'corrected', reviewReasons: 'needs_review', review: 'needs_review', downgrade: 'downgraded' };
  const text = s => { let t = s, p; do { p = t; t = t.replace(/\{[^{}]*\}/g, '\u2026'); } while (t !== p); return t.replace(/\\(.)/g, '$1'); };
  let doc = [];
  lines.forEach((l, i) => {
    if (/^\s*\/\/\//.test(l)) { doc.push(l); return; }
    let m;
    const chk = l.match(/\b(issues|warnings|reviewReasons|review)\.Add\(\$?"((?:[^"\\]|\\.)*)"/) || l.match(/\b(downgrade) = \$?"((?:[^"\\]|\\.)*)"/);
    if (chk && !/^SeverityRubric/.test(path.basename(rel)) && !text(chk[2]).includes('{')) out.checks.push({ kind: KIND[chk[1]], text: text(chk[2]), ...ref(rel, i + 1) });
    const forb = l.match(/^\s*\(Rx\(.*\), "([^"]+)"\),?\s*$/);
    if (forb) out.forbidden.push({ text: forb[1], ...ref(rel, i + 1) });
    if ((m = l.match(/^\s*public static class (\w+)/))) out.classes.push({ name: m[1], summary: clean(doc.join(' ')), ...ref(rel, i + 1) });
    else if ((m = l.match(/^\s*(?:public|private) const (\w+) (\w+) = ([^;]+);\s*(\/\/\s*(.*))?$/))) out.consts.push({ name: m[2], type: m[1], value: m[3].trim(), comment: m[5] || '', ...ref(rel, i + 1) });
    else if (!/\b(record|class)\b/.test(l) && (m = l.match(/^\s*public (?:static |async )*[\w<>\[\],?. ]+? (\w+)\(/))) out.methods.push({ name: m[1], summary: clean(doc.join(' ')), ...ref(rel, i + 1) });
    if (l.trim() && !/^\s*\[/.test(l)) doc = [];
  });
  return out;
}

// ---------- MCP tools and the data each one reads ----------
function storeMethodContainers() {
  const lines = read('src/LeakOps.Mcp/LeakOpsStore.cs'); const map = {};
  let cur = null;
  lines.forEach(l => {
    const m = l.match(/^\s{4}public (?:async )?[\w<>?,. ]+? (\w+)\(/);
    if (m) cur = m[1];
    if (cur) for (const c of l.matchAll(/LeakOpsContainers\.(\w+)/g)) if (c[1] !== 'DefaultDatabase') (map[cur] ||= new Set()).add(c[1]);
  });
  return map;
}
function parseTools() {
  const storeMap = storeMethodContainers(); const tools = {};
  const dir = path.join(root, 'src/LeakOps.Mcp');
  for (const f of fs.readdirSync(dir).filter(f => /Tools\.cs$/.test(f))) {
    const rel = 'src/LeakOps.Mcp/' + f; const lines = read(rel);
    const triggers = [];
    lines.forEach((l, i) => { const m = l.match(/McpToolTrigger\("([\w-]+)",\s*"((?:[^"\\]|\\.)*)"/); if (m) triggers.push({ i, name: m[1], desc: m[2].replace(/\\(.)/g, '$1') }); });
    triggers.forEach((t, k) => {
      const end = k + 1 < triggers.length ? triggers[k + 1].i - 2 : lines.length;
      const body = lines.slice(t.i, end).join('\n');
      const containers = new Set();
      for (const c of body.matchAll(/store\.(\w+)\(/g)) (storeMap[c[1]] || []).forEach(x => containers.add(x));
      tools[t.name] = { name: t.name, description: t.desc, group: f.replace('.cs', ''), ...ref(rel, t.i + 1), containers: [...containers] };
    });
  }
  return tools;
}

// ---------- data: containers, partition keys, seed files ----------
const SEED = {
  Sensors: 'sensors.json', PipeSegments: 'pipe-segments.json', Valves: 'valves.json', PlannedWorks: 'planned-works.json', UsageEvents: 'usage-events.json',
  Technicians: 'technicians.json', Inventory: 'inventory.json', Warehouses: 'warehouses.json', Suppliers: 'suppliers.json',
  RepairProcedures: 'repair-procedures.json', Scenarios: 'scenarios.json',
};
const GENERATED = { SensorReadings: 'GenerateReadings', WeatherObservations: 'GenerateWeather', UsageProfiles: 'GenerateUsageProfiles' };
function containerInfo(name) {
  const pk = (read('src/LeakOps.Contracts/LeakOpsContainers.cs').join('\n').match(new RegExp(`\\[${name}\\] = "([^"]+)"`)) || [])[1];
  if (!pk) fail('no partition key for ' + name);
  const info = { name, partitionKey: pk, ...lineRef('src/LeakOps.Contracts/LeakOpsContainers.cs', new RegExp(`public const string ${name} = `)) };
  if (SEED[name]) {
    const data = JSON.parse(fs.readFileSync(path.join(root, 'data', SEED[name]), 'utf8').replace(/^\uFEFF/, ''));
    info.kind = 'seed'; info.file = ref('data/' + SEED[name], 1); info.records = Array.isArray(data) ? data.length : 1;
  } else if (GENERATED[name]) {
    info.kind = 'generated'; info.generator = lineRef('src/LeakOps.Seeder/SimulatedDataGenerator.cs', new RegExp(`static .* ${GENERATED[name]}\\(`));
    info.config = ref('data/simulation.json', 1);
  } else info.kind = 'runtime';
  return info;
}

// ---------- assemble ----------
const catalog = read('src/LeakOps.Agents/AgentCatalog.cs').join('\n');
const tools = parseTools();
const agents = {};
for (const [key, cls, rel] of STEPS) {
  const s = parseSpec(rel);
  const cm = catalog.match(new RegExp(`new\\(${cls}\\.Spec, typeof\\((\\w+)\\), "([^"]+)"\\)`));
  if (!cm) fail('catalog entry missing for ' + cls);
  const finalType = (read(rel).join('\n').match(/Task<AgentResult<(\w+)>>/) || [])[1];
  const agentTools = s.allowedTools.map(n => tools[n] || fail(`tool ${n} of ${cls} not found in the MCP project`));
  const containerNames = [...new Set(agentTools.flatMap(t => t.containers))];
  agents[key] = {
    name: cls, description: cm[2], ...s,
    classRef: lineRef(rel, new RegExp(`public class ${cls}\\(`)),
    answerType: parseRecord('src/LeakOps.Contracts/Agents.cs', cm[1]),
    resultType: finalType && finalType !== cm[1] ? parseRecord('src/LeakOps.Contracts/Agents.cs', finalType) : null,
    tools: agentTools,
    containers: containerNames.map(containerInfo),
    ruleCalls: ruleCalls(rel),
    rules: RULES[key].map(parseRules),
  };
}
// What the agents write at run time: Waterstoring (communications, scheduling) and, through code calls to MCP tools, stock, orders and work orders.
const writes = { communications: ['WaterstoringIncidents'], scheduling: ['WaterstoringIncidents', 'WorkOrders'], procurement: ['StockReservations', 'PurchaseOrders', 'Inventory'] };
for (const [k, list] of Object.entries(writes)) agents[k].writes = list.map(containerInfo);
agents.communications.external = { label: 'Waterstoring ingest API', ...lineRef('src/LeakOps.Agents/WaterstoringClient.cs', /public class WaterstoringClient/) };
agents.scheduling.external = agents.communications.external;

const allTools = Object.values(tools).sort((a, b) => a.group.localeCompare(b.group) || a.line - b.line);

// ---------- knowledge (Foundry IQ): documents, knowledge bases, skills ----------
function parseDocs(folder) {
  const dir = path.join(root, 'knowledge', folder);
  return fs.readdirSync(dir).filter(f => f.endsWith('.md')).sort().map(f => {
    const rel = `knowledge/${folder}/${f}`; const lines = read(rel);
    const titleAt = lines.findIndex(l => /^# /.test(l));
    const m = lines[titleAt].match(/^# ([A-Z]+(?:-[A-Z0-9]+)+): (.+)$/);
    if (!m) fail(`${rel}: the title must look like "# CODE-01: Title"`);
    if (!/SIMULATED DOCUMENT/.test(lines.slice(0, 3).join(' '))) fail(`${rel}: the simulated banner is missing`);
    return { file: f, code: m[1], title: m[2], internal: /INTERNAL/.test(lines[titleAt]), ...ref(rel, titleAt + 1) };
  });
}
function parseSkill(name) {
  const rel = `skills/${name}/SKILL.md`; const lines = read(rel);
  const end = lines.findIndex((l, i) => i > 0 && l.trim() === '---');
  const get = k => (lines.slice(1, end).find(l => l.startsWith(k + ':')) || '').slice(k.length + 1).trim();
  return { name: get('name') || fail(rel + ': no name'), description: get('description'), lineCount: lines.length - end - 1, ...ref(rel, 1), body: lines.slice(end + 1).join('\n').trim() };
}
const consts = runtimeConstants();
const skillNames = Object.keys(consts).filter(k => ['NoticeStyle', 'IsolationPlaybook', 'EvidenceCitation'].includes(k)).map(k => consts[k]);
const kbDefs = [
  { name: consts.OpsKnowledgeBase, connection: consts.OpsConnection, source: 'leakops-ops-docs', folder: 'ops' },
  { name: consts.CommsKnowledgeBase, connection: consts.CommsConnection, source: 'leakops-comms-docs', folder: 'comms' },
].map(k => ({
  ...k, docs: parseDocs(k.folder),
  agents: Object.entries(agents).filter(([, a]) => a.knowledge && a.knowledge.knowledgeBase === k.name).map(([key]) => key),
  definition: lineRef('deploy/deploy-knowledge.ps1', new RegExp(`Put-Search 'knowledgebases/${k.name}'`)),
  sourceDefinition: lineRef('deploy/deploy-knowledge.ps1', new RegExp(`New-BlobSource '${k.source}'`)),
  connectionRef: lineRef('deploy/knowledge.bicep', new RegExp(`resource ${k.folder}Connection `)),
}));
const skills = skillNames.map(parseSkill).map(sk => ({ ...sk, usedBy: Object.entries(agents).filter(([, a]) => a.skills.includes(sk.name)).map(([key]) => key) }));
for (const sk of skills) if (!sk.usedBy.length) fail(`skill ${sk.name} is not used by any agent`);
for (const a of Object.values(agents)) for (const n of a.skills) if (!skills.some(s => s.name === n)) fail(`agent skill ${n} has no SKILL.md`);

const tech = {
  note: 'Generated by demo-site/tools/build-technical.js from the source of the demo. Do not edit by hand.',
  agents,
  knowledge: {
    bases: kbDefs,
    toolName: consts.ToolName, serverLabel: consts.ServerLabel,
    planner: { model: (read('deploy/deploy-knowledge.ps1').join('\n').match(/\$model = '([^']+)'/) || [])[1], ...lineRef('deploy/deploy-knowledge.ps1', /\$model = '/) },
    embedding: { model: (read('deploy/deploy-knowledge.ps1').join('\n').match(/\$embedding = '([^']+)'/) || [])[1], ...lineRef('deploy/deploy-knowledge.ps1', /\$embedding = '/) },
    effort: (read('deploy/deploy-knowledge.ps1').join('\n').match(/retrievalReasoningEffort = @\{ kind = '(\w+)' \}/) || [])[1],
    apiVersion: { value: (read('deploy/deploy-knowledge.ps1').join('\n').match(/\$ApiVersion = '([^']+)'/) || [])[1], ...lineRef('deploy/deploy-knowledge.ps1', /\[string\] \$ApiVersion/) },
    script: ref('deploy/deploy-knowledge.ps1', 1),
    upload: lineRef('deploy/deploy-knowledge.ps1', /== 2\. Documents/),
    indexing: lineRef('deploy/deploy-knowledge.ps1', /== 4\. Indexing/),
    bicep: {
      search: lineRef('deploy/knowledge.bicep', /resource search /),
      noKeys: lineRef('deploy/knowledge.bicep', /disableLocalAuth: true/),
      embedding: lineRef('deploy/knowledge.bicep', /resource embedding /),
      container: lineRef('deploy/knowledge.bicep', /resource knowledgeBlobs /),
      searchReadsBlobs: lineRef('deploy/knowledge.bicep', /resource searchReadsBlobs /),
      searchCallsModels: lineRef('deploy/knowledge.bicep', /resource searchCallsModels /),
      projectReadsIndexes: lineRef('deploy/knowledge.bicep', /resource projectReadsIndexes /),
    },
    registry: {
      tool: lineRef('src/LeakOps.AgentRegistry/Program.cs', /serverLabel: KnowledgeBases\.ServerLabel/),
      noKnowledge: lineRef('src/LeakOps.AgentRegistry/Program.cs', /--no-knowledge/),
      spec: lineRef('src/LeakOps.Agents/LeakOpsAgentRuntime.cs', /public record KnowledgeSpec/),
      constants: lineRef('src/LeakOps.Agents/LeakOpsAgentRuntime.cs', /public static class KnowledgeBases/),
    },
    tests: [lineRef('tests/LeakOps.Tests/TriageRulesTests.cs', /A_knowledge_document_can_inform_a_verdict_but_never_close_an_alarm/)],
    evals: lineRef('evals/leakops-evals.json', /"diagnostic_knowledge"/),
  },
  skills: {
    items: skills,
    api: { store: lineRef('src/LeakOps.AgentRegistry/SkillStore.cs', /public class SkillStore/), ensure: lineRef('src/LeakOps.AgentRegistry/SkillStore.cs', /public async Task<SkillVersion> EnsureAsync/),
      publish: lineRef('src/LeakOps.AgentRegistry/SkillStore.cs', /private async Task<string> PublishAsync/), inject: lineRef('src/LeakOps.AgentRegistry/Program.cs', /Skills \(versioned how-to's from Foundry/),
      names: lineRef('src/LeakOps.Agents/LeakOpsAgentRuntime.cs', /public static class SkillNames/) },
  },
  mcp: {
    tools: allTools,
    server: [
      lineRef('src/LeakOps.Mcp/Program.cs', /MCP server for the Evides LeakOps agents/),
      lineRef('src/LeakOps.Mcp/LeakOpsStore.cs', /public class LeakOpsStore/),
    ],
    client: [
      lineRef('src/LeakOps.Agents/LeakOpsAgentRuntime.cs', /ConnectMcpAsync\(CancellationToken/),
      lineRef('src/LeakOps.Agents/LeakOpsAgentRuntime.cs', /public async Task<AgentResult<T>> RunAsync/),
    ],
    registry: [
      lineRef('src/LeakOps.AgentRegistry/Program.cs', /CreateMcpTool\(/),
      lineRef('src/LeakOps.AgentRegistry/Program.cs', /CreateJsonSchemaFormat/),
      lineRef('src/LeakOps.AgentRegistry/Program.cs', /definitionHash/),
    ],
    connection: lineRef('deploy/main.bicep', /resource mcpConnection /),
    serverLabel: (() => {
      const rel = 'src/LeakOps.AgentRegistry/Program.cs';
      const at = find(rel, /serverLabel: "/);
      return { value: read(rel)[at - 1].match(/serverLabel: "([^"]+)"/)[1], ...ref(rel, at) };
    })(),
  },
  workflow: {
    host: [
      lineRef('src/LeakOps.Workflow/Program.cs', /var operatorKey = /),
      lineRef('src/LeakOps.Workflow/Program.cs', /app\.MapPost\("\/api\/runs"/),
      lineRef('src/LeakOps.Workflow/Program.cs', /MapGet\("\/api\/scenarios"/),
    ],
    runner: [
      lineRef('src/LeakOps.Contracts/Logic/WorkflowRunner.cs', /public class WorkflowRunner/),
      lineRef('src/LeakOps.Contracts/Workflow.cs', /public static class WorkflowSteps/),
      lineRef('src/LeakOps.Contracts/Workflow.cs', /public static class WorkflowOutcomes/),
      lineRef('src/LeakOps.Contracts/Workflow.cs', /public record WorkflowEvent\(/),
    ],
    gateway: [
      lineRef('src/LeakOps.Workflow/AgentGateway.cs', /public class AgentGateway/),
      lineRef('src/LeakOps.Agents/AgentFunctions.cs', /public class AgentFunctions/),
    ],
    scenarios: [lineRef('src/LeakOps.Workflow/ScenarioCatalog.cs', /public class ScenarioCatalog/), lineRef('data/scenarios.json', /"id"/), lineRef('src/LeakOps.Contracts/Scenario.cs', /public record AnomalyAlarm\(/)],
    guard: [lineRef('src/LeakOps.Workflow/ScenarioCatalog.cs', /public class RunGuard/)],
    infra: [lineRef('deploy/main.bicep', /module workflow /), lineRef('deploy/webapp.bicep', /resource app /)],
    alarm: parseRecord('src/LeakOps.Contracts/Scenario.cs', 'AnomalyAlarm'),
  },
  foundry: {
    registry: [
      lineRef('src/LeakOps.AgentRegistry/Program.cs', /DeclarativeAgentDefinition\(/),
      lineRef('src/LeakOps.AgentRegistry/Program.cs', /CreateAgentVersionAsync/),
    ],
    runtime: [lineRef('src/LeakOps.Agents/LeakOpsAgentRuntime.cs', /AgentReference\(spec\.Name\)/), lineRef('src/LeakOps.Agents/LeakOpsAgentRuntime.cs', /UseOpenTelemetry/)],
    bicep: [
      lineRef('deploy/main.bicep', /resource foundry /), lineRef('deploy/main.bicep', /disableLocalAuth: true/), lineRef('deploy/main.bicep', /resource model /),
      lineRef('deploy/main.bicep', /resource tracingConnection /), lineRef('deploy/main.bicep', /resource mcpConnection /), lineRef('deploy/main.bicep', /resource agentsFoundryAccess /),
    ],
    evals: [lineRef('evals/leakops-evals.json', /\S/), lineRef('evals/run-evals.ps1', /\.SYNOPSIS/)],
    guardrails: [lineRef('README.md', /^## Guardrails in code/), lineRef('DEMO.md', /^## 6\. Guardrails/)],
    approval: lineRef('src/LeakOps.Contracts/Logic/Stock.cs', /ApprovalThresholdEur/),
    idempotency: [lineRef('src/LeakOps.Mcp/LogisticsTools.cs', /Safe to repeat/)],
  },
};

const out = path.join(root, 'demo-site', 'js', 'technical.js');
fs.writeFileSync(out, '// ' + tech.note + '\nwindow.TECH = ' + JSON.stringify(tech) + ';\n');
console.log(Object.entries(agents).map(([k, a]) => `${k}: ${a.instructions.lineCount} instruction lines, ${a.tools.length} tools, ${a.containers.length} containers, ${a.rules.reduce((n, r) => n + r.consts.length + r.methods.length, 0)} rule items, ${a.ruleCalls.length} rule calls, answer fields ${a.answerType.fields.length}`).join('\n'));
console.log('tools total:', allTools.length, ' bytes:', fs.statSync(out).size);
