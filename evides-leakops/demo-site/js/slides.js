/* Slide content and the builders that turn the captured run into HTML.
   Everything shown as "answer", "tools" or "trace" comes from window.CAPTURED (a real run of the deployed demo).
   Text is { en, nl }; plain strings are language-neutral. */
(function () {
  'use strict';
  const C = window.CAPTURED;

  /* ---------- helpers ---------- */
  const esc = s => String(s).replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
  const T = (o, lang) => (o && typeof o === 'object' ? (o[lang] ?? o.en) : o);
  const sec = ms => (ms / 1000).toFixed(1);
  const nf = n => new Intl.NumberFormat('en-US').format(n);
  const hm = iso => new Intl.DateTimeFormat('nl-NL', { hour: '2-digit', minute: '2-digit', timeZone: 'Europe/Amsterdam' }).format(new Date(iso));
  const eur = n => '\u20AC' + nf(n);
  const scen = key => C.scenarios[key];
  const stepOf = (key, name) => scen(key).steps.find(s => s.step === name);
  const burst = name => stepOf('burst', name);
  const isRule = n => n.startsWith('(rules)');
  const KB_TOOL = 'knowledge_base_retrieve';
  const isKb = n => n === KB_TOOL;
  // "tool calls" are the data tools the model called; knowledge lookups and code re-checks are counted separately.
  const countCalls = (s, rule) => s.toolCalls.filter(c => isRule(c.name) === rule && !isKb(c.name)).length;
  const countKb = s => s.toolCalls.filter(c => isKb(c.name)).length;

  const ZONES = {
    operator: [14, 258, 92, 126], entra: [14, 420, 92, 72],
    host: [150, 140, 268, 390], hostRun: [166, 306, 236, 70], hostNL: [166, 388, 236, 70],
    agents: [488, 140, 268, 390], foundry: [826, 140, 268, 390], mcp: [1164, 140, 268, 390],
    appins: [150, 590, 268, 76], storage: [488, 590, 268, 76], conn: [826, 590, 268, 76], cosmos: [1164, 590, 268, 76],
    skills: [150, 706, 268, 76], docs: [488, 706, 268, 76], search: [826, 706, 268, 76], kb: [1164, 706, 268, 76],
    strip: [150, 802, 1280, 34]
  };
  const ARCH_W = 1480, ARCH_H = 920;
  const zoneStyle = id => {
    const [x, y, w, h] = ZONES[id];
    return `left:${x / (ARCH_W / 100)}%;top:${y / (ARCH_H / 100)}%;width:${w / (ARCH_W / 100)}%;height:${h / (ARCH_H / 100)}%;` +
      `background-image:url(img/architecture.svg);background-size:${ARCH_W / w * 100}% auto;` +
      `background-position:${x / (ARCH_W - w) * 100}% ${y / (ARCH_H - h) * 100}%`;
  };
  // An agent that has a knowledge base lights up the connection, the search service, the knowledge bases and the documents; one with skills lights the skills.
  const withKnowledge = (key, zones) => {
    const a = window.TECH.agents[key];
    return [...zones, ...(a.knowledge ? ['conn', 'search', 'kb', 'docs'] : []), ...(a.skills.length ? ['skills'] : [])];
  };
  function arch(zones, base = 0, extraClass = '') {
    return `<div class="arch">` +
      `<img src="img/architecture.svg" alt="Architecture of the Evides LeakOps demo">` +
      zones.map((z, i) => `<div class="zone static ${extraClass}" data-z="${z}" style="${zoneStyle(z)};--d:${base + i * 180}"></div>`).join('') +
      `</div>`;
  }

  function jl(v, ind, key, comma) {
    const pad = '  '.repeat(ind);
    const k = key !== null ? `<span class="k">"${esc(key)}"</span><span class="p">: </span>` : '';
    const c = comma ? '<span class="p">,</span>' : '';
    const prim = x => typeof x === 'string' ? `<span class="s">"${esc(x)}"</span>` : typeof x === 'number' ? `<span class="n">${x}</span>` : `<span class="b">${x}</span>`;
    if (Array.isArray(v)) {
      if (!v.length) return [pad + k + '[]' + c];
      if (v.every(x => x === null || typeof x !== 'object') && JSON.stringify(v).length < 80)
        return [pad + k + '[' + v.map(prim).join('<span class="p">, </span>') + ']' + c];
      const out = [pad + k + '['];
      v.forEach((x, i) => out.push(...jl(x, ind + 1, null, i < v.length - 1)));
      out.push(pad + ']' + c);
      return out;
    }
    if (v && typeof v === 'object') {
      const e = Object.entries(v);
      const out = [pad + k + '{'];
      e.forEach(([kk, vv], i) => out.push(...jl(vv, ind + 1, kk, i < e.length - 1)));
      out.push(pad + '}' + c);
      return out;
    }
    return [pad + k + prim(v) + c];
  }
  const jsonBlock = (obj, base = 0) =>
    `<div class="code">${jl(obj, 0, null, false).map((l, i) => `<div class="ln" style="--d:${base + i * 55}">${l}</div>`).join('')}</div>`;

  function toolChips(s, base = 0) {
    const counts = new Map();
    s.toolCalls.forEach(c => counts.set(c.name, (counts.get(c.name) || 0) + 1));
    const entries = [...counts.entries()].sort((a, b) => (isRule(a[0]) - isRule(b[0])) || (isKb(a[0]) - isKb(b[0])));
    return entries.map(([n, c], i) => {
      const rule = isRule(n), kb = isKb(n);
      const label = rule ? 'rules \u00B7 ' + n.replace('(rules) ', '') : kb ? 'knowledge \u00B7 Foundry IQ' : n;
      return `<span class="tool ${rule ? 'rule' : kb ? 'kb' : ''}" style="--d:${base + i * 70}">${esc(label)}${c > 1 ? `<span class="n">\u00D7${c}</span>` : ''}</span>`;
    }).join('');
  }
  const noTools = (lang) => `<span class="tool none" style="--d:0">${T({ en: 'no MCP tools: least privilege', nl: 'geen MCP-tools: minste rechten' }, lang)}</span>`;
  const noDataTools = (lang) => `<span class="tool none" style="--d:0">${T({ en: 'no data tools: it can only answer', nl: 'geen datatools: hij kan alleen antwoorden' }, lang)}</span>`;
  const evidence = (list, n = 4) => `<div class="ev">` + list.slice(0, n).map(e =>
    `<div class="${e.supports === 'no_leak' ? 'no' : e.supports === 'neutral' ? 'warn' : ''}"><b>${esc(e.source)}</b>${esc(e.finding)}</div>`).join('') + `</div>`;
  const rulesList = (arr, lang) => `<ul class="rules">` + arr.map(r => `<li>${T(r, lang)}</li>`).join('') + `</ul>`;
  const meta = (s, lang) => {
    const tools = countCalls(s, false), rules = countCalls(s, true), kb = countKb(s);
    return `<div class="meta"><span><b>${esc(s.agent)}</b></span><span>${esc(s.model)}</span><span><b>${sec(s.durationMs)} s</b></span>` +
      `<span>${nf(s.inputTokens)} + ${nf(s.outputTokens)} tokens</span>` +
      `<span>${tools} ${T({ en: 'tool calls', nl: 'tool-aanroepen' }, lang)}</span>` +
      (kb ? `<span>${kb} ${T({ en: 'knowledge lookup', nl: 'kennisopvraging' }, lang)}</span>` : '') +
      (rules ? `<span>${rules} ${T({ en: 'code re-checks', nl: 'code-controles' }, lang)}</span>` : '') + `</div>`;
  };
  const L = {
    tools: { en: 'Tools the agent used (MCP)', nl: 'Tools die de agent gebruikte (MCP)' },
    rules: { en: 'Guardrails in code', nl: 'Vangrails in code' },
    answer: { en: 'The agent\u2019s real answer (excerpt)', nl: 'Het echte antwoord van de agent (fragment)' },
    where: { en: 'Where it runs', nl: 'Waar het draait' },
    value: { en: 'AI value', nl: 'AI-waarde' }
  };

  function frame(ctx, o) {
    const lang = ctx.lang;
    const val = typeof o.value === 'function' ? o.value(ctx) : o.value;
    return `
      <div class="sol-head rv" style="--d:0"><span class="tag sol">${T({ en: 'The solution', nl: 'De oplossing' }, lang)}</span>${o.tag ? `<span class="tag agent">${o.tag}</span>` : ''}</div>
      <h3 class="rv" style="--d:60">${T(o.title, lang)}</h3>
      ${o.meta ? `<div class="rv" style="--d:120">${o.meta}</div>` : ''}
      <div class="grid2" style="margin-top:6px">
        <div>
          <div class="sec-label rv" style="--d:200">${T(L.tools, lang)}</div>
          <div class="chips">${o.chips}</div>
          <div class="sec-label rv" style="--d:500">${T(L.rules, lang)}</div>
          <div class="rv" style="--d:560">${rulesList(o.rules, lang)}</div>
        </div>
        <div class="rv" style="--d:300">
          <div class="sec-label">${T(L.where, lang)}</div>
          ${arch(o.zones, 400)}
        </div>
      </div>
      <div class="grid2" style="margin-top:6px">
        <div>
          <div class="sec-label rv" style="--d:700">${T(o.answerTitle || L.answer, lang)}</div>
          ${jsonBlock(o.json, 760)}
        </div>
        <div class="rv" style="--d:900">${o.side || ''}</div>
      </div>
      ${o.banner || ''}
      <div class="value rv" style="--d:1500"><div class="star">\u2605</div><div><b>${T(L.value, lang)}</b><span>${T(val, lang)}</span></div></div>`;
  }

  const phone = (ctx, which) => {
    const r = burst('communications').result, p = r.published, lang = ctx.lang, tl = p.timeline || [];
    const en = lang === 'en';
    const first = tl[0] || {}, last = tl[tl.length - 1] || {};
    const title = en ? p.titleEn : p.title;
    const msg = x => en ? x.messageEn : x.message;
    return `<div class="phone"><div class="site"><svg width="18" height="24"><use href="#drop"/></svg> WATERSTORING.nl <i>DEMO</i></div>
      <div class="panel"><div class="bubble alert"><b>${esc(title)}</b><br>${esc(msg(which === 'update' ? last : first))}</div>
      ${which === 'update' && last.expectedResolutionUtc ? `<div class="bubble ok"><b>${en ? 'Expected restored' : 'Verwacht hersteld'}:</b> ${hm(last.expectedResolutionUtc)} \u00B7 ${nf(p.customersAffected)} ${en ? 'households' : 'huishoudens'}</div>` : `<div class="bubble"><b>${en ? 'Affected' : 'Getroffen'}:</b> ${esc(p.affectedStreets.join(', '))} \u00B7 ${nf(p.customersAffected)} ${en ? 'households' : 'huishoudens'}</div>`}
      </div></div>`;
  };

  /* ---------- "Before AI": how an incident is handled today ---------- */
  // The 16 steps come from the Evides team's description of a typical drinking-water incident (the exact implementation may vary).
  // Systems are the usual utility system types; they are not a confirmed list of what Evides runs.
  const SYS = {
    scada: 'SCADA', hist: 'Historian', alarm: 'Alarm mgmt', eam: 'EAM (SAP PM / Maximo / Ultimo)', gis: 'GIS', wfm: 'Workforce mgmt',
    mob: 'Mobile field app', cis: 'CIS', inc: 'Incident mgmt', pub: 'Publishing portal / API', ws: 'Waterstoring.nl', rep: 'Reporting'
  };
  // phase: 1 detect and verify, 2 analyse and mobilise, 3 inform residents, 4 repair and close. agent: the demo agent that takes this step over.
  const BEFORE_STEPS = [
    { p: 1, t: { en: 'The network fails', nl: 'Het net valt uit' }, who: { en: 'Nobody knows yet', nl: 'Nog niemand weet het' }, sys: [], pp: '0' },
    { p: 1, t: { en: 'SCADA raises an alarm', nl: 'SCADA geeft alarm' }, who: { en: 'Control room operator', nl: 'Operator in de meldkamer' }, sys: ['scada'], pp: '1' },
    { p: 1, t: { en: 'First check: real or sensor fault?', nl: 'Eerste check: echt of sensorfout?' }, who: { en: 'Control room operator', nl: 'Operator in de meldkamer' }, sys: ['scada', 'hist', 'alarm'], pp: '1' },
    { p: 1, t: { en: 'Incident ticket and priority', nl: 'Incidentticket en prioriteit' }, who: { en: 'Operator and duty coordinator', nl: 'Operator en dienstco\u00F6rdinator' }, sys: ['eam', 'inc'], pp: '2' },
    { p: 2, t: { en: 'GIS: which pipe, valves, customers?', nl: 'GIS: welke leiding, afsluiters, klanten?' }, who: { en: 'Network coordinator and duty engineer', nl: 'Netco\u00F6rdinator en dienstingenieur' }, sys: ['gis'], pp: '2-3' },
    { p: 2, t: { en: 'Schedule and brief the crew', nl: 'Ploeg inplannen en briefen' }, who: { en: 'Dispatcher and 2-4 technicians', nl: 'Planner en 2-4 monteurs' }, sys: ['wfm', 'mob'], pp: '4-6' },
    { p: 3, t: { en: 'Assess the impact on customers', nl: 'Impact op klanten beoordelen' }, who: { en: 'Coordinator and communications officer', nl: 'Co\u00F6rdinator en communicatiemedewerker' }, sys: ['inc', 'cis'], pp: '5-7' },
    { p: 3, t: { en: 'Prepare and publish on Waterstoring.nl', nl: 'Opstellen en publiceren op Waterstoring.nl' }, who: { en: 'Communications officer', nl: 'Communicatiemedewerker' }, sys: ['inc', 'pub', 'ws'], pp: '6-8' },
    { p: 4, t: { en: 'Technician arrives and inspects', nl: 'Monteur arriveert en inspecteert' }, who: { en: '2-4 technicians', nl: '2-4 monteurs' }, sys: ['mob', 'gis'], pp: '2-4' },
    { p: 4, t: { en: 'Field status goes back to the office', nl: 'Status uit het veld terug naar kantoor' }, who: { en: 'Technician, dispatcher, control room', nl: 'Monteur, planner, meldkamer' }, sys: ['mob', 'eam', 'gis'], pp: '3+' },
    { p: 4, t: { en: 'Isolate: close valves, protect hospitals and industry', nl: 'Isoleren: afsluiters dicht, ziekenhuizen en industrie ontzien' }, who: { en: 'Network engineer, crew, control room', nl: 'Netwerkingenieur, ploeg, meldkamer' }, sys: ['scada', 'gis'], pp: '6-12' },
    { p: 4, t: { en: 'Repair: dig, replace, test pressure', nl: 'Repareren: graven, vervangen, drukproef' }, who: { en: '2-6 technicians, maybe a contractor', nl: '2-6 monteurs, evt. aannemer' }, sys: ['mob'], pp: '2-6' },
    { p: 4, t: { en: 'Update the public status again', nl: 'Publieke status opnieuw bijwerken' }, who: { en: 'Communications officer', nl: 'Communicatiemedewerker' }, sys: ['inc', 'pub', 'ws'], pp: '1-2' },
    { p: 4, t: { en: 'Restore pressure, flush, check water quality', nl: 'Druk herstellen, spoelen, waterkwaliteit controleren' }, who: { en: 'Operators and laboratory', nl: 'Operators en laboratorium' }, sys: ['scada', 'hist'], pp: '2+' },
    { p: 4, t: { en: 'Close the incident: cause and costs', nl: 'Incident afsluiten: oorzaak en kosten' }, who: { en: 'Coordinator, asset management', nl: 'Co\u00F6rdinator, assetmanagement' }, sys: ['eam', 'gis', 'rep'], pp: '2' },
    { p: 4, t: { en: 'Mark resolved, later remove from the site', nl: 'Op opgelost zetten, later van de site halen' }, who: { en: 'Communications officer', nl: 'Communicatiemedewerker' }, sys: ['pub', 'ws'], pp: '1' }
  ];
  // People involved while the incident unfolds (typical, mid-points of the ranges above), steps 1-8 and the isolation step.
  const RAMP = [[1, 0, '0'], [2, 1, '1'], [3, 1, '1'], [4, 2, '2'], [5, 2.5, '2-3'], [6, 5, '4-6'], [7, 6, '5-7'], [8, 7, '6-8'], [11, 9, '6-12']];

  function beforeSlide(lang) {
    const L2 = o => T(o, lang);
    const systems = new Set(BEFORE_STEPS.flatMap(s => s.sys));
    const phaseName = [null,
      { en: 'Detect and verify', nl: 'Detecteren en verifi\u00EBren' }, { en: 'Analyse and mobilise', nl: 'Analyseren en mobiliseren' },
      { en: 'Inform residents', nl: 'Bewoners informeren' }, { en: 'Repair and close', nl: 'Repareren en afsluiten' }];
    const kpi = (v, l, d) => `<div class="bk rv" style="--d:${d}"><b>${v}</b><span>${L2(l)}</span></div>`;
    const card = (s, i) => `<li class="bs p${s.p} rv" style="--d:${300 + i * 60}">
        <div class="bn">${i + 1}</div>
        <div class="bt"><b>${L2(s.t)}</b><span>${L2(s.who)}</span>
          ${s.sys.length ? `<div class="bsys">${s.sys.map(k => `<i>${esc(SYS[k])}</i>`).join('')}</div>` : ''}
        </div>
        <div class="bp" title="${L2({ en: 'people involved', nl: 'betrokken mensen' })}">\uD83D\uDC65 ${s.pp}</div>
      </li>`;
    const ramp = RAMP.map(([n, v, label], i) => `<div class="rb" style="--d:${500 + i * 70}"><i style="height:${Math.max(4, v * 11)}px"></i><b>${label}</b><span>${n}</span></div>`).join('');
    const why = [
      { en: '<b>Many systems, built apart.</b> Alarm, ticket, map, planning, customer data and website are different systems; people carry the information from one to the next.', nl: '<b>Veel systemen, los gebouwd.</b> Alarm, ticket, kaart, planning, klantdata en website zijn verschillende systemen; mensen dragen de informatie van het ene naar het andere over.' },
      { en: '<b>Many roles, one step each.</b> Six to eight people touch the incident before a resident is told anything. Every hand-over costs minutes and can lose detail.', nl: '<b>Veel rollen, elk \u00E9\u00E9n stap.</b> Zes tot acht mensen raken het incident aan voordat een bewoner iets hoort. Elke overdracht kost minuten en kan details kwijtraken.' },
      { en: '<b>A wide service area.</b> Evides supplies the south-west of Zuid-Holland, the Brabantse Wal and Zeeland. The network, the districts and the crews are spread out, so every alarm needs local knowledge.', nl: '<b>Een groot verzorgingsgebied.</b> Evides voorziet het zuidwesten van Zuid-Holland, de Brabantse Wal en Zeeland. Net, gebieden en ploegen liggen verspreid, dus elk alarm vraagt lokale kennis.' },
      { en: '<b>Publishing is a manual, checked step.</b> Waterstoring.nl shows information \u201Cdirectly from the source\u201D, supplied and verified by the water company. If the site has nothing yet, residents are told to phone 0900 0787, so the phone line and the website are two channels to keep in step. Boil-water advice still goes out as orange or red cards in the letterbox.', nl: '<b>Publiceren is een handmatige, gecontroleerde stap.</b> Waterstoring.nl toont informatie \u201Crechtstreeks vanuit de bron\u201D, aangeleverd en geverifieerd door het waterbedrijf. Staat er nog niets, dan moeten bewoners bellen naar 0900 0787: telefoon en website zijn twee kanalen die gelijk moeten lopen. Een kookadvies gaat nog als oranje of rode kaart in de brievenbus.' },
      { en: '<b>High stakes, little time.</b> At night, in frost, valves must be closed without cutting off hospitals or industry, and every check has to be done by someone who knows the network.', nl: '<b>Hoge inzet, weinig tijd.</b> \u2019s Nachts, bij vorst, moeten afsluiters dicht zonder ziekenhuizen of industrie af te sluiten, en elke controle moet door iemand die het net kent.' }
    ];
    const pains = [
      { en: 'Time to the first public notice depends on a chain of people being free and awake.', nl: 'De tijd tot het eerste publieke bericht hangt af van een keten van mensen die beschikbaar en wakker zijn.' },
      { en: 'The same facts (street, pipe, customers, ETA) are typed again in several systems.', nl: 'Dezelfde feiten (straat, leiding, klanten, verwachte tijd) worden in meerdere systemen opnieuw ingetypt.' },
      { en: 'A medium outage ties up 7-12 people; a trunk main 15-30+, often at night.', nl: 'Een middelgrote storing houdt 7-12 mensen bezig; een hoofdleiding 15-30+, vaak \u2019s nachts.' },
      { en: 'On waterstoring.nl a large disruption can run for many hours (one example there: 21 hours, boil-water advice active).', nl: 'Op waterstoring.nl kan een grote storing vele uren duren (daar bijvoorbeeld: 21 uur, kookadvies actief).' }
    ];
    const total = Math.round(scen('burst').summary.totalMs / 1000);
    const agentChips = ['Triage', 'Diagnostic', 'Communications', 'Resource Matchmaker', 'Procurement', 'Scheduling'].map(a => `<span class="tag agent">${a}</span>`).join('');
    const link = (href, text) => `<a href="${href}" target="_blank" rel="noopener noreferrer">${text}</a>`;
    return `<div class="before">
      <div class="bhead rv" style="--d:0"><span class="tag bad">${L2({ en: 'Before AI \u00B7 today', nl: 'V\u00F3\u00F3r AI \u00B7 vandaag' })}</span></div>
      <h2 class="rv" style="--d:60">${L2({ en: 'One burst pipe sets off a chain of 16 steps', nl: 'E\u00E9n leidingbreuk zet een keten van 16 stappen in gang' })}</h2>
      <p class="bl rv" style="--d:120">${L2({
        en: 'This is how a drinking-water incident typically runs from the first pressure drop until Waterstoring.nl says \u201Cresolved\u201D. People do the thinking, the typing and the hand-overs.',
        nl: 'Zo loopt een drinkwaterstoring doorgaans, van de eerste drukval tot Waterstoring.nl \u201Copgelost\u201D meldt. Mensen doen het denkwerk, het typewerk en de overdrachten.' })}</p>
      <div class="bkpis">
        ${kpi('16', { en: 'steps from pipe break to closed ticket', nl: 'stappen van leidingbreuk tot gesloten ticket' }, 160)}
        ${kpi('6-8', { en: 'people involved before residents are told', nl: 'mensen betrokken voordat bewoners het horen' }, 220)}
        ${kpi('7-12', { en: 'people for a medium outage (15-30+ for a trunk main)', nl: 'mensen bij een middelgrote storing (15-30+ bij een hoofdleiding)' }, 280)}
        ${kpi(String(systems.size), { en: 'system types in the chain', nl: 'systeemtypen in de keten' }, 340)}
        ${kpi('2-24+ h', { en: 'repair: 2-4 h small leak, 6-24+ h trunk main', nl: 'reparatie: 2-4 u klein lek, 6-24+ u hoofdleiding' }, 400)}
      </div>
      <div class="bgrid">
        <div>
          <ol class="bsteps">${BEFORE_STEPS.map(card).join('')}</ol>
          <div class="blegend rv" style="--d:1300">
            ${[1, 2, 3, 4].map(p => `<span><i class="p${p}"></i>${L2(phaseName[p])}</span>`).join('')}
          </div>
        </div>
        <aside>
          <div class="sec-label rv" style="--d:500">${L2({ en: 'People involved as the incident unfolds (typical)', nl: 'Betrokken mensen terwijl het incident zich ontvouwt (typisch)' })}</div>
          <div class="ramp rv" style="--d:540">${ramp}</div>
          <div class="ramp-x rv" style="--d:560">${L2({ en: 'step number in the chain', nl: 'stapnummer in de keten' })}</div>
          <div class="sec-label rv" style="--d:700">${L2({ en: 'Why it is so complex', nl: 'Waarom het zo complex is' })}</div>
          <ul class="why">${why.map((w, i) => `<li class="rv" style="--d:${760 + i * 90}">${L2(w)}</li>`).join('')}</ul>
        </aside>
      </div>
      <div class="sec-label rv" style="--d:1000">${L2({ en: 'Where it hurts', nl: 'Waar het pijn doet' })}</div>
      <ul class="pain">${pains.map((p, i) => `<li class="rv" style="--d:${1040 + i * 80}">${L2(p)}</li>`).join('')}</ul>
      <div class="value rv" style="--d:1400"><div class="star">\u2605</div><div><b>${L2({ en: 'The question for this demo', nl: 'De vraag voor deze demo' })}</b>
        <span>${L2({
          en: `What if six AI agents took the checking, the map work, the matching, the notice, the parts and the planning, in about ${total} seconds in the real run, and people only approved the decisions that matter?`,
          nl: `Wat als zes AI-agents het controleren, het kaartwerk, het koppelen, het bericht, de onderdelen en de planning overnemen, in ongeveer ${total} seconden in de echte run, en mensen alleen de beslissingen goedkeuren die ertoe doen?` })}</span>
        <div class="bagents">${agentChips}</div></div></div>
      <p class="bsrc rv" style="--d:1500">${L2({
        en: 'The 16 steps, roles and system types are a typical drinking-water incident process described by the Evides team; the exact implementation at Evides may vary, and the system names are typical for utilities, not a confirmed list. Public facts: ',
        nl: 'De 16 stappen, rollen en systeemtypen zijn een typisch drinkwaterstoringsproces zoals beschreven door het Evides-team; de exacte invulling bij Evides kan afwijken en de systeemnamen zijn gebruikelijk voor nutsbedrijven, geen bevestigde lijst. Openbare feiten: ' })}
        ${link('https://www.evides.nl/storing-onderhoud', 'evides.nl/storing-onderhoud')} \u00B7 ${link('https://waterstoring.nl/over-ons', 'waterstoring.nl/over-ons')} \u00B7 ${link('https://waterstoring.nl/zoek-uw-waterbedrijf', 'waterstoring.nl/zoek-uw-waterbedrijf')} \u00B7 ${link('https://nl.wikipedia.org/wiki/Evides', 'Wikipedia: Evides')}</p>
    </div>`;
  }

  /* ---------- slides ---------- */
  const SLIDES = [
    { id: 'intro', type: 'intro', short: { en: 'Start', nl: 'Start' } },

    {
      id: 'before', type: 'before', short: { en: 'Before AI', nl: 'V\u00F3\u00F3r AI' },
      render: ctx => beforeSlide(ctx.lang)
    },

    {
      id: 'alarm', type: 'step', no: 1, short: { en: 'Alarm', nl: 'Alarm' }, art: 'alarm',
      label: { en: 'Trigger', nl: 'Aanleiding' },
      title: { en: 'A pressure alarm at 04:10. What now?', nl: 'Een drukalarm om 04:10. En nu?' },
      text: {
        en: 'Pressure station PS-101 in Spijkenisse drops below its limit in the middle of the night, in frost. The duty manager gets a notification and has to decide quickly: is this real?',
        nl: 'Drukstation PS-101 in Spijkenisse zakt midden in de nacht, bij vorst, onder de ondergrens. De dienstdoende manager krijgt een melding en moet snel beslissen: is dit echt?'
      },
      today: [
        { en: 'Open SCADA and compare the sensors', nl: 'SCADA openen en sensoren vergelijken' },
        { en: 'Check the planned-works calendar', nl: 'Planning van werkzaamheden raadplegen' },
        { en: 'Look at weather and normal usage', nl: 'Weer en normaal verbruik bekijken' },
        { en: 'Phone colleagues one by one', nl: 'Collega\u2019s een voor een bellen' }
      ],
      facts: ctx => { const a = scen('burst').alarm; return [`<b>${a.sensorId}</b> \u00B7 ${a.districtId}`, `<b>${a.observedValue} ${a.unit}</b> (limit ${a.thresholdLow.toFixed(2)})`, hm(a.triggeredAtUtc) + ' local']; },
      zones: ['operator', 'entra', 'host'],
      solution: ctx => {
        const a = scen('burst').alarm;
        return frame(ctx, {
          tag: 'Workflow host', title: { en: 'One alarm in, one automated workflow out', nl: 'Eén alarm erin, één geautomatiseerde workflow eruit' },
          chips: ['ASP.NET (.NET 10)', 'App Service', 'Server-sent events', 'X-Operator-Key'].map((c, i) => `<span class="tool" style="--d:${250 + i * 70}">${c}</span>`).join(''),
          rules: [
            { en: 'The workflow API only starts with the operator key (Entra sign-in and RBAC around it).', nl: 'De workflow-API start alleen met de operator-sleutel (Entra-aanmelding en RBAC eromheen).' },
            { en: 'Every run is idempotent: the same alarm twice books or orders nothing twice.', nl: 'Elke run is idempotent: hetzelfde alarm twee keer boekt of bestelt niets dubbel.' }
          ],
          zones: ['operator', 'entra', 'host'], answerTitle: { en: 'The alarm payload (real, from the demo)', nl: 'De alarm-payload (echt, uit de demo)' },
          json: { alarmId: a.alarmId, sensorId: a.sensorId, districtId: a.districtId, triggeredAtUtc: a.triggeredAtUtc, metric: a.metric, observedValue: a.observedValue, unit: a.unit, thresholdLow: a.thresholdLow, message: a.message },
          side: `<div class="sec-label">${T({ en: 'What the workflow runner does', nl: 'Wat de workflow-runner doet' }, ctx.lang)}</div>
            <ul class="tl"><li>${T({ en: 'Takes the alarm and starts the six agents <b>in order</b>', nl: 'Neemt het alarm en start de zes agents <b>in volgorde</b>' }, ctx.lang)}</li>
            <li>${T({ en: 'Streams live progress of every step to the operator console', nl: 'Streamt live de voortgang van elke stap naar de operatorconsole' }, ctx.lang)}</li>
            <li class="last">${T({ en: 'A false alarm <b>stops the flow</b> after the first agent', nl: 'Een vals alarm <b>stopt de flow</b> na de eerste agent' }, ctx.lang)}</li></ul>`,
          value: { en: 'The alarm is no longer an e-mail someone has to read: it starts the whole response by itself, and a person only gets involved where it matters.', nl: 'Het alarm is geen e-mail meer die iemand moet lezen: het start zelf de hele afhandeling en een mens komt alleen in beeld waar het ertoe doet.' }
        });
      }
    },

    {
      id: 'triage', type: 'step', no: 2, short: { en: 'Triage', nl: 'Triage' }, art: 'triage', toggle: true,
      artFix: [['genuine_leak', 'real leak?'], ['0.95 confident', 'confidence?']],
      label: { en: 'Agent 1 \u00B7 Triage', nl: 'Agent 1 \u00B7 Triage' },
      title: { en: 'Is this a real leak, or a false alarm?', nl: 'Is dit een echte lekkage, of vals alarm?' },
      text: {
        en: 'Sending a crew out for a false alarm is expensive. Ignoring a real burst is worse. The call depends on sensors, planned works, weather and history together.',
        nl: 'Een ploeg uitsturen voor vals alarm is duur. Een echte leidingbreuk negeren is erger. De beslissing hangt af van sensoren, geplande werken, weer en historie samen.'
      },
      today: [
        { en: 'Compare pressure at neighbouring sensors', nl: 'Druk bij nabijgelegen sensoren vergelijken' },
        { en: 'Is the inlet flow abnormal for this hour?', nl: 'Is de instroom afwijkend voor dit uur?' },
        { en: 'Is there planned work in the district?', nl: 'Zijn er geplande werken in de wijk?' },
        { en: 'Is it freezing?', nl: 'Vriest het?' }
      ],
      facts: ctx => { const a = scen(ctx.scen).alarm; return [`<b>${a.sensorId}</b> \u00B7 ${a.districtId}`, `<b>${a.observedValue} ${a.unit}</b> (limit ${a.thresholdLow.toFixed(2)})`]; },
      zones: ['host', 'agents', 'foundry', 'mcp', 'cosmos'],
      solution: ctx => {
        const key = ctx.scen, s = stepOf(key, 'triage'), r = s.result, fa = key === 'falseAlarm';
        const banner = fa
          ? `<div class="banner stop rv" style="--d:1300"><b>\u23F8 ${T({ en: 'The flow stops here', nl: 'De flow stopt hier' }, ctx.lang)}</b>${T({ en: `Planned work documented in the register explains the dip. Closed after ${sec(scen(key).summary.totalMs)} s: nobody is woken up and no residents are notified.`, nl: `Gedocumenteerd gepland werk verklaart de drukval. Gesloten na ${sec(scen(key).summary.totalMs)} s: niemand wordt gewekt en geen bewoners geïnformeerd.` }, ctx.lang)}</div>`
          : `<div class="banner go rv" style="--d:1300"><b>\u2713 ${T({ en: 'Genuine leak: the flow continues with Diagnostic', nl: 'Echte lekkage: de flow gaat verder met Diagnostic' }, ctx.lang)}</b></div>`;
        return frame(ctx, {
          tag: 'TriageAgent', title: { en: 'A narrow agent with read-only tools', nl: 'Een smalle agent met alleen-lezen tools' },
          meta: meta(s, ctx.lang), chips: toolChips(s, 250),
          rules: [
            { en: 'Code accepts <b>false_alarm</b> only when the planned-works register documents an active work.', nl: 'Code accepteert <b>false_alarm</b> alleen als het register van geplande werken een actieve werkzaamheid vastlegt.' },
            { en: 'Text inside an alarm is data, never an instruction. Prompt shields block injection attempts.', nl: 'Tekst in een alarm is data, nooit een instructie. Prompt shields blokkeren injectiepogingen.' },
            { en: 'Unknown sensor or low confidence: a person looks (<i>uncertain</i>).', nl: 'Onbekende sensor of lage zekerheid: een mens kijkt mee (<i>uncertain</i>).' },
            { en: 'The playbook and past incidents inform the verdict as neutral evidence. They can never close an alarm: that still needs the planned-works register.', nl: 'Het draaiboek en eerdere incidenten informeren het oordeel als neutraal bewijs. Ze kunnen een alarm nooit sluiten: daarvoor is nog steeds het register van geplande werken nodig.' }
          ],
          zones: withKnowledge('triage', ['host', 'agents', 'foundry', 'mcp', 'cosmos']),
          json: { verdict: r.verdict, confidence: r.confidence, suspectedSegmentId: r.suspectedSegmentId, nextStep: r.nextStep, summary: r.summary },
          side: `<div class="sec-label">${T({ en: 'Evidence the agent gathered through tools', nl: 'Bewijs dat de agent via tools verzamelde' }, ctx.lang)}</div>${evidence(r.evidence, 4)}`,
          banner,
          value: c => fa
            ? { en: `Closed in ${sec(s.durationMs)} s with ${s.toolCalls.filter(x => !isRule(x.name)).length} data lookups across 4 sources. No crew dispatched, no residents alarmed.`, nl: `Gesloten in ${sec(s.durationMs)} s met ${s.toolCalls.filter(x => !isRule(x.name)).length} data-opvragingen uit 4 bronnen. Geen ploeg uitgestuurd, geen bewoners opgeschrikt.` }
            : { en: `${s.toolCalls.length} lookups across sensors, planned works, weather and usage history in ${sec(s.durationMs)} s, with the evidence written down for an audit.`, nl: `${s.toolCalls.length} opvragingen over sensoren, geplande werken, weer en verbruikshistorie in ${sec(s.durationMs)} s, met het bewijs vastgelegd voor een audit.` }
        });
      }
    },

    {
      id: 'diagnostic', type: 'step', no: 3, short: { en: 'Diagnostic', nl: 'Diagnose' }, art: 'diagnostic',
      artFix: [['SEG-0412', 'SEG-?'], ['DN300 &middot; S1 &middot; 1,840', '? &middot; ? &middot; ?']],
      label: { en: 'Agent 2 \u00B7 Diagnostic', nl: 'Agent 2 \u00B7 Diagnose' },
      title: { en: 'Where is the break, how bad is it, who is affected?', nl: 'Waar zit de breuk, hoe ernstig, wie is getroffen?' },
      text: {
        en: 'The crew and the residents both need facts: a location, a severity, the number of customers, critical sites such as care homes, and which valves to close.',
        nl: 'Ploeg en bewoners hebben feiten nodig: een locatie, een ernst, het aantal klanten, kritieke locaties zoals zorginstellingen en welke afsluiters dicht moeten.'
      },
      today: [
        { en: 'Look up the pipe in the asset register / GIS', nl: 'De leiding opzoeken in het assetregister / GIS' },
        { en: 'Estimate the break from the pressure drop', nl: 'De breuk schatten uit de drukval' },
        { en: 'Count customers and critical sites', nl: 'Klanten en kritieke locaties tellen' },
        { en: 'Find the right repair procedure', nl: 'De juiste reparatieprocedure vinden' }
      ],
      facts: () => ['Input: <b>genuine_leak</b>', 'Suspected: <b>SEG-0412</b>'],
      zones: ['agents', 'foundry', 'mcp', 'cosmos'],
      solution: ctx => {
        const s = burst('diagnostic'), r = s.result;
        return frame(ctx, {
          tag: 'DiagnosticAgent', title: { en: 'Network and asset knowledge, behind MCP tools', nl: 'Netwerk- en assetkennis, achter MCP-tools' },
          meta: meta(s, ctx.lang), chips: toolChips(s, 250),
          rules: [
            { en: 'The code reads the same MCP tools again and compares them with what the model says.', nl: 'De code leest dezelfde MCP-tools opnieuw uit en vergelijkt ze met wat het model zegt.' },
            { en: 'A broken rule means <i>needs review</i>, never silent approval.', nl: 'Een geschonden regel betekent <i>needs review</i>, nooit stilzwijgende goedkeuring.' }
          ],
          zones: withKnowledge('diagnostic', ['agents', 'foundry', 'mcp', 'cosmos']),
          json: { segmentId: r.segmentId, street: r.street, material: r.material, diameterMm: r.diameterMm, installYear: r.installYear, breachType: r.breachType, severity: r.severity, chainageM: r.chainageM, uncertaintyM: r.uncertaintyM, customersAffected: r.customersAffected, criticalCustomers: r.criticalCustomers, isolationPlan: r.isolationPlan.map(v => `${v.order}. close ${v.valveId}`) },
          side: `<div class="sec-label">${T({ en: 'Why severity S1', nl: 'Waarom ernst S1' }, ctx.lang)}</div><ul class="tl">${r.severityReasons.map((x, i, a) => `<li class="${i === a.length - 1 ? 'last' : ''}">${esc(x)}</li>`).join('')}</ul>
            <div class="sec-label">${T({ en: 'Evidence', nl: 'Bewijs' }, ctx.lang)}</div>${evidence(r.evidence, 2)}`,
          value: (() => {
            const bk = (r.summary.match(/backup valve (V-\d{4}-[A-Z])/i) || [])[1];
            return { en: `From a sensor alarm to a map position (\u00B1${r.uncertaintyM} m), severity S1, ${nf(r.customersAffected)} customers including a care home, and a valve order for the crew${bk ? `, with backup valve ${bk} ready because the SOP says a valve unused for years is stiff` : ''}.`, nl: `Van sensoralarm naar kaartpositie (\u00B1${r.uncertaintyM} m), ernst S1, ${nf(r.customersAffected)} klanten waaronder een zorginstelling, en een afsluitervolgorde voor de ploeg${bk ? `, met reserveafsluiter ${bk} klaar omdat de SOP zegt dat een jarenlang ongebruikte afsluiter vastzit` : ''}.` };
          })()
        });
      }
    },

    {
      id: 'comms', type: 'step', no: 4, short: { en: 'Residents', nl: 'Bewoners' }, art: 'comms',
      label: { en: 'Agent 3 \u00B7 Communications', nl: 'Agent 3 \u00B7 Communicatie' },
      title: { en: 'Residents need to know, honestly and quickly', nl: 'Bewoners moeten het weten, eerlijk en snel' },
      text: {
        en: 'Silence creates phone calls and anger. But a notice must not speculate about causes and must not promise what cannot be kept.',
        nl: 'Stilte leidt tot telefoontjes en boosheid. Maar een bericht mag niet speculeren over oorzaken en mag niets beloven wat niet waargemaakt kan worden.'
      },
      today: [
        { en: 'Write a notice by hand, in two languages', nl: 'Een bericht met de hand schrijven, in twee talen' },
        { en: 'Get it approved', nl: 'Het laten goedkeuren' },
        { en: 'Copy it to the website', nl: 'Het naar de website kopiëren' },
        { en: 'Remember to update it later', nl: 'Onthouden het later bij te werken' }
      ],
      facts: () => ['<b>1,840</b> households', 'Dutch + English'],
      zones: ['hostNL', 'agents', 'foundry'],
      solution: ctx => {
        const s = burst('communications'), r = s.result, p = r.published;
        return frame(ctx, {
          tag: 'CommunicationsAgent', title: { en: 'The model writes, code checks the wording, then it is published', nl: 'Het model schrijft, code controleert de tekst, dan wordt er gepubliceerd' },
          meta: meta(s, ctx.lang), chips: noDataTools(ctx.lang) + toolChips({ toolCalls: s.toolCalls.filter(c => isKb(c.name)) }, 250) + `<span class="tool" style="--d:320">Waterstoring ingest API</span><span class="tool" style="--d:390">API key</span>`,
          rules: [
            { en: 'Wording rules: no internal ids, costs, blame or health claims. A bad draft is replaced by a safe template.', nl: 'Tekstregels: geen interne id\u2019s, kosten, schuldvraag of gezondheidsclaims. Een slecht concept wordt vervangen door een veilig sjabloon.' },
            { en: `This run: fallback text used = <b>${r.usedFallbackText}</b>, rejected draft issues = <b>${r.rejectedDraftIssues.length}</b>.`, nl: `Deze run: terugvaltekst gebruikt = <b>${r.usedFallbackText}</b>, afgekeurde conceptpunten = <b>${r.rejectedDraftIssues.length}</b>.` },
            { en: 'The agent has no data tools: it can only answer, not act. Its one tool is the knowledge base with the public guidelines.', nl: 'De agent heeft geen datatools: hij kan alleen antwoorden, niet handelen. Zijn enige tool is de kennisbank met de publieke richtlijnen.' },
            { en: 'A citation mark from the knowledge base (like 【…】) in a notice is rejected by the same check.', nl: 'Een bronverwijzing uit de kennisbank (zoals 【…】) in een bericht wordt door dezelfde controle afgekeurd.' }
          ],
          zones: withKnowledge('communications', ['hostNL', 'agents', 'foundry']),
          json: { incidentId: r.incidentId, usedFallbackText: r.usedFallbackText, rejectedDraftIssues: r.rejectedDraftIssues, titleNl: r.notice.titleNl, titleEn: r.notice.titleEn, published: { status: p.timeline[0].status, place: p.place, affectedStreets: p.affectedStreets, customersAffected: p.customersAffected } },
          side: `<div class="sec-label">${T({ en: 'What residents see on the (fake) Waterstoring.nl', nl: 'Wat bewoners zien op de (nep) Waterstoring.nl' }, ctx.lang)}</div>
            <a class="live-link" href="https://leakops-waterstoring-88546e.azurewebsites.net/" target="_blank" rel="noopener noreferrer"><b>\u2197 ${T({ en: 'Open the live Waterstoring (demo) site', nl: 'Open de live Waterstoring (demo)-site' }, ctx.lang)}</b><small>${T({ en: 'The real page residents would see, with the incident the agents published. It has a Clear alerts button (operator key) to start again.', nl: 'De echte pagina die bewoners zouden zien, met het incident dat de agents publiceerden. Met een knop Meldingen wissen (operatorsleutel) om opnieuw te beginnen.' }, ctx.lang)}</small></a>${phone(ctx, 'first')}`,
          value: { en: `Dutch and English notice for ${nf(p.customersAffected)} households, drafted, checked and published in ${sec(s.durationMs)} s.`, nl: `Nederlands en Engels bericht voor ${nf(p.customersAffected)} huishoudens, opgesteld, gecontroleerd en gepubliceerd in ${sec(s.durationMs)} s.` }
        });
      }
    },

    {
      id: 'matchmaker', type: 'step', no: 5, short: { en: 'Technician', nl: 'Monteur' }, art: 'matchmaker',
      artFix: [['6 min away', '? min away']],
      label: { en: 'Agent 4 \u00B7 Resource Matchmaker', nl: 'Agent 4 \u00B7 Resource Matchmaker' },
      title: { en: 'Who can fix this, right now?', nl: 'Wie kan dit nu repareren?' },
      text: {
        en: 'The nearest technician is not always the right one. Certifications, shift and current workload decide, and the roster changes every hour.',
        nl: 'De dichtstbijzijnde monteur is niet altijd de juiste. Certificeringen, dienst en huidige werklast bepalen het, en het rooster verandert elk uur.'
      },
      today: [
        { en: 'Open the roster', nl: 'Het rooster openen' },
        { en: 'Check who holds the required certificates', nl: 'Nagaan wie de vereiste certificaten heeft' },
        { en: 'Phone around for position and travel time', nl: 'Rondbellen voor positie en reistijd' }
      ],
      facts: () => ['Needs: <b>CI-REPAIR-L3</b>', '<b>EXCAVATOR-OP</b>', '<b>TRAFFIC-MGMT</b>'],
      zones: ['agents', 'foundry', 'mcp', 'cosmos'],
      solution: ctx => {
        const s = burst('matchmaker'), r = s.result;
        const rows = [...r.candidates].sort((a, b) => a.travelMinutes - b.travelMinutes).map(c => {
          const chosen = c.technicianId === r.primary.id, backup = c.technicianId === r.backup.id;
          const pill = chosen ? '<span class="pill g">chosen</span>' : backup ? '<span class="pill b">backup</span>' : /busy/.test(c.reason) ? '<span class="pill y">busy</span>' : '<span class="pill r">not eligible</span>';
          return `<tr><td><b>${esc(c.name)}</b> <small>${c.technicianId}</small></td><td class="nw">${c.travelMinutes} min</td><td>${pill}<br><small>${esc(c.reason)}</small></td></tr>`;
        }).join('');
        return frame(ctx, {
          tag: 'ResourceMatchmakerAgent', title: { en: 'Matching job requirements to people, not just distance', nl: 'Klus en mensen matchen, niet alleen afstand' },
          meta: meta(s, ctx.lang), chips: toolChips(s, 250),
          rules: [
            { en: 'Code re-checks the choice: certification, shift end and availability. An uncertified technician can never be dispatched.', nl: 'Code controleert de keuze opnieuw: certificering, einde dienst en beschikbaarheid. Een niet-gecertificeerde monteur kan nooit worden ingezet.' },
            { en: 'The agent also names a backup (T-05).', nl: 'De agent noemt ook een reserve (T-05).' }
          ],
          zones: ['agents', 'foundry', 'mcp', 'cosmos'],
          json: { primary: { id: r.primary.id, name: r.primary.name, travelMinutes: r.primary.estimatedTravelMinutes, hasAllRequiredCertifications: r.primary.hasAllRequiredCertifications }, backup: { id: r.backup.id, name: r.backup.name, travelMinutes: r.backup.estimatedTravelMinutes }, requiredCertifications: r.requiredCertifications, estimatedRepairHours: r.estimatedRepairHours, needsExcavator: r.needsExcavator },
          side: `<a class="live-link" href="https://leakops-waterstoring-88546e.azurewebsites.net/technicians" target="_blank" rel="noopener noreferrer"><b>\u2197 ${T({ en: 'Open the live technicians page (demo)', nl: 'Open de live technici-pagina (demo)' }, ctx.lang)}</b><small>${T({ en: 'All eight technicians with portraits (AI-generated, fictional) and who is available. Pause the operator console before step 4 to show it first; after the run the chosen technician shows as assigned, with the work order.', nl: 'Alle acht monteurs met portret (door AI gemaakt, fictief) en wie beschikbaar is. Pauzeer de operator console voor stap 4 om dit eerst te tonen; na de run staat de gekozen monteur als toegewezen, met de werkbon.' }, ctx.lang)}</small></a><div class="sec-label">${T({ en: 'All technicians weighed (nearest first)', nl: 'Alle monteurs gewogen (dichtstbij eerst)' }, ctx.lang)}</div><table class="tbl"><tbody>${rows}</tbody></table>`,
          value: { en: `${r.candidates.length} technicians weighed. The nearest are busy, off shift or not certified, so the right one is ${r.primary.estimatedTravelMinutes} minutes away.`, nl: `${r.candidates.length} monteurs gewogen. De dichtstbijzijnde zijn bezet, buiten dienst of niet gecertificeerd, dus de juiste is ${r.primary.estimatedTravelMinutes} minuten weg.` }
        });
      }
    },

    {
      id: 'procurement', type: 'step', no: 6, short: { en: 'Parts', nl: 'Materiaal' }, art: 'procurement',
      artFix: [['9 reserved &middot; 1 ordered', 'in stock? ordered?']],
      label: { en: 'Agent 5 \u00B7 Procurement', nl: 'Agent 5 \u00B7 Inkoop' },
      title: { en: 'The right parts in the van, not on a waiting list', nl: 'De juiste onderdelen in de bus, niet op een wachtlijst' },
      text: {
        en: 'A burst on a DN300 cast-iron main needs a clamp, gaskets, pipe, couplings and an excavator. What is missing must be ordered, and big orders need a decision from a person.',
        nl: 'Een breuk in een DN300 gietijzeren hoofdleiding vraagt om een klem, pakkingen, buis, koppelingen en een graafmachine. Wat ontbreekt moet worden besteld en grote orders vragen een besluit van een mens.'
      },
      today: [
        { en: 'Look up the repair procedure', nl: 'De reparatieprocedure opzoeken' },
        { en: 'Check stock in the warehouse', nl: 'Voorraad in het magazijn controleren' },
        { en: 'Reserve parts and equipment', nl: 'Onderdelen en materieel reserveren' },
        { en: 'Order what is missing, ask for approval', nl: 'Bestellen wat ontbreekt, goedkeuring vragen' }
      ],
      facts: () => ['Procedure: <b>RP-CI-300-400-BURST</b>'],
      zones: ['agents', 'foundry', 'mcp', 'cosmos'],
      solution: ctx => {
        const s = burst('procurement'), r = s.result;
        const lineChips = r.lines.map(l => l.short > 0
          ? `<span class="pill y">${esc(l.name)} \u00B7 ${l.reserved}/${l.needed}</span>` : `<span class="pill g">\u2713 ${esc(l.name)}</span>`).join(' ');
        const poRows = r.purchaseOrders.map(p => `<tr><td><b>${esc(p.sku)}</b> \u00D7${p.quantity}${p.express ? ' <span class="pill y">express</span>' : ''}</td><td>${eur(p.totalEur)}</td><td>${p.approvalRequired ? `<span class="pill r">${T({ en: 'waits for a person', nl: 'wacht op een mens' }, ctx.lang)}</span>` : '<span class="pill g">ordered</span>'}</td></tr>`).join('');
        return frame(ctx, {
          tag: 'ProcurementAgent', title: { en: 'Stock-aware, within a spending limit', nl: 'Voorraadbewust, binnen een bestedingslimiet' },
          meta: meta(s, ctx.lang), chips: toolChips(s, 250),
          rules: [
            { en: `Orders above ${eur(2500)} need human approval: the ${eur(r.purchaseOrders.find(p => p.approvalRequired).totalEur)} restock stays pending.`, nl: `Orders boven ${eur(2500)} vragen menselijke goedkeuring: de restock van ${eur(r.purchaseOrders.find(p => p.approvalRequired).totalEur)} blijft in afwachting.` },
            { en: 'Stock and purchase orders are written by code tools, so nothing is reserved or ordered twice.', nl: 'Voorraad en inkooporders worden door code-tools geschreven, dus niets wordt dubbel gereserveerd of besteld.' },
            { en: `Honest result: <b>packageComplete = ${r.packageComplete}</b> (one coupling is still short).`, nl: `Eerlijk resultaat: <b>packageComplete = ${r.packageComplete}</b> (één koppeling ontbreekt nog).` },
            { en: 'The purchasing policy comes from the knowledge base, but the EUR 2,500 limit is a constant in code: a document can never raise it.', nl: 'Het inkoopbeleid komt uit de kennisbank, maar de grens van EUR 2.500 is een constante in code: een document kan die nooit verhogen.' }
          ],
          zones: withKnowledge('procurement', ['agents', 'foundry', 'mcp', 'cosmos']),
          json: { packageComplete: r.packageComplete, reserved: r.lines.filter(l => l.reserved > 0).length, short: r.lines.filter(l => l.short > 0).map(l => ({ sku: l.sku, short: l.short })), purchaseOrders: r.purchaseOrders.map(p => ({ sku: p.sku, quantity: p.quantity, totalEur: p.totalEur, express: p.express, status: p.status, approvalRequired: p.approvalRequired })) },
          side: `<a class="live-link" href="https://leakops-waterstoring-88546e.azurewebsites.net/warehouse" target="_blank" rel="noopener noreferrer"><b>\u2197 ${T({ en: 'Open the live warehouse app (demo)', nl: 'Open de live magazijn-app (demo)' }, ctx.lang)}</b><small>${T({ en: 'The Evides internal warehouse as a Power Apps style app: stock of both depots, what is reserved for this incident and the orders. Pause the operator console before step 5 to show it first; after the run the changed rows light up.', nl: 'Het interne Evides-magazijn als app in Power Apps-stijl: voorraad van beide depots, wat voor dit incident is gereserveerd en de orders. Pauzeer de operator console voor stap 5 om het eerst te tonen; na de run lichten de gewijzigde rijen op.' }, ctx.lang)}</small></a><div class="sec-label">${T({ en: 'Repair package', nl: 'Reparatiepakket' }, ctx.lang)}</div><div class="chips">${lineChips}</div>
            <div class="sec-label">${T({ en: 'Purchase orders', nl: 'Inkooporders' }, ctx.lang)}</div><table class="tbl"><tbody>${poRows}</tbody></table>`,
          value: { en: `${r.lines.filter(l => l.reserved > 0).length} items reserved, the missing coupling ordered express, and the one decision that belongs to a person is left to a person.`, nl: `${r.lines.filter(l => l.reserved > 0).length} items gereserveerd, de ontbrekende koppeling express besteld, en het ene besluit dat bij een mens hoort blijft bij een mens.` }
        });
      }
    },

    {
      id: 'scheduling', type: 'step', no: 7, short: { en: 'Planning', nl: 'Planning' }, art: 'scheduling',
      artFix: [['water back 18:15', 'water back ?']],
      label: { en: 'Agent 6 \u00B7 Scheduling', nl: 'Agent 6 \u00B7 Planning' },
      title: { en: 'A work order and an honest promise', nl: 'Een werkbon en een eerlijke belofte' },
      text: {
        en: 'Residents want one number: when is the water back? An optimistic promise that is missed costs more trust than a realistic one.',
        nl: 'Bewoners willen één getal: wanneer is het water terug? Een optimistische belofte die niet wordt gehaald kost meer vertrouwen dan een realistische.'
      },
      today: [
        { en: 'Create the work order by hand', nl: 'De werkbon met de hand maken' },
        { en: 'Add travel, valve closing, parts wait and repair time', nl: 'Reistijd, afsluiten, wachten op onderdelen en reparatietijd optellen' },
        { en: 'Decide what to tell residents', nl: 'Bepalen wat je bewoners vertelt' }
      ],
      facts: () => ['Technician: <b>T-02</b>', 'Missing part: <b>coupling</b>'],
      zones: ['hostNL', 'agents', 'foundry', 'mcp', 'cosmos'],
      solution: ctx => {
        const s = burst('scheduling'), r = s.result, wo = r.workOrder, pub = r.published, lang = ctx.lang;
        const bd = r.timeBreakdown;
        return frame(ctx, {
          tag: 'SchedulingAgent', title: { en: 'A plan built from facts, with a safety margin', nl: 'Een planning op basis van feiten, met veiligheidsmarge' },
          meta: meta(s, lang), chips: noTools(lang) + toolChips({ toolCalls: s.toolCalls }, 320),
          rules: [
            { en: 'Code checks that the promised time includes a safety margin for flushing and quality checks.', nl: 'Code controleert dat de beloofde tijd een veiligheidsmarge voor spoelen en kwaliteitscontrole bevat.' },
            { en: 'The briefing for the technician is checked for rules; otherwise a safe template is used.', nl: 'De instructie voor de monteur wordt op regels gecontroleerd; anders wordt een veilig sjabloon gebruikt.' },
            { en: 'The resident notice is updated to <i>repair planned</i> with the realistic time.', nl: 'Het bericht voor bewoners wordt bijgewerkt naar <i>reparatie gepland</i> met de realistische tijd.' }
          ],
          zones: ['hostNL', 'agents', 'foundry', 'mcp', 'cosmos'],
          json: { workOrderId: wo.id, technician: `${wo.technicianId} ${wo.technicianName}`, arrivesAt: hm(wo.expectedArrivalUtc), repairDoneAt: hm(wo.expectedCompletionUtc), residentsToldAt: hm(r.expectedResolutionUtc), travelMinutes: wo.travelMinutes, isolationMinutes: wo.isolationMinutes, partsWaitMinutes: wo.partsWaitMinutes, repairHours: wo.repairHours, purchaseOrderIds: wo.purchaseOrderIds.length + ' linked' },
          side: `<div class="sec-label">${T({ en: 'How the time was built (from the agent)', nl: 'Hoe de tijd is opgebouwd (door de agent)' }, lang)}</div><ul class="tl">${bd.map((x, i) => `<li class="${i === bd.length - 1 ? 'last' : ''}">${esc(x)}</li>`).join('')}</ul>`,
          value: { en: `Residents are told <b>${hm(r.expectedResolutionUtc)}</b>, not the optimistic 11:43: the plan already counts the missing coupling and a possibly seized valve.`, nl: `Bewoners horen <b>${hm(r.expectedResolutionUtc)}</b>, niet de optimistische 11:43: de planning telt de ontbrekende koppeling en een mogelijk vastzittende afsluiter al mee.` }
        });
      }
    },

    {
      id: 'stack', type: 'step', short: { en: 'Stack', nl: 'Stack' }, art: 'result',
      label: { en: 'The whole picture', nl: 'Het totaalbeeld' },
      title: { en: 'What does an agent need besides a model?', nl: 'Wat heeft een agent nodig naast een model?' },
      text: {
        en: 'A model alone knows what it learned during training. It has no live sensor data, has not read your procedures or past incident reports, does not know how your organisation writes a notice, and forgets everything after each call.',
        nl: 'Een model alleen weet wat het tijdens de training leerde. Het heeft geen live sensordata, heeft jullie procedures en eerdere incidentrapporten niet gelezen, weet niet hoe jullie organisatie een bericht schrijft en vergeet alles na elke aanroep.'
      },
      today: [
        { en: 'Today\u2019s readings? Needs live data.', nl: 'De metingen van nu? Vraagt live data.' },
        { en: 'What does our SOP say? Needs our documents.', nl: 'Wat zegt onze SOP? Vraagt onze documenten.' },
        { en: 'How do we write a notice? Needs our way of working.', nl: 'Hoe schrijven wij een bericht? Vraagt onze werkwijze.' },
        { en: 'What happened before? Needs earlier incident reports.', nl: 'Wat gebeurde eerder? Vraagt eerdere incidentrapporten.' }
      ],
      facts: () => ['<b>gpt-5.4</b> \u00B7 one deployment', '<b>6</b> agents', '<b>4</b> layers'],
      solution: ctx => {
        const lang = ctx.lang, TECH = window.TECH, X = window.TT.stack;
        const order = ['triage', 'diagnostic', 'communications', 'matchmaker', 'procurement', 'scheduling'];
        const cell = (cls, html) => `<td class="${cls}">${html}</td>`;
        const rows = order.map(k => {
          const a = TECH.agents[k], st = stepOf('burst', k);
          const kb = a.knowledge ? TECH.knowledge.bases.find(b => b.name === a.knowledge.knowledgeBase) : null;
          const rechecks = countCalls(st, true);
          return `<tr>
            ${cell('ag', `<b>${esc(a.name.replace('Agent', ''))}</b><small>${esc(st.model)}</small>`)}
            ${cell('', `<small>${esc(T(X.inputs[k], lang))}</small>`)}
            ${cell('', a.tools.length ? `<span class="mxc t">${a.tools.length} ${T({ en: 'data tools', nl: 'datatools' }, lang)}</span>${rechecks ? `<span class="mxc r">${rechecks} ${T({ en: 're-checks in code', nl: 'controles in code' }, lang)}</span>` : ''}` : `<span class="mxc none">${T({ en: 'none', nl: 'geen' }, lang)}</span>${rechecks ? `<span class="mxc r">${rechecks} ${T({ en: 'calls made by code', nl: 'aanroepen door code' }, lang)}</span>` : ''}`)}
            ${cell('', kb ? `<span class="mxc k">${esc(kb.name)}</span><small>${kb.docs.length} ${T({ en: 'documents', nl: 'documenten' }, lang)}</small>` : `<span class="mxc none">${T({ en: 'none', nl: 'geen' }, lang)}</span>`)}
            ${cell('', a.skills.length ? a.skills.map(n => `<span class="mxc s">${esc(n)}</span>`).join('') : `<span class="mxc none">${T({ en: 'none', nl: 'geen' }, lang)}</span>`)}
            ${cell('', `<span class="mxc none">${T({ en: 'none', nl: 'geen' }, lang)}</span>`)}
          </tr>`;
        }).join('');
        const layer = (cls, h, p, d) => `<div class="tile ${cls} rv" style="--d:${d}"><h4>${T(h, lang)}</h4><p>${T(p, lang)}</p></div>`;
        const needRows = X.needs.map(n => `<tr><td>${esc(T(n.need, lang))}</td><td><span class="mxc ${n.cls}">${esc(T(n.layer, lang))}</span></td><td><small>${esc(T(n.example, lang))}</small></td></tr>`).join('');
        return `
          <div class="sol-head rv"><span class="tag sol">${T({ en: 'The solution', nl: 'De oplossing' }, lang)}</span></div>
          <h3 class="rv" style="--d:60">${T(X.title, lang)}</h3>
          <div class="sec-label rv" style="--d:120">${T(X.matrix, lang)}</div>
          <div class="rv" style="--d:180;overflow-x:auto"><table class="mx"><thead><tr><th>${T({ en: 'Agent', nl: 'Agent' }, lang)}</th><th>${T({ en: 'Input', nl: 'Invoer' }, lang)}</th><th>${T({ en: 'Tools (MCP)', nl: 'Tools (MCP)' }, lang)}</th><th>${T({ en: 'Knowledge (Foundry IQ)', nl: 'Kennis (Foundry IQ)' }, lang)}</th><th>${T({ en: 'Skills (preview)', nl: 'Skills (preview)' }, lang)}</th><th>${T({ en: 'Memory', nl: 'Geheugen' }, lang)}</th></tr></thead><tbody>${rows}</tbody></table></div>
          <p class="small rv" style="--d:300">${T(X.memory, lang)}</p>
          <div class="sec-label rv" style="--d:380">${T(X.layers, lang)}</div>
          <div class="grid2">
            ${layer('', X.model.h, X.model.p, 440)}${layer('', X.tools.h, X.tools.p, 500)}
            ${layer('', X.knowledge.h, X.knowledge.p, 560)}${layer('purple', X.skills.h, X.skills.p, 620)}
          </div>
          <div class="sec-label rv" style="--d:700">${T(X.which, lang)}</div>
          <div class="rv" style="--d:760"><table class="tbl compact"><tbody>${needRows}</tbody></table></div>
          <div class="value rv" style="--d:1000"><div class="star">\u2605</div><div><b>${T(L.value, lang)}</b><span>${T(X.value, lang)}</span></div></div>`;
      }
    },

    {
      id: 'trust', type: 'trust', short: { en: 'Trust', nl: 'Vertrouwen' }, art: 'result',
      label: { en: 'Trust', nl: 'Vertrouwen' },
      title: { en: 'Can we trust AI with an operation like this?', nl: 'Kunnen we AI vertrouwen met zo\u2019n operatie?' },
      text: {
        en: 'Water is critical infrastructure. Operators, auditors and residents need to know what the AI did, why, and what it was never allowed to do.',
        nl: 'Water is kritieke infrastructuur. Operators, auditors en bewoners moeten weten wat de AI deed, waarom, en wat het nooit mocht doen.'
      },
      today: [
        { en: 'Who decided this, and on what data?', nl: 'Wie besliste dit, en op welke data?' },
        { en: 'Can a bad input fool the system?', nl: 'Kan een slechte invoer het systeem foppen?' },
        { en: 'How do we test a change before it goes live?', nl: 'Hoe testen we een wijziging voordat die live gaat?' }
      ],
      facts: () => ['<b>Entra ID</b> only', '<b>Managed identities</b>', 'Simulated data'],
      zones: ['foundry', 'conn', 'appins', 'strip', 'entra'],
      solution: ctx => {
        const lang = ctx.lang, steps = scen('burst').steps;
        const tools = steps.reduce((a, s) => a + countCalls(s, false), 0), rules = steps.reduce((a, s) => a + countCalls(s, true), 0), kbN = steps.reduce((a, s) => a + countKb(s), 0);
        const tile = (cls, ic, h, p, d) => `<div class="tile ${cls} rv" style="--d:${d}"><div class="ic">${ic}</div><h4>${T(h, lang)}</h4><p>${T(p, lang)}</p></div>`;
        return `
          <div class="sol-head rv"><span class="tag sol">${T({ en: 'The solution', nl: 'De oplossing' }, lang)}</span></div>
          <h3 class="rv" style="--d:60">${T({ en: 'The model judges and writes. Code owns the facts and the rules.', nl: 'Het model oordeelt en schrijft. Code beheert de feiten en de regels.' }, lang)}</h3>
          <div class="sec-label rv" style="--d:120">${T({ en: 'Four guardrails', nl: 'Vier vangrails' }, lang)}</div>
          <div class="grid2">
            ${tile('purple', '\u2713', { en: 'Rule checks in code', nl: 'Regelcontroles in code' }, { en: `In the burst run the agents made ${tools} data tool calls and ${kbN} knowledge lookups, and the code made ${rules} re-checks of its own. A broken rule means needs review.`, nl: `In de burst-run deden de agents ${tools} datatool-aanroepen en ${kbN} kennisopvragingen, en deed de code zelf ${rules} hercontroles. Een geschonden regel betekent needs review.` }, 200)}
            ${tile('yellow', '\u23F8', { en: 'Short-circuit on false alarms', nl: 'Kortsluiting bij vals alarm' }, { en: `Planned works close the flow early (${sec(scen('falseAlarm').summary.totalMs)} s in the real run): no crew, no resident notices.`, nl: `Geplande werken sluiten de flow vroeg (${sec(scen('falseAlarm').summary.totalMs)} s in de echte run): geen ploeg, geen berichten aan bewoners.` }, 320)}
            ${tile('', '\u26A0', { en: 'Content filter on input', nl: 'Contentfilter op invoer' }, { en: 'Prompt injection such as \u201Cignore all previous instructions\u201D is blocked by Foundry prompt shields before the model sees it.', nl: 'Prompt-injectie zoals \u201Cignore all previous instructions\u201D wordt door Foundry prompt shields geblokkeerd voordat het model het ziet.' }, 440)}
            ${tile('green', '\uD83D\uDC64', { en: 'Human approval', nl: 'Menselijke goedkeuring' }, { en: `Orders above ${eur(2500)} and any needs-review result wait for an operator. People stay accountable.`, nl: `Orders boven ${eur(2500)} en elk needs-review-resultaat wachten op een operator. Mensen blijven verantwoordelijk.` }, 560)}
          </div>
          <div class="sec-label rv" style="--d:700">${T({ en: 'What Microsoft Foundry adds', nl: 'Wat Microsoft Foundry toevoegt' }, lang)}</div>
          <div class="grid3">
            ${tile('', '\u25A3', { en: 'Agents & versions', nl: 'Agents & versies' }, { en: 'Six named agents with instructions, model, tools and answer schema, versioned and reusable.', nl: 'Zes benoemde agents met instructies, model, tools en antwoordschema, geversioneerd en herbruikbaar.' }, 780)}
            ${tile('', '\u223F', { en: 'Tracing', nl: 'Tracing' }, { en: 'Every run shows agent, model call and tool call as spans in Application Insights. Ideal for audits and root cause.', nl: 'Elke run toont agent, modelaanroep en tool-aanroep als spans in Application Insights. Ideaal voor audits en oorzaakanalyse.' }, 860)}
            ${tile('', '\u2714', { en: 'Evaluations', nl: 'Evaluaties' }, { en: 'Test questions with expected answers plus AI judges. Five evaluations pass: Triage 4/4, Diagnostic 1/1, and one each for the knowledge and skills of Diagnostic, Communications and Procurement. Test every change before go-live.', nl: 'Testvragen met verwachte antwoorden plus AI-beoordelaars. Vijf evaluaties slagen: Triage 4/4, Diagnostic 1/1 en één elk voor kennis en skills van Diagnostic, Communications en Procurement. Test elke wijziging voor go-live.' }, 940)}
          </div>
          <div class="grid2" style="margin-top:12px"><div class="rv" style="--d:1000"><div class="sec-label">${T({ en: 'Where it lives', nl: 'Waar het leeft' }, lang)}</div>${arch(['foundry', 'conn', 'appins', 'strip', 'entra'], 1000)}</div>
            <div class="rv" style="--d:1100"><div class="sec-label">${T({ en: 'Security by design', nl: 'Beveiliging door ontwerp' }, lang)}</div>${rulesList([
              { en: 'Entra ID only on Foundry (no local keys); managed identities between Azure services.', nl: 'Alleen Entra ID op Foundry (geen lokale sleutels); managed identities tussen Azure-diensten.' },
              { en: 'Keys only where a key is needed: MCP tools, agent functions, Waterstoring API and the operator console.', nl: 'Sleutels alleen waar een sleutel nodig is: MCP-tools, agent-functies, Waterstoring-API en operatorconsole.' },
              { en: 'Least privilege: each agent has an allow-list of data tools and its own knowledge base; two agents have no data tools, and the communications agent never sees internal documents.', nl: 'Minste rechten: elke agent heeft een toegestane lijst datatools en een eigen kennisbank; twee agents hebben geen datatools en de communicatie-agent ziet nooit interne documenten.' }
            ], lang)}</div></div>
          <div class="value rv" style="--d:1500"><div class="star">\u2605</div><div><b>${T(L.value, lang)}</b><span>${T({ en: 'AI that is useful because it is bounded: every decision is traceable, tested and, where it matters, in human hands.', nl: 'AI die nuttig is omdat ze begrensd is: elke beslissing is herleidbaar, getest en, waar het ertoe doet, in menselijke handen.' }, lang)}</span></div></div>`;
      }
    },

    { id: 'finale', type: 'finale', short: { en: 'Workflow', nl: 'Workflow' } }
  ];

  window.SLIDES = SLIDES;
  window.H = { esc, T, sec, nf, hm, eur, scen, stepOf, burst, isRule, isKb, countCalls, countKb, arch, ZONES, zoneStyle, phone };
})();
