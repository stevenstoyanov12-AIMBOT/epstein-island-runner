mergeInto(LibraryManager.library, {
  WS_Open: function (urlPtr) {
    var url = UTF8ToString(urlPtr);
    if (window.__ws) { try { window.__ws.close(); } catch (e) {} }
    window.__wsq = [];
    var ws = new WebSocket(url);
    window.__ws = ws;
    ws.onmessage = function (e) { if (typeof e.data === 'string') window.__wsq.push(e.data); };
  },
  WS_State: function () { return window.__ws ? window.__ws.readyState : 3; },
  WS_Send: function (p) { if (window.__ws && window.__ws.readyState === 1) window.__ws.send(UTF8ToString(p)); },
  WS_Pop: function () {
    var q = window.__wsq; if (!q || !q.length) return 0;
    var s = q.shift(); var n = lengthBytesUTF8(s) + 1; var b = _malloc(n); stringToUTF8(s, b, n); return b;
  },
  WS_Free: function (p) { _free(p); }
});
