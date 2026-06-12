/* SPOKE — anime for your ears. Vanilla JS, no build step. */
(() => {
  const $ = (s) => document.querySelector(s);
  const fmt = (s) => {
    if (!isFinite(s)) return "0:00";
    s = Math.max(0, Math.floor(s));
    return Math.floor(s / 60) + ":" + String(s % 60).padStart(2, "0");
  };

  const audio = new Audio();
  audio.preload = "metadata";
  audio.setAttribute("playsinline", "");
  audio.id = "spokeAudio";
  audio.style.display = "none";
  document.addEventListener("DOMContentLoaded", () => document.body.appendChild(audio));
  if (document.readyState !== "loading") document.body.appendChild(audio);
  let data = null, styles = [], idx = 0, cues = [], activeCue = -1;
  let seeking = false, pendingSeek = 0;

  // ---------- boot ----------
  fetch("data/episodes.json")
    .then((r) => r.json())
    .then((m) => { data = m; styles = m.styles; render(); })
    .catch((e) => { $("#npLine").textContent = "Could not load episode data."; console.error(e); });

  function render() {
    const ep = data.episode;
    $("#epSeries").textContent = ep.series;
    $("#epTitle").textContent = ep.title;
    $("#epNum").textContent = String(ep.number).padStart(2, "0");
    $("#epLogline").textContent = ep.logline;
    document.title = `${ep.series} — ${ep.title} · SPOKE`;

    // tabs
    const tabs = $("#styleTabs");
    tabs.innerHTML = "";
    styles.forEach((s, i) => {
      const b = document.createElement("button");
      b.className = "tab" + (i === idx ? " active" : "");
      b.setAttribute("role", "tab");
      b.innerHTML = `<div class="n">0${i + 1} · ${fmtLen(s.duration)}</div>
                     <div class="l">${s.label}</div>
                     <div class="t">${s.tag}</div>`;
      b.onclick = () => selectStyle(i, true);
      tabs.appendChild(b);
    });

    // speeds
    const sp = $("#speeds");
    sp.innerHTML = "";
    [0.85, 1, 1.25, 1.5, 1.75].forEach((r) => {
      const b = document.createElement("button");
      b.textContent = r === 1 ? "1×" : r + "×";
      if (r === 1) b.classList.add("on");
      b.onclick = () => {
        audio.playbackRate = r;
        [...sp.children].forEach((c) => c.classList.remove("on"));
        b.classList.add("on");
      };
      sp.appendChild(b);
    });

    loadStyle(idx, false);
    bindControls();
    registerSW();
    refreshOfflineState();
  }

  const fmtLen = (s) => `${Math.floor(s / 60)}:${String(Math.round(s % 60)).padStart(2, "0")}`;

  // ---------- style loading ----------
  function selectStyle(i, autoplay) {
    if (i < 0) i = styles.length - 1;
    if (i >= styles.length) i = 0;
    const keepT = audio.currentTime;       // A/B compare: stay at the same moment
    const wasPlaying = !audio.paused;
    idx = i;
    [...$("#styleTabs").children].forEach((t, k) => t.classList.toggle("active", k === idx));
    loadStyle(idx, true, Math.min(keepT, styles[idx].duration - 0.3));
    if (wasPlaying || autoplay) audio.play().catch(() => {});
  }

  function loadStyle(i, restore, t = 0) {
    const s = styles[i];
    cues = s.cues;
    pendingSeek = restore && t > 0.05 ? t : 0;   // applied once metadata loads
    audio.src = s.audio;
    audio.load();
    $("#npStyle").textContent = s.label;
    $("#styleBlurb").textContent = s.blurb;
    $("#dur").textContent = fmt(s.duration);
    renderCues();
    activeCue = -1;
    if (pendingSeek) {
      $("#cur").textContent = fmt(pendingSeek);
      updateActiveCue(pendingSeek);
    } else {
      $("#npLine").textContent = "Press play";
      $("#npWho").textContent = s.label;
    }
    setMediaMeta();
  }

  function renderCues() {
    const ol = $("#cues");
    ol.innerHTML = "";
    cues.forEach((c, i) => {
      const li = document.createElement("li");
      const isAct = c.who === "NARRATOR";
      li.className = "cue" + (isAct ? " act" : "");
      li.innerHTML = `<span class="who">${isAct ? "" : c.name}</span><span class="txt">${c.text}</span>`;
      li.onclick = () => { audio.currentTime = c.t + 0.01; if (audio.paused) audio.play(); };
      ol.appendChild(li);
    });
  }

  // ---------- controls ----------
  function bindControls() {
    $("#playBtn").onclick = () => (audio.paused ? audio.play() : audio.pause());
    $("#back15").onclick = () => (audio.currentTime = Math.max(0, audio.currentTime - 15));
    $("#fwd15").onclick = () => (audio.currentTime = Math.min(audio.duration || 1e9, audio.currentTime + 15));
    $("#prevStyle").onclick = () => selectStyle(idx - 1, false);
    $("#nextStyle").onclick = () => selectStyle(idx + 1, false);
    $("#offlineBtn").onclick = saveOffline;

    const seek = $("#seek");
    seek.addEventListener("input", () => { seeking = true; });
    seek.addEventListener("change", () => {
      audio.currentTime = (seek.value / 1000) * (audio.duration || styles[idx].duration);
      seeking = false;
    });

    audio.addEventListener("play", () => { $("#playBtn").textContent = "❚❚"; setPlayState("playing"); });
    audio.addEventListener("pause", () => { $("#playBtn").textContent = "▶"; setPlayState("paused"); });
    audio.addEventListener("timeupdate", onTime);
    audio.addEventListener("ended", () => selectStyle(idx + 1, false));
    audio.addEventListener("loadedmetadata", () => {
      if (pendingSeek) { try { audio.currentTime = pendingSeek; } catch (e) {} pendingSeek = 0; }
      onTime();
    });

    document.addEventListener("keydown", (e) => {
      if (e.target.tagName === "INPUT") return;
      if (e.code === "Space") { e.preventDefault(); audio.paused ? audio.play() : audio.pause(); }
      if (e.code === "ArrowLeft") audio.currentTime -= 5;
      if (e.code === "ArrowRight") audio.currentTime += 5;
      if (e.code === "BracketRight") selectStyle(idx + 1, false);
      if (e.code === "BracketLeft") selectStyle(idx - 1, false);
    });

    window.addEventListener("online", refreshNet);
    window.addEventListener("offline", refreshNet);
    refreshNet();
  }

  function onTime() {
    const d = audio.duration || styles[idx].duration;
    const t = audio.currentTime;
    if (!seeking) {
      const p = d ? (t / d) * 1000 : 0;
      const seek = $("#seek");
      seek.value = p;
      seek.style.setProperty("--p", (p / 10) + "%");
    }
    $("#cur").textContent = fmt(t);
    updateActiveCue(t);
    if (activeCue < 0 && cues.length && t > 0.3 && t < cues[0].t) {
      $("#npLine").textContent = "♪ opening theme";
      $("#npWho").textContent = styles[idx].label;
    }
    updatePosition();
  }

  function updateActiveCue(t) {
    let found = -1;
    for (let i = 0; i < cues.length; i++) {
      if (t >= cues[i].t - 0.05) found = i; else break;
    }
    if (found === activeCue) return;
    activeCue = found;
    const ol = $("#cues");
    [...ol.children].forEach((li, i) => li.classList.toggle("on", i === found));
    if (found >= 0) {
      const c = cues[found];
      $("#npLine").textContent = c.text;
      $("#npWho").textContent = c.who === "NARRATOR" ? "narration" : c.name;
      const li = ol.children[found];
      if (li) ol.scrollTop = li.offsetTop - ol.clientHeight / 2 + li.clientHeight / 2;
      setMediaMeta(c);
    }
  }

  // ---------- MediaSession (lockscreen / handlebars controls) ----------
  function setMediaMeta(cue) {
    if (!("mediaSession" in navigator)) return;
    const s = styles[idx], ep = data.episode;
    navigator.mediaSession.metadata = new MediaMetadata({
      title: cue ? cue.text.slice(0, 80) : `${ep.title} — ${s.label}`,
      artist: `${s.label} · ${ep.series}`,
      album: "SPOKE",
      artwork: [
        { src: "assets/icon-192.png", sizes: "192x192", type: "image/png" },
        { src: "assets/icon-512.png", sizes: "512x512", type: "image/png" },
      ],
    });
    const set = (a, fn) => { try { navigator.mediaSession.setActionHandler(a, fn); } catch (e) {} };
    set("play", () => audio.play());
    set("pause", () => audio.pause());
    set("seekbackward", () => (audio.currentTime -= 15));
    set("seekforward", () => (audio.currentTime += 15));
    set("previoustrack", () => selectStyle(idx - 1, false));
    set("nexttrack", () => selectStyle(idx + 1, false));
    set("seekto", (d) => { if (d.seekTime != null) audio.currentTime = d.seekTime; });
  }
  function setPlayState(st) { if ("mediaSession" in navigator) navigator.mediaSession.playbackState = st; }
  function updatePosition() {
    if (!("mediaSession" in navigator) || !navigator.mediaSession.setPositionState) return;
    const d = audio.duration;
    if (!isFinite(d) || d <= 0) return;
    try {
      navigator.mediaSession.setPositionState({
        duration: d, position: Math.min(audio.currentTime, d), playbackRate: audio.playbackRate,
      });
    } catch (e) {}
  }

  // ---------- offline ----------
  function registerSW() {
    if ("serviceWorker" in navigator) navigator.serviceWorker.register("sw.js").catch(() => {});
  }
  async function saveOffline() {
    const btn = $("#offlineBtn");
    btn.classList.add("busy"); $("#offlineLabel").textContent = "Saving…";
    try {
      const cache = await caches.open("spoke-media-v1");
      const urls = ["./", "index.html", "styles.css", "app.js", "manifest.webmanifest",
        "data/episodes.json", "assets/cover.svg", "assets/icon-192.png", "assets/icon-512.png",
        ...styles.map((s) => s.audio)];
      await cache.addAll(urls);
      btn.classList.remove("busy"); btn.classList.add("saved");
      $("#offlineLabel").textContent = "Saved ✓";
    } catch (e) {
      btn.classList.remove("busy"); $("#offlineLabel").textContent = "Save failed";
      console.error(e);
    }
  }
  async function refreshOfflineState() {
    if (!("caches" in window)) return;
    try {
      const cache = await caches.open("spoke-media-v1");
      const hit = await cache.match(styles[0].audio);
      if (hit) { $("#offlineBtn").classList.add("saved"); $("#offlineLabel").textContent = "Saved ✓"; }
    } catch (e) {}
  }
  function refreshNet() {
    const el = $("#netState");
    if (navigator.onLine) { el.textContent = "online"; el.classList.remove("off"); }
    else { el.textContent = "offline · cached"; el.classList.add("off"); }
  }
})();
