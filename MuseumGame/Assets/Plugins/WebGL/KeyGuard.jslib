mergeInto(LibraryManager.library, {
  // Stops Ctrl+W (crouch + walk) from closing the tab:
  //  - first click in the game goes fullscreen and uses the Keyboard Lock API, which lets the game
  //    receive Ctrl+W / Ctrl+T etc. instead of the browser (Chrome/Edge)
  //  - outside fullscreen, a "Leave site?" confirmation catches an accidental Ctrl+W
  KeyGuard_Install: function () {
    if (window.__keyGuard) return; window.__keyGuard = true;
    window.addEventListener('beforeunload', function (e) { e.preventDefault(); e.returnValue = ''; return ''; });
    var lock = function () {
      if (navigator.keyboard && navigator.keyboard.lock) navigator.keyboard.lock().catch(function () {});
    };
    document.addEventListener('fullscreenchange', function () { if (document.fullscreenElement) lock(); });
    var canvas = document.querySelector('#unity-canvas') || document.querySelector('canvas');
    var goFull = function () {
      var el = document.documentElement;
      if (!document.fullscreenElement && el.requestFullscreen) el.requestFullscreen().then(lock).catch(function () {});
    };
    (canvas || document).addEventListener('mousedown', goFull);
    document.addEventListener('keydown', function (e) {
      if (e.ctrlKey && !document.fullscreenElement) e.preventDefault();   // blocks what the browser allows (Ctrl+S, Ctrl+D...)
    }, true);
  }
});
