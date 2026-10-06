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
    public static string Base = "wss://museum-server.steven-stoyanov12.workers.dev/";
    // first the lobby (it hands out a room in your region, or a place in the queue), then the room itself
    public static string Room;            // e.g. "eu-2"; null until the lobby has assigned one
    public static int QueuePos;           // > 0 while waiting for a free place in your region
    public static bool Queued => Room == null && QueuePos > 0;
    static string Url => Room == null ? Base + "lobby" : Base + "?room=" + Room;
    float lobbyPingT; int roomFails;
    public static Net I;
    [Serializable] public class Msg { public string t, id, to, c, room; public int pos; public float x, y, z, r, p, ts, sp, d, tx, ty, tz; public int cr, sh, al, sl; }   // sl = the crate number the server gave us, plus one (0 = none)

    string myId = ""; float sendT, retryT; bool wasOpen;
    readonly Dictionary<string, RemotePlayer> remotes = new Dictionary<string, RemotePlayer>();
    float lastShot = -10f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot() { if (I != null) return; var g = new GameObject("Net"); g.AddComponent<Net>(); DontDestroyOnLoad(g); }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetS() { I = null; Room = null; QueuePos = 0; }
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
        var root = LocalRoot(); if (root == null) return;
        // cloud instance: stay offline until a browser player is attached, so idle instances don't hold room seats
        if (CloudArgs.IsCloud)
        {
            if (!CloudArgs.PlayerAttached) return;
            if (Room == null && CloudArgs.Room.Length > 0) Room = CloudArgs.Room;
        }
        int st = SockState();
        if (st == 3)
        {
            if (wasOpen) { wasOpen = false; ClearRemotes(); }
            retryT -= Time.unscaledDeltaTime;
            if (retryT <= 0f)
            {
                retryT = Room == null ? 1f : 3f;
                if (Room != null && ++roomFails > 3) { Room = null; roomFails = 0; }   // the room keeps refusing us: ask the lobby again
                SockOpen();
            }
            return;
        }
        if (st == 1) wasOpen = true;
        string m; int guard = 0; while (guard++ < 200 && (m = SockPop()) != null) Handle(m);
        if (Room == null)   // still in the lobby: keep our place in the queue alive
        {
            if (st == 1) { lobbyPingT -= Time.unscaledDeltaTime; if (lobbyPingT <= 0f) { lobbyPingT = 5f; SockSend("ping"); } }
            return;
        }
        if (st == 1) roomFails = 0;
        if (CharacterSelect.Chosen == null) return;   // connected early only to hear which crate is ours; nobody sees us until we have picked a character
        FlushWorld();
        if (st != 1) return;

        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame && !CrateSpawn.Hidden && Cursor.lockState == CursorLockMode.Locked && FirstPersonController.RelockFrame != Time.frameCount) lastShot = Time.unscaledTime;
        sendT -= Time.unscaledDeltaTime;
        if (sendT <= 0f)
        {
            sendT = 1f / 30f;
            var g = root.GetComponent<GazeTarget>();
            var cc = root.GetComponent<CharacterController>();
            float sp = cc != null ? new Vector3(cc.velocity.x, 0f, cc.velocity.z).magnitude : 0f;
            var cam = root.GetComponentInChildren<Camera>();
            float px = cam != null ? cam.transform.eulerAngles.x : 0f; if (px > 180f) px -= 360f;
            var o = new Msg { t = "s", c = CharacterSelect.Chosen, x = root.position.x, y = root.position.y, z = root.position.z, r = root.eulerAngles.y, p = px, ts = Time.unscaledTime, sp = sp,
                cr = (FirstPersonController.Crouching || CrateSpawn.Hidden) ? 1 : 0,   // inside a crate he is always crouched
                sh = (Time.unscaledTime - lastShot < 0.8f) ? 1 : 0, al = (g == null || g.Alive) && !CrateSpawn.Hidden ? 1 : 0 };
            SockSend(JsonUtility.ToJson(o));
        }
    }

    void Handle(string json)
    {
        Msg m; try { m = JsonUtility.FromJson<Msg>(json); } catch (Exception) { return; }
        if (m == null) return;
        switch (m.t)
        {
            case "room": Room = m.room; QueuePos = 0; retryT = 0f; break;   // the lobby found us a room; this socket closes and the room one opens
            case "wait": QueuePos = m.pos; break;
            case "hello": myId = m.id; if (m.sl > 0) CrateSpawn.Assign(m.sl - 1); break;
            case "s":
                RemotePlayer rp;
                if (!remotes.TryGetValue(m.id, out rp) || rp == null) { rp = RemotePlayer.Create(m.id, m.c, LocalRoot()); if (rp == null) return; remotes[m.id] = rp; }
                rp.Apply(m); break;
            case "shot": case "act": pendingWorld.Add(m); FlushWorld(); break;
            case "st": StatueGaze.ApplyRemote(m); break;
            case "sg": case "sf": SniperGuard.ApplyRemote(m); break;
            case "bp": WineBarrel.ApplyPose(m); break;
            case "leave": if (remotes.TryGetValue(m.id, out rp)) { if (rp != null) Destroy(rp.gameObject); remotes.Remove(m.id); } break;
            case "hit":
                if (m.to == myId) { var root = LocalRoot(); var g = root != null ? root.GetComponent<GazeTarget>() : null; if (g != null) g.Hit(m.d); }
                break;
        }
    }

    // world events wait until the street and the museum are both loaded (late joiners get the room's history right away)
    readonly List<Msg> pendingWorld = new List<Msg>();
    static bool WorldReady()
    {
        var o = UnityEngine.SceneManagement.SceneManager.GetSceneByName("Outside"); var i = UnityEngine.SceneManagement.SceneManager.GetSceneByName("Inside");
        return o.IsValid() && o.isLoaded && i.IsValid() && i.isLoaded;
    }
    void FlushWorld()
    {
        if (pendingWorld.Count == 0 || !WorldReady()) return;
        foreach (var m in pendingWorld)
        {
            if (m.t == "shot") SimpleGun.RemoteShot(new Vector3(m.x, m.y, m.z), new Vector3(m.tx, m.ty, m.tz));
            else if (m.t == "act" && m.to == "tip") TippableCover.RemoteTip(m.c, new Vector3(m.x, m.y, m.z));
            else if (m.t == "act" && m.to == "burst") WineBarrel.RemoteBurst(m.c);
        }
        pendingWorld.Clear();
    }

    GUIStyle qStyle;
    void OnGUI()
    {
        if (!Queued) return;
        if (qStyle == null) { qStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, wordWrap = true }; qStyle.normal.textColor = Color.white; }
        qStyle.fontSize = Mathf.Max(22, Screen.height / 28);
        var r = new Rect(Screen.width * 0.2f, Screen.height * 0.08f, Screen.width * 0.6f, Screen.height * 0.12f);
        GUI.color = new Color(0f, 0f, 0f, 0.7f); GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = Color.white;
        GUI.Label(r, "All servers in your region are full\nYou are #" + QueuePos + " in the queue - you will join automatically", qStyle);
    }
    void ClearRemotes() { foreach (var kv in remotes) if (kv.Value != null) Destroy(kv.Value.gameObject); remotes.Clear(); }

    // ---- host: the player with the lowest id runs the shared AI (statues) and physics (falling barrels) for everyone ----
    public bool Online => SockState() == 1 && !string.IsNullOrEmpty(myId);
    public string MyId => myId;
    public bool IsHost
    {
        get
        {
            if (!Online) return true;
            foreach (var k in remotes.Keys) if (string.CompareOrdinal(k, myId) < 0) return false;
            return true;
        }
    }
    public void SendRaw(Msg m) { if (SockState() == 1) SockSend(JsonUtility.ToJson(m)); }

    // ---- world sync: every client replays other players' shots in its own copy of the world, so destruction matches everywhere ----
    public void SendShot(Vector3 from, Vector3 to)
    {
        if (SockState() != 1) return;
        SockSend(JsonUtility.ToJson(new Msg { t = "shot", x = from.x, y = from.y, z = from.z, tx = to.x, ty = to.y, tz = to.z }));
    }
    public void SendAct(string kind, Transform target, Vector3 at)
    {
        if (SockState() != 1 || target == null) return;
        SockSend(JsonUtility.ToJson(new Msg { t = "act", to = kind, c = PathOf(target), x = at.x, y = at.y, z = at.z }));
    }
    // same scene object on every client: scene name + sibling indices down the hierarchy
    public static string PathOf(Transform t)
    {
        var sb = new System.Text.StringBuilder();
        for (var c = t; c != null; c = c.parent) sb.Insert(0, "/" + c.GetSiblingIndex());
        return t.gameObject.scene.name + sb;
    }
    public static Transform FindByPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var parts = path.Split('/'); var sc = UnityEngine.SceneManagement.SceneManager.GetSceneByName(parts[0]);
        if (!sc.IsValid() || !sc.isLoaded || parts.Length < 2) return null;
        var roots = sc.GetRootGameObjects(); int i0; if (!int.TryParse(parts[1], out i0)) return null;
        Transform cur = null; foreach (var r in roots) if (r.transform.GetSiblingIndex() == i0) cur = r.transform;
        for (int k = 2; k < parts.Length && cur != null; k++) { int ix; if (!int.TryParse(parts[k], out ix) || ix >= cur.childCount) return null; cur = cur.GetChild(ix); }
        return cur;
    }

    public void SendHit(string targetId, float dmg)
    {
        if (SockState() != 1) return;
        // Fortnite-style number over the player we hit (RemotePlayer sends 34 for a headshot, ~15-17 for the body)
        RemotePlayer rp;
        if (remotes.TryGetValue(targetId, out rp) && rp != null)
        {
            bool head = dmg >= 30f;
            DamageNumbers.Show(rp.transform.position + Vector3.up * (head ? 1.85f : 1.3f), dmg, head);
        }
        SockSend(JsonUtility.ToJson(new Msg { t = "hit", to = targetId, d = dmg }));
    }
}
