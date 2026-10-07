using UnityEngine;
using UnityEngine.InputSystem;
// Every run starts with the player hidden inside a cover crate, looking out through the cracks between the planks.
// "Press E to escape" pops him out next to it.
//
// The crate state is a hard lock, not a polite request: from the moment this script wakes up until E is pressed the player's body is
// held at the crate centre (controller off, controls off, avatar hidden) and the camera is forced inside the crate right before every
// frame is drawn. Nothing else (physics push, portals, late-loading models, other scripts) can move him out or put his body in the view.
[DefaultExecutionOrder(2000)]
public class CrateSpawn : MonoBehaviour
{
    Transform player, crate; CharacterController pcc; bool inside, started; float t0;
    FirstPersonController fpc; GUIStyle style;
    GameObject interior; Light lamp; Camera pcam; PlayerDeath death; float yaw, pitch, faceYaw; Vector3 camPos; bool lookInit;
    Material wood; static Mesh inwardCube;
    bool initDone; float initT0;
    // geometry of the crate he is in, captured once (never recomputed from objects that may change later)
    Vector3 cCenter, cSize; Quaternion cRot; Bounds cAABB; Collider crateCol; Vector3 spawnPos;
    readonly System.Collections.Generic.HashSet<Behaviour> held = new System.Collections.Generic.HashSet<Behaviour>();
    readonly System.Collections.Generic.HashSet<Renderer> hidden = new System.Collections.Generic.HashSet<Renderer>();

    public static bool Hidden;   // true while the player is hidden inside the crate
    public static int AssignedSlot = -1;   // the crate number the server gave this player (lowest free one in the room), -1 until it has spoken
    static CrateSpawn instance; System.Collections.Generic.List<Transform> crates;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetHidden() { Hidden = false; AssignedSlot = -1; instance = null; }
    // called by Net when the server tells us our crate: nobody else in the room gets the same one
    public static void Assign(int slot) { AssignedSlot = slot; if (instance != null) instance.Reseat(); }

    void Awake() { instance = this; initT0 = Time.time; TryInit(); }   // before every Start and before the first frame is drawn
    void OnDestroy() { if (instance == this) instance = null; }
    void OnEnable() { Application.onBeforeRender += BeforeRender; }
    void OnDisable()
    {
        Application.onBeforeRender -= BeforeRender;
        if (inside && pcc != null) pcc.enabled = true;   // switched off from outside (death): give the controller back
    }

    // ---------- getting in ----------
    bool TryInit()
    {
        if (initDone) return true;
        var p = GameObject.Find("Player"); if (!p) return false;
        player = p.transform; fpc = p.GetComponent<FirstPersonController>(); death = p.GetComponent<PlayerDeath>(); pcam = p.GetComponentInChildren<Camera>(); pcc = p.GetComponent<CharacterController>();
        var root = GameObject.Find("CoverCrates");
        var list = new System.Collections.Generic.List<Transform>();
        if (root) foreach (Transform c in root.transform) if (c.gameObject.activeInHierarchy && System.Text.RegularExpressions.Regex.IsMatch(c.name, @"^Crate_\d+$") && c.GetComponentInChildren<Renderer>()) list.Add(c);
        list.Sort((x, y) => CrateNo(x).CompareTo(CrateNo(y)));
        crates = list;
        if (list.Count > 0)
        {
            Physics.SyncTransforms();
            float yw;
            // the server hands every player a different crate number; until it has spoken, take the best-looking free-standing crate
            Transform pick = AssignedSlot >= 0 ? list[AssignedSlot % list.Count] : PickCrate(list, out yw);
            EnterCrate(pick);
        }
        else
        {
            if (Time.time - initT0 < 2f) return false;   // the street may still be arriving: wait a moment, then never leave him standing in the open
            MakeEmergencyCrate(); faceYaw = player.eulerAngles.y; SetSpawn();
        }
        initDone = true;
        inside = true; Hidden = true; t0 = Time.time;
        LockPlayer(true); HoldControls(); HideAvatar(); PlaceCamera();
        return true;
    }
    static int CrateNo(Transform t) { int n; return int.TryParse(t.name.Substring(6), out n) ? n : 0; }
    void SetSpawn()
    {
        spawnPos = new Vector3(cAABB.center.x, cAABB.min.y + 1.05f, cAABB.center.z);
        player.rotation = Quaternion.Euler(0f, faceYaw, 0f);
        BuildInterior();
        camPos = cCenter + cRot * new Vector3(0, -cSize.y * 0.5f + 0.78f, 0);
    }
    void EnterCrate(Transform c)
    {
        crate = c; bool v; ScoreCrate(c, out faceYaw, out v);   // the direction with the most to look at
        CaptureGeometry(crate);
        crateCol = crate.GetComponentInChildren<Collider>(); if (crateCol) crateCol.enabled = false;
        foreach (var rr in crate.GetComponentsInChildren<Renderer>()) rr.enabled = false;
        SetSpawn();
    }
    // the server's answer arrived after we had already picked a crate: move to the one that is ours (normally this happens while the select screen is still up)
    void Reseat()
    {
        if (!inside || !initDone || crates == null || crates.Count == 0 || AssignedSlot < 0) return;
        var target = crates[AssignedSlot % crates.Count]; if (target == crate) return;
        if (crateCol) crateCol.enabled = true;
        if (crate) foreach (var rr in crate.GetComponentsInChildren<Renderer>()) rr.enabled = true;
        if (interior) Destroy(interior);
        EnterCrate(target);
        LockPlayer(false); PlaceCamera();
    }
    void CaptureGeometry(Transform c)
    {
        var rs = c.GetComponentsInChildren<Renderer>(); cAABB = rs[0].bounds; foreach (var r in rs) cAABB.Encapsulate(r.bounds);
        var bc = c.GetComponentInChildren<BoxCollider>();
        if (bc) { var ls = bc.transform.lossyScale; cSize = Vector3.Scale(bc.size, new Vector3(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z))); cCenter = bc.transform.TransformPoint(bc.center); cRot = bc.transform.rotation; }
        else { cSize = cAABB.size; cCenter = cAABB.center; cRot = c.rotation; }
    }
    void MakeEmergencyCrate()   // no crate to be found: put a box around him right where he stands
    {
        float floor = player.position.y - 1.0f;
        foreach (var h in Physics.RaycastAll(player.position + Vector3.up, Vector3.down, 6f, ~0, QueryTriggerInteraction.Ignore)) if (!h.collider.transform.IsChildOf(player)) { floor = Mathf.Max(floor, h.point.y); break; }
        cSize = new Vector3(1.16f, 1.22f, 1.16f); cRot = Quaternion.Euler(0f, player.eulerAngles.y, 0f);
        cCenter = new Vector3(player.position.x, floor + cSize.y * 0.5f, player.position.z); cAABB = new Bounds(cCenter, cSize);
    }

    // ---------- choosing the crate: free inside, on the ground, with something to look at ----------
    Transform PickCrate(System.Collections.Generic.List<Transform> list, out float yawOut)
    {
        int n = list.Count; var sc = new float[n]; var yw = new float[n]; var ok = new bool[n]; float mx = 0f; bool anyOk = false;
        for (int i = 0; i < n; i++) { sc[i] = ScoreCrate(list[i], out yw[i], out ok[i]); if (ok[i]) { anyOk = true; if (sc[i] > mx) mx = sc[i]; } }
        if (!anyOk) for (int i = 0; i < n; i++) if (sc[i] > mx) mx = sc[i];
        var good = new System.Collections.Generic.List<int>();
        for (int i = 0; i < n; i++) if ((!anyOk || ok[i]) && sc[i] >= mx * 0.7f) good.Add(i);
        if (good.Count == 0) good.Add(0);
        int k = good[Random.Range(0, good.Count)]; yawOut = yw[k]; return list[k];
    }
    float ScoreCrate(Transform c, out float bestYaw, out bool valid)
    {
        bestYaw = 0f; Vector3 center, S; Quaternion rot;
        var bc = c.GetComponentInChildren<BoxCollider>();
        if (bc) { var ls = bc.transform.lossyScale; center = bc.transform.TransformPoint(bc.center); S = Vector3.Scale(bc.size, new Vector3(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z))); rot = bc.transform.rotation; }
        else { var rs = c.GetComponentsInChildren<Renderer>(); var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); center = b.center; S = b.size; rot = c.rotation; }
        Vector3 eye = center + rot * new Vector3(0, -S.y * 0.5f + 0.78f, 0);
        // 1) the inside must be empty (a crate sunk into a wall or another prop is no hiding place)
        bool clear = true;
        foreach (var o in Physics.OverlapBox(center, S * 0.5f - Vector3.one * 0.08f, rot, ~0, QueryTriggerInteraction.Ignore)) { var t = o.transform; if (t.IsChildOf(c) || t.IsChildOf(player)) continue; clear = false; break; }
        // 2) it must stand on something
        bool ground = false; foreach (var h in Physics.RaycastAll(center, Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore)) if (!h.collider.transform.IsChildOf(c)) { ground = true; break; }
        // 3) and have open space to look into: best 90-degree window around the crate
        const int N = 24; var free = new float[N];
        for (int d = 0; d < N; d++) free[d] = FreeDist(eye, Quaternion.Euler(0f, d * 15f, 0f) * Vector3.forward, 10f, c);
        float best = -1f;
        for (int d = 0; d < N; d++)
        {
            float s = 0f; for (int o = -3; o <= 3; o++) s += free[(d + o + N) % N] * (1f - Mathf.Abs(o) / 4f);
            if (s > best) { best = s; bestYaw = d * 15f; }
        }
        best /= 4f;
        valid = clear && ground && best >= 1.5f;
        return best;
    }
    float FreeDist(Vector3 o, Vector3 dir, float len, Transform self)
    {
        float best = len;
        foreach (var h in Physics.RaycastAll(o, dir, len, ~0, QueryTriggerInteraction.Ignore))
        { var t = h.collider.transform; if (t.IsChildOf(self) || (player != null && t.IsChildOf(player))) continue; if (h.distance < best) best = h.distance; }
        return best;
    }

    // ---------- the hard lock ----------
    void LockPlayer(bool first)
    {
        if (pcc != null && pcc.enabled) pcc.enabled = false;                 // no physics can push a body that has no controller
        if ((player.position - spawnPos).sqrMagnitude > 1e-6f) { player.position = spawnPos; if (!first) Physics.SyncTransforms(); }
    }
    void HoldControls()   // controls and guns stay off, even if something (the select screen) switches them back on
    {
        foreach (var m in player.GetComponents<MonoBehaviour>()) if (m != null && m.enabled && (m is FirstPersonController || m.GetType().Name.Contains("Gun"))) { m.enabled = false; held.Add(m); }
    }
    Renderer[] avatarR; float avatarT; int avatarCount = -1;
    void HideAvatar()   // the avatar must never show, including a model that loads in late
    {
        int hc = player.hierarchyCount;
        if (avatarR == null || hc != avatarCount || Time.unscaledTime - avatarT > 0.1f) { avatarR = player.GetComponentsInChildren<Renderer>(true); avatarCount = hc; avatarT = Time.unscaledTime; }
        for (int i = 0; i < avatarR.Length; i++) { var r = avatarR[i]; if (r != null && r.enabled) { r.enabled = false; hidden.Add(r); } }
    }
    void PlaceCamera()
    {
        if (death == null && player != null) death = player.GetComponent<PlayerDeath>();   // added a moment after scene load
        if (CrateCutscene.Active || (death != null && death.dead)) return;   // the cutscene and the death camera own the view
        var cam = pcam != null ? pcam : Camera.main; if (cam == null) return;
        cam.transform.SetPositionAndRotation(camPos, Quaternion.Euler(pitch, faceYaw + yaw, 0f));
        cam.nearClipPlane = 0.02f;
    }
    void BeforeRender() { if (!inside) return; LockPlayer(false); HideAvatar(); PlaceCamera(); }   // last word before the frame is drawn

    bool Ready()   // the character-select screen is gone
    {
        if (CrateCutscene.Active || Net.Queued) return false;   // waiting in the queue for a free place: stay hidden in the crate
        if (CharacterSelect.Chosen != null) return true;
        if (!started) { if (FindFirstObjectByType<CharacterSelect>() != null) started = true; else if (Time.time - t0 > 1.5f) started = true; else return false; }
        return FindFirstObjectByType<CharacterSelect>() == null;
    }

    // ---------- the inside of the crate: planks with real gaps, the outside shows through ----------
    static Mesh InwardCube()
    {
        if (inwardCube != null) return inwardCube;
        var prim = GameObject.CreatePrimitive(PrimitiveType.Cube); var src = prim.GetComponent<MeshFilter>().sharedMesh; Destroy(prim);
        var m = new Mesh { name = "InwardCube" }; m.vertices = src.vertices; m.uv = src.uv;
        var n = src.normals; for (int i = 0; i < n.Length; i++) n[i] = -n[i]; m.normals = n;
        var t = src.triangles; for (int i = 0; i < t.Length; i += 3) { int a = t[i]; t[i] = t[i + 2]; t[i + 2] = a; } m.triangles = t; m.RecalculateBounds();
        return inwardCube = m;
    }
    Texture2D WoodTex()
    {
        int W = 128, H = 128; var tex = new Texture2D(W, H, TextureFormat.RGB24, true);
        for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
        {
            float grain = 0.5f + 0.5f * Mathf.Sin(y * 0.55f + Mathf.PerlinNoise(x * 0.05f, y * 0.12f) * 6f);
            float n = Mathf.PerlinNoise(x * 0.12f, y * 0.3f);
            float v = 0.16f + 0.14f * grain + 0.08f * n;
            tex.SetPixel(x, y, new Color(v * 1.05f, v * 0.72f, v * 0.45f));
        }
        tex.Apply(); tex.wrapMode = TextureWrapMode.Repeat; return tex;
    }
    void BuildInterior()
    {
        Vector3 S = cSize, center = cCenter; Quaternion rot = cRot;
        interior = new GameObject("CrateInterior"); interior.transform.SetPositionAndRotation(center, rot);
        var sh = Shader.Find("Universal Render Pipeline/Lit"); if (sh == null) sh = Shader.Find("Standard");
        wood = new Material(sh); var tex = WoodTex();
        if (wood.HasProperty("_BaseMap")) wood.SetTexture("_BaseMap", tex); if (wood.HasProperty("_MainTex")) wood.SetTexture("_MainTex", tex);
        if (wood.HasProperty("_Smoothness")) wood.SetFloat("_Smoothness", 0.05f); if (wood.HasProperty("_Glossiness")) wood.SetFloat("_Glossiness", 0.05f);
        const float t = 0.035f, inset = 0.03f; int N = 6; float fill = 0.84f;
        float hx = S.x * 0.5f - inset, hy = S.y * 0.5f - inset, hz = S.z * 0.5f - inset;
        // four walls: horizontal planks with gaps between them
        for (int i = 0; i < N; i++)
        {
            float pitch = (2f * hy) / N, h = pitch * fill, y = -hy + pitch * (i + 0.5f);
            Plank(new Vector3(0, y, hz), new Vector3(2f * hx, h, t)); Plank(new Vector3(0, y, -hz), new Vector3(2f * hx, h, t));
            Plank(new Vector3(hx, y, 0), new Vector3(t, h, 2f * hz)); Plank(new Vector3(-hx, y, 0), new Vector3(t, h, 2f * hz));
        }
        // lid: planks with cracks that let beams of light in
        int NL = 7; for (int i = 0; i < NL; i++) { float pitch = (2f * hx) / NL, w = pitch * 0.78f, x = -hx + pitch * (i + 0.5f); Plank(new Vector3(x, hy, 0), new Vector3(w, t, 2f * hz)); }
        // corner posts and floor
        foreach (var sx in new float[] { -1, 1 }) foreach (var sz in new float[] { -1, 1 }) Plank(new Vector3(sx * (hx - 0.03f), 0, sz * (hz - 0.03f)), new Vector3(0.07f, 2f * hy, 0.07f));
        Plank(new Vector3(0, -hy, 0), new Vector3(2f * hx, t, 2f * hz), true);
        var lg = new GameObject("CrateLamp"); lg.transform.SetParent(interior.transform, false); lg.transform.localPosition = new Vector3(0, 0.1f, 0);
        lamp = lg.AddComponent<Light>(); lamp.type = LightType.Point; lamp.range = 2.2f; lamp.intensity = 0.35f; lamp.color = new Color(1f, 0.8f, 0.55f); lamp.shadows = LightShadows.None;
    }
    void Plank(Vector3 localPos, Vector3 size, bool floor = false)
    {
        var g = new GameObject("Plank"); g.transform.SetParent(interior.transform, false); g.transform.localPosition = localPos; g.transform.localScale = size;
        g.AddComponent<MeshFilter>().sharedMesh = InwardCube(); var r = g.AddComponent<MeshRenderer>(); r.sharedMaterial = wood; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; r.receiveShadows = true;
    }

    // ---------- running ----------
    void Update()
    {
        if (!initDone) { TryInit(); return; }
        if (!inside) return;
        LockPlayer(false); HoldControls(); HideAvatar();
        if (!Ready()) return;
        lookInit = true;
        if (Mouse.current != null) { var d = Mouse.current.delta.ReadValue(); yaw = Mathf.Clamp(yaw + d.x * 0.12f, -80f, 80f); pitch = Mathf.Clamp(pitch - d.y * 0.12f, -50f, 50f); }
        if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame) Escape();
    }
    void LateUpdate()
    {
        if (!inside) return;
        LockPlayer(false); HideAvatar(); PlaceCamera();
    }
    void Escape()
    {
        var b = cAABB; Vector3[] dirs = { Vector3.forward, Vector3.back, Vector3.right, Vector3.left };
        Vector3 best = dirs[0]; float bestFree = -1f;
        foreach (var d in dirs)   // exit on a side with the most free space
        {
            float reach = Mathf.Abs(Vector3.Dot(b.extents, new Vector3(Mathf.Abs(d.x), 0, Mathf.Abs(d.z))));
            var from = new Vector3(b.center.x, b.min.y + 0.9f, b.center.z) + d * (reach + 0.5f);
            float free = Physics.SphereCast(from, 0.3f, d, out var h, 3f, ~0, QueryTriggerInteraction.Ignore) ? h.distance : 3f;
            if (Physics.CheckSphere(from, 0.3f, ~0, QueryTriggerInteraction.Ignore)) free = 0f;
            if (free > bestFree) { bestFree = free; best = d; }
        }
        float r = Mathf.Abs(Vector3.Dot(b.extents, new Vector3(Mathf.Abs(best.x), 0, Mathf.Abs(best.z))));
        var outPos = new Vector3(b.center.x, b.min.y + 1.05f, b.center.z) + best * (r + 0.7f);
        float yawOut = Quaternion.LookRotation(best).eulerAngles.y;
        if (crateCol) crateCol.enabled = true; if (crate) foreach (var rr in crate.GetComponentsInChildren<Renderer>()) rr.enabled = true;
        foreach (var rr in hidden) if (rr) rr.enabled = true; hidden.Clear(); avatarR = null;
        if (pcc != null) pcc.enabled = true;
        foreach (var mb in held) if (mb) mb.enabled = true; held.Clear();
        if (interior) Destroy(interior);
        if (fpc) fpc.Teleport(outPos, yawOut); else player.position = outPos;
        inside = false; Hidden = false;
    }
    Texture2D bgTex;
    void OnGUI()
    {
        if (!inside || !Ready()) return;
        int fs = Mathf.Max(28, Mathf.RoundToInt(Screen.height / 20f));
        if (style == null || style.fontSize != fs) { style = new GUIStyle(GUI.skin.label) { fontSize = fs, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, wordWrap = false, clipping = TextClipping.Overflow }; style.normal.textColor = Color.white; }
        if (bgTex == null) { bgTex = new Texture2D(1, 1); bgTex.SetPixel(0, 0, Color.white); bgTex.Apply(); }
        float w = Mathf.Min(Screen.width * 0.9f, fs * 14f), h = fs * 1.9f;
        var rect = new Rect((Screen.width - w) * 0.5f, Screen.height * 0.78f, w, h);   // centred, bottom of the screen
        GUI.color = new Color(0f, 0f, 0f, 0.65f); GUI.DrawTexture(rect, bgTex);
        GUI.color = Color.white; GUI.Label(rect, "Press  E  to escape", style);
    }
}
