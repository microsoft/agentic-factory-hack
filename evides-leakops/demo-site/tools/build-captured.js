// Builds demo-site/js/captured.js from two recorded runs of the deployed workflow (server-sent events of POST /api/runs).
// Usage: node build-captured.js <scenarios.json> <burst.sse> <false-alarm.sse> <out captured.js>
// Nothing is typed in by hand: answers, tool calls (including knowledge base calls and code re-checks), durations and token counts are the recorded ones.
'use strict';
const fs = require('fs');
const [scenariosFile, burstFile, falseFile, outFile] = process.argv.slice(2);
if (!outFile) { console.error('usage: node build-captured.js <scenarios.json> <burst.sse> <false-alarm.sse> <out>'); process.exit(1); }

const scenarios = JSON.parse(fs.readFileSync(scenariosFile, 'utf8').replace(/^\uFEFF/, ''));
const out = { capturedAt: new Date().toISOString().slice(0, 10), source: 'Deployed demo, rg-evides-ai-demo (simulated data)', scenarios: {} };

for (const [key, id, file] of [['burst', 'scn-burst-cast-iron', burstFile], ['falseAlarm', 'scn-false-alarm-planned-works', falseFile]]) {
  const events = [];
  for (const line of fs.readFileSync(file, 'utf8').split('\n')) if (line.startsWith('data: ')) events.push(JSON.parse(line.slice(6)));
  const scenario = scenarios.find(s => s.id === id);
  if (!scenario) throw new Error('scenario not found: ' + id);
  const steps = events.filter(e => e.type === 'step_completed').map(e => {
    const trace = e.data.trace || {};
    return {
      step: e.step, durationMs: e.durationMs, headline: e.headline, agent: trace.agent, model: trace.model,
      inputTokens: trace.inputTokens, outputTokens: trace.outputTokens,
      toolCalls: (trace.toolCalls || []).map(c => ({ name: c.name, arguments: c.arguments })), result: e.data.result,
    };
  });
  const done = events.find(e => e.type === 'run_completed');
  if (!done) throw new Error(file + ' has no run_completed event');
  out.scenarios[key] = { id, name: scenario.name, description: scenario.description, alarm: scenario.alarm, steps, outcome: done.outcome, reasons: done.reasons, summary: done.summary };
}

fs.writeFileSync(outFile, '// Generated from a real run of the deployed demo (simulated data). Do not edit by hand.\nwindow.CAPTURED = ' + JSON.stringify(out) + ';\n');
for (const [k, s] of Object.entries(out.scenarios))
  console.log(k, s.outcome, Math.round(s.summary.totalMs / 1000) + ' s', s.steps.map(x => `${x.step}${x.toolCalls.some(c => c.name === 'knowledge_base_retrieve') ? '+KB' : ''}`).join(' '));
