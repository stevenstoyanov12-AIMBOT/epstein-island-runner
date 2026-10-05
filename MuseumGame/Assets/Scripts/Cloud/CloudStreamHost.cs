#if CLOUD_STREAMING
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.WebRTC;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// One cloud streaming instance = one remote player. Lives for one session: after "end" (or the player
// leaving / the link dying) it quits and the card supervisor starts a fresh instance in the same slot.
//  - signalling: WebSocket to the Worker (/cloud/signal?role=host), relays SDP/ICE with the browser
//  - video: the whole composited screen (game + UI) copied into a 1280x720 texture, H.264 (NVENC)
//  - audio: tapped from whichever AudioListener is active
//  - input: browser mouse/keyboard over a data channel, fed into virtual Input System devices
public class CloudStreamHost : MonoBehaviour
{
    const int Width = 1280, Height = 720;
    const ulong MaxBitrate = 8_000_000;

    [Serializable] class Sig { public string t, sdp, candidate, sdpMid, reason; public int sdpMLineIndex, left; }
    [Serializable] class In { public string t, c; public float dx, dy, x, y; public int b, d; }

    static CloudStreamHost I;
    ClientWebSocket ws;
    readonly ConcurrentQueue<string> inbox = new ConcurrentQueue<string>();
    readonly ConcurrentQueue<string> inputs = new ConcurrentQueue<string>();
    readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);

    RTCPeerConnection pc;
    RTCDataChannel channel;
    VideoStreamTrack video;
    AudioStreamTrack audio;
    RenderTexture frame, grab;
    AudioListener tappedListener;
    float lostSince = -1f;
    string warning;

    Keyboard kb;
    Mouse mouse;
    readonly HashSet<Key> keys = new HashSet<Key>();
    MouseState ms;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (!CloudArgs.IsCloud || I != null) return;
        var go = new GameObject("CloudStreamHost");
        DontDestroyOnLoad(go);
        I = go.AddComponent<CloudStreamHost>();
    }

    void Start()
    {
        Application.runInBackground = true;
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = CloudArgs.Fps;
        Screen.SetResolution(Width, Height, FullScreenMode.Windowed);
        AudioListener.volume = 1f;

        kb = InputSystem.AddDevice<Keyboard>("CloudKeyboard");
        mouse = InputSystem.AddDevice<Mouse>("CloudMouse");
        kb.MakeCurrent();
        mouse.MakeCurrent();

        StartCoroutine(WebRTC.Update());
        StartCoroutine(CaptureLoop());
        _ = SignalLoop();
        Debug.Log($"[Cloud] slot {CloudArgs.Slot} on card {CloudArgs.Card} ready");
    }

    // --- signalling ----------------------------------------------------------------------

    async Task SignalLoop()
    {
        string url = $"{CloudArgs.Signal}?role=host&card={Uri.EscapeDataString(CloudArgs.Card)}" +
                     $"&slot={CloudArgs.Slot}&key={Uri.EscapeDataString(CloudArgs.Key)}";
        var buf = new byte[64 * 1024];
        while (true)
        {
            ws = new ClientWebSocket();
            try
            {
                await ws.ConnectAsync(new Uri(url), CancellationToken.None);
                var sb = new StringBuilder();
                while (ws.State == WebSocketState.Open)
                {
                    var r = await ws.ReceiveAsync(new ArraySegment<byte>(buf), CancellationToken.None);
                    if (r.MessageType == WebSocketMessageType.Close) break;
                    sb.Append(Encoding.UTF8.GetString(buf, 0, r.Count));
                    if (!r.EndOfMessage) continue;
                    inbox.Enqueue(sb.ToString());
                    sb.Clear();
                }
            }
            catch (Exception e) { Debug.LogWarning($"[Cloud] signalling: {e.Message}"); }
            if (CloudArgs.PlayerAttached) { inbox.Enqueue("{\"t\":\"end\",\"reason\":\"signal lost\"}"); return; }
            await Task.Delay(3000);
        }
    }

    async void Send(Sig m)
    {
        var w = ws;
        if (w == null || w.State != WebSocketState.Open) return;
        var bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(m));
        await sendLock.WaitAsync();
        try { await w.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None); }
        catch (Exception e) { Debug.LogWarning($"[Cloud] send: {e.Message}"); }
        finally { sendLock.Release(); }
    }

    void Update()
    {
        while (inbox.TryDequeue(out var raw)) Handle(JsonUtility.FromJson<Sig>(raw));
        while (inputs.TryDequeue(out var raw)) Feed(JsonUtility.FromJson<In>(raw));
        TapAudio();

        // the browser vanished without a clean goodbye
        if (pc != null)
        {
            var s = pc.ConnectionState;
            bool bad = s == RTCPeerConnectionState.Failed || s == RTCPeerConnectionState.Disconnected;
            if (!bad) lostSince = -1f;
            else if (lostSince < 0f) lostSince = Time.unscaledTime;
            else if (Time.unscaledTime - lostSince > 10f) Quit("connection lost");
        }
    }

    void Handle(Sig m)
    {
        if (m == null) return;
        switch (m.t)
        {
            case "join":
                CloudArgs.SetPlayerAttached(true);
                StartCoroutine(Offer());
                break;
            case "answer":
                if (pc == null) return;
                var d = new RTCSessionDescription { type = RTCSdpType.Answer, sdp = m.sdp };
                StartCoroutine(Wait(pc.SetRemoteDescription(ref d)));
                break;
            case "ice":
                if (pc == null || string.IsNullOrEmpty(m.candidate)) return;
                pc.AddIceCandidate(new RTCIceCandidate(new RTCIceCandidateInit
                    { candidate = m.candidate, sdpMid = m.sdpMid, sdpMLineIndex = m.sdpMLineIndex }));
                break;
            case "warn":
                warning = $"Cloud session ends in {Mathf.CeilToInt(m.left / 60f)} minutes";
                break;
            case "end":
                Quit(m.reason);
                break;
        }
    }

    IEnumerator Wait(AsyncOperationBase op) { yield return op; if (op.IsError) Debug.LogWarning($"[Cloud] {op.Error.message}"); }

    IEnumerator Offer()
    {
        // the browser reaches us through the card's own TURN server; we only need host candidates
        var cfg = default(RTCConfiguration);
        cfg.iceServers = new RTCIceServer[0];
        pc = new RTCPeerConnection(ref cfg);
        pc.OnIceCandidate = c => Send(new Sig { t = "ice", candidate = c.Candidate, sdpMid = c.SdpMid, sdpMLineIndex = c.SdpMLineIndex ?? 0 });

        var vt = pc.AddTransceiver(video, new RTCRtpTransceiverInit { direction = RTCRtpTransceiverDirection.SendOnly });
        var h264 = RTCRtpSender.GetCapabilities(TrackKind.Video).codecs.Where(c => c.mimeType == "video/H264").ToArray();
        if (h264.Length > 0) vt.SetCodecPreferences(h264);
        var p = vt.Sender.GetParameters();
        foreach (var e in p.encodings) { e.maxBitrate = MaxBitrate; e.maxFramerate = (uint)CloudArgs.Fps; }
        vt.Sender.SetParameters(p);
        if (audio != null)
            pc.AddTransceiver(audio, new RTCRtpTransceiverInit { direction = RTCRtpTransceiverDirection.SendOnly });

        channel = pc.CreateDataChannel("input", new RTCDataChannelInit { ordered = true });
        channel.OnMessage = bytes => inputs.Enqueue(Encoding.UTF8.GetString(bytes));

        var op = pc.CreateOffer();
        yield return op;
        if (op.IsError) { Quit("offer failed: " + op.Error.message); yield break; }
        var desc = op.Desc;
        var set = pc.SetLocalDescription(ref desc);
        yield return set;
        Send(new Sig { t = "offer", sdp = desc.sdp });
    }

    // --- video / audio -------------------------------------------------------------------

    IEnumerator CaptureLoop()
    {
        frame = new RenderTexture(Width, Height, 0, RenderTextureFormat.BGRA32);
        grab = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGB32);
        frame.Create();
        grab.Create();
        video = new VideoStreamTrack(frame);
        audio = new AudioStreamTrack();
        bool flip = SystemInfo.graphicsUVStartsAtTop ^ Environment.GetCommandLineArgs().Contains("-noflip");
        var eof = new WaitForEndOfFrame();
        while (true)
        {
            yield return eof;
            ScreenCapture.CaptureScreenshotIntoRenderTexture(grab);
            if (flip) Graphics.Blit(grab, frame, new Vector2(1f, -1f), new Vector2(0f, 1f));
            else Graphics.Blit(grab, frame);
        }
    }

    // follow the active AudioListener across scene loads
    void TapAudio()
    {
        if (tappedListener != null && tappedListener.isActiveAndEnabled) return;
        tappedListener = FindFirstObjectByType<AudioListener>();
        if (tappedListener != null && tappedListener.GetComponent<CloudAudioTap>() == null)
            tappedListener.gameObject.AddComponent<CloudAudioTap>().host = this;
    }

    internal void OnAudio(float[] data, int channels)
    {
        if (audio != null && CloudArgs.PlayerAttached) audio.SetData(data, channels, AudioSettings.outputSampleRate);
    }

    // --- input ---------------------------------------------------------------------------

    void Feed(In m)
    {
        if (m == null) return;
        switch (m.t)
        {
            case "k":
                var key = KeyFromCode(m.c);
                if (key == Key.None) return;
                if (m.d != 0) keys.Add(key); else keys.Remove(key);
                InputSystem.QueueStateEvent(kb, new KeyboardState(keys.ToArray()));
                break;
            case "m":
                ms.delta = new Vector2(m.dx, -m.dy);
                InputSystem.QueueStateEvent(mouse, ms);
                ms.delta = Vector2.zero;
                break;
            case "p":
                ms.position = new Vector2(Mathf.Clamp01(m.x) * Screen.width, (1f - Mathf.Clamp01(m.y)) * Screen.height);
                InputSystem.QueueStateEvent(mouse, ms);
                break;
            case "b":
                var btn = m.b == 0 ? MouseButton.Left : m.b == 2 ? MouseButton.Right : MouseButton.Middle;
                ms = ms.WithButton(btn, m.d != 0);
                InputSystem.QueueStateEvent(mouse, ms);
                break;
            case "w":
                ms.scroll = new Vector2(0f, -m.dy);
                InputSystem.QueueStateEvent(mouse, ms);
                ms.scroll = Vector2.zero;
                break;
            case "reset":
                keys.Clear();
                ms = default;
                InputSystem.QueueStateEvent(kb, new KeyboardState());
                InputSystem.QueueStateEvent(mouse, ms);
                break;
        }
    }

    // browser KeyboardEvent.code -> Input System Key
    static Key KeyFromCode(string code)
    {
        if (string.IsNullOrEmpty(code)) return Key.None;
        if (code.StartsWith("Key") && code.Length == 4) return Enum.TryParse(code.Substring(3), out Key k1) ? k1 : Key.None;
        if (code.StartsWith("Digit")) return Enum.TryParse("Digit" + code.Substring(5), out Key k2) ? k2 : Key.None;
        switch (code)
        {
            case "ShiftLeft": return Key.LeftShift;
            case "ShiftRight": return Key.RightShift;
            case "ControlLeft": return Key.LeftCtrl;
            case "ControlRight": return Key.RightCtrl;
            case "AltLeft": return Key.LeftAlt;
            case "AltRight": return Key.RightAlt;
            case "ArrowUp": return Key.UpArrow;
            case "ArrowDown": return Key.DownArrow;
            case "ArrowLeft": return Key.LeftArrow;
            case "ArrowRight": return Key.RightArrow;
            case "Escape": return Key.Escape;
            case "Enter": return Key.Enter;
            case "Space": return Key.Space;
            case "Tab": return Key.Tab;
            case "Backspace": return Key.Backspace;
            case "Backquote": return Key.Backquote;
            case "Minus": return Key.Minus;
            case "Equal": return Key.Equals;
        }
        return Enum.TryParse(code, out Key k3) ? k3 : Key.None;   // F1..F12 and the like
    }

    // --- end of session ------------------------------------------------------------------

    void OnGUI()
    {
        if (warning == null) return;
        GUI.color = new Color(1f, 0.4f, 0.3f);
        GUI.Label(new Rect(Screen.width / 2f - 160, 12, 320, 24), warning);
    }

    bool quitting;
    void Quit(string reason)
    {
        if (quitting) return;
        quitting = true;
        Debug.Log($"[Cloud] session over: {reason}");
        CloudArgs.SetPlayerAttached(false);
        try { channel?.Close(); pc?.Close(); } catch { }
        Application.Quit();   // the supervisor restarts a clean instance in this slot
    }
}

// Sits on the active AudioListener and forwards the final mix to the WebRTC audio track.
public class CloudAudioTap : MonoBehaviour
{
    internal CloudStreamHost host;
    void OnAudioFilterRead(float[] data, int channels) { host?.OnAudio(data, channels); }
}
#endif
