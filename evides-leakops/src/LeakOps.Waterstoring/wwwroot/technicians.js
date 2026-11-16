"use strict";

// Technicians page of the Waterstoring demo: the simulated Evides field roster, who is available and who is assigned to which job.
// It polls the API every 5 seconds, so during the demo you can watch a technician become "assigned".
const TEXT = {
  nl: {
    ribbon: "DEMO - gesimuleerde gegevens. De technici, hun posities en de portretten zijn verzonnen.",
    back: "Naar Waterstoring",
    eyebrow: "Evides LeakOps - demo",
    title: "Technici",
    subtitle: "De ploeg die de Resource Matchmaker ziet: wie is er in dienst, wie is bezet en wie is al toegewezen aan een storing.",
    clock: "Situatie op de simulatieklok:",
    note: "De portretten zijn door AI gemaakt: fictieve personen, geen echte medewerkers. Alle gegevens zijn gesimuleerd.",
    footer: "Evides LeakOps demo. Technici, voertuigen en werkbonnen zijn gesimuleerd.",
    available: "Beschikbaar", assigned: "Toegewezen", busy: "Bezet", off_shift: "Niet in dienst",
    total: "technici",
    vehicle: "Voertuig", shift: "Dienst tot", nextShift: "Volgende dienst", busyWith: "Bezet met", until: "tot", position: "Laatste positie",
    excavator: "kan een graafmachine trekken",
    job: "Werkbon", leaves: "Vertrekt", arrives: "Aankomst", done: "Klaar", incident: "Bekijk de melding",
    loadError: "De technici konden niet worden geladen. Er wordt opnieuw geprobeerd.",
  },
  en: {
    ribbon: "DEMO - simulated data. The technicians, their positions and the portraits are made up.",
    back: "Back to Waterstoring",
    eyebrow: "Evides LeakOps - demo",
    title: "Technicians",
    subtitle: "The crew as the Resource Matchmaker sees it: who is on shift, who is busy and who is already assigned to an incident.",
    clock: "Situation on the simulation clock:",
    note: "The portraits are AI-generated: fictional people, not real employees. All data is simulated.",
    footer: "Evides LeakOps demo. Technicians, vehicles and work orders are simulated.",
    available: "Available", assigned: "Assigned", busy: "Busy", off_shift: "Off shift",
    total: "technicians",
    vehicle: "Vehicle", shift: "On shift until", nextShift: "Next shift", busyWith: "Busy with", until: "until", position: "Last position",
    excavator: "can tow an excavator",
    job: "Work order", leaves: "Leaves", arrives: "Arrives", done: "Done", incident: "View the notice",
    loadError: "Could not load the technicians. Trying again.",
  },
};

const $ = (id) => document.getElementById(id);
let lang = "nl";
let latest = null;
let seenAssigned = new Set();

function el(tag, attrs = {}, ...children) {
  const node = document.createElement(tag);
  for (const [key, value] of Object.entries(attrs)) {
    if (key === "class") node.className = value;
    else node.setAttribute(key, value);
  }
  for (const child of children) if (child != null) node.append(child);
  return node;
}

const timeZone = "Europe/Amsterdam";
const hm = (iso) => new Date(iso).toLocaleTimeString("nl-NL", { hour: "2-digit", minute: "2-digit", timeZone });
const dayHm = (iso) => new Date(iso).toLocaleString(lang === "nl" ? "nl-NL" : "en-GB", { weekday: "short", hour: "2-digit", minute: "2-digit", timeZone });

function initials(name) {
  return name.split(/\s+/).filter(Boolean).map((p) => p[0]).slice(0, 2).join("").toUpperCase();
}

function fact(label, value) {
  return el("div", {}, el("strong", {}, label + ": "), value);
}

function card(t, t9n) {
  const photo = el("div", { class: "tech-photo", "aria-hidden": "true" }, initials(t.name));
  const img = el("img", { src: t.photo, alt: "", loading: "lazy" });
  img.addEventListener("error", () => img.remove());   // no portrait file: the initials stay visible
  photo.append(img);

  const facts = el("div", { class: "tech-facts" },
    fact(t9n.vehicle, `${t.vehicle}${t.canTowExcavator ? ", " + t9n.excavator : ""}`));
  if (t.shiftEndsUtc) facts.append(fact(t9n.shift, hm(t.shiftEndsUtc)));
  else if (t.nextShiftStartsUtc) facts.append(fact(t9n.nextShift, dayHm(t.nextShiftStartsUtc)));
  if (t.busyWith) facts.append(fact(t9n.busyWith, `${t.busyWith} ${t9n.until} ${hm(t.busyUntilUtc)}`));
  facts.append(fact(t9n.position, `${t.lastPosition.lat.toFixed(3)}, ${t.lastPosition.lon.toFixed(3)} (${hm(t.lastPositionAsOfUtc)})`));

  const li = el("li", { class: `tech-card ${t.state}`, id: `tech-${t.id}` },
    el("div", { class: "tech-head" }, photo,
      el("div", {}, el("h2", {}, t.name), el("div", { class: "tech-id" }, t.id),
        el("span", { class: `tech-badge ${t.state}` }, t9n[t.state]))),
    facts,
    el("ul", { class: "tech-certs" }, ...t.certifications.map((c) => el("li", {}, c))));

  if (t.assignment) {
    const job = t.assignment;
    li.append(el("div", { class: "tech-job" },
      el("strong", {}, `${t9n.job} ${job.workOrderId}`),
      el("div", {}, job.street),
      el("div", {}, `${t9n.leaves} ${hm(job.leavesUtc)} - ${t9n.arrives} ${hm(job.arrivesUtc)} - ${t9n.done} ${hm(job.doneUtc)}`),
      el("a", { href: "/evides" }, `${t9n.incident} ${job.incidentId}`)));
  }
  return li;
}

function render() {
  if (!latest) return;
  const t9n = TEXT[lang];
  const { summary, technicians, atUtc } = latest;

  $("clock").textContent = `${t9n.clock} ${new Date(atUtc).toLocaleString(lang === "nl" ? "nl-NL" : "en-GB", { dateStyle: "medium", timeStyle: "short", timeZone })}`;
  $("summary").replaceChildren(
    el("li", {}, el("strong", {}, String(summary.total)), t9n.total),
    ...["available", "assigned", "busy", "off_shift"].map((s) =>
      el("li", { class: `s-${s}` }, el("strong", {}, String(summary[s === "off_shift" ? "offShift" : s])), t9n[s])));

  // Only a technician who is newly assigned gets the pop animation, not every 5-second refresh.
  const now = new Set(technicians.filter((t) => t.state === "assigned").map((t) => t.id));
  const cards = technicians.map((t) => {
    const node = card(t, t9n);
    if (now.has(t.id) && seenAssigned.has(t.id)) node.style.animation = "none";
    return node;
  });
  seenAssigned = now;
  $("grid").replaceChildren(...cards);
}

function applyLanguage(next) {
  lang = next;
  document.documentElement.lang = lang;
  $("lang-nl").classList.toggle("active", lang === "nl");
  $("lang-en").classList.toggle("active", lang === "en");
  document.querySelectorAll("[data-i18n]").forEach((node) => { node.textContent = TEXT[lang][node.dataset.i18n]; });
  render();
}

async function load() {
  try {
    const response = await fetch("/api/technicians", { cache: "no-store" });
    if (!response.ok) throw new Error(String(response.status));
    latest = await response.json();
    $("error").hidden = true;
    render();
  } catch {
    $("error").textContent = TEXT[lang].loadError;
    $("error").hidden = false;
  }
}

$("lang-nl").addEventListener("click", () => applyLanguage("nl"));
$("lang-en").addEventListener("click", () => applyLanguage("en"));
applyLanguage("nl");
load();
setInterval(load, 5000);
