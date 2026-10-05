// Player page for cloud streaming, served at /stream.html. Plain JS, no template placeholders inside.
export const STREAM_HTML = String.raw`<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Museum Heist - Cloud</title>
<style>
  html, body { margin: 0; height: 100%; background: #000; color: #eee; font: 15px/1.4 system-ui, sans-serif; overflow: hidden; }
  video { position: fixed; inset: 0; width: 100%; height: 100%; object-fit: contain; background: #000; outline: none; }
  #panel { position: fixed; inset: 0; display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 14px; background: rgba(0,0,0,.82); text-align: center; padding: 16px; }
  #panel.hidden { display: none; }
  button { font: inherit; padding: 9px 18px; border-radius: 6px; border: 1px solid #777; background: #222; color: #eee; cursor: pointer; }
  #banner { position: fixed; top: 12px; left: 50%; transform: translateX(-50%); background: rgba(160,40,30,.9); padding: 6px 14px; border-radius: 6px; display: none; }
  #stats { position: fixed; left: 8px; bottom: 8px; font: 12px monospace; background: rgba(0,0,0,.6); padding: 4px 8px; display: none; white-space: pre; }
</style>
</head>
<body>
<video id="v" autoplay playsinline muted></video>
<div id="panel"><div id="msg">Finding a cloud machine...</div><div id="sub"></div>
  <button id="browser">Play in the browser instead</button></div>
<div id="banner"></div>
<div id="stats"></div>
<script>
(function () {
  var v = document.getElementById("v"), panel = document.getElementById("panel");
  var msg = document.getElementById("msg"), sub = document.getElementById("sub");
  var banner = document.getElementById("banner"), statsBox = document.getElementById("stats");
  var qid = null, pc = null, ws = null, chan = null, ended = false;

  document.getElementById("browser").onclick = function () { location.href = "/?nocloud=1"; };
  function show(a, b) { panel.classList.remove("hidden"); msg.textContent = a; sub.textContent = b || ""; }

  function claim() {
    fetch("/cloud/claim" + (qid ? "?q=" + encodeURIComponent(qid) : ""), { cache: "no-store" })
      .then(function (r) { return r.json(); })
      .then(function (c) {
        if (c.none) { show(c.reason || "Cloud play unavailable.", "Starting the browser version..."); setTimeout(function () { location.href = "/?nocloud=1"; }, 2500); return; }
        if (c.wait) { qid = c.q; show("All cloud machines are busy.", "You are number " + c.wait + " in the queue."); setTimeout(claim, 3000); return; }
        connect(c);
      })
      .catch(function () { show("Can't reach the server.", "Retrying..."); setTimeout(claim, 4000); });
  }

  function connect(c) {
    show("Connecting...", "Session limit " + c.sessionMinutes + " minutes");
    pc = new RTCPeerConnection({ iceServers: c.ice, iceTransportPolicy: "relay" });
    pc.ontrack = function (e) {
      if (!v.srcObject) v.srcObject = new MediaStream();
      v.srcObject.addTrack(e.track);
      if (e.track.kind === "video") { panel.classList.add("hidden"); v.muted = false; v.play().catch(function () {}); }
    };
    pc.ondatachannel = function (e) { chan = e.channel; };
    pc.onicecandidate = function (e) {
      if (e.candidate) send({ t: "ice", candidate: e.candidate.candidate, sdpMid: e.candidate.sdpMid, sdpMLineIndex: e.candidate.sdpMLineIndex });
    };
    pc.onconnectionstatechange = function () {
      if (pc.connectionState === "failed") fail("The video connection failed (UDP blocked?).");
    };
    ws = new WebSocket((location.protocol === "https:" ? "wss://" : "ws://") + location.host + c.signal);
    ws.onmessage = function (e) {
      var m = JSON.parse(e.data);
      if (m.t === "offer") {
        pc.setRemoteDescription({ type: "offer", sdp: m.sdp })
          .then(function () { return pc.createAnswer(); })
          .then(function (a) { return pc.setLocalDescription(a); })
          .then(function () { send({ t: "answer", sdp: pc.localDescription.sdp }); });
      } else if (m.t === "ice" && m.candidate) {
        pc.addIceCandidate({ candidate: m.candidate, sdpMid: m.sdpMid, sdpMLineIndex: m.sdpMLineIndex }).catch(function () {});
      } else if (m.t === "warn") {
        banner.textContent = "Cloud session ends in " + Math.ceil(m.left / 60) + " minutes";
        banner.style.display = "block";
      } else if (m.t === "end") {
        finish(m.reason);
      }
    };
    ws.onclose = function () { if (!ended) finish("left"); };
  }

  function send(o) { if (ws && ws.readyState === 1) ws.send(JSON.stringify(o)); }
  function input(o) { if (chan && chan.readyState === "open") chan.send(JSON.stringify(o)); }

  function cleanup() { ended = true; try { pc && pc.close(); } catch (e) {} try { ws && ws.close(); } catch (e) {} if (document.pointerLockElement) document.exitPointerLock(); }
  function fail(text) { cleanup(); show(text, ""); }
  function finish(reason) {
    if (ended) return;
    cleanup();
    if (reason === "reassign") { ended = false; v.srcObject = null; show("Machine went away, moving you...", ""); qid = null; setTimeout(claim, 1000); return; }
    if (reason === "time") show("Your 30-minute cloud session is over.", "Thanks for playing!");
    else show("Session ended.", "");
    var back = document.createElement("button"); back.textContent = "Back to the menu"; back.onclick = function () { location.href = "/"; };
    panel.appendChild(back);
  }

  // --- input: pointer lock for aiming, absolute position for menus ---
  v.addEventListener("click", function () { if (!document.pointerLockElement) v.requestPointerLock(); });
  function norm(e) { var r = v.getBoundingClientRect(); return [(e.clientX - r.left) / r.width, (e.clientY - r.top) / r.height]; }
  document.addEventListener("mousemove", function (e) {
    if (document.pointerLockElement) input({ t: "m", dx: e.movementX, dy: e.movementY });
    else { var p = norm(e); input({ t: "p", x: p[0], y: p[1] }); }
  });
  document.addEventListener("mousedown", function (e) { input({ t: "b", b: e.button, d: 1 }); });
  document.addEventListener("mouseup", function (e) { input({ t: "b", b: e.button, d: 0 }); });
  document.addEventListener("wheel", function (e) { input({ t: "w", dy: e.deltaY }); }, { passive: true });
  document.addEventListener("contextmenu", function (e) { e.preventDefault(); });
  document.addEventListener("keydown", function (e) {
    if (e.code === "F2") { statsBox.style.display = statsBox.style.display === "block" ? "none" : "block"; return; }
    if (!e.repeat) input({ t: "k", c: e.code, d: 1 });
    if (e.code !== "F11" && e.code !== "F12") e.preventDefault();
  });
  document.addEventListener("keyup", function (e) { input({ t: "k", c: e.code, d: 0 }); });
  window.addEventListener("blur", function () { input({ t: "reset" }); });

  // F2: latency/fps readout for test day
  setInterval(function () {
    if (!pc || statsBox.style.display !== "block") return;
    pc.getStats().then(function (s) {
      var rtt = "-", fps = "-", kbps = "-", lost = "-";
      s.forEach(function (r) {
        if (r.type === "candidate-pair" && r.nominated && r.currentRoundTripTime != null) rtt = Math.round(r.currentRoundTripTime * 1000) + " ms";
        if (r.type === "inbound-rtp" && r.kind === "video") {
          fps = r.framesPerSecond || "-"; lost = r.packetsLost;
          if (window._lb != null) kbps = Math.round((r.bytesReceived - window._lb) * 8 / 1000);
          window._lb = r.bytesReceived;
        }
      });
      statsBox.textContent = "rtt " + rtt + "\nfps " + fps + "\nkbps " + kbps + "\nlost " + lost;
    });
  }, 1000);

  claim();
})();
</script>
</body>
</html>`;

// Snippet for index.html, right before the WebGPU/WebGL choice:
//   if (oldGpu() && !/[?&]nocloud=1/.test(location.search)) { location.replace("/stream.html"); return; }
