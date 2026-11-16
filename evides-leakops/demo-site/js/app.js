/* Slide engine: navigation, reveal, language, and the animated replay of the captured workflow run. */
(function () {
  'use strict';
  const { esc, T, sec, nf, hm, eur, scen, burst, isRule, isKb, countCalls, countKb, ZONES, zoneStyle, phone } = window.H;
  const SLIDES = window.SLIDES, C = window.CAPTURED;
  const $ = s => document.querySelector(s);
  const stage = $('#stage');

  const state = {
    i: 0,
    tab: 0,
    lang: (() => { try { return localStorage.getItem('evides-demo-lang') || 'en'; } catch { return 'en'; } })(),
    scen: 'burst',
    revealed: {},
    seen: {}
  };

  const UI = {
    back: { en: 'Back', nl: 'Terug' },
    next: { en: 'Next', nl: 'Volgende' },
    reveal: { en: 'Reveal the solution', nl: 'Toon de oplossing' },
    start: { en: 'Start the demo', nl: 'Start de demo' },
    play: { en: 'Play the workflow', nl: 'Speel de workflow af' },
    pause: { en: 'Pause', nl: 'Pauzeer' },
    replay: { en: 'Replay', nl: 'Opnieuw' },
    need: { en: 'The need', nl: 'De behoefte' },
    today: { en: 'Today, by hand', nl: 'Vandaag, met de hand' },
    lockedTitle: { en: 'How does AI solve this?', nl: 'Hoe lost AI dit op?' },
    lockedText: { en: 'Click reveal, or press Space, to show the technical solution behind this step.', nl: 'Klik op tonen, of druk op Spatie, om de technische oplossing achter deze stap te zien.' },
    hint: {
      en: '<kbd>Space</kbd> reveal / next &nbsp; <kbd>\u2190</kbd> back &nbsp; <kbd>1</kbd>\u2013<kbd>6</kbd> technical tabs &nbsp; <kbd>L</kbd> language &nbsp; <kbd>F</kbd> fullscreen',
      nl: '<kbd>Spatie</kbd> tonen / volgende &nbsp; <kbd>\u2190</kbd> terug &nbsp; <kbd>1</kbd>\u2013<kbd>6</kbd> technische tabs &nbsp; <kbd>L</kbd> taal &nbsp; <kbd>F</kbd> volledig scherm'
    },
    scenA: { en: 'A \u00B7 Burst on a cast-iron main', nl: 'A \u00B7 Breuk in gietijzeren hoofdleiding' },
    scenB: { en: 'B \u00B7 Planned valve work (false alarm)', nl: 'B \u00B7 Gepland afsluiterwerk (vals alarm)' },
    scenNote: { en: 'The next slides follow scenario A.', nl: 'De volgende slides volgen scenario A.' }
  };
  const t = o => T(o, state.lang);

  /* ---------- rendering ---------- */
  function render(opts = {}) {
    stopPlayer();
    const slide = SLIDES[state.i];
    state.seen[slide.id] = true;
    let html;
    if (slide.type === 'intro') html = introHtml();
    else if (slide.type === 'finale') html = finaleHtml();
    else html = stepHtml(slide);
    stage.innerHTML = `<div class="slide" ${opts.anim === false ? 'style="animation:none"' : ''} data-id="${slide.id}">${html}</div>`;
    if (opts.keepScroll !== true) stage.scrollTop = 0;
    if (slide.type === 'finale') initFinale();
    updateChrome();
    if (location.hash !== '#/' + state.i) history.replaceState(null, '', '#/' + state.i);
    document.documentElement.lang = state.lang;
    document.title = 'Evides LeakOps - ' + t(slide.short);
  }

  function ctx() { return { lang: state.lang, scen: state.scen }; }

  // The solution card: tab bar plus the open tab. The overview is the story; the other tabs are the technical detail from the source.
  function solHtml(slide) { return window.TechUI.wrap(slide, ctx(), state.tab, () => slide.solution(ctx())); }

  // The illustrations come from the static functional-flow page; the answers they show are hidden until the reveal.
  function artHtml(slide) {
    return (slide.artFix || []).reduce((h, [from, to]) => h.split(from).join(to), window.ART[slide.art] || '');
  }

  function introHtml() {
    const b = scen('burst').summary;
    return `<div class="intro">
      <div>
        <div class="eyebrow">Evides \u00D7 Microsoft Foundry \u00B7 ${t({ en: 'interactive demo', nl: 'interactieve demo' })}</div>
        <h1>${t({ en: 'From pressure alarm to <span>water back on the tap</span>', nl: 'Van drukalarm naar <span>water terug op de kraan</span>' })}</h1>
        <p class="lead">${t({
          en: 'Six AI agents in Microsoft Foundry handle a burst water main end to end: judge the alarm, locate the damage, inform residents, find the technician, secure the parts and plan the repair. People stay in control of every decision that matters.',
          nl: 'Zes AI-agents in Microsoft Foundry handelen een leidingbreuk van begin tot eind af: het alarm beoordelen, de schade lokaliseren, bewoners informeren, de monteur vinden, onderdelen veiligstellen en de reparatie plannen. Mensen houden de controle over elke beslissing die telt.'
        })}</p>
        <div class="pills">
          <span class="pill2">\u23F1 ${t({ en: 'about', nl: 'ongeveer' })} ${Math.round(b.totalMs / 1000)} s ${t({ en: 'end to end', nl: 'van begin tot eind' })}</span>
          <span class="pill2">6 agents</span>
<span class="pill2">${t({ en: '4 with knowledge (Foundry IQ)', nl: '4 met kennis (Foundry IQ)' })}</span>
          <span class="pill2">${nf(1840)} ${t({ en: 'households informed', nl: 'huishoudens ge\u00EFnformeerd' })}</span>
          <span class="pill2">Waterstoring.nl</span>
        </div>
        <button class="cta" data-act="next">${t(UI.start)} \u2192</button>
        <div class="how">
          <div><b>1 \u00B7 ${t(UI.need)}</b>${t({ en: 'Every slide starts with the functional need.', nl: 'Elke slide begint met de functionele behoefte.' })}</div>
          <div><b>2 \u00B7 ${t({ en: 'The solution', nl: 'De oplossing' })}</b>${t({ en: 'Reveal the technical solution: agent, tools, guardrails.', nl: 'Toon de technische oplossing: agent, tools, vangrails.' })}</div>
          <div><b>3 \u00B7 ${t({ en: 'The whole workflow', nl: 'De hele workflow' })}</b>${t({ en: 'The finale plays it all, end to end.', nl: 'De finale speelt alles af, van begin tot eind.' })}</div>
        </div>
      </div>
      <div class="art">${window.ART.hero.split('PS-101 &#8722;2.4 bar').join('PS-101 2.85 bar')}</div>
    </div>`;
  }

  function stepHtml(slide) {
    const c = ctx();
    const revealed = !!state.revealed[slide.id];
    const facts = slide.facts ? slide.facts(c).map(f => `<span class="fact">${f}</span>`).join('') : '';
    const toggle = slide.toggle ? `<div class="seg" role="group" aria-label="Scenario">
        <button data-act="scen" data-v="burst" class="${state.scen === 'burst' ? 'on' : ''}">${t(UI.scenA)}</button>
        <button data-act="scen" data-v="falseAlarm" class="${state.scen === 'falseAlarm' ? 'on' : ''}">${t(UI.scenB)}</button></div>
        ${state.scen === 'falseAlarm' ? `<div class="fact" style="display:inline-block">${t(UI.scenNote)}</div>` : ''}` : '';
    const sol = revealed ? solHtml(slide) : lockedHtml();
    return `<div class="cols">
      <article class="card need">
        <div style="display:flex;gap:10px;align-items:center;flex-wrap:wrap">${slide.no ? `<span class="step-no">${slide.no}</span>` : ''}<span class="tag need">${t(UI.need)} \u00B7 ${t(slide.label)}</span></div>
        <h2>${t(slide.title)}</h2>
        <p>${t(slide.text)}</p>
        ${toggle}
        <h4>${t(UI.today)}</h4>
        <ul>${slide.today.map(x => `<li>${t(x)}</li>`).join('')}</ul>
        ${facts ? `<div class="facts">${facts}</div>` : ''}
        <div class="art">${artHtml(slide)}</div>
      </article>
      <article class="card solcard" id="sol">${sol}</article>
    </div>`;
  }

  function lockedHtml() {
    return `<div class="locked">
      <div class="lock-ico"><svg viewBox="0 0 24 24" fill="none" stroke="#064f57" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="4" y="11" width="16" height="10" rx="3"/><path d="M8 11V8a4 4 0 018 0v3"/></svg></div>
      <h3>${t(UI.lockedTitle)}</h3><p>${t(UI.lockedText)}</p>
      <button class="reveal-btn" data-act="reveal">${t(UI.reveal)} \u25B8</button></div>`;
  }

  function updateChrome() {
    const slide = SLIDES[state.i];
    $('#dots').innerHTML = SLIDES.map((s, i) =>
      `<button class="dot ${i === state.i ? 'on' : state.seen[s.id] ? 'seen' : ''}" data-go="${i}" title="${esc(t(s.short))}" aria-label="${esc(t(s.short))}" ${i === state.i ? 'aria-current="step"' : ''}>${i}</button>`).join('');
    $('#btn-prev').disabled = state.i === 0;
    $('#btn-prev span').textContent = t(UI.back);
    const nb = $('#btn-next');
    nb.disabled = false;
    if (slide.type === 'intro') nb.textContent = t(UI.start) + ' \u2192';
    else if (slide.type === 'finale') nb.textContent = playerLabel();
    else if (!state.revealed[slide.id]) nb.textContent = t(UI.reveal) + ' \u25B8';
    else nb.textContent = t(UI.next) + ' \u2192';
    $('#hint').innerHTML = t(UI.hint);
    $('#btn-lang').textContent = state.lang === 'en' ? 'EN | nl' : 'en | NL';
  }

  /* ---------- navigation ---------- */
  function go(i) { state.i = Math.max(0, Math.min(SLIDES.length - 1, i)); state.tab = 0; render(); }
  function reveal() {
    const slide = SLIDES[state.i];
    if (slide.type === 'intro' || slide.type === 'finale' || state.revealed[slide.id]) return;
    state.revealed[slide.id] = true;
    state.tab = 0;
    $('#sol').innerHTML = solHtml(slide);
    $('#sol').scrollIntoView({ block: 'nearest', behavior: 'smooth' });
    updateChrome();
  }
  function setTab(n) {
    const slide = SLIDES[state.i];
    if (!state.revealed[slide.id] || !(slide.type === 'step' || slide.type === 'trust') || n < 0 || n >= window.TechUI.tabCount) return;
    state.tab = n;
    const sol = $('#sol');
    sol.classList.add('noanim');
    sol.innerHTML = solHtml(slide);
  }
  function copyText(text, el) {
    const done = () => { const old = el.textContent; el.textContent = '\u2713'; setTimeout(() => { el.textContent = old; }, 1200); };
    if (navigator.clipboard?.writeText) navigator.clipboard.writeText(text).then(done, done);
    else { const ta = document.createElement('textarea'); ta.value = text; document.body.appendChild(ta); ta.select(); try { document.execCommand('copy'); } catch { } ta.remove(); done(); }
  }
  function advance() {
    const slide = SLIDES[state.i];
    if (slide.type === 'finale') return togglePlay();
    if (slide.type === 'intro' || state.revealed[slide.id]) return go(state.i + 1);
    reveal();
  }
  function back() { go(state.i - 1); }
  function setLang(l) { state.lang = l; try { localStorage.setItem('evides-demo-lang', l); } catch { } const keep = fin.t; render({ anim: false, keepScroll: true }); if (SLIDES[state.i].type === 'finale') restoreFinale(keep); }
  function fullscreen() { if (document.fullscreenElement) document.exitFullscreen(); else document.documentElement.requestFullscreen?.(); }

  document.addEventListener('click', e => {
    const el = e.target.closest('[data-act],[data-go],[data-speed],[data-tab],[data-copy],[data-copy-instr],[data-copy-all]');
    if (!el) return;
    if (el.dataset.tab !== undefined) return setTab(+el.dataset.tab);
    if (el.dataset.copy !== undefined) return copyText(el.dataset.copy, el);
    if (el.dataset.copyInstr) return copyText(window.TechUI.instructionText(el.dataset.copyInstr), el);
    if (el.dataset.copyAll) return copyText(el.dataset.all, el);
    if (el.dataset.go !== undefined) return go(+el.dataset.go);
    if (el.dataset.speed) { el.blur(); fin.speed = +el.dataset.speed; document.querySelectorAll('[data-speed]').forEach(b => b.classList.toggle('on', b === el)); return; }
    switch (el.dataset.act) {
      case 'next': return advance();
      case 'reveal': return reveal();
      case 'scen': state.scen = el.dataset.v; return render({ anim: false, keepScroll: true });
      case 'play': return togglePlay();
      case 'reset': return resetFinale();
    }
  });
  $('#btn-prev').addEventListener('click', back);
  $('#btn-next').addEventListener('click', advance);
  $('#btn-lang').addEventListener('click', () => setLang(state.lang === 'en' ? 'nl' : 'en'));
  $('#btn-full').addEventListener('click', fullscreen);
  document.addEventListener('keydown', e => {
    if (e.ctrlKey || e.metaKey || e.altKey) return;
    if (e.target.closest('button') && (e.key === ' ' || e.key === 'Enter')) return;
    switch (e.key) {
      case 'ArrowRight': case 'PageDown': case ' ': case 'Enter': e.preventDefault(); advance(); break;
      case 'ArrowLeft': case 'PageUp': e.preventDefault(); back(); break;
      case 'Home': go(0); break;
      case 'End': go(SLIDES.length - 1); break;
      case 'l': case 'L': setLang(state.lang === 'en' ? 'nl' : 'en'); break;
      case 'f': case 'F': fullscreen(); break;
      case '1': case '2': case '3': case '4': case '5': case '6': setTab(+e.key - 1); break;
      case 'r': case 'R': if (SLIDES[state.i].type === 'finale') { resetFinale(); togglePlay(); } break;
    }
  });
  window.addEventListener('hashchange', () => { const n = parseInt(location.hash.replace('#/', ''), 10); if (!isNaN(n) && n !== state.i) go(n); });

  /* ---------- finale: animated replay of the captured run ---------- */
  const fin = { t: 0, speed: 1, playing: false, raf: 0, last: 0, events: [], steps: [], total: 0, cursor: 0, done: false };
  const PRE = 1800, POST = 700;
  const LIVE_URL = 'https://leakops-workflow-88546e.azurewebsites.net/';

  function buildTimeline() {
    const steps = scen('burst').steps;
    fin.steps = []; fin.events = [];
    let t0 = PRE;
    steps.forEach((s, idx) => {
      const start = t0, dur = s.durationMs, end = start + dur;
      const tools = s.toolCalls.filter(c => !isRule(c.name) && !isKb(c.name)), kbs = s.toolCalls.filter(c => isKb(c.name)), rules = s.toolCalls.filter(c => isRule(c.name));
      // data tools first, then the knowledge lookup (the instructions ask for it after the data tools)
      const calls = [...tools.map(c => ({ c, kb: false })), ...kbs.map(c => ({ c, kb: true }))];
      fin.events.push({ t: start, cls: 'a', text: `invoke_agent ${s.agent}  (${s.model})` });
      calls.forEach(({ c, kb }, i) => fin.events.push({ t: start + dur * (0.12 + 0.6 * (i + 0.5) / calls.length), cls: kb ? 'k' : '', text: kb ? `  execute_tool leakops_kb.${c.name}  ${kbQuery(c)}` : `  execute_tool leakops_mcp.${c.name}` }));
      if (!calls.length) fin.events.push({ t: start + dur * 0.55, cls: '', text: `  chat ${s.model}: ${s.inputTokens} in, ${s.outputTokens} out` });
      rules.forEach((c, i) => fin.events.push({ t: start + dur * (0.76 + 0.2 * (i + 0.5) / rules.length), cls: 'r', text: `  code re-check: ${c.name.replace('(rules) ', '')}` }));
      fin.events.push({ t: end, cls: 'd', text: `\u2713 ${s.headline}` });
      const a = window.TECH.agents[s.step];
      fin.steps.push({ idx, start, end, dur, step: s, hasTools: tools.length > 0, hasKb: kbs.length > 0, hasSkills: !!(a && a.skills.length), kbFrom: 0.12 + 0.6 * tools.length / Math.max(1, calls.length), hasRules: rules.length > 0 });
      t0 = end;
    });
    fin.total = t0 + POST;
    fin.events.sort((a, b) => a.t - b.t);
  }
  // A short snippet of the first question the agent asked the knowledge base.
  function kbQuery(c) {
    try { const q = (JSON.parse(c.arguments).query_variants || [])[0]; return q ? `\u201C${q.length > 70 ? q.slice(0, 67) + '\u2026' : q}\u201D` : ''; } catch { return ''; }
  }

  const FZ = ['operator', 'entra', 'host', 'hostRun', 'hostNL', 'agents', 'foundry', 'mcp', 'cosmos', 'appins', 'conn', 'skills', 'docs', 'search', 'kb'];
  const SEG = [[100, 150], [418, 488], [756, 826], [1094, 1164]];

  function finaleHtml() {
    buildTimeline();
    const names = { triage: ['Triage', { en: 'Real leak or false alarm?', nl: 'Echte lekkage of vals alarm?' }], diagnostic: ['Diagnostic', { en: 'Where, what, how bad?', nl: 'Waar, wat, hoe erg?' }], communications: ['Communications', { en: 'Inform the residents', nl: 'Bewoners informeren' }], matchmaker: ['Resource Matchmaker', { en: 'The right technician', nl: 'De juiste monteur' }], procurement: ['Procurement', { en: 'Parts and orders', nl: 'Onderdelen en orders' }], scheduling: ['Scheduling', { en: 'Work order and promise', nl: 'Werkbon en belofte' }] };
    fin.names = names;
    return `<div class="finale">
      <div>
        <span class="tag sol">${t({ en: 'The whole workflow', nl: 'De hele workflow' })}</span>
        <h2>${t({ en: 'One alarm, six agents, one traceable incident', nl: 'Eén alarm, zes agents, één herleidbaar incident' })}</h2>
        <p style="margin:4px 0 0">${t({ en: `Replay of a real run of the deployed demo (captured ${C.capturedAt}, simulated data). Durations and tool calls are the real ones.`, nl: `Replay van een echte run van de gedeployde demo (vastgelegd ${C.capturedAt}, gesimuleerde data). Duur en tool-aanroepen zijn de echte.` })}</p>
        <a class="live-link" href="${LIVE_URL}" target="_blank" rel="noopener noreferrer"><b>\u2197 ${t({ en: 'Run it for real: Evides LeakOps - Operator console (demo)', nl: 'Draai het echt: Evides LeakOps - Operator console (demo)' })}</b><small>${t({ en: 'The deployed workflow: pick a scenario and watch the six agents run live. Tick \u201CPause before step 3\u201D to show that Waterstoring has no notice yet, \u201Cstep 4\u201D to open the technicians page before anyone is assigned, or \u201Cstep 5\u201D to open the warehouse app before anything is reserved, then resume. Asks for the operator key; a live run takes about a minute.', nl: 'De gedeployde workflow: kies een scenario en zie de zes agents live draaien. Vink \u201CPause before step 3\u201D aan om te laten zien dat Waterstoring nog geen bericht heeft, \u201Cstep 4\u201D om de technici-pagina te openen voordat iemand is toegewezen, of \u201Cstep 5\u201D om de magazijn-app te openen voordat iets is gereserveerd, en ga daarna door. Vraagt om de operatorsleutel; een live run duurt ongeveer een minuut.' })}</small></a>
        <div class="ctrl">
          <button data-act="play" id="f-play"></button><button class="sec" data-act="reset">\u21BA ${t({ en: 'Reset', nl: 'Reset' })}</button>
          <span class="seg" style="margin:0"><button data-speed="1" class="on">1\u00D7</button><button data-speed="3">3\u00D7</button><button data-speed="10">10\u00D7</button></span>
          <span class="clock" id="f-clock">00:00 / ${fmtClock(fin.total - POST)}</span>
        </div>
        <div class="progress"><i id="f-prog"></i></div>
        <div class="arch" id="f-arch"><img src="img/architecture.svg" alt="Architecture">
          ${FZ.map(z => `<div class="zone pulse" data-z="${z}" style="${zoneStyle(z)}"></div>`).join('')}
          ${SEG.map((s, i) => `<div class="pkt" data-seg="${i}" style="top:35.87%;--x1:${s[0] / 14.8}%;--x2:${s[1] / 14.8}%"></div>`).join('')}
        </div>
        <div class="log" id="f-log" aria-live="off"></div>
      </div>
      <aside>
        <ol class="steps" id="f-steps">
          <li data-i="-1"><span class="no">\u26A1</span><div><b>${t({ en: 'Alarm', nl: 'Alarm' })}</b><small>PS-101 \u00B7 2.85 bar \u00B7 ${hm(scen('burst').alarm.triggeredAtUtc)}</small></div><span class="t"></span></li>
          ${fin.steps.map(s => `<li data-i="${s.idx}"><span class="no">${s.idx + 1}</span><div><b>${names[s.step.step][0]}</b><small>${t(names[s.step.step][1])}</small></div><span class="t"></span></li>`).join('')}
        </ol>
      </aside>
    </div>
    <div class="results hidden" id="f-results"></div>`;
  }

  function fmtClock(ms) { const s = Math.floor(ms / 1000); return String(Math.floor(s / 60)).padStart(2, '0') + ':' + String(s % 60).padStart(2, '0'); }
  function playerLabel() { return fin.playing ? '\u23F8 ' + t(UI.pause) : fin.done ? '\u21BA ' + t(UI.replay) : '\u25B6 ' + t(UI.play); }

  function initFinale() { fin.t = 0; fin.cursor = 0; fin.done = false; fin.playing = false; paint(); }
  function restoreFinale(keep) { fin.t = keep || 0; fin.cursor = fin.events.filter(e => e.t <= fin.t).length; fin.done = fin.t >= fin.total; paint(true); if (fin.done) showResults(false); }
  function stopPlayer() { fin.playing = false; cancelAnimationFrame(fin.raf); }

  function togglePlay() {
    if (fin.done) resetFinale();
    fin.playing = !fin.playing;
    if (fin.playing) { fin.last = performance.now(); fin.raf = requestAnimationFrame(tick); } else cancelAnimationFrame(fin.raf);
    updateChrome(); setPlayBtn();
  }
  function resetFinale() { stopPlayer(); fin.t = 0; fin.cursor = 0; fin.done = false; const log = $('#f-log'); if (log) log.innerHTML = ''; const r = $('#f-results'); if (r) { r.classList.add('hidden'); r.innerHTML = ''; } paint(); updateChrome(); setPlayBtn(); }
  function setPlayBtn() { const b = $('#f-play'); if (b) b.textContent = playerLabel(); }

  function tick(now) {
    if (!fin.playing) return;
    fin.t = Math.min(fin.total, fin.t + (now - fin.last) * fin.speed);
    fin.last = now;
    paint();
    if (fin.t >= fin.total) { fin.playing = false; fin.done = true; showResults(true); updateChrome(); setPlayBtn(); return; }
    fin.raf = requestAnimationFrame(tick);
  }

  let lastStepIdx = -2;
  function paint(silent) {
    const root = $('#f-arch'); if (!root) return;
    const tt = fin.t, steps = fin.steps;
    const cur = steps.find(s => tt >= s.start && tt < s.end);
    const alarmPhase = tt < PRE;
    const idx = cur ? cur.idx : alarmPhase ? -1 : fin.t >= fin.total - POST ? 99 : steps.length - 1;
    const on = new Set(), ruleZones = new Set();
    if (tt === 0 && !fin.playing) { /* idle: nothing lit */ }
    else if (alarmPhase) ['operator', 'entra', 'host', 'hostRun'].forEach(z => on.add(z));
    else if (cur) {
      const ph = (tt - cur.start) / cur.dur;
      on.add('host'); on.add('agents');
      if (ph < 0.12) { on.add('foundry'); if (cur.hasSkills) on.add('skills'); }
      else if (ph < 0.76) {
        on.add('foundry');
        if (cur.hasTools && ph < cur.kbFrom) { on.add('mcp'); on.add('cosmos'); on.add('conn'); }
        if (cur.hasKb && ph >= cur.kbFrom) { on.add('conn'); on.add('search'); on.add('kb'); on.add('docs'); }
      }
      else { if (cur.hasRules) { on.add('mcp'); on.add('cosmos'); ruleZones.add('mcp'); ruleZones.add('cosmos'); ruleZones.add('agents'); } else on.add('foundry'); }
      if (!cur.hasTools && ph > 0.8) on.add('hostNL');
      if (cur.idx >= 1 || ph > 0.3) on.add('appins');
    } else ['host', 'agents', 'foundry', 'mcp', 'cosmos', 'appins', 'conn', 'skills', 'docs', 'search', 'kb'].forEach(z => on.add(z));
    root.querySelectorAll('.zone').forEach(el => {
      el.classList.toggle('on', on.has(el.dataset.z));
      el.classList.toggle('rules', ruleZones.has(el.dataset.z));
      el.classList.toggle('pulse', !!cur || alarmPhase);
    });
    if (idx !== lastStepIdx && !silent && fin.playing) {
      const segs = idx === -1 ? [0] : [1, 2, 3];
      segs.forEach((sg, k) => setTimeout(() => { const p = root.querySelector(`[data-seg="${sg}"]`); if (!p) return; p.classList.remove('go'); void p.offsetWidth; p.classList.add('go'); }, k * 350 / Math.max(1, fin.speed)));
    }
    lastStepIdx = idx;
    // step list
    document.querySelectorAll('#f-steps li').forEach(li => {
      const i = +li.dataset.i; const s = steps[i];
      const done = i === -1 ? tt >= PRE : s && tt >= s.end;
      const run = i === -1 ? tt > 0 && tt < PRE : s && tt >= s.start && tt < s.end;
      li.className = done ? 'done' : run ? 'run' : '';
      if (s) {
        li.querySelector('.t').textContent = done ? sec(s.dur) + ' s' : '';
        li.querySelector('small').textContent = done ? s.step.headline : t(fin.names[s.step.step][1]);
      }
    });
    // log
    const log = $('#f-log');
    while (fin.cursor < fin.events.length && fin.events[fin.cursor].t <= tt) {
      const e = fin.events[fin.cursor++];
      if (log && !silent) { const d = document.createElement('div'); d.className = e.cls; d.textContent = e.text; log.appendChild(d); log.scrollTop = log.scrollHeight; }
    }
    $('#f-clock').textContent = fmtClock(Math.min(tt, fin.total - POST)) + ' / ' + fmtClock(fin.total - POST);
    $('#f-prog').style.width = Math.min(100, tt / fin.total * 100) + '%';
    setPlayBtn();
  }

  function showResults(scroll) {
    const steps = scen('burst').steps, sm = scen('burst').summary;
    const tools = steps.reduce((a, s) => a + countCalls(s, false), 0), rules = steps.reduce((a, s) => a + countCalls(s, true), 0), kbs = steps.reduce((a, s) => a + countKb(s), 0);
    const tokIn = steps.reduce((a, s) => a + (s.inputTokens || 0), 0), tokOut = steps.reduce((a, s) => a + (s.outputTokens || 0), 0);
    const pending = burst('procurement').result.purchaseOrders.filter(p => p.approvalRequired);
    const r = $('#f-results'); if (!r) return;
    const sched = burst('scheduling').result;
    r.innerHTML = `
      <div class="card">
        <div class="sol-head"><span class="tag sol">${t({ en: 'The result', nl: 'Het resultaat' })}</span></div>
        <h2 style="margin:6px 0 12px">${t({ en: 'Incident handled, humans informed, everything traceable', nl: 'Incident afgehandeld, mensen ge\u00EFnformeerd, alles herleidbaar' })}</h2>
        <div class="kpis">
          <div class="kpi"><b>${Math.round(sm.totalMs / 1000)} s</b><span>${t({ en: 'from alarm to work order, 6 agents', nl: 'van alarm tot werkbon, 6 agents' })}</span></div>
          <div class="kpi"><b>${nf(1840)}</b><span>${t({ en: 'households informed in NL and EN', nl: 'huishoudens ge\u00EFnformeerd in NL en EN' })}</span></div>
          <div class="kpi"><b>${tools} + ${kbs} + ${rules}</b><span>${t({ en: 'data tool calls + knowledge lookups + code re-checks', nl: 'datatool-aanroepen + kennisopvragingen + code-controles' })}</span></div>
          <div class="kpi light"><b>${pending.length}</b><span>${t({ en: `order (${eur(pending[0].totalEur)}) waits for a person`, nl: `order (${eur(pending[0].totalEur)}) wacht op een mens` })}</span></div>
          <div class="kpi light"><b>${Math.round(tokIn / 1000)}k + ${(tokOut / 1000).toFixed(1)}k</b><span>${t({ en: 'tokens in + out in this run (model usage, measured)', nl: 'tokens in + uit in deze run (modelgebruik, gemeten)' })}</span></div>
        </div>
        <div class="grid2" style="margin-top:16px">
          <div><div class="sec-label">${t({ en: 'What the operator sees: every step, every tool call, every rule check', nl: 'Wat de operator ziet: elke stap, elke tool-aanroep, elke regelcontrole' })}</div>
            <table class="tbl"><tbody>${steps.map(s => `<tr><td><b>${esc(s.agent.replace('Agent', ''))}</b></td><td>${esc(s.headline)}</td><td class="nw">${sec(s.durationMs)} s</td></tr>`).join('')}</tbody></table>
            <div class="facts"><span class="fact"><b>${esc(sm.incidentId)}</b></span><span class="fact"><b>${esc(sm.workOrderId)}</b></span><span class="fact">${t({ en: 'arrives', nl: 'aankomst' })} <b>${hm(sched.workOrder.expectedArrivalUtc)}</b></span><span class="fact">${t({ en: 'repair done', nl: 'reparatie klaar' })} <b>${hm(sched.workOrder.expectedCompletionUtc)}</b></span></div></div>
          <div><div class="sec-label">${t({ en: 'What residents see', nl: 'Wat bewoners zien' })}</div>${phone(ctx(), 'update')}</div>
        </div>
        <div class="compare" style="margin-top:16px">
          <div class="col today"><h4>${t({ en: 'Today', nl: 'Vandaag' })}</h4><ul>
            <li>${t({ en: 'Six hand-overs between people and systems', nl: 'Zes overdrachten tussen mensen en systemen' })}</li>
            <li>${t({ en: 'Each one waits for a phone call or an e-mail', nl: 'Elke overdracht wacht op een telefoontje of e-mail' })}</li>
            <li>${t({ en: 'Notes live in heads and chat messages', nl: 'Notities leven in hoofden en chatberichten' })}</li></ul></div>
          <div class="col with"><h4>${t({ en: 'With AI agents in Foundry', nl: 'Met AI-agents in Foundry' })}</h4><ul>
            <li>${t({ en: 'One alarm starts the whole chain', nl: 'Eén alarm start de hele keten' })}</li>
            <li>${t({ en: 'Every step reads trusted data through tools and the company\u2019s own documents through Foundry IQ', nl: 'Elke stap leest betrouwbare data via tools en de eigen documenten van het bedrijf via Foundry IQ' })}</li>
            <li>${t({ en: 'Every step leaves a trace; people decide where it matters', nl: 'Elke stap laat een spoor na; mensen beslissen waar het telt' })}</li></ul></div>
        </div>
        <div class="value" style="margin-top:16px"><div class="star">\u2605</div><div><b>${t({ en: 'AI value', nl: 'AI-waarde' })}</b><span>${t({ en: 'The duty manager receives a finished, explained and traceable incident instead of a raw alarm, and keeps the decisions that belong to a person.', nl: 'De dienstdoende manager ontvangt een afgerond, uitgelegd en herleidbaar incident in plaats van een kaal alarm, en houdt de beslissingen die bij een mens horen.' })}</span></div></div>
      </div>`;
    r.classList.remove('hidden');
    if (scroll) setTimeout(() => r.scrollIntoView({ behavior: 'smooth', block: 'start' }), 400);
  }

  /* ---------- start ---------- */
  const start = parseInt(location.hash.replace('#/', ''), 10);
  if (!isNaN(start) && start >= 0 && start < SLIDES.length) state.i = start;
  render();
})();
