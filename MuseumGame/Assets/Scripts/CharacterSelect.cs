using UnityEngine;

// Very simple "choose your character" screen shown when the scene starts.
// White background, characters above their names, "CHOOSE YOUR TRENCHER" at the bottom.
// Click a card (or press 1-8) to pick and start.
// A card shows a 3D model when Assets/Resources/SelectModels/<name>.prefab exists (cented, tjr so far).
public class CharacterSelect : MonoBehaviour
{
    public static readonly string[] Names = { "cented", "orangie", "flames", "ramset", "brez", "tjr", "yolo", "cupsey" };
    public static string Chosen;
    public static int ChosenIndex = -1;
    public static System.Action OnChosen;
    int hover = -1;
    GUIStyle nameStyle, titleStyle;
    static Texture2D white, glow;

    RenderTexture[] rts;
    Transform stage;
    int renderFrames;
    // hover: the hovered character plays the menu idle animation and gets an outline
    Animator[] anims; UnityEngine.Playables.PlayableGraph[] graphs; bool[] graphOn;
    Transform[][] boneT; Vector3[][] boneP; Quaternion[][] boneR;
    System.Collections.Generic.Dictionary<string, Transform>[] boneMap; Transform[] instT;
    int playing = -1; AnimationClip menuClip; Texture2D outlineTex; Color32[] outPx, maskBuf, tmpBuf; Texture2D readTex;
    const int RW = 400, RH = 540, OutR = 5;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Chosen = null; ChosenIndex = -1; OnChosen = null; }

    Behaviour[] frozen;

    void Start()
    {
        BuildModels();
        // freeze the player + gun while the screen is open
        var list = new System.Collections.Generic.List<Behaviour>();
        var fpc = FindFirstObjectByType<FirstPersonController>();
        if (fpc != null) foreach (var b in fpc.GetComponents<MonoBehaviour>()) if (b != null && b.enabled && b != this) list.Add(b);
        foreach (var b in list) b.enabled = false;
        frozen = list.ToArray();
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
    }

    // one small off-screen "photo studio" per character, rendered into a RenderTexture for its card
    void BuildModels()
    {
        rts = new RenderTexture[Names.Length];
        anims = new Animator[Names.Length]; graphs = new UnityEngine.Playables.PlayableGraph[Names.Length]; graphOn = new bool[Names.Length];
        boneT = new Transform[Names.Length][]; boneP = new Vector3[Names.Length][]; boneR = new Quaternion[Names.Length][];
        Baked.Load(); boneMap = new System.Collections.Generic.Dictionary<string, Transform>[Names.Length]; instT = new Transform[Names.Length];
        foreach (var c in Res.LoadAll<AnimationClip>("SelectModels/Pistol_Idle")) if (!c.name.StartsWith("__preview")) menuClip = c;
        stage = new GameObject("SelectStage").transform;
        stage.position = new Vector3(6000f, 0f, 6000f);

        var key = new GameObject("Key").AddComponent<Light>(); key.transform.SetParent(stage, false);
        key.type = LightType.Directional; key.intensity = 1.6f; key.shadows = LightShadows.None;
        key.transform.rotation = Quaternion.Euler(30f, 160f, 0f);               // from the front, slightly above
        var fill = new GameObject("Fill").AddComponent<Light>(); fill.transform.SetParent(stage, false);
        fill.type = LightType.Directional; fill.intensity = 0.7f; fill.shadows = LightShadows.None;
        fill.transform.rotation = Quaternion.Euler(10f, 230f, 0f);

        for (int i = 0; i < Names.Length; i++)
        {
            var prefab = Res.Load<GameObject>("SelectModels/" + Names[i]);
            if (prefab == null) continue;
            var pos = stage.position + new Vector3(i * 4f, 0f, 0f);
            var inst = Instantiate(prefab, pos, Quaternion.Euler(0f, 25f, 0f), stage);     // mostly facing the camera
            FixSleeves(inst, Names[i]);
            foreach (var a in inst.GetComponentsInChildren<Animator>()) { a.enabled = false; a.applyRootMotion = false; if (anims[i] == null) anims[i] = a; }   // neutral pose until hovered
            instT[i] = inst.transform; boneMap[i] = new System.Collections.Generic.Dictionary<string, Transform>(); foreach (var tt in inst.GetComponentsInChildren<Transform>(true)) boneMap[i][tt.name] = tt;
            foreach (var smr in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true)) { smr.updateWhenOffscreen = true; smr.forceMatrixRecalculationPerRender = true; }   // keep every mesh in sync with the baked pose
            AttachGun(inst, i);                    // while still in the bind pose
            if (Baked.names != null) Baked.Apply(inst.transform, boneMap[i], 0, Names[i]);   // neutral standing pose = first frame of the baked idle
            else if (menuClip != null) menuClip.SampleAnimation(inst, 0f);
            boneT[i] = inst.GetComponentsInChildren<Transform>(true); boneP[i] = new Vector3[boneT[i].Length]; boneR[i] = new Quaternion[boneT[i].Length];
            for (int b = 0; b < boneT[i].Length; b++) { boneP[i][b] = boneT[i][b].localPosition; boneR[i][b] = boneT[i][b].localRotation; }

            int nAv = 0; foreach (var nm in Names) if (Res.Load<GameObject>("SelectModels/" + nm) != null) nAv++; float cwB = Screen.width * 0.96f / Mathf.Max(1, nAv), phB = Screen.height * 0.68f; int rh = 720, rw = Mathf.RoundToInt(720f * cwB / phB);
            var rt = new RenderTexture(rw, rh, 24, RenderTextureFormat.ARGB32);
            Bounds bb = new Bounds(inst.transform.position, Vector3.zero); foreach (var bt in inst.GetComponentsInChildren<Transform>()) bb.Encapsulate(bt.position);
            bb.Expand(new Vector3(0.45f, 0f, 0f)); bb.max = new Vector3(bb.max.x, bb.max.y + 0.2f, bb.max.z); bb.min = new Vector3(bb.min.x, bb.min.y - 0.06f, bb.min.z);
            rts[i] = rt;
            var cam = new GameObject("Cam_" + Names[i]).AddComponent<Camera>();
            cam.transform.SetParent(stage, false);
            cam.orthographic = true; cam.orthographicSize = Mathf.Max(bb.size.y * 0.5f * 1.03f, bb.size.x * 0.5f * 1.03f * rh / rw);               // whole body
            cam.transform.position = new Vector3(bb.center.x, bb.center.y, pos.z + 4f);
            cam.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(1f, 1f, 1f, 0f);
            cam.nearClipPlane = 0.1f; cam.farClipPlane = 20f;
            cam.targetTexture = rt;
            cam.enabled = false;                                                   // rendered by hand a few times
        }
    }

    void LateUpdate()
    {
        if (rts == null || stage == null) return;
        int h = (hover >= 0 && hover < Names.Length && rts[hover] != null && Baked.names != null) ? hover : -1;
        if (h != playing) { SetPlaying(h); renderFrames = 0; }
        if (playing >= 0) { var bc = Baked.Get(Names[playing]); Baked.Apply(instT[playing], boneMap[playing], (int)(Time.unscaledTime * (bc != null ? bc.fps : 30f)), Names[playing]); }
        if (renderFrames <= 3)
        {
            renderFrames++;
            foreach (var cam in stage.GetComponentsInChildren<Camera>(true)) RenderCam(cam);
        }
        else if (playing >= 0)
        {
            foreach (var cam in stage.GetComponentsInChildren<Camera>(true)) if (cam.targetTexture == rts[playing]) RenderCam(cam);
        }
        // outline removed
    }
    static void RenderCam(Camera cam)
    {
        var req = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = cam.targetTexture };
        if (UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(cam, req)) UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(cam, req);
        else cam.Render();
    }
    void SetPlaying(int i)
    {
        if (playing >= 0 && boneT[playing] != null)
            for (int b = 0; b < boneT[playing].Length; b++) if (boneT[playing][b] != null) { boneT[playing][b].localPosition = boneP[playing][b]; boneT[playing][b].localRotation = boneR[playing][b]; }   // back to the neutral pose
        playing = (i >= 0 && boneMap[i] != null) ? i : -1;
    }
    // silhouette outline: read the rendered character, find everything that is not the white background, grow it a few pixels, keep only the ring
    void UpdateOutline(int i)
    {
        var rt = rts[i]; if (rt == null) return;
        if (readTex == null) { readTex = new Texture2D(RW, RH, TextureFormat.RGBA32, false); outlineTex = new Texture2D(RW, RH, TextureFormat.RGBA32, false); outlineTex.wrapMode = TextureWrapMode.Clamp; }
        var prev = RenderTexture.active; RenderTexture.active = rt;
        readTex.ReadPixels(new Rect(0, 0, RW, RH), 0, 0, false); RenderTexture.active = prev;
        var px = readTex.GetPixels32();
        if (maskBuf == null || maskBuf.Length != px.Length) { maskBuf = new Color32[px.Length]; tmpBuf = new Color32[px.Length]; outPx = new Color32[px.Length]; }
        for (int k = 0; k < px.Length; k++) { var c = px[k]; maskBuf[k].a = (byte)((c.r < 240 || c.g < 240 || c.b < 240) ? 255 : 0); }
        for (int y = 0; y < RH; y++)                                        // grow horizontally
            for (int x = 0; x < RW; x++)
            {
                byte a = 0; int x0 = Mathf.Max(0, x - OutR), x1 = Mathf.Min(RW - 1, x + OutR), row = y * RW;
                for (int xx = x0; xx <= x1; xx++) if (maskBuf[row + xx].a != 0) { a = 255; break; }
                tmpBuf[row + x].a = a;
            }
        var col = new Color32(26, 140, 26, 255); var none = new Color32(0, 0, 0, 0);
        for (int y = 0; y < RH; y++)                                        // grow vertically, ring = grown minus original
            for (int x = 0; x < RW; x++)
            {
                byte a = 0; int y0 = Mathf.Max(0, y - OutR), y1 = Mathf.Min(RH - 1, y + OutR);
                for (int yy = y0; yy <= y1; yy++) if (tmpBuf[yy * RW + x].a != 0) { a = 255; break; }
                outPx[y * RW + x] = (a != 0 && maskBuf[y * RW + x].a == 0) ? col : none;
            }
        outlineTex.SetPixels32(outPx); outlineTex.Apply(false);
    }

    void OnDestroy()
    {
        if (graphs != null) for (int g = 0; g < graphs.Length; g++) if (graphOn != null && graphOn[g]) graphs[g].Destroy();
        if (readTex != null) Destroy(readTex); if (outlineTex != null) Destroy(outlineTex);
        if (rts != null) foreach (var rt in rts) if (rt != null) { rt.Release(); Destroy(rt); }
        if (stage != null) Destroy(stage.gameObject);
    }

    void Update()
    {
        if (Chosen == null) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb == null) return;
        for (int i = 0; i < Names.Length; i++)
            if (kb[(UnityEngine.InputSystem.Key)((int)UnityEngine.InputSystem.Key.Digit1 + i)].wasPressedThisFrame) Pick(i);
    }


    // swap the player's model for the chosen (Mixamo-rigged) character, keep the gun and the animation setup
    // the pistol: the local avatar's, or (select screen before the world is loaded) the shipped prefab
    static Transform FindGunPivot()
    {
        var av = FindAnyObjectByType<PlayerAvatarAnim>();
        if (av != null) { var oa = av.GetComponentInChildren<Animator>(); var oh = oa != null ? oa.GetBoneTransform(HumanBodyBones.RightHand) : null; var g = oh != null ? oh.Find("GunPivot") : null; if (g != null) return g; }
        var pf = Res.Load<GameObject>("SelectModels/GunPivot"); return pf != null ? pf.transform : null;
    }

    // gives another player's model the same pistol the local avatar holds (used by RemotePlayer)
    public static void AttachGunTo(GameObject inst, Animator a)
    {
        if (a == null) return; var gp = FindGunPivot(); if (gp == null) return;
        var nh = a.GetBoneTransform(HumanBodyBones.RightHand); if (nh == null) return;
        var ta = Res.Load<TextAsset>("SelectModels/gun_attach"); if (ta == null) return;
        var d = JsonUtility.FromJson<GunAttachData>(ta.text);
        var g = Instantiate(gp.gameObject, nh); g.name = "GunPivot"; g.SetActive(true);
        foreach (var mb in g.GetComponentsInChildren<MonoBehaviour>(true)) mb.enabled = false;
        foreach (var c in g.GetComponentsInChildren<Collider>(true)) c.enabled = false;
        g.transform.localPosition = Vector3.zero; g.transform.localRotation = Quaternion.identity;
        var mf = g.GetComponentInChildren<MeshFilter>(); if (mf == null) return;
        float s = 0.003595496f; float cur = mf.transform.lossyScale.x; if (cur > 1e-6f) mf.transform.localScale *= s / cur;
        mf.transform.rotation = nh.rotation * new Quaternion(d.rot[0], d.rot[1], d.rot[2], d.rot[3]);
        mf.transform.position = nh.TransformPoint(new Vector3(d.pos[0], d.pos[1], d.pos[2]));
    }

    void AttachGun(GameObject inst, int idx)
    {
        var gp = FindGunPivot(); if (gp == null) return;
        Transform nh; if (!boneMap[idx].TryGetValue("mixamorig:RightHand", out nh)) return;
        var ta = Res.Load<TextAsset>("SelectModels/gun_attach"); if (ta == null) return;
        var d = JsonUtility.FromJson<GunAttachData>(ta.text);
        var g = Instantiate(gp.gameObject, nh); g.name = "GunPivot"; g.SetActive(true);
        foreach (var mb in g.GetComponentsInChildren<MonoBehaviour>(true)) mb.enabled = false;
        g.transform.localPosition = Vector3.zero; g.transform.localRotation = Quaternion.identity;
        var mf = g.GetComponentInChildren<MeshFilter>(); if (mf == null) return;
        float s = 0.003595496f; float cur = mf.transform.lossyScale.x; if (cur > 1e-6f) mf.transform.localScale *= s / cur;
        mf.transform.rotation = nh.rotation * new Quaternion(d.rot[0], d.rot[1], d.rot[2], d.rot[3]);
        mf.transform.position = nh.TransformPoint(new Vector3(d.pos[0], d.pos[1], d.pos[2]));
    }

    // every non-orangie character gets the same tidy sleeve ends as orangie (sleeves lengthened over the gloves)
    public static void FixSleeves(GameObject inst, string name)
    {
        if (name == "orangie") { foreach (var hb in inst.GetComponentsInChildren<Transform>(true)) if (hb.name == "mixamorig:Head") hb.localScale = Vector3.one * 1.12f; return; }
        foreach (var smr in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr.gameObject.name.StartsWith("m6_Prom_220")) { smr.gameObject.SetActive(false); continue; }   // loose skin piece at the wrists
            string mn = smr.gameObject.name.StartsWith("m3_Prom_212") ? "m3_Prom_212_cut" : smr.gameObject.name.StartsWith("m4_Prom_214") ? "m4_Prom_214_cut" : null;
            if (mn == null) continue;
            var m = Res.Load<Mesh>("SelectModels/Sleeves/" + mn); if (m != null) smr.sharedMesh = m;
        }
    }

    public static void ApplyToPlayer(string name)
    {
        var prefab = Res.Load<GameObject>("SelectModels/" + name);
        var av = FindAnyObjectByType<PlayerAvatarAnim>();
        if (prefab == null || av == null) return;
        var oldAnim = av.GetComponentInChildren<Animator>();
        if (oldAnim == null) return;
        var oldRoot = oldAnim.gameObject; var parent = oldRoot.transform.parent;
        var inst = Instantiate(prefab, parent); inst.name = "Model_" + name; FixSleeves(inst, name);
        inst.transform.localPosition = oldRoot.transform.localPosition; inst.transform.localRotation = oldRoot.transform.localRotation; inst.transform.localScale = Vector3.one;
        var newAnim = inst.GetComponent<Animator>(); if (newAnim == null) newAnim = inst.AddComponent<Animator>();
        newAnim.enabled = true; newAnim.runtimeAnimatorController = oldAnim.runtimeAnimatorController; newAnim.applyRootMotion = false; newAnim.cullingMode = oldAnim.cullingMode;
        // carry the gun over: both rigs in the same humanoid pose, keep the gun's world transform relative to the hand
        var oldHand = oldAnim.GetBoneTransform(HumanBodyBones.RightHand); var newHand = newAnim.GetBoneTransform(HumanBodyBones.RightHand);
        var gp = oldHand != null ? oldHand.Find("GunPivot") : null;
        var clip = Res.LoadAll<AnimationClip>("SelectModels/Pistol_Idle");
        AnimationClip pose = null; foreach (var c in clip) if (!c.name.StartsWith("__preview")) pose = c;
        if (gp != null && newHand != null)
        {
            if (pose != null) { pose.SampleAnimation(oldRoot, 0f); pose.SampleAnimation(inst, 0f); }
            var wp = gp.position; var wr = gp.rotation; var ws = gp.lossyScale;
            gp.SetParent(newHand, true);   // keeps the world position/rotation computed from the identical pose
            gp.position = wp; gp.rotation = wr;
        }
        oldRoot.SetActive(false);
        av.Rebind();
    }

    void Pick(int i)
    {
        if (Chosen != null) return;
        ChosenIndex = i; Chosen = Names[i];
        if (frozen != null) foreach (var b in frozen) if (b != null) b.enabled = true;
        Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
        ApplyToPlayer(Names[i]);
        var cb = OnChosen; OnChosen = null; cb?.Invoke();
        Destroy(gameObject);
    }

    void OnGUI()
    {
        if (white == null) { white = new Texture2D(1, 1); white.SetPixel(0, 0, Color.white); white.Apply(); }
        GUI.depth = -100;
        float W = Screen.width, H = Screen.height;
        GUI.color = new Color(0.14f, 0.50f, 0.19f); GUI.DrawTexture(new Rect(0, 0, W, H), white);                  // green background
        if (nameStyle == null)
        {
            nameStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            titleStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        }
        nameStyle.fontSize = Mathf.RoundToInt(H / 24f); titleStyle.fontSize = Mathf.RoundToInt(H / 16f);
        int n = Names.Length, na = 0; int[] slot = new int[n]; for (int q = 0; q < n; q++) { slot[q] = (rts != null && rts[q] != null) ? na++ : -1; } if (na == 0) na = 1;
        float cw = W * 0.96f / na, x0 = W * 0.02f;
        float photoH = H * 0.68f, photoY = H * 0.03f, nameY = photoY + photoH, nameH = H * 0.08f;
        var mouse = Event.current.mousePosition; hover = -1;
        if (glow == null) { glow = new Texture2D(64, 64, TextureFormat.RGBA32, false); glow.wrapMode = TextureWrapMode.Clamp; for (int gy = 0; gy < 64; gy++) for (int gx = 0; gx < 64; gx++) { float d = Mathf.Clamp01(new Vector2((gx - 31.5f) / 32f, (gy - 20f) / 44f).magnitude); glow.SetPixel(gx, gy, Color.Lerp(Color.white, new Color(0.3f, 0.3f, 0.3f), d)); } glow.Apply(); }
        for (int i = 0; i < n; i++) { if (slot[i] < 0) continue; var cr = new Rect(x0 + slot[i] * cw, photoY, cw, photoH + nameH); if (cr.Contains(mouse)) { GUI.color = Names[i] == "tjr" ? new Color(0.62f, 0.30f, 0.95f) : Names[i] == "cented" ? new Color(0.25f, 0.5f, 1f) : Names[i] == "yolo" ? new Color(0.12f, 0.5f, 0.18f) : Names[i] == "orangie" ? new Color(1f, 0.55f, 0.08f) : new Color(0.35f, 0.95f, 0.4f); GUI.DrawTexture(new Rect(cr.x, 0f, cr.width, H), glow); } }
        for (int i = 0; i < n; i++)
        {
            if (slot[i] < 0) continue;
            var col = new Rect(x0 + slot[i] * cw, photoY, cw, photoH + nameH);                        // clickable column (no box drawn)
            bool over = col.Contains(mouse); if (over) hover = i;
            if (rts != null && rts[i] != null)                                                  // character above the name
            {
                GUI.color = Color.white;
                float ph = photoH, pw = ph * rts[i].width / rts[i].height;
                var pr = new Rect(col.center.x - pw * 0.5f, photoY + photoH - ph, pw, ph);
                GUI.DrawTexture(pr, rts[i], ScaleMode.ScaleToFit, true);
                // outline removed
            }
            GUI.color = over ? Color.white : new Color(0.1f, 0.1f, 0.1f);
            if (rts != null && rts[i] != null) GUI.Label(new Rect(col.x, nameY, col.width, nameH), char.ToUpper(Names[i][0]) + Names[i].Substring(1), nameStyle);
            if (over) { GUI.color = Color.white; GUI.DrawTexture(new Rect(col.x - 1f, 0f, 2f, H), white); GUI.DrawTexture(new Rect(col.xMax - 1f, 0f, 2f, H), white); }   // line between characters
            if (over && Event.current.type == EventType.MouseDown) { Pick(i); return; }
        }
        GUI.color = new Color(0.1f, 0.1f, 0.1f);
        GUI.Label(new Rect(0, H * 0.84f, W, H * 0.12f), "CHOOSE YOUR TRENCHER", titleStyle);
    }

    // baked menu animation (authored in Blender: pistol idle, gun gripped, two-hand hold, breathing) -> world rotations per bone per frame
    public static class Baked
    {
        public class Clip { public string[] names; public Quaternion[][] q; public Vector3[] hips; public float fps = 30f; public int count; }
        public static string[] names;   // non-null once the default clip is loaded
        static System.Collections.Generic.Dictionary<string, Clip> cache = new System.Collections.Generic.Dictionary<string, Clip>();
        static Clip Read(string path)
        {
            var ta = Res.Load<TextAsset>(path); if (ta == null) return null;
            var c = new Clip();
            using (var br = new System.IO.BinaryReader(new System.IO.MemoryStream(ta.bytes)))
            {
                c.count = br.ReadInt32(); int nb = br.ReadInt32(); c.fps = br.ReadSingle();
                var nm = new string[nb]; for (int i = 0; i < nb; i++) { int l = br.ReadInt32(); nm[i] = System.Text.Encoding.UTF8.GetString(br.ReadBytes(l)); }
                c.q = new Quaternion[c.count][]; c.hips = new Vector3[c.count];
                for (int f = 0; f < c.count; f++)
                {
                    c.hips[f] = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                    c.q[f] = new Quaternion[nb]; for (int i = 0; i < nb; i++) c.q[f][i] = new Quaternion(br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                }
                c.names = nm;
            }
            return c;
        }
        public static void Load() { if (names != null) return; var d = Get(null); if (d != null) names = d.names; }
        // a character can have its own clip: Resources/SelectModels/PistolAnim_<name>.bytes, otherwise the shared PistolAnim
        public static Clip Get(string key)
        {
            string k = key ?? ""; Clip c;
            if (cache.TryGetValue(k, out c)) return c;
            c = key != null ? Read("SelectModels/PistolAnim_" + key) : null;
            if (c == null) c = (key != null && cache.TryGetValue("", out var dflt)) ? dflt : Read("SelectModels/PistolAnim");
            cache[k] = c; if (key != null && !cache.ContainsKey("") && c != null) { }
            return c;
        }
        public static void Apply(Transform root, System.Collections.Generic.Dictionary<string, Transform> map, int f, string key = null)
        {
            var c = Get(key); if (c == null || map == null) return; f = ((f % c.count) + c.count) % c.count;
            Transform hp; if (map.TryGetValue("mixamorig:Hips", out hp)) hp.position = root.TransformPoint(c.hips[f]);
            for (int i = 0; i < c.names.Length; i++) { Transform t; if (map.TryGetValue(c.names[i], out t)) t.rotation = root.rotation * c.q[f][i]; }
            if (key == "yolo") FitHands(root, map, f);
            TriggerFinger(map);
        }
        // right index finger onto the trigger: tip target in the right hand's own space (measured on the gun), solved with CCD over the three finger joints
        static readonly Vector3 trigLocal = new Vector3(-0.03059f, 0.16521f, -0.02111f);
        static void TriggerFinger(System.Collections.Generic.Dictionary<string, Transform> map)
        {
            Transform hand, i1, i2, i3, i4;
            if (!map.TryGetValue("mixamorig:RightHand", out hand) || !map.TryGetValue("mixamorig:RightHandIndex1", out i1) || !map.TryGetValue("mixamorig:RightHandIndex2", out i2) || !map.TryGetValue("mixamorig:RightHandIndex3", out i3) || !map.TryGetValue("mixamorig:RightHandIndex4", out i4)) return;
            Vector3 target = hand.TransformPoint(trigLocal);
            var chain = new Transform[] { i3, i2, i1 };
            for (int it = 0; it < 16; it++)
                foreach (var j in chain)
                {
                    Vector3 a = i4.position - j.position, b = target - j.position;
                    if (a.sqrMagnitude < 1e-10f || b.sqrMagnitude < 1e-10f) continue;
                    j.rotation = Quaternion.FromToRotation(a, b) * j.rotation;
                }
        }
        // characters with different arm proportions: put both hands where the standard rig's hands are (relative to the hips, in the character's own space)
        static readonly Vector3 tRH = new Vector3(0.2680f, 0.3401f, 0.4304f), tLH = new Vector3(0.0985f, 0.3312f, 0.4855f), tRE = new Vector3(0.3083f, 0.3361f, 0.2122f), tLE = new Vector3(-0.0604f, 0.3603f, 0.3328f);
        static bool fitInit; static Vector3[] fitLocal = new Vector3[4];
        static void FitHands(Transform root, System.Collections.Generic.Dictionary<string, Transform> map, int f)
        {
            Transform hips, ch; if (!map.TryGetValue("mixamorig:Hips", out hips) || !map.TryGetValue("mixamorig:Spine2", out ch)) return;
            // targets are stored in chest space (captured on the first frame) so the arms follow the torso sway instead of stretching/jittering
            if (!fitInit) { if (f != 0) return; Vector3[] w = { tRH, tRE, tLH, tLE }; for (int q = 0; q < 4; q++) fitLocal[q] = ch.InverseTransformPoint(hips.position + root.TransformVector(w[q])); fitInit = true; }
            // remember the hands' and fingers' baked rotations so the IK only moves the arms
            var keep = new System.Collections.Generic.List<Transform>(); var rots = new System.Collections.Generic.List<Quaternion>();
            foreach (var kv in map) if (kv.Key.Contains("Hand")) { keep.Add(kv.Value); rots.Add(kv.Value.rotation); }
            Arm(map, "Right", ch.TransformPoint(fitLocal[0]), ch.TransformPoint(fitLocal[1]));
            Arm(map, "Left", ch.TransformPoint(fitLocal[2]), ch.TransformPoint(fitLocal[3]));
            for (int i = 0; i < keep.Count; i++) keep[i].rotation = rots[i];
        }
        static void Arm(System.Collections.Generic.Dictionary<string, Transform> map, string side, Vector3 target, Vector3 hint)
        {
            Transform u, l, h; if (!map.TryGetValue("mixamorig:" + side + "Arm", out u) || !map.TryGetValue("mixamorig:" + side + "ForeArm", out l) || !map.TryGetValue("mixamorig:" + side + "Hand", out h)) return;
            float a = (l.position - u.position).magnitude, b = (h.position - l.position).magnitude;
            Vector3 toT = target - u.position; float d = Mathf.Clamp(toT.magnitude, 0.05f, (a + b) * 0.999f); Vector3 dir = toT.normalized;
            Vector3 perp = Vector3.ProjectOnPlane(hint - u.position, dir); perp = perp.sqrMagnitude > 1e-8f ? perp.normalized : Vector3.up;
            float cosA = Mathf.Clamp((a * a + d * d - b * b) / (2f * a * d), -1f, 1f), sinA = Mathf.Sqrt(1f - cosA * cosA);
            Vector3 elbow = u.position + dir * (a * cosA) + perp * (a * sinA);
            u.rotation = Quaternion.FromToRotation(l.position - u.position, elbow - u.position) * u.rotation;
            l.rotation = Quaternion.FromToRotation(h.position - l.position, target - l.position) * l.rotation;
        }
    }
    [System.Serializable] class GunAttachData { public float[] pos; public float[] rot; }
}
