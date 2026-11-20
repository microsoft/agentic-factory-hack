"use strict";

// Warehouse page, styled like an internal Power Apps model-driven app on Dataverse: a sitemap, a command bar, a data grid with views
// and a record form. It polls the API every 5 seconds, so during the demo you see Procurement reserve parts and place orders.
const TEXT = {
  nl: {
    app: "Magazijn", search: "Zoek artikelen, orders of incidenten", env: "DEMO - gesimuleerd",
    grpStock: "Voorraad", grpProc: "Inkoop", grpAudit: "Audit", grpApps: "Andere apps",
    navItems: "Artikelen", navOrders: "Inkooporders", navReservations: "Reserveringen", navChanges: "Recente wijzigingen",
    navTech: "Technici", navIncidents: "Meldingen",
    cmdNew: "Nieuw", cmdRefresh: "Vernieuwen", cmdExport: "Exporteren naar Excel", live: "Live", clock: "Simulatieklok",
    statusNote: "Alle gegevens zijn gesimuleerd (Evides-depots, leveranciers en prijzen zijn verzonnen).",
    vItems: "Actieve artikelen", vOrders: "Inkooporders", vReservations: "Voorraadreserveringen", vChanges: "Recente wijzigingen",
    fAll: "Alles", fParts: "Onderdelen", fEquipment: "Materieel", fLow: "Lage voorraad", fChanged: "Gewijzigd door Procurement",
    kItems: "Artikelen", kLow: "Lage voorraad", kOut: "Niet beschikbaar", kReserved: "Gereserveerd", kOrders: "Open orders", kApproval: "Wacht op goedkeuring", kChanged: "Gewijzigd",
    cSku: "Artikel", cName: "Omschrijving", cWarehouse: "Depot", cOnHand: "Op voorraad", cReserved: "Gereserveerd", cAvailable: "Beschikbaar", cLevel: "Niveau",
    cReorder: "Bestelniveau", cPrice: "Prijs", cSupplier: "Leverancier",
    cOrder: "Order", cIncident: "Incident", cQty: "Aantal", cType: "Type", cStatus: "Status", cTotal: "Totaal", cDelivery: "Verwacht", cAt: "Gereserveerd op",
    ok: "Op voorraad", low: "Laag", out: "Geen voorraad", express: "Spoed", standard: "Standaard", ordered: "Besteld", pending_approval: "Wacht op goedkeuring",
    changed: "Gewijzigd", none: "Niets gevonden.", count: (n, total) => `${n} van ${total} records`,
    part: "Onderdeel", equipment: "Materieel", backAt: "Weer beschikbaar vanaf",
    tabGeneral: "Algemeen", tabReservations: "Reserveringen", tabOrders: "Inkooporders",
    secStock: "Voorraad", secDetails: "Details", secSupplier: "Leverancier",
    fKind: "Soort", fCategory: "Categorie", fWarehouse: "Depot", fReorder: "Bestelniveau", fReorderQty: "Bestelhoeveelheid", fPrice: "Stukprijs", fNotes: "Opmerking",
    fLead: "Levertijd standaard", fExpress: "Levertijd spoed", days: "dagen", hours: "uur",
    gReserved: "gereserveerd", gAvailable: "beschikbaar", gOnHand: "op voorraad",
    noReservations: "Niet gereserveerd voor een incident.", noOrders: "Geen openstaande inkooporders.",
    reserved: "Gereserveerd", reservedFor: "voor incident", orderedFor: "Besteld voor incident", expressNote: "spoedlevering",
    chReservation: "Gereserveerd", chOrder: "Inkooporder", selectRow: "Kies een artikel.",
    loadError: "Het magazijn kon niet worden geladen. Er wordt opnieuw geprobeerd.",
  },
  en: {
    app: "Warehouse", search: "Search items, orders or incidents", env: "DEMO - simulated",
    grpStock: "Stock", grpProc: "Procurement", grpAudit: "Audit", grpApps: "Other apps",
    navItems: "Items", navOrders: "Purchase orders", navReservations: "Reservations", navChanges: "Recent changes",
    navTech: "Technicians", navIncidents: "Incidents",
    cmdNew: "New", cmdRefresh: "Refresh", cmdExport: "Export to Excel", live: "Live", clock: "Simulation clock",
    statusNote: "All data is simulated (Evides depots, suppliers and prices are made up).",
    vItems: "Active items", vOrders: "Purchase orders", vReservations: "Stock reservations", vChanges: "Recent changes",
    fAll: "All", fParts: "Parts", fEquipment: "Equipment", fLow: "Low stock", fChanged: "Changed by Procurement",
    kItems: "Items", kLow: "Low stock", kOut: "Unavailable", kReserved: "Reserved", kOrders: "Open orders", kApproval: "Pending approval", kChanged: "Changed",
    cSku: "Item", cName: "Description", cWarehouse: "Depot", cOnHand: "On hand", cReserved: "Reserved", cAvailable: "Available", cLevel: "Level",
    cReorder: "Reorder level", cPrice: "Price", cSupplier: "Supplier",
    cOrder: "Order", cIncident: "Incident", cQty: "Quantity", cType: "Type", cStatus: "Status", cTotal: "Total", cDelivery: "Expected", cAt: "Reserved at",
    ok: "In stock", low: "Low", out: "No stock", express: "Express", standard: "Standard", ordered: "Ordered", pending_approval: "Pending approval",
    changed: "Changed", none: "Nothing found.", count: (n, total) => `${n} of ${total} records`,
    part: "Part", equipment: "Equipment", backAt: "Available again from",
    tabGeneral: "General", tabReservations: "Reservations", tabOrders: "Purchase orders",
    secStock: "Stock", secDetails: "Details", secSupplier: "Supplier",
    fKind: "Kind", fCategory: "Category", fWarehouse: "Depot", fReorder: "Reorder level", fReorderQty: "Reorder quantity", fPrice: "Unit price", fNotes: "Note",
    fLead: "Standard lead time", fExpress: "Express lead time", days: "days", hours: "hours",
    gReserved: "reserved", gAvailable: "available", gOnHand: "on hand",
    noReservations: "Not reserved for an incident.", noOrders: "No open purchase orders.",
    reserved: "Reserved", reservedFor: "for incident", orderedFor: "Ordered for incident", expressNote: "express delivery",
    chReservation: "Reserved", chOrder: "Purchase order", selectRow: "Select an item.",
    loadError: "Could not load the warehouse. Trying again.",
  },
};

const timeZone = "Europe/Amsterdam";
const $ = (id) => document.getElementById(id);
let lang = "nl", latest = null, view = "items", filter = "all", query = "", tab = "general";
let sort = { key: "sku", dir: 1 }, selectedSku = null, snapshot = null, flash = new Set();

function el(tag, attrs = {}, ...children) {
  const node = document.createElement(tag);
  for (const [key, value] of Object.entries(attrs)) {
    if (key === "class") node.className = value;
    else node.setAttribute(key, value);
  }
  for (const child of children) if (child != null) node.append(child);
  return node;
}

const loc = () => (lang === "nl" ? "nl-NL" : "en-GB");
const money = (v) => v.toLocaleString(loc(), { style: "currency", currency: "EUR" });
const when = (iso) => new Date(iso).toLocaleString(loc(), { day: "2-digit", month: "short", hour: "2-digit", minute: "2-digit", timeZone });
const isChanged = (it) => it.reservations.length > 0 || it.orders.length > 0;
const t9 = () => TEXT[lang];

function icon(path) {
  const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
  svg.setAttribute("viewBox", "0 0 24 24");
  svg.setAttribute("aria-hidden", "true");
  svg.innerHTML = path;
  return svg;
}
const ICON = {
  box: '<path d="M3.5 8 12 3.5 20.5 8v8L12 20.5 3.5 16V8Z"/><path d="M3.5 8 12 12.5 20.5 8M12 12.5v8"/>',
  lock: '<rect x="5" y="10.5" width="14" height="10" rx="2"/><path d="M8 10.5V8a4 4 0 0 1 8 0v2.5"/>',
  doc: '<path d="M6 3.5h9l3 3V20.5H6V3.5Z"/><path d="M9 11h6M9 14.5h6M9 8h3"/>',
};

function levelChip(it) { return el("span", { class: `chip ${it.level}` }, t9()[it.level]); }

function levelBar(it) {
  const max = Math.max(it.onHand, it.reorderLevel * 2, 1);
  return el("span", { class: `bar ${it.level}`, title: `${it.available}/${it.onHand}` }, el("i", { style: `width:${Math.min(100, (it.available / max) * 100)}%` }));
}

// ---- Views ------------------------------------------------------------------------------------------------------

function itemColumns() {
  const t = t9();
  return [
    { key: "sku", label: t.cSku, get: (r) => r.sku, render: (r) => el("span", {}, r.sku, isChanged(r) ? el("span", { class: "tag-changed" }, t.changed) : null) },
    { key: "name", label: t.cName, get: (r) => r.name, render: (r) => el("div", { class: "name", title: r.name }, r.name, el("span", { class: "sub" }, `${r.kind === "part" ? t.part : t.equipment} \u00B7 ${r.warehouse.replace(" (simulated)", "").replace("Depot ", "")}`)) },
    { key: "onHand", label: t.cOnHand, num: true, get: (r) => r.onHand },
    { key: "reserved", label: t.cReserved, num: true, get: (r) => r.reserved },
    { key: "available", label: t.cAvailable, get: (r) => r.available, render: (r) => el("span", {}, levelBar(r), String(r.available)) },
    { key: "level", label: t.cLevel, get: (r) => r.level, render: levelChip },
  ];
}

function orderRows() {
  return latest.items.flatMap((it) => it.orders.map((o) => ({ ...o, sku: it.sku, name: it.name })));
}
function reservationRows() {
  return latest.items.flatMap((it) => it.reservations.map((r) => ({ ...r, sku: it.sku, name: it.name, warehouse: it.warehouse })));
}

function orderColumns() {
  const t = t9();
  return [
    { key: "id", label: t.cOrder, get: (r) => r.id, render: (r) => r.id.replace(/^PO-INC-/, "PO-") },
    { key: "name", label: t.cName, get: (r) => r.name, render: (r) => el("div", { class: "name" }, r.name) },
    { key: "incidentId", label: t.cIncident, get: (r) => r.incidentId },
    { key: "quantity", label: t.cQty, num: true, get: (r) => r.quantity },
    { key: "express", label: t.cType, get: (r) => r.express, render: (r) => el("span", { class: `chip ${r.express ? "express" : "standard"}` }, r.express ? t.express : t.standard) },
    { key: "status", label: t.cStatus, get: (r) => r.status, render: (r) => el("span", { class: `chip ${r.status}` }, t[r.status] || r.status) },
    { key: "totalEur", label: t.cTotal, num: true, get: (r) => r.totalEur, render: (r) => money(r.totalEur) },
    { key: "expectedDeliveryUtc", label: t.cDelivery, get: (r) => r.expectedDeliveryUtc, render: (r) => when(r.expectedDeliveryUtc) },
  ];
}

function reservationColumns() {
  const t = t9();
  return [
    { key: "sku", label: t.cSku, get: (r) => r.sku },
    { key: "name", label: t.cName, get: (r) => r.name, render: (r) => el("div", { class: "name" }, r.name) },
    { key: "incidentId", label: t.cIncident, get: (r) => r.incidentId },
    { key: "quantity", label: t.cQty, num: true, get: (r) => r.quantity },
    { key: "warehouse", label: t.cWarehouse, get: (r) => r.warehouse, render: (r) => r.warehouse.replace(" (simulated)", "") },
    { key: "reservedAtUtc", label: t.cAt, get: (r) => r.reservedAtUtc, render: (r) => when(r.reservedAtUtc) },
  ];
}

const matches = (values) => {
  const q = query.trim().toLowerCase();
  return !q || values.some((v) => v != null && String(v).toLowerCase().includes(q));
};

function currentRows() {
  if (view === "items") {
    return latest.items.filter((it) =>
      (filter === "all" || (filter === "parts" && it.kind === "part") || (filter === "equipment" && it.kind === "equipment")
        || (filter === "low" && it.level !== "ok") || (filter === "changed" && isChanged(it)))
      && matches([it.sku, it.name, it.category, it.warehouse, it.supplier, ...it.reservations.map((r) => r.incidentId), ...it.orders.map((o) => o.id)]));
  }
  if (view === "orders") return orderRows().filter((o) => matches([o.id, o.sku, o.name, o.incidentId, o.status, o.supplierName]));
  if (view === "reservations") return reservationRows().filter((r) => matches([r.sku, r.name, r.incidentId, r.warehouse]));
  return latest.changes.filter((c) => matches([c.sku, c.name, c.incidentId, c.kind]));
}

function columnsFor() {
  return view === "items" ? itemColumns() : view === "orders" ? orderColumns() : reservationColumns();
}

function sorted(rows, columns) {
  const col = columns.find((c) => c.key === sort.key) ?? columns[0];
  return [...rows].sort((a, b) => {
    const x = col.get(a), y = col.get(b);
    return (typeof x === "number" || typeof x === "boolean" ? Number(x) - Number(y) : String(x).localeCompare(String(y), loc(), { numeric: true })) * sort.dir;
  });
}

function renderGrid() {
  const t = t9();
  const wrap = $("grid-wrap");
  const top = wrap.scrollTop, left = wrap.scrollLeft;
  const rows = currentRows();

  if (view === "changes") {
    wrap.replaceChildren(rows.length ? renderTimeline(rows) : el("p", { class: "empty" }, t.none));
    return;
  }
  if (!rows.length) { wrap.replaceChildren(el("p", { class: "empty" }, t.none)); return; }

  const columns = columnsFor();
  const head = el("tr", {}, ...columns.map((c) => {
    const th = el("th", { class: c.num ? "num" : "", scope: "col", tabindex: "0", "aria-sort": sort.key === c.key ? (sort.dir === 1 ? "ascending" : "descending") : "none" },
      c.label, sort.key === c.key ? el("span", { class: "arrow" }, sort.dir === 1 ? "\u2191" : "\u2193") : null);
    const toggle = () => { sort = { key: c.key, dir: sort.key === c.key ? -sort.dir : 1 }; renderGrid(); };
    th.addEventListener("click", toggle);
    th.addEventListener("keydown", (e) => { if (e.key === "Enter") toggle(); });
    return th;
  }));

  const body = sorted(rows, columns).map((r) => {
    const sku = r.sku;
    const classes = ["row", sku === selectedSku ? "selected" : "", view === "items" && isChanged(r) ? "changed" : "", view === "items" && flash.has(sku) ? "flash" : ""].filter(Boolean).join(" ");
    const tr = el("tr", { class: classes, tabindex: "0", "data-sku": sku }, ...columns.map((c, i) =>
      el("td", { class: `${c.num ? "num" : ""}${i === 0 ? " link" : ""}` }, c.render ? c.render(r) : String(c.get(r)))));
    tr.addEventListener("click", () => select(sku));
    tr.addEventListener("keydown", (e) => {
      if (e.key === "Enter" || e.key === " ") { e.preventDefault(); select(sku); }
      if (e.key === "ArrowDown" || e.key === "ArrowUp") {
        e.preventDefault();
        const sibling = e.key === "ArrowDown" ? tr.nextElementSibling : tr.previousElementSibling;
        if (sibling?.dataset.sku) { select(sibling.dataset.sku); $("grid-wrap").querySelector(`[data-sku="${sibling.dataset.sku}"]`)?.focus(); }
      }
    });
    return tr;
  });

  wrap.replaceChildren(el("table", { class: "grid" }, el("thead", {}, head), el("tbody", {}, ...body)));
  wrap.scrollTop = top; wrap.scrollLeft = left;
}

function renderTimeline(changes) {
  const t = t9();
  return el("ul", { class: "timeline" }, ...changes.map((c) => {
    const isOrder = c.kind === "order";
    const [orderId, status, speed, total] = isOrder ? c.detail.split("|") : [];
    const title = isOrder
      ? `${t.chOrder} ${orderId}: ${c.quantity} \u00D7 ${c.name}`
      : `${t.chReservation}: ${c.quantity} \u00D7 ${c.name}`;
    const meta = isOrder
      ? `${t[status] || status} \u00B7 ${speed === "express" ? t.express : t.standard} \u00B7 ${money(Number(total))} \u00B7 ${t.orderedFor} ${c.incidentId}`
      : `${c.sku} \u00B7 ${c.detail} \u00B7 ${t.reservedFor} ${c.incidentId}`;
    const li = el("li", { class: "tl-item", tabindex: "0" },
      el("div", { class: `tl-dot ${isOrder ? "order" : ""}` }, icon(isOrder ? ICON.doc : ICON.lock)),
      el("div", {}, el("div", { class: "tl-title" }, title), el("div", { class: "tl-meta" }, meta), el("div", { class: "tl-meta" }, when(c.atUtc))));
    li.addEventListener("click", () => select(c.sku));
    return li;
  }));
}

// ---- Record form ------------------------------------------------------------------------------------------------

function field(label, value, wide = false) {
  return el("div", { class: `field${wide ? " wide" : ""}` }, el("label", {}, label), el("div", {}, value));
}

function renderForm() {
  const t = t9();
  const it = latest.items.find((x) => x.sku === selectedSku);
  const pane = $("form");
  if (!it) { pane.replaceChildren(el("p", { class: "empty" }, t.selectRow)); return; }

  const rest = Math.max(0, it.onHand - it.reserved - it.available);
  const total = Math.max(it.onHand, 1);
  const gauge = el("div", {},
    el("div", { class: "gauge", role: "img", "aria-label": `${it.reserved} ${t.gReserved}, ${it.available} ${t.gAvailable}, ${it.onHand} ${t.gOnHand}` },
      el("div", { class: "res", style: `width:${(it.reserved / total) * 100}%` }),
      el("div", { class: `avail ${it.level === "ok" ? "" : "low"}`, style: `width:${(it.available / total) * 100}%` })),
    el("div", { class: "gauge-legend" },
      el("span", {}, el("i", { class: "dot res" }), `${it.reserved} ${t.gReserved}`),
      el("span", {}, el("i", { class: "dot avail" }), `${it.available} ${t.gAvailable}`),
      el("span", {}, el("i", { class: "dot rest" }), `${it.onHand} ${t.gOnHand}`)));

  const tabs = el("div", { class: "tabs", role: "tablist" }, ...[
    ["general", t.tabGeneral, null], ["reservations", t.tabReservations, it.reservations.length], ["orders", t.tabOrders, it.orders.length],
  ].map(([key, label, n]) => {
    const b = el("button", { type: "button", role: "tab", "aria-selected": String(tab === key), class: tab === key ? "active" : "" }, label, n ? el("span", { class: "n" }, String(n)) : null);
    b.addEventListener("click", () => { tab = key; renderForm(); });
    return b;
  }));

  let content;
  if (tab === "reservations") {
    content = el("div", { class: "section" }, it.reservations.length
      ? el("div", {}, ...it.reservations.map((r) => el("div", { class: "record-card" }, el("strong", {}, `${r.quantity} \u00D7 ${it.name}`), el("span", {}, `${t.reservedFor} ${r.incidentId} \u00B7 ${when(r.reservedAtUtc)}`))))
      : el("p", { class: "no-rec" }, t.noReservations));
  } else if (tab === "orders") {
    content = el("div", { class: "section" }, it.orders.length
      ? el("div", {}, ...it.orders.map((o) => el("div", { class: "record-card" },
        el("strong", {}, `${o.id}: ${o.quantity} \u00D7 ${it.name}`),
        el("span", {}, `${t[o.status] || o.status} \u00B7 ${o.express ? t.expressNote : t.standard} \u00B7 ${money(o.totalEur)} \u00B7 ${o.supplierName.replace(" (simulated)", "")}`),
        el("span", { class: "sub" }, `${t.cDelivery}: ${when(o.expectedDeliveryUtc)}`),
        el("span", { class: "sub" }, o.reason))))
      : el("p", { class: "no-rec" }, t.noOrders));
  } else {
    content = el("div", {},
      el("div", { class: "section" }, el("h3", {}, t.secStock), gauge, el("span", { class: "sub" }, " "),
        it.availableFromUtc && it.available === 0 && Date.parse(it.availableFromUtc) > Date.parse(latest.atUtc)
          ? el("p", { class: "sub" }, `${t.backAt} ${when(it.availableFromUtc)}`) : null),
      el("div", { class: "section" }, el("h3", {}, t.secDetails), el("div", { class: "fields" },
        field(t.fKind, it.kind === "part" ? t.part : t.equipment), field(t.fCategory, it.category.replaceAll("_", " ")),
        field(t.fWarehouse, it.warehouse.replace(" (simulated)", "")), field(t.cLevel, levelChip(it)),
        field(t.fReorder, it.kind === "part" ? String(it.reorderLevel) : "-"), field(t.fReorderQty, it.kind === "part" ? String(it.reorderQuantity) : "-"),
        field(t.fPrice, money(it.unitPriceEur)), field(t.cSupplier, it.supplier.replace(" (simulated)", "")),
        field(t.fNotes, it.notes || "-", true))),
      el("div", { class: "section" }, el("h3", {}, t.secSupplier), el("div", { class: "fields" },
        field(t.fLead, `${it.supplierLeadDays} ${t.days}`), field(t.fExpress, `${it.supplierExpressHours} ${t.hours}`))));
  }

  pane.replaceChildren(
    el("div", { class: "form-head" }, el("div", { class: "form-icon" }, icon(ICON.box)),
      el("div", {}, el("h2", {}, it.name), el("div", { class: "sku" }, `${it.sku} \u00B7 ${it.warehouse.replace(" (simulated)", "")}`))),
    tabs, content);
}

// ---- Page chrome ------------------------------------------------------------------------------------------------

function renderChrome() {
  const t = t9();
  const s = latest.summary;
  const changedItems = latest.items.filter(isChanged).length;
  const titles = { items: t.vItems, orders: t.vOrders, reservations: t.vReservations, changes: t.vChanges };
  $("view-title").textContent = titles[view];
  document.querySelectorAll(".nav-item[data-view]").forEach((b) => b.classList.toggle("active", b.dataset.view === view));
  $("n-orders").textContent = s.openOrders ? String(s.openOrders) : "";
  $("n-reservations").textContent = latest.items.some((i) => i.reservations.length) ? String(reservationRows().length) : "";
  $("n-changes").textContent = latest.changes.length ? String(latest.changes.length) : "";

  const kpi = (label, value, cls = "") => el("li", { class: cls }, el("strong", {}, String(value)), label);
  $("kpis").replaceChildren(kpi(t.kItems, s.items), kpi(t.kLow, s.low, s.low ? "warn" : ""), kpi(t.kOut, s.out, s.out ? "bad" : ""),
    kpi(t.kReserved, s.reservedUnits, s.reservedUnits ? "chg" : ""), kpi(t.kOrders, s.openOrders, s.openOrders ? "chg" : ""),
    kpi(t.kApproval, s.pendingApproval, s.pendingApproval ? "warn" : ""), kpi(t.kChanged, changedItems, changedItems ? "chg" : ""));

  const filters = $("filters");
  filters.hidden = view !== "items";
  filters.replaceChildren(...[["all", t.fAll], ["parts", t.fParts], ["equipment", t.fEquipment], ["low", t.fLow], ["changed", t.fChanged]].map(([key, label]) => {
    const b = el("button", { type: "button", class: `chip-btn${filter === key ? " active" : ""}`, "aria-pressed": String(filter === key) }, label);
    b.addEventListener("click", () => { filter = key; render(); });
    return b;
  }));

  const total = view === "items" ? latest.items.length : view === "orders" ? orderRows().length : view === "reservations" ? reservationRows().length : latest.changes.length;
  $("status-count").textContent = t.count(currentRows().length, total);
  $("clock").textContent = `${t.clock}: ${new Date(latest.atUtc).toLocaleString(loc(), { dateStyle: "medium", timeStyle: "short", timeZone })}`;
}

function select(sku) {
  selectedSku = sku;
  tab = "general";
  renderGrid();
  renderForm();
}

function render() {
  if (!latest) return;
  renderChrome(); renderGrid(); renderForm();
}

function applyLanguage(next) {
  lang = next;
  document.documentElement.lang = lang;
  $("lang-nl").classList.toggle("active", lang === "nl");
  $("lang-en").classList.toggle("active", lang === "en");
  document.querySelectorAll("[data-i18n]").forEach((n) => { n.textContent = TEXT[lang][n.dataset.i18n]; });
  document.querySelectorAll("[data-i18n-placeholder]").forEach((n) => { n.placeholder = TEXT[lang][n.dataset.i18nPlaceholder]; });
  document.querySelectorAll("[data-i18n-aria]").forEach((n) => { n.setAttribute("aria-label", TEXT[lang][n.dataset.i18nAria]); });
  render();
}

// What the audience came to see: the rows Procurement just changed. They flash, and the form jumps to the ordered item (or the first changed one).
function adopt(data) {
  const now = new Map(data.items.map((i) => [i.sku, i.reservations.length + i.orders.length]));
  flash = new Set();
  if (snapshot) for (const [sku, n] of now) if (n > (snapshot.get(sku) ?? 0)) flash.add(sku);
  latest = data;
  if (flash.size) {
    selectedSku = [...flash].find((sku) => data.items.find((i) => i.sku === sku).orders.length) ?? [...flash][0];
    tab = "general";
  } else if (!data.items.some((i) => i.sku === selectedSku)) {
    selectedSku = data.items.find((i) => i.orders.length)?.sku ?? data.items.find(isChanged)?.sku ?? data.items[0]?.sku ?? null;
  }
  snapshot = now;
}

async function load() {
  try {
    const response = await fetch("/api/warehouse", { cache: "no-store" });
    if (!response.ok) throw new Error(String(response.status));
    adopt(await response.json());
    $("error").hidden = true;
    render();
  } catch {
    $("error").textContent = t9().loadError;
    $("error").hidden = false;
  }
}

document.querySelectorAll(".nav-item[data-view]").forEach((b) => b.addEventListener("click", () => {
  view = b.dataset.view;
  sort = { key: view === "orders" ? "id" : view === "reservations" ? "reservedAtUtc" : "sku", dir: view === "reservations" ? -1 : 1 };
  if (latest) {
    const rows = currentRows();
    if (rows.length && !rows.some((r) => r.sku === selectedSku)) selectedSku = rows[0].sku;
    render();
  }
}));
$("lang-nl").addEventListener("click", () => applyLanguage("nl"));
$("lang-en").addEventListener("click", () => applyLanguage("en"));
$("search").addEventListener("input", (e) => { query = e.target.value; if (latest) { renderGrid(); renderChrome(); } });
$("refresh").addEventListener("click", load);
applyLanguage("nl");
load();
setInterval(load, 5000);
