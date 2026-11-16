"use strict";

// Technicians page, styled like an internal Microsoft 365 / Outlook planning app: a mail-style list, a reading pane for the selected
// technician and a "scheduling assistant" timeline. It polls the API every 5 seconds, so during the demo a technician turns to "assigned".
const TEXT = {
  nl: {
    app: "Resource planning", search: "Zoek technici, certificaten of straten", demo: "DEMO - gesimuleerd",
    navTech: "Technici", navWarehouse: "Magazijn", navIncidents: "Meldingen",
    title: "Technici", live: "Live", clock: "Simulatieklok",
    plannerTitle: "Planning (alle technici)", legendShift: "Dienst", legendBusy: "Bezet", legendJob: "Werkbon", legendNow: "Nu (simulatieklok)",
    statusNote: "Alle gegevens zijn gesimuleerd. De portretten zijn door AI gemaakt: fictieve personen, geen echte medewerkers.",
    all: "Alle", available: "Beschikbaar", assigned: "Toegewezen", busy: "Bezet", off_shift: "Niet in dienst",
    count: (n, total) => `${n} van ${total} technici`,
    availableUntil: "Beschikbaar tot", nextShift: "Volgende dienst", nightShift: "Nachtdienst", dayShift: "Dagdienst",
    none: "Geen technici gevonden.",
    shift: "Dienst", shiftUntil: "tot", vehicle: "Voertuig", position: "Laatste positie", busyWith: "Bezet met", region: "Regio",
    excavator: "kan een graafmachine trekken", certs: "Certificaten", job: "Werkbon", noJob: "Geen werkbon toegewezen.",
    leaves: "Vertrekt", arrives: "Aankomst", done: "Klaar", incident: "Bekijk de melding",
    loadError: "De technici konden niet worden geladen. Er wordt opnieuw geprobeerd.",
  },
  en: {
    app: "Resource planning", search: "Search technicians, certifications or streets", demo: "DEMO - simulated",
    navTech: "Technicians", navWarehouse: "Warehouse", navIncidents: "Incidents",
    title: "Technicians", live: "Live", clock: "Simulation clock",
    plannerTitle: "Planning (all technicians)", legendShift: "Shift", legendBusy: "Busy", legendJob: "Work order", legendNow: "Now (simulation clock)",
    statusNote: "All data is simulated. The portraits are AI-generated: fictional people, not real employees.",
    all: "All", available: "Available", assigned: "Assigned", busy: "Busy", off_shift: "Off shift",
    count: (n, total) => `${n} of ${total} technicians`,
    availableUntil: "Available until", nextShift: "Next shift", nightShift: "Night shift", dayShift: "Day shift",
    none: "No technicians found.",
    shift: "Shift", shiftUntil: "until", vehicle: "Vehicle", position: "Last position", busyWith: "Busy with", region: "Region",
    excavator: "can tow an excavator", certs: "Certifications", job: "Work order", noJob: "No work order assigned.",
    leaves: "Leaves", arrives: "Arrives", done: "Done", incident: "View the notice",
    loadError: "Could not load the technicians. Trying again.",
  },
};

const STATES = ["available", "assigned", "busy", "off_shift"];
const timeZone = "Europe/Amsterdam";
const BEFORE_H = 8, AFTER_H = 14;   // the planner shows 8 hours before and 14 hours after the simulation clock

const $ = (id) => document.getElementById(id);
let lang = "nl";
let latest = null;
let selectedId = null;
let filter = "all";
let query = "";
let seenAssigned = null;   // null until the first load, so only a later change counts as "newly assigned"
let flashId = null;

function el(tag, attrs = {}, ...children) {
  const node = document.createElement(tag);
  for (const [key, value] of Object.entries(attrs)) {
    if (key === "class") node.className = value;
    else node.setAttribute(key, value);
  }
  for (const child of children) if (child != null) node.append(child);
  return node;
}

const hm = (iso) => new Date(iso).toLocaleTimeString("nl-NL", { hour: "2-digit", minute: "2-digit", timeZone });
const dayHm = (iso) => new Date(iso).toLocaleString(lang === "nl" ? "nl-NL" : "en-GB", { weekday: "short", hour: "2-digit", minute: "2-digit", timeZone });
const initials = (name) => name.split(/\s+/).filter(Boolean).map((p) => p[0]).slice(0, 2).join("").toUpperCase();

function avatar(t, size = "") {
  const node = el("div", { class: `avatar ${size}`.trim(), "aria-hidden": "true" }, initials(t.name));
  const img = el("img", { src: t.photo, alt: "", loading: "lazy" });
  img.addEventListener("error", () => img.remove());   // no portrait file: the initials stay visible
  node.append(img, el("span", { class: `dot ${t.state}` }));
  return node;
}

function preview(t, t9n) {
  if (t.assignment) return `${t.assignment.workOrderId} \u00B7 ${t.assignment.street}`;
  if (t.state === "busy" && t.busyWith) return t.busyWith;
  if (t.state === "off_shift" && t.nextShiftStartsUtc) return `${t9n.nextShift} ${dayHm(t.nextShiftStartsUtc)}`;
  if (t.shiftEndsUtc) return `${t9n.availableUntil} ${hm(t.shiftEndsUtc)}`;
  return "";
}

function visible() {
  const q = query.trim().toLowerCase();
  return latest.technicians.filter((t) =>
    (filter === "all" || t.state === filter)
    && (!q || [t.name, t.id, t.region, t.vehicle, t.assignment?.street, ...t.certifications].some((v) => v && v.toLowerCase().includes(q))));
}

function select(id) {
  selectedId = id;
  renderList();
  renderReading();
  renderPlanner();
}

function renderPivot() {
  const t9n = TEXT[lang];
  const counts = { all: latest.summary.total, available: latest.summary.available, assigned: latest.summary.assigned, busy: latest.summary.busy, off_shift: latest.summary.offShift };
  $("pivot").replaceChildren(...["all", ...STATES].map((key) => {
    const button = el("button", { type: "button", role: "tab", "aria-selected": String(filter === key), class: filter === key ? "active" : "" },
      t9n[key], el("span", { class: "n" }, String(counts[key])));
    button.addEventListener("click", () => { filter = key; renderPivot(); renderList(); renderStatus(); });
    return button;
  }));
}

function renderList() {
  const t9n = TEXT[lang];
  const list = $("list");
  const scroll = list.scrollTop;
  const rows = visible();
  if (!rows.length) { list.replaceChildren(el("li", { class: "empty" }, t9n.none)); return; }

  list.replaceChildren(...rows.map((t) => {
    const item = el("li", {
      class: `mail-item${t.id === selectedId ? " selected" : ""}${t.id === flashId ? " new-assign" : ""}`,
      role: "option", "aria-selected": String(t.id === selectedId), tabindex: "0", "data-id": t.id,
    }, avatar(t),
      el("span", { class: "mail-name" }, t.name),
      el("span", { class: `mail-state state-${t.state}` }, t9n[t.state]),
      el("span", { class: "mail-preview" }, preview(t, t9n)));
    item.addEventListener("click", () => select(t.id));
    item.addEventListener("keydown", (e) => {
      if (e.key === "Enter" || e.key === " ") { e.preventDefault(); select(t.id); }
      if (e.key === "ArrowDown" || e.key === "ArrowUp") {
        e.preventDefault();
        const next = rows[rows.findIndex((r) => r.id === t.id) + (e.key === "ArrowDown" ? 1 : -1)];
        if (next) { select(next.id); $("list").querySelector(`[data-id="${next.id}"]`)?.focus(); }
      }
    });
    return item;
  }));
  list.scrollTop = scroll;
}

function fact(label, value) {
  return [el("dt", {}, label), el("dd", {}, value)];
}

function renderReading() {
  const t9n = TEXT[lang];
  const t = latest.technicians.find((x) => x.id === selectedId);
  if (!t) { $("reading").replaceChildren(el("p", { class: "no-event" }, t9n.none)); return; }

  const facts = el("dl", { class: "facts" },
    ...fact(t9n.region, t.region),
    ...fact(t9n.vehicle, `${t.vehicle}${t.canTowExcavator ? ", " + t9n.excavator : ""}`),
    ...(t.shiftEndsUtc ? fact(t9n.shift, `${t9n.shiftUntil} ${hm(t.shiftEndsUtc)}`) : t.nextShiftStartsUtc ? fact(t9n.nextShift, dayHm(t.nextShiftStartsUtc)) : []),
    ...(t.busyWith ? fact(t9n.busyWith, `${t.busyWith} ${t9n.shiftUntil} ${hm(t.busyUntilUtc)}`) : []),
    ...fact(t9n.position, `${t.lastPosition.lat.toFixed(3)}, ${t.lastPosition.lon.toFixed(3)} (${hm(t.lastPositionAsOfUtc)})`));

  const jobBlock = t.assignment
    ? el("div", { class: "event" }, el("div", { class: "event-body" },
      el("div", { class: "event-title" }, `${t.assignment.workOrderId} \u00B7 ${t.assignment.street}`),
      el("div", { class: "event-time" }, `${t9n.leaves} ${hm(t.assignment.leavesUtc)} \u00B7 ${t9n.arrives} ${hm(t.assignment.arrivesUtc)} \u00B7 ${t9n.done} ${hm(t.assignment.doneUtc)}`),
      el("a", { href: "/evides" }, `${t9n.incident} ${t.assignment.incidentId}`)))
    : el("p", { class: "no-event" }, t9n.noJob);

  $("reading").replaceChildren(
    el("div", { class: "reading-head" }, avatar(t, "xl"),
      el("div", {}, el("h1", {}, t.name), el("div", { class: "reading-sub" }, `${t.id} \u00B7 ${t.vehicle}`), el("span", { class: `pill ${t.state}` }, t9n[t.state]))),
    facts,
    el("h3", {}, t9n.job), jobBlock,
    el("h3", {}, t9n.certs), el("ul", { class: "certs" }, ...t.certifications.map((c) => el("li", {}, c))));
}

function renderPlanner() {
  const t9n = TEXT[lang];
  const at = Date.parse(latest.atUtc);
  const start = at - BEFORE_H * 3600000, end = at + AFTER_H * 3600000, hours = BEFORE_H + AFTER_H;
  const pos = (ms) => Math.min(100, Math.max(0, ((ms - start) / (end - start)) * 100));
  const bar = (cls, from, to, text, tip) => {
    const a = Date.parse(from), b = Date.parse(to);
    if (b <= start || a >= end) return null;
    const left = pos(a), width = pos(b) - left;
    return el("div", { class: `bar ${cls}`, style: `left:${left}%;width:${width}%`, title: tip }, text);
  };

  // Hour ticks: a line every hour, a label every second local hour.
  const ticks = [];
  for (let ms = Math.ceil(start / 3600000) * 3600000; ms < end; ms += 3600000) {
    const label = hm(new Date(ms).toISOString());
    ticks.push(el("div", { class: "tick", style: `left:${pos(ms)}%` }, parseInt(label, 10) % 2 === 0 ? label : ""));
  }

  const head = el("div", { class: "pl-row pl-head" }, el("div", { class: "pl-label" }), el("div", { class: "pl-track" }, ...ticks));
  const rows = latest.technicians.map((t) => {
    const track = el("div", { class: "pl-track" },
      ...t.shifts.map((s) => bar("shift", s.startUtc, s.endUtc, s.type === "day" ? t9n.dayShift : t9n.nightShift, `${t9n.shift} ${hm(s.startUtc)} - ${hm(s.endUtc)}`)),
      ...t.busyBlocks.map((b) => bar("busy", b.startUtc, b.endUtc, b.description, `${b.description} (${hm(b.startUtc)} - ${hm(b.endUtc)})`)),
      t.assignment ? bar("job", t.assignment.leavesUtc, t.assignment.doneUtc, `${t.assignment.workOrderId} \u00B7 ${t.assignment.street}`,
        `${t9n.job} ${t.assignment.workOrderId}: ${t9n.leaves} ${hm(t.assignment.leavesUtc)}, ${t9n.done} ${hm(t.assignment.doneUtc)}`) : null);
    const row = el("div", { class: `pl-row${t.id === selectedId ? " selected" : ""}` },
      el("div", { class: "pl-label" }, avatar(t, "sm"), el("span", {}, t.name)), track);
    row.addEventListener("click", () => select(t.id));
    return row;
  });

  const inner = el("div", { class: "planner-inner", style: `--hours:${hours}` }, head, ...rows);
  // The red "now" line sits over the track column: the label column is a fixed 190px wide.
  const nowPct = pos(at);
  inner.append(el("div", { class: "now-line", style: `left:calc(190px + (100% - 190px) * ${nowPct / 100});` }));
  $("planner").replaceChildren(inner);
}

function renderStatus() {
  const t9n = TEXT[lang];
  $("status-count").textContent = t9n.count(visible().length, latest.summary.total);
  $("clock").textContent = `${t9n.clock}: ${new Date(latest.atUtc).toLocaleString(lang === "nl" ? "nl-NL" : "en-GB", { dateStyle: "medium", timeStyle: "short", timeZone })}`;
}

function render() {
  if (!latest) return;
  renderPivot(); renderList(); renderReading(); renderPlanner(); renderStatus();
}

function applyLanguage(next) {
  lang = next;
  document.documentElement.lang = lang;
  $("lang-nl").classList.toggle("active", lang === "nl");
  $("lang-en").classList.toggle("active", lang === "en");
  document.querySelectorAll("[data-i18n]").forEach((node) => { node.textContent = TEXT[lang][node.dataset.i18n]; });
  document.querySelectorAll("[data-i18n-placeholder]").forEach((node) => { node.placeholder = TEXT[lang][node.dataset.i18nPlaceholder]; });
  document.querySelectorAll("[data-i18n-aria]").forEach((node) => { node.setAttribute("aria-label", TEXT[lang][node.dataset.i18nAria]); });
  render();
}

function adopt(data) {
  latest = data;
  const assigned = new Set(data.technicians.filter((t) => t.state === "assigned").map((t) => t.id));
  const fresh = [...assigned].find((id) => seenAssigned && !seenAssigned.has(id));
  flashId = fresh ?? null;
  // A newly assigned technician is what the audience came to see: jump to them.
  if (fresh) selectedId = fresh;
  else if (!data.technicians.some((t) => t.id === selectedId)) selectedId = [...assigned][0] ?? data.technicians[0]?.id ?? null;
  seenAssigned = assigned;
}

async function load() {
  try {
    const response = await fetch("/api/technicians", { cache: "no-store" });
    if (!response.ok) throw new Error(String(response.status));
    adopt(await response.json());
    $("error").hidden = true;
    render();
  } catch {
    $("error").textContent = TEXT[lang].loadError;
    $("error").hidden = false;
  }
}

$("lang-nl").addEventListener("click", () => applyLanguage("nl"));
$("lang-en").addEventListener("click", () => applyLanguage("en"));
$("search").addEventListener("input", (e) => { query = e.target.value; if (latest) { renderList(); renderStatus(); } });
applyLanguage("nl");
load();
setInterval(load, 5000);
