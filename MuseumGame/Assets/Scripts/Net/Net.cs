using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
#if !(UNITY_WEBGL && !UNITY_EDITOR)
using System.Net.WebSockets; using System.Text; using System.Threading; using System.Threading.Tasks; using System.Collections.Concurrent;
#else
using System.Runtime.InteropServices;
#endif

// Multiplayer client (Cloudflare Durable Object relay, up to 20 players per room).
// Sends this player's pose ~15x/s, shows everyone else, and turns pistol hits on other players into damage messages.
public class Net : MonoBehaviour
{
    public static string Url = "wss://museum-server.steven-stoyanov12.workers.dev/?room=main";
    public static Net I;
    [Serializable] public class Msg { public string t, id, to, c; public float x, y, z, r, sp, d; public int cr, sh, al; }

    string myId = ""; float sendT, retryT; bool wasOpen;
    readonly Dictionary<string, RemotePlayer> remotes = new Dictionary<string, RemotePlayer>();
    float lastShot = -10f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot() { if (I != null) return; var g = new GameObject("Net"); g.AddComponent<Net>(); DontDestroyOnLoad(g); }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetS() { I = null; }
    void Awake() { I = this; }

    // ---- socket layer ----
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] static extern void WS_Open(string url);
    [DllImport("__Internal")] static extern int WS_State();
    [DllImport("__Internal")] static extern void WS_Send(string msg);
    [DllImport("__Internal")] static extern IntPtr WS_Pop();
    [DllImport("__Internal")] static extern void WS_Free(IntPtr p);
    void SockOpen() { WS_Open(Url); }
    int SockState() { return WS_State(); }
    void SockSend(string s) { WS_Send(s); }
    string SockPop() { var p = WS_Pop(); if (p == IntPtr.Zero) return null; var s = Marshal.PtrToStringUTF8(p); WS_Free(p); return s; }
#else
    ClientWebSocket ws; readonly ConcurrentQueue<string> inbox = new ConcurrentQueue<string>(); Task sendChain = Task.CompletedTask;
    void SockOpen()
    {
        var w = new ClientWebSocket(); ws = w; string url = Url;
        Task.Run(async () =>
        {
            try
            {
                await w.ConnectAsync(new Uri(url), CancellationToken.None);
                var buf = new byte[8192];
                while (w.State == WebSocketState.Open)
                {
                    var sb = new StringBuilder(); WebSocketReceiveResult r;
                    do { r = await w.ReceiveAsync(new ArraySegment<byte>(buf), CancellationToken.None); if (r.MessageType == WebSocketMessageType.Close) return; sb.Append(Encoding.UTF8.GetString(buf, 0, r.Count)); } while (!r.EndOfMessage);
                    inbox.Enqueue(sb.ToString());
                }
            }
            catch (Exception) { }
        });
    }
    int SockState() { if (ws == null) return 3; switch (ws.State) { case WebSocketState.Connecting: return 0; case WebSocketState.Open: return 1; default: return 3; } }
    void SockSend(string s)
    {
        var w = ws; if (w == null || w.State != WebSocketState.Open) return;
        var b = Encoding.UTF8.GetBytes(s);
        sendChain = sendChain.ContinueWith(_ => { try { if (w.State == WebSocketState.Open) w.SendAsync(new ArraySegment<byte>(b), WebSocketMessageType.Text, true, CancellationToken.None).Wait(); } catch (Exception) { } });
    }
    string SockPop() { string s; return inbox.TryDequeue(out s) ? s : null; }
#endif

    // ---- game side ----
    Transform LocalRoot() { var f = FindFirstObjectByType<FirstPersonController>(); return f != null ? f.transform : null; }

    void Update()
    {
        if (CharacterSelect.Chosen == null) return;
        var root = LocalRoot(); if (root == null) return;
        int st = SockState();
        if (st == 3) { if (wasOpen) { wasOpen = false; ClearRemotes(); } retryT -= Time.unscaledDeltaTime; if (retryT <= 0f) { retryT = 3f; SockOpen(); } return; }
        if (st == 1) wasOpen = true;
        string m; int guard = 0; while (guard++ < 200 && (m = SockPop()) != null) Handle(m);
        if (st != 1) return;

        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame && !CrateSpawn.Hidden) lastShot = Time.unscaledTime;
        sendT -= Time.unscaledDeltaTime;
        if (sendT <= 0f)
        {
            sendT = 1f / 15f;
            var g = root.GetComponent<GazeTarget>();
            var cc = root.GetComponent<CharacterController>();
            float sp = cc != null ? new Vector3(cc.velocity.x, 0f, cc.velocity.z).magnitude : 0f;
            var o = new Msg { t = "s", c = CharacterSelect.Chosen, x = root.position.x, y = root.position.y, z = root.position.z, r = root.eulerAngles.y, sp = sp,
                cr = FirstPersonController.Crouching ? 1 : 0, sh = (Time.unscaledTime - lastShot < 0.8f) ? 1 : 0, al = (g == null || g.Alive) && !CrateSpawn.Hidden ? 1 : 0 };
            SockSend(JsonUtility.ToJson(o));
        }
    }

    void Handle(string json)
    {
        Msg m; try { m = JsonUtility.FromJson<Msg>(json); } catch (Exception) { return; }
        if (m == null) return;
        switch (m.t)
        {
            case "hello": myId = m.id; break;
            case "s":
                RemotePlayer rp;
                if (!remotes.TryGetValue(m.id, out rp) || rp == null) { rp = RemotePlayer.Create(m.id, m.c, LocalRoot()); if (rp == null) return; remotes[m.id] = rp; }
                rp.Apply(m); break;
            case "leave": if (remotes.TryGetValue(m.id, out rp)) { if (rp != null) Destroy(rp.gameObject); remotes.Remove(m.id); } break;
            case "hit":
                if (m.to == myId) { var root = LocalRoot(); var g = root != null ? root.GetComponent<GazeTarget>() : null; if (g != null) g.Hit(m.d); }
                break;
        }
    }

    void ClearRemotes() { foreach (var kv in remotes) if (kv.Value != null) Destroy(kv.Value.gameObject); remotes.Clear(); }

    public void SendHit(string targetId, float dmg)
    {
        if (SockState() != 1) return;
        SockSend(JsonUtility.ToJson(new Msg { t = "hit", to = targetId, d = dmg }));
    }
}
