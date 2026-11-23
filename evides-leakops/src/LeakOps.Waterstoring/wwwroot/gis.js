"use strict";

// Simulated network map. A real street map (OpenStreetMap) with the made-up pipes, valves and sensors of the LeakOps demo on top.
// Once the Diagnostic agent has looked at an alarm, the page also shows where the break is, which valves to close in which order and
// who is affected. It polls /api/gis every 5 seconds, so during the demo the incident appears on its own.
const TEXT = {
  nl: {
    app: "Netkaart", appSub: "Leidingnet-viewer \u00B7 demo", navGis: "Netkaart", navTech: "Technici", navWarehouse: "Magazijn", navIncidents: "Meldingen",
    live: "Live", clock: "Simulatieklok", mapLabel: "Kaart van het gesimuleerde leidingnet",
    bannerTitle: "Gesimuleerde demo.",
    bannerText: "Dit is niet het GIS van Evides en geen echt Evides-systeem. Leidingen, afsluiters, sensoren en de storing zijn verzonnen voor de LeakOps-demo; alleen de straatkaart (OpenStreetMap) is echt.",
    layers: "Kaartlagen", pipes: "Leidingen", valves: "Afsluiters", sensors: "Sensoren", incidentLayer: "Storing (diagnose)",
    noIncident: "Nog geen diagnose: de kaart toont alleen het leidingnet. Zodra de Diagnostic-agent een alarm heeft onderzocht, verschijnen hier de plek van de breuk, de afsluiters en wie er last van heeft.",
    incident: "Storing", breach: { burst: "Breuk", leak: "Lekkage" },
    severity: { S1: "S1 kritiek", S2: "S2 groot", S3: "S3 matig", S4: "S4 klein" },
    review: "Moet worden gecontroleerd", reviewReasons: "Waarom controleren",
    leakAt: "Plek", chainage: (m, u) => `${m} m \u00B1 ${u} m vanaf het begin van de leiding`, coordinate: "Co\u00F6rdinaat",
    pipe: "Leiding", customers: "Getroffen aansluitingen", critical: "Kritieke klant", streets: "Straten", postal: "Postcodes",
    isolation: "Afsluiten in deze volgorde", backup: "Reserve-afsluiter", backupNote: "klaarhouden als het afsluiten niet houdt",
    turns: (n) => `${n} slagen`, stiff: "mogelijk stroef: langer dan 2 jaar niet bediend", lastOperated: "laatst bediend",
    diagnosed: "Alarm (simulatieklok)", zoom: "Zoom naar de breuk", summary: "Samenvatting van de agent",
    material: "Materiaal", diameter: "Diameter", installYear: "Aanlegjaar", length: "Lengte", depth: "Diepte", pressureClass: "Drukklasse",
    served: "Aansluitingen", previousBursts: "Eerdere breuken", district: "Gebied", position: "Positie", onPipe: (m, seg) => `${m} m op ${seg}`,
    type: "Type", status: "Stand", metric: "Meet", alarm: "Alarmgrens", sampling: "Meetinterval", minutes: (n) => `elke ${n} min`,
    materials: { cast_iron: "Gietijzer", ductile_iron: "Nodulair gietijzer", pvc: "PVC", pe: "PE", asbestos_cement: "Asbestcement", steel: "Staal" },
    valveTypes: { gate_valve: "Schuifafsluiter", butterfly_valve: "Vlinderklep" },
    valveStatus: { open: "open", closed: "dicht", seized: "vast" },
    metrics: { pressure: "Druk", flow: "Debiet" }, sensorStatus: { online: "online", offline: "offline" },
    actions: { close: "dichtdraaien" },
    legendMaterial: "Materiaal", legendIncident: "Storing", legendLeak: "Breuk + onzekerheid", legendStep: "Afsluiter (volgorde)", legendBackup: "Reserve-afsluiter",
    statusNote: "Alle netwerkgegevens zijn gesimuleerd. Kaart \u00A9 OpenStreetMap-bijdragers.",
    count: (p, v, s) => `${p} leidingen \u00B7 ${v} afsluiters \u00B7 ${s} sensoren`,
    updated: "bijgewerkt",
    loadError: "De netkaart kon niet worden geladen. Er wordt opnieuw geprobeerd.",
    mapError: "De kaart kon niet worden getekend: de kaartbibliotheek is niet bereikbaar.",
  },
  en: {
    app: "Network map", appSub: "Pipe network viewer \u00B7 demo", navGis: "Network map", navTech: "Technicians", navWarehouse: "Warehouse", navIncidents: "Incidents",
    live: "Live", clock: "Simulation clock", mapLabel: "Map of the simulated pipe network",
    bannerTitle: "Simulated demo.",
    bannerText: "This is not the Evides GIS and not a real Evides system. The pipes, valves, sensors and the incident are made up for the LeakOps demo; only the street map (OpenStreetMap) is real.",
    layers: "Map layers", pipes: "Pipes", valves: "Valves", sensors: "Sensors", incidentLayer: "Incident (diagnosis)",
    noIncident: "No diagnosis yet: the map shows the pipe network only. As soon as the Diagnostic agent has looked at an alarm, the break, the valves to close and who is affected appear here.",
    incident: "Incident", breach: { burst: "Burst", leak: "Leak" },
    severity: { S1: "S1 critical", S2: "S2 major", S3: "S3 moderate", S4: "S4 minor" },
    review: "Needs review", reviewReasons: "Why review",
    leakAt: "Position", chainage: (m, u) => `${m} m \u00B1 ${u} m from the start of the pipe`, coordinate: "Coordinate",
    pipe: "Pipe", customers: "Customers affected", critical: "Critical customer", streets: "Streets", postal: "Postal codes",
    isolation: "Close in this order", backup: "Backup valve", backupNote: "keep ready in case isolation does not hold",
    turns: (n) => `${n} turns`, stiff: "may be stiff: not operated for more than 2 years", lastOperated: "last operated",
    diagnosed: "Alarm (simulation clock)", zoom: "Zoom to the break", summary: "Agent summary",
    material: "Material", diameter: "Diameter", installYear: "Installed", length: "Length", depth: "Depth", pressureClass: "Pressure class",
    served: "Customers", previousBursts: "Previous bursts", district: "District", position: "Position", onPipe: (m, seg) => `${m} m on ${seg}`,
    type: "Type", status: "Status", metric: "Measures", alarm: "Alarm limit", sampling: "Sampling", minutes: (n) => `every ${n} min`,
    materials: { cast_iron: "Cast iron", ductile_iron: "Ductile iron", pvc: "PVC", pe: "PE", asbestos_cement: "Asbestos cement", steel: "Steel" },
    valveTypes: { gate_valve: "Gate valve", butterfly_valve: "Butterfly valve" },
    valveStatus: { open: "open", closed: "closed", seized: "seized" },
    metrics: { pressure: "Pressure", flow: "Flow" }, sensorStatus: { online: "online", offline: "offline" },
    actions: { close: "close" },
    legendMaterial: "Material", legendIncident: "Incident", legendLeak: "Break + uncertainty", legendStep: "Valve (order)", legendBackup: "Backup valve",
    statusNote: "All network data is simulated. Map \u00A9 OpenStreetMap contributors.",
    count: (p, v, s) => `${p} pipes \u00B7 ${v} valves \u00B7 ${s} sensors`,
    updated: "updated",
    loadError: "Could not load the network map. Trying again.",
    mapError: "The map could not be drawn: the map library could not be loaded.",
  },
};

const MATERIAL_COLOR = { cast_iron: "#8c5a2b", ductile_iron: "#3d5a73", pvc: "#2a9d8f", pe: "#d08a1e", asbestos_cement: "#8a8f96", steel: "#4b4f8c" };
const LEAK = "#c4262e";
const timeZone = "Europe/Amsterdam";
const $ = (id) => document.getElementById(id);

let lang = "nl";
let latest = null;
let latestJson = "";
let map = null;
let layers = null;
let fitted = false;
let focusedKey = null;   // which diagnosis the map last zoomed to, so a new one zooms again but polling does not

function el(tag, attrs = {}, ...children) {
  const node = document.createElement(tag);
  for (const [key, value] of Object.entries(attrs)) {
    if (key === "class") node.className = value;
    else node.setAttribute(key, value);
  }
  for (const child of children) if (child != null) node.append(child);
  return node;
}

const t = () => TEXT[lang];
const label = (dict, key) => dict[key] ?? String(key).replace(/_/g, " ");
const ll = (p) => [p.lat, p.lon];
const coord = (p) => `${p.lat.toFixed(5)}, ${p.lon.toFixed(5)}`;
const when = (iso) => new Date(iso).toLocaleString(lang === "nl" ? "nl-NL" : "en-GB", { dateStyle: "medium", timeStyle: "short", timeZone });
const day = (iso) => new Date(iso).toLocaleDateString(lang === "nl" ? "nl-NL" : "en-GB", { dateStyle: "medium", timeZone });
const streetName = (street) => street.split(" (")[0].trim();

function card(title, rows, note = null) {
  const dl = el("dl");
  for (const [k, v] of rows) if (v != null && v !== "") dl.append(el("dt", {}, k), el("dd", {}, String(v)));
  return el("div", { class: "pop" }, el("h4", {}, title), dl, note ? el("p", {}, note) : null);
}

function icon(className, text = "", size = 16, shift = [0, 0]) {
  const span = document.createElement("span");
  span.textContent = text;
  // shift moves the icon off its point in pixels, so two markers on one junction stay visible
  return L.divIcon({ className, html: span.outerHTML, iconSize: [size, size], iconAnchor: [size / 2 - shift[0], size / 2 - shift[1]], popupAnchor: [shift[0], shift[1] - size / 2] });
}

function initMap() {
  map = L.map("map", { zoomControl: true, preferCanvas: false }).setView([51.85, 4.33], 13);
  L.tileLayer("https://tile.openstreetmap.org/{z}/{x}/{y}.png", {
    maxZoom: 19,
    attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors',
  }).addTo(map);
  layers = { pipes: L.layerGroup(), valves: L.layerGroup(), sensors: L.layerGroup(), incident: L.layerGroup() };
  for (const [name, group] of Object.entries(layers)) {
    group.addTo(map);
    $(`lyr-${name}`).addEventListener("change", (e) => (e.target.checked ? group.addTo(map) : group.remove()));
  }
}

function drawNetwork(data) {
  const x = t();
  const affected = data.incident?.segmentId;
  layers.pipes.clearLayers(); layers.valves.clearLayers(); layers.sensors.clearLayers();
  const byId = Object.fromEntries(data.network.pipes.map((p) => [p.id, p]));

  for (const p of data.network.pipes) {
    const hit = p.id === affected;
    L.polyline([ll(p.start), ll(p.end)], {
      color: hit ? LEAK : MATERIAL_COLOR[p.material] ?? "#3d5a73", weight: Math.max(3, Math.round(p.diameterMm / 50)), opacity: hit ? 0.95 : 0.85,
    })
      .bindTooltip(el("span", {}, `${p.id} \u00B7 ${streetName(p.street)}`), { sticky: true })   // a node, not a string: Leaflet inserts strings as HTML
      .bindPopup(card(`${p.id} \u00B7 ${streetName(p.street)}`, [
        [x.district, `${p.place} (${p.districtId})`],
        [x.material, label(x.materials, p.material)],
        [x.diameter, `DN${p.diameterMm}`],
        [x.installYear, p.installYear],
        [x.length, `${Math.round(p.lengthM)} m`],
        [x.depth, `${p.depthM} m`],
        [x.pressureClass, p.pressureClass],
        [x.served, p.customersServed],
        [x.critical, p.criticalCustomers.join(", ")],
        [x.previousBursts, p.previousBursts],
        [x.valves, p.isolationValveIds.join(", ")],
      ]))
      .addTo(layers.pipes);
  }

  for (const v of data.network.valves) {
    const seg = byId[v.segmentId];
    L.marker(ll(v.location), { icon: icon(`mk-valve ${v.status}`), title: v.id })
      .bindPopup(card(v.id, [
        [x.type, `${label(x.valveTypes, v.type)} DN${v.diameterMm}`],
        [x.status, label(x.valveStatus, v.status)],
        [x.position, x.onPipe(Math.round(v.chainageM), seg ? `${seg.id} ${streetName(seg.street)}` : v.segmentId)],
        [x.lastOperated, v.lastOperatedUtc ? day(v.lastOperatedUtc) + (v.mayBeStiff ? ` (${x.stiff})` : "") : "-"],
      ], x.turns(v.turnsToClose)))
      .addTo(layers.valves);
  }

  for (const s of data.network.sensors) {
    const limits = [s.alarmLow != null ? `< ${s.alarmLow}` : null, s.alarmHigh != null ? `> ${s.alarmHigh}` : null].filter(Boolean).join(" / ");
    L.marker(ll(s.location), { icon: icon("mk-sensor"), title: s.id })
      .bindPopup(card(`${s.id} \u00B7 ${s.name}`, [
        [x.metric, `${label(x.metrics, s.metric)} (${s.unit})`],
        [x.alarm, limits ? `${limits} ${s.unit}` : "-"],
        [x.sampling, x.minutes(s.samplingMinutes)],
        [x.position, x.onPipe(Math.round(s.chainageM), s.segmentId)],
        [x.status, label(x.sensorStatus, s.status)],
      ]))
      .addTo(layers.sensors);
  }
}

function drawIncident(inc) {
  const x = t();
  layers.incident.clearLayers();
  if (!inc) return;

  L.polyline(inc.uncertainStretch.map(ll), { color: LEAK, weight: 14, opacity: 0.3, lineCap: "butt" }).addTo(layers.incident);
  L.circle(ll(inc.location), { radius: inc.uncertaintyM, color: LEAK, weight: 1.5, dashArray: "5 5", fillColor: LEAK, fillOpacity: 0.1 }).addTo(layers.incident);
  L.marker(ll(inc.location), { icon: icon("mk-leak", "", 24), title: inc.incidentId, zIndexOffset: 1000 })
    .bindPopup(card(`${label(x.severity, inc.severity)} \u00B7 ${label(x.breach, inc.breachType)} \u00B7 ${inc.segmentId} ${inc.street}`, [
      [x.leakAt, x.chainage(Math.round(inc.chainageM), Math.round(inc.uncertaintyM))],
      [x.coordinate, coord(inc.location)],
      [x.customers, inc.customersAffected],
      [x.critical, inc.criticalCustomers.join(", ")],
    ]))
    .addTo(layers.incident);

  for (const s of inc.isolationPlan) {
    L.marker(ll(s.location), { icon: icon("mk-step", String(s.order), 28), title: `${s.order}. ${s.valveId}`, zIndexOffset: 900 })
      .bindPopup(card(`${s.order}. ${s.valveId}`, [
        [x.status, `${label(x.valveStatus, s.status)} \u2192 ${label(x.actions, s.action)}`],
        [x.lastOperated, s.lastOperatedUtc ? day(s.lastOperatedUtc) + (s.mayBeStiff ? ` (${x.stiff})` : "") : "-"],
      ], x.turns(s.turnsToClose)))
      .addTo(layers.incident);
  }
  for (const b of inc.backupValves) {
    const shared = inc.isolationPlan.some((s) => map.distance(ll(s.location), ll(b.location)) < 5);
    L.marker(ll(b.location), { icon: icon("mk-step backup", "R", 28, shared ? [26, 22] : [0, 0]), title: `${x.backup}: ${b.valveId}`, zIndexOffset: 800 })
      .bindPopup(card(`${x.backup}: ${b.valveId}`, [[x.status, label(x.valveStatus, b.status)], [x.pipe, b.segmentId]], x.backupNote))
      .addTo(layers.incident);
  }
}

function focusIncident(inc) {
  const points = [...inc.segmentPath.map(ll), ...inc.isolationPlan.map((s) => ll(s.location)), ...inc.backupValves.map((b) => ll(b.location))];
  // keep the incident clear of the legend in the bottom-right corner
  const legend = $("legend").offsetWidth || 0;
  map.fitBounds(L.latLngBounds(points), { maxZoom: 17, paddingTopLeft: [60, 60], paddingBottomRight: [legend + 60, 60] });
}

function chips(values) {
  return el("div", { class: "chips" }, ...values.map((v) => el("span", { class: "chip" }, v)));
}

function renderPanel(inc) {
  const x = t();
  const panel = $("incident");
  if (!inc) {
    panel.replaceChildren(el("h2", {}, x.incident), el("p", { class: "empty" }, x.noIncident));
    return;
  }

  const head = el("div", { class: "inc-head" },
    el("span", { class: `sev ${inc.severity}` }, label(x.severity, inc.severity)),
    el("span", { class: "tag" }, label(x.breach, inc.breachType)),
    inc.needsReview ? el("span", { class: "tag review" }, x.review) : null);

  const facts = el("dl", { class: "facts" },
    el("dt", {}, x.leakAt), el("dd", {}, x.chainage(Math.round(inc.chainageM), Math.round(inc.uncertaintyM))),
    el("dt", {}, x.coordinate), el("dd", {}, coord(inc.location)),
    el("dt", {}, x.pipe), el("dd", {}, `${label(x.materials, inc.material)} DN${inc.diameterMm} (${inc.installYear})`),
    el("dt", {}, x.customers), el("dd", { class: "big" }, inc.customersAffected.toLocaleString(lang === "nl" ? "nl-NL" : "en-GB")),
    el("dt", {}, x.diagnosed), el("dd", {}, when(inc.triggeredAtUtc)));

  const critical = inc.criticalCustomers.length
    ? el("div", { class: "critical" }, el("strong", {}, x.critical), inc.criticalCustomers.join(", "))
    : null;

  const steps = el("ol", { class: "steps" },
    ...inc.isolationPlan.map((s) => el("li", {},
      el("span", { class: "num" }, String(s.order)),
      el("div", {},
        el("strong", {}, s.valveId), ` \u00B7 ${x.turns(s.turnsToClose)}`,
        el("small", { class: s.mayBeStiff ? "stiff" : "" },
          `${x.lastOperated} ${s.lastOperatedUtc ? day(s.lastOperatedUtc) : "-"}` + (s.mayBeStiff ? ` \u00B7 ${x.stiff}` : ""))))),
    ...inc.backupValves.map((b) => el("li", {},
      el("span", { class: "num backup" }, "R"),
      el("div", {}, el("strong", {}, `${x.backup}: ${b.valveId}`), el("small", {}, x.backupNote)))));

  const review = inc.needsReview && inc.reviewReasons.length
    ? [el("h3", { class: "sub-h" }, x.reviewReasons), el("ul", {}, ...inc.reviewReasons.map((r) => el("li", {}, r)))]
    : [];

  const zoom = el("button", { class: "zoom-btn", type: "button" }, x.zoom);
  zoom.addEventListener("click", () => focusIncident(inc));

  panel.replaceChildren(...[
    el("h2", {}, `${x.incident} ${inc.incidentId}`),
    head,
    el("p", { class: "inc-title" }, `${inc.segmentId} ${inc.street}`),
    el("p", { class: "inc-sub" }, `${inc.place} \u00B7 ${inc.alarmId}`),
    facts,
    critical,
    el("h3", { class: "sub-h" }, x.isolation), steps,
    el("h3", { class: "sub-h" }, x.streets), chips(inc.servedStreets),
    el("h3", { class: "sub-h" }, x.postal), chips(inc.postalCodes),
    ...review,
    el("h3", { class: "sub-h" }, x.summary), el("p", { class: "summary" }, inc.summary),
    zoom
  ].filter(Boolean));
}

function renderLegend() {
  const x = t();
  const used = [...new Set((latest?.network.pipes ?? []).map((p) => p.material))];
  const inc = latest?.incident;
  $("legend").replaceChildren(...[
    el("h3", {}, x.legendMaterial),
    ...used.map((m) => el("div", {}, el("i", { style: `background:${MATERIAL_COLOR[m] ?? "#3d5a73"}` }), label(x.materials, m))),
    inc ? el("h3", { style: "margin-top:6px" }, x.legendIncident) : null,
    inc ? el("div", {}, el("i", { style: `background:${LEAK};height:8px;opacity:.6` }), x.legendLeak) : null,
    inc ? el("div", {}, el("span", { class: "num", style: "width:16px;height:16px;font-size:10px" }, "1"), x.legendStep) : null,
    inc?.backupValves.length ? el("div", {}, el("span", { class: "num backup", style: "width:16px;height:16px;font-size:10px" }, "R"), x.legendBackup) : null
  ].filter(Boolean));   // replaceChildren would print a null as the text "null"
}

function renderStatus() {
  const x = t();
  const n = latest.network;
  $("n-pipes").textContent = n.pipes.length;
  $("n-valves").textContent = n.valves.length;
  $("n-sensors").textContent = n.sensors.length;
  $("status").textContent = `${x.count(n.pipes.length, n.valves.length, n.sensors.length)} \u00B7 ${x.updated} ${new Date().toLocaleTimeString(lang === "nl" ? "nl-NL" : "en-GB", { timeZone })}`;
  $("clock").textContent = `${x.clock}: ${when(latest.atUtc)}`;
}

function render(redraw) {
  if (!latest) return;
  if (redraw && map) {
    drawNetwork(latest);
    drawIncident(latest.incident);
    if (!fitted) {
      const pts = latest.network.pipes.flatMap((p) => [ll(p.start), ll(p.end)]);
      if (pts.length) map.fitBounds(L.latLngBounds(pts).pad(0.1));
      fitted = true;
    }
    const key = latest.incident ? `${latest.incident.alarmId}|${latest.incident.diagnosedAtUtc}` : null;
    if (key && key !== focusedKey) focusIncident(latest.incident);
    focusedKey = key;
  }
  if (redraw) { renderPanel(latest.incident); renderLegend(); }
  renderStatus();
}

function applyLanguage(next) {
  lang = next;
  document.documentElement.lang = lang;
  document.title = lang === "nl" ? "Netkaart (demo, gesimuleerd) | LeakOps" : "Network map (demo, simulated) | LeakOps";
  $("lang-nl").classList.toggle("active", lang === "nl");
  $("lang-en").classList.toggle("active", lang === "en");
  document.querySelectorAll("[data-i18n]").forEach((node) => { node.textContent = TEXT[lang][node.dataset.i18n]; });
  document.querySelectorAll("[data-i18n-aria]").forEach((node) => { node.setAttribute("aria-label", TEXT[lang][node.dataset.i18nAria]); });
  render(true);
}

async function load() {
  try {
    const response = await fetch("/api/gis", { cache: "no-store" });
    if (!response.ok) throw new Error(String(response.status));
    const text = await response.text();
    const changed = text !== latestJson;   // redraw only on a change, so an open popup stays open while polling
    latestJson = text;
    latest = JSON.parse(text);
    $("error").hidden = !!map;   // without the map library the map error stays visible
    render(changed);
  } catch {
    $("error").textContent = TEXT[lang].loadError;
    $("error").hidden = false;
  }
}

$("lang-nl").addEventListener("click", () => applyLanguage("nl"));
$("lang-en").addEventListener("click", () => applyLanguage("en"));
if (window.L) initMap();
applyLanguage("nl");
if (!window.L) { $("error").textContent = TEXT[lang].mapError; $("error").hidden = false; }
load();
setInterval(load, 5000);
