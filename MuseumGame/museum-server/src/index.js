// Cloud streaming for weak PCs (Vast cards): /cloud/* and /stream.html, see cloud.js
import { CloudBroker, handleCloud } from "./cloud.js";
export { CloudBroker };

// Rooms per region (10 players each) and where Cloudflare should run them.
const CAP = 10;
const REGIONS = { eu: { hint: "weur", rooms: 4 }, us: { hint: "enam", rooms: 4 }, asia: { hint: "apac", rooms: 2 } };
function regionOf(req) {
  const c = (req.cf && req.cf.continent) || "EU";
  if (c === "NA" || c === "SA") return "us";
  if (c === "AS" || c === "OC") return "asia";
  return "eu";   // Europe, Africa, Middle East, unknown
}
function lobbyOf(env) { return env.LOBBY.get(env.LOBBY.idFromName("lobby")); }

// The lobby hands every player a room in their own region; when the region is full they wait in a queue
// and get the next free place the moment someone leaves. Nobody is ever sent to another continent.
export class Lobby {
  constructor(state, env) { this.state = state; this.env = env; }

  async fetch(req) {
    const url = new URL(req.url);
    if (url.pathname === "/report") {   // a room tells us how many players it has
      const room = url.searchParams.get("room"), n = +url.searchParams.get("n");
      const counts = (await this.state.storage.get("counts")) || {}, res = (await this.state.storage.get("res")) || {};
      counts[room] = n;
      if (url.searchParams.get("joined") === "1" && res[room] && res[room].length) res[room].shift();   // a reserved place was taken up
      await this.state.storage.put({ counts, res });
      await this.dispatch();
      return new Response("ok");
    }
    if (req.headers.get("Upgrade") !== "websocket") return new Response("ws only", { status: 426 });
    const region = url.searchParams.get("region") || "eu";
    const pair = new WebSocketPair();
    this.state.acceptWebSocket(pair[1], [region]);
    pair[1].serializeAttachment({ region, t: Date.now() });
    await this.dispatch();
    return new Response(null, { status: 101, webSocket: pair[0] });
  }

  pick(region, counts, res) {
    const r = REGIONS[region];
    for (let k = 1; k <= r.rooms; k++) {
      const name = region + "-" + k;
      if ((counts[name] || 0) + ((res[name] || []).length) < CAP) return name;
    }
    return null;
  }

  async dispatch() {
    const counts = (await this.state.storage.get("counts")) || {}, res = (await this.state.storage.get("res")) || {};
    const now = Date.now();
    for (const k in res) res[k] = res[k].filter(t => now - t < 20000);   // a place nobody took within 20 s is free again
    for (const region of Object.keys(REGIONS)) {
      const waiting = this.state.getWebSockets(region).map(ws => ({ ws, a: ws.deserializeAttachment() }))
        .filter(x => x.a && !x.a.done).sort((x, y) => x.a.t - y.a.t);   // first come, first served
      let i = 0;
      for (; i < waiting.length; i++) {
        const room = this.pick(region, counts, res); if (!room) break;
        (res[room] = res[room] || []).push(now);
        const w = waiting[i]; w.a.done = true; w.ws.serializeAttachment(w.a);
        try { w.ws.send(JSON.stringify({ t: "room", room })); w.ws.close(1000, "assigned"); } catch (e) {}
      }
      for (let j = i; j < waiting.length; j++) { try { waiting[j].ws.send(JSON.stringify({ t: "wait", pos: j - i + 1 })); } catch (e) {} }
    }
    await this.state.storage.put({ res });
  }

  async webSocketMessage(ws, msg) { if (msg === "ping") await this.dispatch(); }
  async webSocketClose(ws) { await this.dispatch(); }
}

// One Durable Object = one room (max 10 players). Relays JSON messages between everyone in the room.
export class Room {
  constructor(state, env) { this.state = state; this.env = env; }
  async report(name, n, joined) {
    if (!name || !this.env.LOBBY) return;
    try { await lobbyOf(this.env).fetch("https://lobby/report?room=" + encodeURIComponent(name) + "&n=" + n + (joined ? "&joined=1" : "")); } catch (e) {}
  }

  async fetch(req) {
    if (req.headers.get("Upgrade") !== "websocket") return new Response("ws only", { status: 426 });
    const roomName = new URL(req.url).searchParams.get("room") || "main";
    if (this.state.getWebSockets().length >= CAP + 2) return new Response("room full", { status: 503 });
    const pair = new WebSocketPair();
    const client = pair[0], server = pair[1];
    const id = crypto.randomUUID().slice(0, 8);
    // every player gets their own crate number (0-19): the lowest one nobody in the room holds, so two players never start in the same crate
    const used = new Set();
    for (const o of this.state.getWebSockets()) { const b = o.deserializeAttachment(); if (b && b.slot !== undefined) used.add(b.slot); }
    let slot = 0; while (used.has(slot)) slot++;
    this.state.acceptWebSocket(server);
    server.serializeAttachment({ id, slot, room: roomName });
    server.send(JSON.stringify({ t: "hello", id, sl: slot + 1 }));
    // catch a late joiner up: replay every shot / action this room has seen, so the world is broken the same way for them
    const ev = (await this.state.storage.get("ev")) || [];
    for (const e of ev) server.send(JSON.stringify(e));
    await this.report(roomName, this.state.getWebSockets().length, true);
    return new Response(null, { status: 101, webSocket: client });
  }

  async webSocketMessage(ws, msg) {
    const a = ws.deserializeAttachment();
    let m; try { m = JSON.parse(msg); } catch (e) { return; }
    if (!m || typeof m !== "object") return;
    m.id = a.id;
    if (m.t === "s") { a.x = +m.x; a.y = +m.y; a.z = +m.z; a.al = m.al; ws.serializeAttachment(a); }
    else if (m.t === "hit") {
      const d = +m.d, now = Date.now();
      if (!(d > 0 && d <= 40) || a.al === 0 || now - (a.lh || 0) < 90) return;
      a.lh = now; ws.serializeAttachment(a);
      for (const o of this.state.getWebSockets()) {
        const b = o.deserializeAttachment();
        if (b.id !== m.to) continue;
        if (b.al === 0) return;
        if (a.x !== undefined && b.x !== undefined) {
          const dx = a.x - b.x, dy = a.y - b.y, dz = a.z - b.z;
          if (dx * dx + dy * dy + dz * dz > 40000) return;
        }
        o.send(JSON.stringify(m)); return;
      }
      return;
    }
    if (m.t === "shot" || m.t === "act") {
      // world events: keep a log (newest 800) for players who join later
      const ev = (await this.state.storage.get("ev")) || [];
      ev.push(m); if (ev.length > 800) ev.splice(0, ev.length - 800);
      await this.state.storage.put("ev", ev);
    }
    const out = JSON.stringify(m);
    for (const o of this.state.getWebSockets()) if (o !== ws) o.send(out);
  }

  async webSocketClose(ws) {
    const a = ws.deserializeAttachment();
    const out = JSON.stringify({ t: "leave", id: a.id });
    let left = 0;
    for (const o of this.state.getWebSockets()) if (o !== ws) { o.send(out); left++; }
    // everyone gone: the next session starts with an untouched world
    if (left === 0) await this.state.storage.delete("ev");
    await this.report(a.room, left, false);   // a place opened up: the lobby lets the next one in
  }
}

const TYPES = { html: "text/html; charset=utf-8", js: "application/javascript", wasm: "application/wasm", json: "application/json", css: "text/css", png: "image/png", jpg: "image/jpeg", ico: "image/x-icon", svg: "image/svg+xml" };

async function serveGame(req, env) {
  let key = decodeURIComponent(new URL(req.url).pathname.slice(1)) || "index.html";
  const range = req.headers.get("Range");
  const obj = await env.GAME.get(key, range ? { range: req.headers } : {});
  if (!obj) return new Response("not found", { status: 404 });
  const h = new Headers();
  let name = key; const gz = name.endsWith(".unityweb");
  if (gz) name = name.slice(0, -9);   // .unityweb: Unity's loader unpacks these itself (gzip or brotli), so no Content-Encoding header
  if (name.endsWith(".bundle")) h.set("Content-Encoding", "br");   // Addressables bundles are uploaded Brotli-compressed (brotli_bundles.js)
  const ext = name.split(".").pop().toLowerCase();
  h.set("Content-Type", TYPES[ext] || "application/octet-stream");
  h.set("Cache-Control", "no-cache");   // always revalidate by ETag: some bundles keep their name when content changes
  h.set("ETag", obj.httpEtag);
  if (!range && req.headers.get("If-None-Match") === obj.httpEtag) return new Response(null, { status: 304, headers: h });
  h.set("Accept-Ranges", "bytes");
  if (obj.range && range) {
    const start = obj.range.offset ?? 0, len = obj.range.length ?? (obj.size - start);
    h.set("Content-Range", `bytes ${start}-${start + len - 1}/${obj.size}`); h.set("Content-Length", String(len));
    return new Response(obj.body, { status: 206, headers: h, encodeBody: h.has("Content-Encoding") ? "manual" : "automatic" });
  }
  h.set("Content-Length", String(obj.size));
  // already-compressed files: tell Cloudflare not to compress them a second time
  return new Response(obj.body, { headers: h, encodeBody: h.has("Content-Encoding") ? "manual" : "automatic" });
}

export default {
  fetch(req, env) {
    const path = new URL(req.url).pathname;
    if (path.startsWith("/cloud/") || path === "/stream.html") return handleCloud(req, env, regionOf(req));
    if (req.headers.get("Upgrade") === "websocket") {
      const url = new URL(req.url);
      if (url.pathname === "/lobby") {   // first stop: which room (or which place in the queue)
        const u = new URL(req.url); u.searchParams.set("region", regionOf(req));
        return lobbyOf(env).fetch(new Request(u.toString(), req));
      }
      const room = url.searchParams.get("room") || "main";
      const reg = REGIONS[room.split("-")[0]];
      const stub = reg ? env.ROOM.get(env.ROOM.idFromName(room), { locationHint: reg.hint }) : env.ROOM.get(env.ROOM.idFromName(room));
      return stub.fetch(req);
    }
    return serveGame(req, env);   // everything else is the game, served from the R2 bucket
  },
};
