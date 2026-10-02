using UnityEngine;
using UnityEngine.InputSystem;
// Every run starts with the player hidden inside a random cover crate, looking out through the cracks between the planks.
// "Press E to escape" pops him out next to it.
[DefaultExecutionOrder(2000)]
public class CrateSpawn : MonoBehaviour
{
    Transform player, crate; Collider crateCol; bool inside, started; float t0;
    FirstPersonController fpc; Behaviour[] held; GUIStyle style;
    GameObject interior; Light lamp; Renderer[] hiddenRenderers; Camera cam; float yaw, pitch, baseYaw; Vector3 camPos; bool lookInit;
    Material wood; static Mesh inwardCube;

    void Start()
    {
        var p = GameObject.Find("Player"); var root = GameObject.Find("CoverCrates");
        if (!p || !root) { enabled = false; return; }
        player = p.transform; fpc = p.GetComponent<FirstPersonController>();
        var list = new System.Collections.Generic.List<Transform>();
        foreach (Transform c in root.transform) if (c.gameObject.activeInHierarchy && System.Text.RegularExpressions.Regex.IsMatch(c.name, @"^Crate_\d+$") && c.GetComponentInChildren<Renderer>()) list.Add(c);
        if (list.Count == 0) { enabled = false; return; }
        crate = list[Random.Range(0, list.Count)]; crateCol = crate.GetComponentInChildren<Collider>();
        var b = CrateBounds(); if (crateCol) crateCol.enabled = false; foreach (var rr in crate.GetComponentsInChildren<Renderer>()) rr.enabled = false;
        var cc = p.GetComponent<CharacterController>(); if (cc) cc.enabled = false;
        spawnPos = new Vector3(b.center.x, b.min.y + 1.05f, b.center.z); player.position = spawnPos; if (cc) cc.enabled = true; Physics.SyncTransforms();
        inside = true; Hidden = true; t0 = Time.time;
        BuildInterior();
    }
    Vector3 spawnPos;
    void PinToCrate() { if ((player.position - spawnPos).sqrMagnitude < 0.25f) return; var cc = player.GetComponent<CharacterController>(); if (cc) cc.enabled = false; player.position = spawnPos; if (cc) cc.enabled = true; Physics.SyncTransforms(); }   // nothing may carry him out of the crate
    Bounds CrateBounds() { var rs = crate.GetComponentsInChildren<Renderer>(); var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b; }
    bool Ready()   // the character-select screen is gone
    {
        if (CrateCutscene.Active) return false;
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
        int W = 128, H = 128; var tex = new Texture2D(W, H, TextureFormat.RGB24, true); var rnd = new System.Random(7);
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
        var bc = crate.GetComponentInChildren<BoxCollider>();
        Vector3 S; Vector3 center; Quaternion rot = crate.rotation;
        if (bc) { var ls = bc.transform.lossyScale; S = Vector3.Scale(bc.size, new Vector3(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z))); center = bc.transform.TransformPoint(bc.center); rot = bc.transform.rotation; }
        else { var b = CrateBounds(); S = b.size; center = b.center; }
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
        camPos = center + rot * new Vector3(0, -S.y * 0.5f + 0.78f, 0);
    }
    void Plank(Vector3 localPos, Vector3 size, bool floor = false)
    {
        var g = new GameObject("Plank"); g.transform.SetParent(interior.transform, false); g.transform.localPosition = localPos; g.transform.localScale = size;
        g.AddComponent<MeshFilter>().sharedMesh = InwardCube(); var r = g.AddComponent<MeshRenderer>(); r.sharedMaterial = wood; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; r.receiveShadows = true;
    }

    // ---------- state ----------
    public static bool Hidden;   // true while the player is hidden inside the crate
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetHidden() { Hidden = false; }
    void Update()
    {
        if (!inside) return;
        PinToCrate();
        if (!Ready()) return;
        if (!lookInit) { baseYaw = player.eulerAngles.y; yaw = 0f; pitch = 0f; lookInit = true; }   // camera state never depends on the lock-up below
        if (held == null)   // lock movement + shooting while hidden, hide the avatar
        {
            baseYaw = player.eulerAngles.y; yaw = 0f; pitch = 0f; lookInit = true;   // the crate view never depends on anything below succeeding
            var l = new System.Collections.Generic.List<Behaviour>();
            foreach (var m in player.GetComponents<MonoBehaviour>()) if (m != null && m.enabled && (m is FirstPersonController || m.GetType().Name.Contains("Gun"))) l.Add(m);
            foreach (var m in l) m.enabled = false; held = l.ToArray();
            try { var rl = new System.Collections.Generic.List<Renderer>(); foreach (var r in player.GetComponentsInChildren<Renderer>()) if (r != null && r.enabled) { r.enabled = false; rl.Add(r); } hiddenRenderers = rl.ToArray(); } catch (System.Exception) { }
        }
        foreach (var m in player.GetComponents<MonoBehaviour>()) if (m != null && m.enabled && (m is FirstPersonController || m.GetType().Name.Contains("Gun"))) { m.enabled = false; extraHeld.Add(m); }   // keep him locked even if something re-enables the controls
        foreach (var r in player.GetComponentsInChildren<Renderer>(true)) if (r != null && r.enabled) { r.enabled = false; extraHidden.Add(r); }   // the avatar must never show through the planks
        if (Mouse.current != null) { var d = Mouse.current.delta.ReadValue(); yaw = Mathf.Clamp(yaw + d.x * 0.12f, -80f, 80f); pitch = Mathf.Clamp(pitch - d.y * 0.12f, -50f, 50f); }
        if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame) Escape();
    }
    void LateUpdate()
    {
        if (!inside || !lookInit) return;
        cam = Camera.main; if (cam == null) return;   // looked up every frame: the main camera can change
        cam.transform.SetPositionAndRotation(camPos, Quaternion.Euler(pitch, baseYaw + yaw, 0f));
        cam.nearClipPlane = 0.02f;
    }
    void Escape()
    {
        var b = CrateBounds(); Vector3[] dirs = { Vector3.forward, Vector3.back, Vector3.right, Vector3.left };
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
        if (hiddenRenderers != null) foreach (var rr in hiddenRenderers) if (rr) rr.enabled = true;
        foreach (var rr in extraHidden) if (rr) rr.enabled = true; extraHidden.Clear();
        foreach (var mb in extraHeld) if (mb) mb.enabled = true; extraHeld.Clear();
        if (held != null) foreach (var m in held) if (m) m.enabled = true;
        if (interior) Destroy(interior);
        if (fpc) fpc.Teleport(outPos, yawOut);
        inside = false; Hidden = false; held = null; hiddenRenderers = null;
    }
    readonly System.Collections.Generic.List<Behaviour> extraHeld = new System.Collections.Generic.List<Behaviour>();
    Texture2D bgTex; readonly System.Collections.Generic.List<Renderer> extraHidden = new System.Collections.Generic.List<Renderer>();
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
