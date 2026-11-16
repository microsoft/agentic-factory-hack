"use strict";

// Operator console: pick an alarm, run the workflow, watch six agents work. Progress arrives as server-sent events over a POST.
const ROLES = {
  triage: ["Triage", "Is it a real leak? Reads sensors, planned works, weather and usage."],
  diagnostic: ["Diagnostic", "Where is it and how bad? Uses GIS data: pipe, position, severity, valves."],
  communications: ["Communications", "Tells residents in Dutch and English and publishes to Waterstoring."],
  matchmaker: ["Resource Matchmaker", "Finds the nearest available technician with the right certifications."],
  procurement: ["Procurement", "Reserves parts and equipment, orders what is short."],
  scheduling: ["Scheduling", "Plans the route, sends the work order, updates the expected time."],
};
const ORDER = Object.keys(ROLES);
const OUTCOME_TEXT = {
  completed: "Done: the leak is handled from alarm to work order.",
  closed_false_alarm: "Closed: no leak, so nothing else was started.",
  needs_review: "Stopped: a person has to decide.",
  failed: "Failed: an agent could not do its job.",
  cancelled: "Cancelled: the run was paused and not resumed.",
};

const $ = (id) => document.getElementById(id);
const stepEls = {};
let running = false;
let timer = null;
let pauseTimer = null;
let currentRunId = null;

function el(tag, attrs = {}, ...children) {
  const node = document.createElement(tag);
  for (const [key, value] of Object.entries(attrs)) {
    if (key === "class") node.className = value;
    else node.setAttribute(key, value);
  }
  for (const child of children) node.append(child);
  return node;
}

function buildSteps() {
  const list = $("steps");
  list.replaceChildren();
  ORDER.forEach((step, i) => {
    const [name, role] = ROLES[step];
    const li = el("li", { class: "step waiting", id: `step-${step}` },
      el("div", { class: "badge" }, String(i + 1)),
      el("div", {}, el("h4", {}, name), el("div", { class: "role" }, role)),
      el("div", { class: "status" }, "Waiting"),
      el("div", { class: "headline" }, ""));
    list.append(li);
    stepEls[step] = li;
  });
}

function setStep(step, state, statusText, headline) {
  const li = stepEls[step];
  li.className = `step ${state}`;
  li.querySelector(".status").textContent = statusText;
  if (headline !== undefined) li.querySelector(".headline").textContent = headline;
}

function addDetails(step, data) {
  const li = stepEls[step];
  li.querySelector("details")?.remove();
  const trace = data?.trace;
  const details = el("details", {}, el("summary", {}, "Details"));
  if (trace) {
    const calls = (trace.toolCalls || []).filter((c) => !c.name.startsWith("(rules)")).map((c) => c.name);
    const checks = (trace.toolCalls || []).filter((c) => c.name.startsWith("(rules)")).map((c) => c.name.replace("(rules) ", ""));
    const parts = [`${trace.model || "model"}`, `${(trace.durationMs / 1000).toFixed(1)} s`];
    if (trace.inputTokens != null) parts.push(`${trace.inputTokens} tokens in, ${trace.outputTokens} out`);
    details.append(el("div", { class: "trace" }, parts.join(" - ")));
    if (calls.length) details.append(el("div", { class: "trace" }, "Tools the agent used: " + calls.join(", ")));
    if (checks.length) details.append(el("div", { class: "trace" }, "Facts fetched by code to check it: " + [...new Set(checks)].join(", ")));
  }
  details.append(el("pre", {}, JSON.stringify(data?.result ?? data, null, 2)));
  li.append(details);
}

function showBanner(summary, reasons) {
  const banner = $("banner");
  banner.hidden = false;
  banner.className = `banner ${summary.outcome}`;
  banner.replaceChildren(el("div", {}, OUTCOME_TEXT[summary.outcome] || summary.outcome));
  const extra = [`${(summary.totalMs / 1000).toFixed(0)} s`];
  if (summary.incidentId) extra.push(`incident ${summary.incidentId}`);
  if (summary.workOrderId) extra.push(`work order ${summary.workOrderId}`);
  banner.append(el("small", {}, extra.join(" - ")));
  if (reasons?.length) banner.append(el("ul", {}, ...reasons.map((r) => el("li", {}, r))));
}

function startTimer(step) {
  stopTimer();
  const started = Date.now();
  const status = stepEls[step].querySelector(".status");
  timer = setInterval(() => { status.textContent = `Working ${Math.floor((Date.now() - started) / 1000)} s`; }, 500);
}
function stopTimer() { if (timer) { clearInterval(timer); timer = null; } }

// The run waits before a step the operator chose. Communications: nothing has been sent to Waterstoring yet. Matchmaker: residents know,
// but nobody is assigned. The operator shows the outside world (a page), then resumes or cancels.
const PAUSES = {
  communications: {
    headline: "Residents have not been told yet. Nothing has been sent to Waterstoring.",
    text: "Open Waterstoring and show that there is no notice for this incident. Then come back and resume.",
    link: ["Open Waterstoring \u2197", () => $("site-link").href],
  },
  matchmaker: {
    headline: "Residents have been told. No technician is assigned yet.",
    text: "Open the technicians page and show everyone on the roster and who is available. Nobody is assigned to this incident yet. Then come back and resume: the agent picks one and Scheduling books them.",
    link: ["Open the technicians page \u2197", () => new URL("/technicians", $("site-link").href).href],
  },
};

function showPause(event) {
  const step = event.step;
  const pause = PAUSES[step];
  setStep(step, "paused", "Paused", pause.headline);
  clearPauseBox();
  const deadline = Date.parse(event.data?.deadlineUtc) || Date.now() + 900000;
  const count = el("span", { class: "count" }, "");
  const open = el("a", { href: pause.link[1](), target: "_blank", rel: "noopener" }, pause.link[0]);
  const go = el("button", { type: "button", class: "go" }, "Resume the workflow");
  const stop = el("button", { type: "button", class: "stop" }, "Cancel the run");
  const msg = el("p", { class: "error", role: "alert", hidden: "" }, "");
  const act = async (kind, button) => {
    go.disabled = stop.disabled = true;
    msg.hidden = true;
    const response = await fetch(`api/runs/${encodeURIComponent(currentRunId)}/${kind}`, { method: "POST", headers: authHeaders() }).catch(() => null);
    if (!response || !response.ok) { go.disabled = stop.disabled = false; msg.textContent = "That did not work (the run may have ended). Try again."; msg.hidden = false; }
  };
  go.addEventListener("click", () => act("resume", go));
  stop.addEventListener("click", () => act("cancel", stop));
  const box = el("div", { class: "pausebox" },
    el("p", {}, pause.text),
    el("div", { class: "row" }, open, go, stop, count), msg);
  stepEls[step].append(box);
  const tick = () => { const left = Math.max(0, Math.round((deadline - Date.now()) / 1000)); count.textContent = `Cancels itself in ${Math.floor(left / 60)}:${String(left % 60).padStart(2, "0")}`; };
  tick();
  pauseTimer = setInterval(tick, 1000);
}
function clearPauseBox() {
  if (pauseTimer) { clearInterval(pauseTimer); pauseTimer = null; }
  document.querySelectorAll(".pausebox").forEach((b) => b.remove());
}

function handle(type, event) {
  switch (type) {
    case "run_started":
      currentRunId = event.runId;
      break;
    case "run_paused":
      showPause(event);
      break;
    case "run_resumed":
      clearPauseBox();
      setStep(event.step, "waiting", "Resuming", "");
      break;
    case "step_started":
      setStep(event.step, "running", "Working 0 s", "");
      startTimer(event.step);
      break;
    case "step_completed":
      stopTimer();
      setStep(event.step, "completed", `Done in ${(event.durationMs / 1000).toFixed(1)} s`, event.headline);
      addDetails(event.step, event.data);
      break;
    case "step_failed":
      stopTimer();
      setStep(event.step, "failed", "Failed", event.headline);
      break;
    case "run_completed": {
      stopTimer();
      clearPauseBox();
      const done = new Set((event.summary?.steps || []).map((s) => s.step));
      ORDER.filter((s) => !done.has(s)).forEach((s) => setStep(s, "skipped", "Not run", ""));
      showBanner(event.summary, event.reasons);
      break;
    }
  }
}

async function readEvents(response) {
  const reader = response.body.getReader();
  const decoder = new TextDecoder();
  let buffer = "";
  for (;;) {
    const { value, done } = await reader.read();
    if (done) break;
    buffer += decoder.decode(value, { stream: true });
    let cut;
    while ((cut = buffer.indexOf("\n\n")) >= 0) {
      const block = buffer.slice(0, cut);
      buffer = buffer.slice(cut + 2);
      const type = /^event: (.*)$/m.exec(block)?.[1];
      const data = /^data: (.*)$/m.exec(block)?.[1];
      if (type && data) handle(type, JSON.parse(data));
    }
  }
}

function showError(message) {
  const box = $("run-error");
  box.textContent = message;
  box.hidden = false;
}

async function run() {
  if (running) return;
  $("run-error").hidden = true;
  let alarm;
  try { alarm = JSON.parse($("alarm").value); }
  catch (e) { return showError("The alarm is not valid JSON: " + e.message); }

  running = true;
  setControlsDisabled(true);
  $("run").textContent = "Running...";
  $("banner").hidden = true;
  buildSteps();
  try {
    const pauseBefore = [$("pause-comms").checked && "communications", $("pause-match").checked && "matchmaker"].filter(Boolean);
    const response = await fetch("api/runs", { method: "POST", headers: authHeaders({ "Content-Type": "application/json" }), body: JSON.stringify({ alarm, pauseBefore }) });
    if (response.status === 401) { $("key-row").hidden = false; throw new Error("The operator key is missing or wrong. Enter it above and run again."); }
    if (!response.ok) {
      const body = await response.json().catch(() => ({}));
      throw new Error(body.error || `The workflow host answered ${response.status}.`);
    }
    await readEvents(response);
  } catch (e) {
    stopTimer();
    clearPauseBox();
    showError(e.message);
  } finally {
    running = false;
    setControlsDisabled(false);
    $("run").textContent = "Run the workflow again";
    loadRecent();
  }
}

// While a run is going the options and the reset are locked (the host refuses a reset then anyway).
function setControlsDisabled(disabled) {
  ["pause-comms", "pause-match", "run", "reset-demo"].forEach((id) => { $(id).disabled = disabled; });
}

async function resetDemo() {
  const msg = $("reset-msg");
  if (!confirm("Reset the demo?\n\nThis clears the Waterstoring page, all work orders, orders and reservations, and restores the stock.")) return;
  $("reset-demo").disabled = true;
  msg.className = "hint";
  msg.textContent = "Resetting...";
  try {
    const response = await fetch("api/admin/reset-demo", { method: "POST", headers: authHeaders() });
    const body = await response.json().catch(() => ({}));
    if (!response.ok) throw new Error(body.error || `The workflow host answered ${response.status}.`);
    msg.textContent = "Done: the demo is back at its start. Every technician is free and Waterstoring is empty.";
    $("banner").hidden = true;
    buildSteps();
    loadRecent();
  } catch (e) {
    msg.className = "hint bad";
    msg.textContent = e.message;
  } finally {
    $("reset-demo").disabled = running;
  }
}

function pickScenario(scenario, button) {
  document.querySelectorAll(".scenario").forEach((b) => b.setAttribute("aria-checked", String(b === button)));
  $("alarm").value = JSON.stringify(scenario.alarm, null, 2);
}

async function loadScenarios() {
  const response = await fetch("api/scenarios", { headers: authHeaders() });
  if (response.status === 401) {
    $("key-row").hidden = false;
    $("run").disabled = true;
    showError(sessionStorage.getItem("operatorKey") ? "That operator key is not right. Check it and try again." : "The Run button is off until you enter the operator key above.");
    return;
  }
  const scenarios = await response.json();
  const box = $("scenarios");
  box.replaceChildren();
  scenarios.forEach((scenario, i) => {
    const button = el("button", { class: "scenario", type: "button", role: "radio", "aria-checked": "false" },
      el("strong", {}, scenario.name), el("span", {}, scenario.description));
    button.addEventListener("click", () => pickScenario(scenario, button));
    box.append(button);
    if (i === 0) pickScenario(scenario, button);
  });
  $("run").disabled = scenarios.length === 0;
}

async function loadRecent() {
  const response = await fetch("api/runs", { headers: authHeaders() });
  if (!response.ok) return;
  const runs = await response.json();
  const list = $("recent");
  list.replaceChildren();
  if (!runs.length) return list.append(el("li", { class: "muted" }, "None yet."));
  runs.slice(0, 6).forEach((r) => list.append(el("li", {},
    el("span", {}, `${r.alarmId} - ${r.outcome.replaceAll("_", " ")}`),
    el("span", { class: "muted" }, `${Math.round(r.totalMs / 1000)} s`))));
}

async function loadConfig() {
  try {
    const config = await (await fetch("api/config")).json();
    $("site-link").href = config.waterstoringUrl;
    // On Azure the API needs an operator key. It is kept in this browser tab only (sessionStorage), never in the page.
    if (config.keyRequired) {
      $("key-row").hidden = false;
      $("operator-key").value = sessionStorage.getItem("operatorKey") || "";
    }
  } catch { /* keep the default link */ }
}

function authHeaders(extra = {}) {
  const key = sessionStorage.getItem("operatorKey");
  return key ? { ...extra, "X-Operator-Key": key } : extra;
}

function start() {
  $("run-error").hidden = true;
  loadScenarios().catch((e) => showError("Could not load the scenarios: " + e.message));
  loadRecent();
}

// The key is applied as soon as it is typed or pasted (after a short pause), not only when the field loses focus.
let keyTimer = null;
function applyKey() {
  clearTimeout(keyTimer);
  const key = $("operator-key").value.trim();
  if (key === (sessionStorage.getItem("operatorKey") || "")) return;
  sessionStorage.setItem("operatorKey", key);
  start();
}

buildSteps();
$("run").addEventListener("click", run);
$("reset-demo").addEventListener("click", resetDemo);
$("operator-key").addEventListener("input", () => { clearTimeout(keyTimer); keyTimer = setTimeout(applyKey, 400); });
$("operator-key").addEventListener("change", applyKey);
loadConfig().then(start);
