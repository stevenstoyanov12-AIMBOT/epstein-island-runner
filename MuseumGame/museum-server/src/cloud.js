// Cloud streaming broker for weak PCs (Vast.ai cards running the Linux build).
//
//   POST /cloud/report         card heartbeat every 10 s (header X-Cloud-Key = env.CLOUD_SECRET)
//   GET  /cloud/claim?q=<id>   player asks for a slot -> {ticket, ice, signal} or {wait: position}
//   WS   /cloud/signal         role=host (one per Unity instance) or role=player; SDP/ICE relay
//   GET  /stream.html          the player page (video + input)
//
// Media never touches Cloudflare: the browser talks straight to the card over its direct UDP/TCP port,
// through the card's own coturn (credentials minted here from the secret the card reports).
// Regions follow the Lobby rule (regionOf in index.js); there are no Asia cards, so Asia plays in the browser.
import { STREAM_HTML } from "./cloud_page.js";

const CAP = 40;                    // concurrent cloud players across all cards
const SESSION_MS = 30 * 60e3;      // hard cap per session
const WARN_MS = 25 * 60e3;
const CARD_DEAD_MS = 30e3;
const TICKET_MS = 30e3;            // a claimed slot must connect within this
const QUEUE_STALE_MS = 15e3;       // queue entries that stop polling drop out
const TICK_MS = 5e3;

// Route /cloud/* and /stream.html here from the main fetch handler.
// region: the Lobby's regionOf(request), so cloud players follow exactly the same continent rule.
export async function handleCloud(request, env, region) {
  const url = new URL(request.url);
  if (url.pathname === "/stream.html")
    return new Response(STREAM_HTML, { headers: { "content-type": "text/html; charset=utf-8", "cache-control": "no-store" } });
  const id = env.CLOUD.idFromName("global");
  const headers = new Headers(request.headers);
  headers.set("x-region", region || "eu");
  return env.CLOUD.get(id).fetch(new Request(request, { headers }));
}

export class CloudBroker {
  constructor(state, env) {
    this.state = state;
    this.env = env;
    this.cards = new Map();     // id -> {region, ip, udp, tcp, turnSecret, seen, slots: Map(n -> slot)}
    this.tickets = new Map();   // ticket -> {card, slot, region, made, player, host, start, warned}
    this.queue = { eu: [], us: [], asia: [] };   // [{id, seen}]
  }

  async fetch(request) {
    const url = new URL(request.url);
    const region = request.headers.get("x-region") || "eu";
    this.tickArm();
    if (url.pathname === "/cloud/report" && request.method === "POST") return this.report(request);
    if (url.pathname === "/cloud/claim") return this.claim(url, region);
    if (url.pathname === "/cloud/signal" && request.headers.get("Upgrade") === "websocket") return this.signal(url);
    if (url.pathname === "/cloud/status" && this.authorised(url.searchParams.get("key"))) return json(this.status());
    return new Response("not found", { status: 404 });
  }

  authorised(key) {
    return !!this.env.CLOUD_SECRET && key === this.env.CLOUD_SECRET;
  }

  // --- cards -------------------------------------------------------------------------

  async report(request) {
    if (!this.authorised(request.headers.get("x-cloud-key"))) return new Response("forbidden", { status: 403 });
    const r = await request.json();
    if (!r.card || !r.ip || !r.udp) return new Response("bad report", { status: 400 });
    let card = this.cards.get(r.card);
    if (!card) {
      card = { slots: new Map() };
      this.cards.set(r.card, card);
    }
    Object.assign(card, {
      region: r.region === "us" ? "us" : "eu", ip: r.ip, udp: r.udp, tcp: r.tcp || null,
      turnSecret: r.turnSecret, seen: Date.now(), gpu: r.gpu || null,
    });
    for (const s of r.slots || []) {
      const slot = this.slot(card, s.slot);
      slot.alive = !!s.alive;
    }
    return json({ ok: true, active: this.active() });
  }

  slot(card, n) {
    let s = card.slots.get(n);
    if (!s) card.slots.set(n, (s = { host: null, ticket: null, draining: false, alive: false }));
    return s;
  }

  // --- players -----------------------------------------------------------------------

  active() {
    return this.tickets.size;
  }

  freeSlot(region) {
    const now = Date.now();
    let best = null;
    for (const [id, card] of this.cards) {
      if (card.region !== region || now - card.seen > CARD_DEAD_MS) continue;
      let free = [], busy = 0;
      for (const [n, s] of card.slots) {
        if (s.ticket) busy++;
        else if (s.host && !s.draining) free.push(n);
      }
      // fill the busiest card first so whole cards can be switched off when quiet
      if (free.length && (!best || busy > best.busy)) best = { id, card, n: free[0], busy };
    }
    return best;
  }

  freeCount(region) {
    const now = Date.now();
    let n = 0;
    for (const card of this.cards.values()) {
      if (card.region !== region || now - card.seen > CARD_DEAD_MS) continue;
      for (const s of card.slots.values()) if (!s.ticket && s.host && !s.draining) n++;
    }
    return n;
  }

  async claim(url, region) {
    if (region === "asia")
      return json({ none: true, reason: "No cloud machines in Asia yet. Playing in the browser instead." });
    const now = Date.now();
    const q = this.queue[region];
    const qid = url.searchParams.get("q") || crypto.randomUUID();
    for (let i = q.length - 1; i >= 0; i--) if (now - q[i].seen > QUEUE_STALE_MS) q.splice(i, 1);
    let entry = q.find(e => e.id === qid);
    if (!entry) q.push((entry = { id: qid, seen: now }));
    entry.seen = now;

    const pos = q.indexOf(entry);
    const anyCards = [...this.cards.values()].some(c => c.region === region && now - c.seen <= CARD_DEAD_MS);
    if (!anyCards) {
      q.splice(pos, 1);
      return json({ none: true, reason: "Cloud play is offline right now." });
    }
    const room = Math.min(this.freeCount(region), CAP - this.active());
    const pick = pos < room ? this.freeSlot(region) : null;
    if (!pick) return json({ wait: pos + 1, q: qid });

    q.splice(pos, 1);
    const ticket = crypto.randomUUID();
    const slot = pick.card.slots.get(pick.n);
    slot.ticket = ticket;
    this.tickets.set(ticket, { card: pick.id, slot: pick.n, region, made: now, player: null, start: 0, warned: false });
    return json({
      ticket,
      ice: await this.ice(pick.card),
      signal: `/cloud/signal?role=player&ticket=${ticket}`,
      sessionMinutes: SESSION_MS / 60e3,
    });
  }

  // coturn "use-auth-secret": username = expiry, password = base64(HMAC-SHA1(secret, username))
  async ice(card) {
    const username = `${Math.floor(Date.now() / 1000) + 3600}:museum`;
    const key = await crypto.subtle.importKey("raw", new TextEncoder().encode(card.turnSecret),
      { name: "HMAC", hash: "SHA-1" }, false, ["sign"]);
    const sig = await crypto.subtle.sign("HMAC", key, new TextEncoder().encode(username));
    const credential = btoa(String.fromCharCode(...new Uint8Array(sig)));
    const urls = [`turn:${card.ip}:${card.udp}?transport=udp`];
    if (card.tcp) urls.push(`turn:${card.ip}:${card.tcp}?transport=tcp`);
    return [{ urls, username, credential }, { urls: "stun:stun.l.google.com:19302" }];
  }

  // --- signalling --------------------------------------------------------------------

  signal(url) {
    const role = url.searchParams.get("role");
    const pair = new WebSocketPair();
    const [client, server] = Object.values(pair);
    server.accept();

    if (role === "host") {
      if (!this.authorised(url.searchParams.get("key"))) {
        server.close(1008, "forbidden");
        return new Response(null, { status: 101, webSocket: client });
      }
      const cardId = url.searchParams.get("card");
      const n = parseInt(url.searchParams.get("slot") || "0", 10);
      let card = this.cards.get(cardId);
      if (!card) this.cards.set(cardId, (card = { slots: new Map(), region: "eu", seen: 0 }));
      const slot = this.slot(card, n);
      if (slot.host) try { slot.host.close(1000, "replaced"); } catch {}
      slot.host = server;
      slot.draining = false;
      slot.ticket = null;      // a fresh instance never carries an old session
      server.addEventListener("message", e => this.fromHost(cardId, n, e.data));
      const gone = () => {
        if (slot.host !== server) return;
        slot.host = null;
        slot.draining = false;
        if (slot.ticket) this.end(slot.ticket, "reassign");
      };
      server.addEventListener("close", gone);
      server.addEventListener("error", gone);
    } else {
      const ticket = url.searchParams.get("ticket");
      const t = this.tickets.get(ticket);
      if (!t || t.player) {
        server.send(JSON.stringify({ t: "end", reason: "expired" }));
        server.close(1000, "expired");
        return new Response(null, { status: 101, webSocket: client });
      }
      t.player = server;
      t.start = Date.now();
      server.addEventListener("message", e => this.fromPlayer(ticket, e.data));
      const gone = () => { if (this.tickets.get(ticket)?.player === server) this.end(ticket, "left"); };
      server.addEventListener("close", gone);
      server.addEventListener("error", gone);
      const host = this.hostOf(t);
      if (host) host.send(JSON.stringify({ t: "join", ticket }));
      else this.end(ticket, "reassign");
    }
    return new Response(null, { status: 101, webSocket: client });
  }

  hostOf(t) {
    const card = this.cards.get(t.card);
    return card && card.slots.get(t.slot)?.host;
  }

  fromHost(cardId, n, data) {
    const slot = this.cards.get(cardId)?.slots.get(n);
    const t = slot?.ticket && this.tickets.get(slot.ticket);
    if (t?.player) try { t.player.send(data); } catch {}
  }

  fromPlayer(ticket, data) {
    const t = this.tickets.get(ticket);
    const host = t && this.hostOf(t);
    if (host) try { host.send(data); } catch {}
  }

  // Close a session. The host instance quits on "end" and the supervisor starts a fresh one,
  // which reconnects and frees the slot; until then the slot is draining and never handed out.
  end(ticket, reason) {
    const t = this.tickets.get(ticket);
    if (!t) return;
    this.tickets.delete(ticket);
    const msg = JSON.stringify({ t: "end", reason });
    if (t.player) try { t.player.send(msg); t.player.close(1000, reason); } catch {}
    const card = this.cards.get(t.card);
    const slot = card && card.slots.get(t.slot);
    if (slot && slot.ticket === ticket) {
      slot.ticket = null;
      if (slot.host) {
        slot.draining = true;
        try { slot.host.send(msg); } catch {}
      }
    }
  }

  // --- timers ------------------------------------------------------------------------

  async tickArm() {
    if ((await this.state.storage.getAlarm()) == null) await this.state.storage.setAlarm(Date.now() + TICK_MS);
  }

  async alarm() {
    const now = Date.now();
    for (const [ticket, t] of [...this.tickets]) {
      if (!t.player && now - t.made > TICKET_MS) { this.end(ticket, "expired"); continue; }
      if (!t.start) continue;
      const age = now - t.start;
      if (age >= SESSION_MS) this.end(ticket, "time");
      else if (age >= WARN_MS && !t.warned) {
        t.warned = true;
        const msg = JSON.stringify({ t: "warn", left: Math.round((SESSION_MS - age) / 1000) });
        try { t.player.send(msg); } catch {}
        try { this.hostOf(t)?.send(msg); } catch {}
      }
    }
    for (const [id, card] of [...this.cards]) {
      if (now - card.seen <= CARD_DEAD_MS) continue;
      for (const s of card.slots.values()) if (s.ticket) this.end(s.ticket, "reassign");
      this.cards.delete(id);
    }
    if (this.cards.size || this.tickets.size) await this.state.storage.setAlarm(now + TICK_MS);
  }

  status() {
    const cards = [...this.cards].map(([id, c]) => ({
      id, region: c.region, ip: c.ip, ageS: Math.round((Date.now() - c.seen) / 1000), gpu: c.gpu,
      slots: [...c.slots].map(([n, s]) => ({ n, host: !!s.host, busy: !!s.ticket, draining: s.draining })),
    }));
    return { active: this.active(), cap: CAP, queue: { eu: this.queue.eu.length, us: this.queue.us.length }, cards };
  }
}

function json(o) {
  return new Response(JSON.stringify(o), {
    headers: { "content-type": "application/json", "cache-control": "no-store", "access-control-allow-origin": "*" },
  });
}
