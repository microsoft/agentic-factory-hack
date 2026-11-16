// Fake Waterstoring.nl front end. Plain JavaScript, no build step.
// Everything that comes from the API is inserted with textContent (never innerHTML) so incident text cannot inject markup.
// The only innerHTML below are fixed icon strings that are part of this file.

const TEXT = {
  nl: {
    ribbon: "DEMO - dit is een nagebootste website, niet de echte waterstoring.nl. Alle gegevens zijn gesimuleerd.",
    clearBtn: "Meldingen wissen",
    clearTitle: "Alle meldingen wissen?",
    clearText: "Dit haalt alle storingen van deze demosite weg, zodat u opnieuw kunt beginnen. Het kan niet ongedaan worden gemaakt.",
    clearKeyLabel: "Operatorsleutel (dezelfde als voor de operator console)",
    clearCancel: "Annuleren",
    clearGo: "Wissen",
    clearBusy: "Bezig met wissen...",
    clearWrongKey: "Deze sleutel klopt niet.",
    clearFailed: "Wissen is niet gelukt. Probeer het opnieuw.",
    navCompany: "Uw waterbedrijf",
    navReport: "Meld een storing",
    navTech: "Technici (demo)",
    eyebrow: "Live overzicht",
    titleA: "Snel zekerheid over",
    titleB: "uw watervoorziening",
    subtitle: "Vul uw adres, postcode of plaats in. Wij zoeken het direct voor u uit.",
    placeholder: "Adres, postcode of plaats",
    search: "Zoeken",
    locate: "Gebruik mijn locatie",
    mapCardTitle: "Bekijk alle storingen op de kaart",
    mapCardSubShow: "Totaaloverzicht voor het Evides-gebied",
    mapCardSubHide: "Verberg de kaart",
    listTitle: "Actuele storingen en werkzaamheden",
    companiesTitle: "Aangesloten waterbedrijven",
    companiesSub: "Direct uit de bron",
    reportTitle: "Meld een storing",
    reportText: "Staat uw storing er niet bij? Bel Evides op 0900 0787 (optie 2: Storingen). Voor storingen zijn zij 24 uur per dag, 7 dagen per week bereikbaar.",
    footerAbout: "Waterstoring (demo) laat zien hoe realtime informatie over storingen en geplande werkzaamheden er voor bewoners uit kan zien. Dit is een nagebootste site voor het Evides LeakOps-voorbeeld.",
    footerNav: "Navigatie",
    lastUpdated: "Laatst bijgewerkt",
    source: "Bron: gesimuleerde gegevens van het Evides LeakOps-voorbeeld",
    mapCredit: "Kaart: © OpenStreetMap-bijdragers",
    noIncidents: "Er zijn op dit moment geen storingen bekend.",
    noneAtAddress: "Geen storing bekend op deze locatie. Heeft u toch geen water? Bel Evides: 0900 0787 (optie 2).",
    typeMore: "Typ minimaal een postcode, straat of plaats.",
    locating: "Uw locatie wordt bepaald...",
    locationDenied: "Uw locatie is niet beschikbaar. Zoek op adres, postcode of plaats.",
    locationNone: "Geen storing bekend binnen 2 km van uw locatie.",
    expected: "Verwachte oplossing",
    resolvedAt: "Opgelost",
    unknownEta: "Wordt bepaald",
    where: "Locatie",
    customers: "Getroffen aansluitingen",
    history: "Verloop",
    updated: "Bijgewerkt",
    mapOffline: "De kaart kan niet worden geladen (geen internet?). De lijst werkt wel.",
    mapActive: (n) => `${n} actieve storing(en) op de kaart.`,
    status: { reported: "Melding ontvangen", repair_planned: "Reparatie gepland", repair_in_progress: "Reparatie bezig", resolved: "Opgelost" },
  },
  en: {
    ribbon: "DEMO - this is a mock-up site, not the real waterstoring.nl. All data is simulated.",
    clearBtn: "Clear alerts",
    clearTitle: "Clear all alerts?",
    clearText: "This removes every disruption from this demo site so you can start again. It cannot be undone.",
    clearKeyLabel: "Operator key (the same as for the operator console)",
    clearCancel: "Cancel",
    clearGo: "Clear",
    clearBusy: "Clearing...",
    clearWrongKey: "That key is not right.",
    clearFailed: "Clearing failed. Please try again.",
    navCompany: "Your water company",
    navReport: "Report a disruption",
    navTech: "Technicians (demo)",
    eyebrow: "Live overview",
    titleA: "Quick certainty about",
    titleB: "your water supply",
    subtitle: "Enter your address, postcode or town. We look it up for you right away.",
    placeholder: "Address, postcode or town",
    search: "Search",
    locate: "Use my location",
    mapCardTitle: "View all disruptions on the map",
    mapCardSubShow: "Overview for the Evides area",
    mapCardSubHide: "Hide the map",
    listTitle: "Current disruptions and works",
    companiesTitle: "Connected water companies",
    companiesSub: "Straight from the source",
    reportTitle: "Report a disruption",
    reportText: "Is your disruption not listed? Call Evides on 0900 0787 (option 2: Disruptions). They can be reached 24 hours a day, 7 days a week.",
    footerAbout: "Waterstoring (demo) shows what real-time information about disruptions and planned works could look like for residents. This is a mock-up site for the Evides LeakOps example.",
    footerNav: "Navigation",
    lastUpdated: "Last updated",
    source: "Source: simulated data from the Evides LeakOps example",
    mapCredit: "Map: © OpenStreetMap contributors",
    noIncidents: "There are no known disruptions at the moment.",
    noneAtAddress: "No disruption known at this location. Still no water? Call Evides: 0900 0787 (option 2).",
    typeMore: "Type at least a postcode, street or town.",
    locating: "Finding your location...",
    locationDenied: "Your location is not available. Search by address, postcode or town.",
    locationNone: "No disruption known within 2 km of your location.",
    expected: "Expected resolution",
    resolvedAt: "Resolved",
    unknownEta: "To be determined",
    where: "Location",
    customers: "Affected connections",
    history: "Timeline",
    updated: "Updated",
    mapOffline: "The map could not be loaded (offline?). The list still works.",
    mapActive: (n) => `${n} active disruption(s) on the map.`,
    status: { reported: "Report received", repair_planned: "Repair planned", repair_in_progress: "Repair in progress", resolved: "Resolved" },
  },
};

const STATUS_COLOR = { reported: "#cd0024", repair_planned: "#aa5d10", repair_in_progress: "#006a82", resolved: "#0d763e" };
const CHECK_ICON = '<svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="12" cy="12" r="9"/><path d="m8 12.500 2.700 2.700L16 9.500"/></svg>';

const state = { lang: "nl", incidents: [], seen: new Map(), firstLoad: true, mapKey: "", lastSearch: null, mapOpen: true };
const t = () => TEXT[state.lang];

const $ = (id) => document.getElementById(id);
const el = (tag, className, text) => {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text !== undefined) node.textContent = text;
  return node;
};

const locale = () => (state.lang === "nl" ? "nl-NL" : "en-GB");
const formatTime = (iso) =>
  new Date(iso).toLocaleString(locale(), { timeZone: "Europe/Amsterdam", day: "numeric", month: "short", hour: "2-digit", minute: "2-digit" });
const pick = (nl, en) => (state.lang === "en" && en ? en : nl);

// ---- Map ---------------------------------------------------------------------------------------------------------

let map = null;
let layer = null;

function initMap() {
  if (typeof L === "undefined") { $("map-note").textContent = t().mapOffline; return; }
  map = L.map("map").setView([51.86, 4.34], 12);
  L.tileLayer("https://tile.openstreetmap.org/{z}/{x}/{y}.png", { maxZoom: 19, attribution: "&copy; OpenStreetMap contributors" }).addTo(map);
  layer = L.layerGroup().addTo(map);
}

function drawMap() {
  if (!map) return;
  layer.clearLayers();
  const active = state.incidents.filter((i) => i.status !== "resolved");
  const bounds = [];

  for (const incident of active) {
    const color = STATUS_COLOR[incident.status];
    const popup = () => {
      const box = el("div");
      box.append(el("strong", "", pick(incident.title, incident.titleEn)), el("br"), el("span", "", t().status[incident.status]));
      return box;
    };
    if (incident.affectedPath && incident.affectedPath.length > 1) {
      const points = incident.affectedPath.map((p) => [p.lat, p.lon]);
      L.polyline(points, { color, weight: 7, opacity: 0.85 }).bindPopup(popup()).addTo(layer);
      bounds.push(...points);
    }
    L.circleMarker([incident.location.lat, incident.location.lon], { radius: 10, color: "#fff", weight: 2, fillColor: color, fillOpacity: 1 })
      .bindPopup(popup()).addTo(layer);
    bounds.push([incident.location.lat, incident.location.lon]);
  }

  $("map-note").textContent = active.length ? t().mapActive(active.length) : t().noIncidents;

  // Re-fit only when the set of active incidents changes, so the visitor's own panning is not undone every 5 seconds.
  const key = active.map((i) => i.incidentId).sort().join("|");
  if (key !== state.mapKey && state.mapOpen) {
    state.mapKey = key;
    if (bounds.length) map.fitBounds(bounds, { padding: [40, 40], maxZoom: 16 });
  }
}

function setMapOpen(open) {
  state.mapOpen = open;
  $("map-wrap").hidden = !open;
  $("map-toggle").setAttribute("aria-expanded", String(open));
  $("map-card-sub").textContent = open ? t().mapCardSubHide : t().mapCardSubShow;
  if (open && map) {
    map.invalidateSize();   // Leaflet needs this after its container was hidden
    state.mapKey = "";
    drawMap();
  }
}

// ---- Incident cards ----------------------------------------------------------------------------------------------

function fact(list, label, value) {
  list.append(el("dt", "", label), el("dd", "", value));
}

function incidentCard(incident, { withHistory }) {
  const resolved = incident.status === "resolved";
  const changed = state.seen.has(incident.incidentId) && state.seen.get(incident.incidentId) !== incident.updatedUtc;
  const isNew = !state.firstLoad && !state.seen.has(incident.incidentId);
  const card = el("article", `incident ${incident.status}${changed || isNew ? " fresh" : ""}`);

  const top = el("div", "incident-top");
  top.append(el("span", "badge", t().status[incident.status]), el("span", "incident-time", `${t().updated}: ${formatTime(incident.updatedUtc)}`));
  card.append(top, el("h3", "", pick(incident.title, incident.titleEn)), el("p", "", pick(incident.message, incident.messageEn)));

  const facts = el("dl", "facts");
  fact(facts, t().where, [...incident.affectedStreets, incident.place].filter(Boolean).join(", "));
  fact(facts, resolved ? t().resolvedAt : t().expected, resolved ? formatTime(incident.updatedUtc) : incident.expectedResolutionUtc ? formatTime(incident.expectedResolutionUtc) : t().unknownEta);
  fact(facts, t().customers, String(incident.customersAffected));
  card.append(facts);

  if (incident.advice && !resolved) card.append(el("div", "advice", pick(incident.advice, incident.adviceEn)));

  if (withHistory && incident.timeline.length > 1) {
    const details = el("details");
    details.append(el("summary", "", `${t().history} (${incident.timeline.length})`));
    const list = el("ul", "timeline");
    for (const entry of [...incident.timeline].reverse())
      list.append(el("li", "", `${formatTime(entry.atUtc)} - ${t().status[entry.status]}: ${pick(entry.message, entry.messageEn)}`));
    details.append(list);
    card.append(details);
  }
  return card;
}

function renderList() {
  const list = $("incident-list");
  list.replaceChildren();
  if (state.incidents.length === 0) { list.append(el("p", "empty", t().noIncidents)); return; }
  for (const incident of state.incidents) list.append(incidentCard(incident, { withHistory: true }));
}

// ---- Search ------------------------------------------------------------------------------------------------------

function note(message, neutral = false) {
  const box = el("div", `result-note${neutral ? " neutral" : ""}`);
  if (!neutral) box.insertAdjacentHTML("afterbegin", CHECK_ICON);
  box.append(el("span", "", message));
  return box;
}

function showHits(incidents, emptyMessage) {
  const box = $("search-result");
  box.replaceChildren();
  if (incidents.length === 0) { box.append(note(emptyMessage)); return; }
  for (const incident of incidents) box.append(incidentCard(incident, { withHistory: false }));
}

async function search(query) {
  state.lastSearch = { type: "text", query };
  if (query.trim().length < 3) { $("search-result").replaceChildren(note(t().typeMore, true)); return; }
  const response = await fetch(`/api/search?q=${encodeURIComponent(query)}`);
  const result = await response.json();
  showHits(result.incidents, t().noneAtAddress);
}

const metres = (a, b) => {
  const rad = (d) => (d * Math.PI) / 180;
  const h = Math.sin(rad(b.lat - a.lat) / 2) ** 2 + Math.cos(rad(a.lat)) * Math.cos(rad(b.lat)) * Math.sin(rad(b.lon - a.lon) / 2) ** 2;
  return 2 * 6371000 * Math.asin(Math.sqrt(h));
};

function locate() {
  state.lastSearch = { type: "location" };
  const box = $("search-result");
  box.replaceChildren(note(t().locating, true));
  if (!navigator.geolocation) { box.replaceChildren(note(t().locationDenied, true)); return; }

  navigator.geolocation.getCurrentPosition(
    (position) => {
      const here = { lat: position.coords.latitude, lon: position.coords.longitude };
      const near = state.incidents.filter((i) =>
        i.status !== "resolved" && [i.location, ...(i.affectedPath ?? [])].some((p) => metres(here, p) <= 2000));
      showHits(near, t().locationNone);
    },
    () => box.replaceChildren(note(t().locationDenied, true)),
    { timeout: 8000 });
}

// ---- Data --------------------------------------------------------------------------------------------------------

async function refresh() {
  try {
    const response = await fetch("/api/incidents");
    state.incidents = await response.json();
    renderList();
    drawMap();
    const now = new Date();
    $("updated").textContent = `${t().lastUpdated}: ${now.toLocaleTimeString(locale(), { hour: "2-digit", minute: "2-digit", second: "2-digit" })}`;
    $("footer-time").textContent = now.toLocaleTimeString(locale(), { hour: "2-digit", minute: "2-digit" });
    for (const incident of state.incidents) state.seen.set(incident.incidentId, incident.updatedUtc);
    state.firstLoad = false;
  } catch {
    $("updated").textContent = "offline";
  }
}

// ---- Language ----------------------------------------------------------------------------------------------------

function applyLanguage(lang) {
  state.lang = lang;
  document.documentElement.lang = lang;
  $("lang-nl").classList.toggle("active", lang === "nl");
  $("lang-en").classList.toggle("active", lang === "en");
  for (const node of document.querySelectorAll("[data-i18n]")) node.textContent = t()[node.dataset.i18n];
  for (const node of document.querySelectorAll("[data-i18n-placeholder]")) node.placeholder = t()[node.dataset.i18nPlaceholder];
  for (const node of document.querySelectorAll("[data-i18n-aria]")) node.setAttribute("aria-label", t()[node.dataset.i18nAria]);
  $("map-card-title").textContent = t().mapCardTitle;
  $("map-card-sub").textContent = state.mapOpen ? t().mapCardSubHide : t().mapCardSubShow;
  renderList();
  drawMap();
  if (state.lastSearch?.type === "text") search(state.lastSearch.query);   // show the current result in the new language
}

$("lang-nl").addEventListener("click", () => applyLanguage("nl"));
$("lang-en").addEventListener("click", () => applyLanguage("en"));
$("search-form").addEventListener("submit", (event) => { event.preventDefault(); search($("search-input").value); });
$("locate").addEventListener("click", locate);
$("map-toggle").addEventListener("click", () => setMapOpen(!state.mapOpen));

// ---- Clear alerts (demo reset) ----------------------------------------------------------------------------------
// The operator key is typed in each time and only sent to this site's own API; it is never stored.

const clearDialog = $("clear-dialog");
$("clear-open").addEventListener("click", () => {
  $("clear-key").value = "";
  $("clear-msg").textContent = "";
  clearDialog.showModal();
  $("clear-key").focus();
});
$("clear-cancel").addEventListener("click", () => clearDialog.close());
$("clear-form").addEventListener("submit", async (event) => {
  event.preventDefault();
  const go = $("clear-go");
  go.disabled = true;
  $("clear-msg").textContent = t().clearBusy;
  try {
    const response = await fetch("/api/admin/reset", { method: "POST", headers: { "X-Operator-Key": $("clear-key").value } });
    if (response.status === 401) { $("clear-msg").textContent = t().clearWrongKey; return; }
    if (!response.ok) { $("clear-msg").textContent = t().clearFailed; return; }
    state.seen.clear();
    $("search-result").replaceChildren();
    state.lastSearch = null;
    await refresh();
    clearDialog.close();
  } catch {
    $("clear-msg").textContent = t().clearFailed;
  } finally {
    go.disabled = false;
    $("clear-key").value = "";
  }
});

initMap();
applyLanguage("nl");
setMapOpen(true);
refresh();
setInterval(refresh, 5000);   // new incidents and ETA changes show up without reloading the page
