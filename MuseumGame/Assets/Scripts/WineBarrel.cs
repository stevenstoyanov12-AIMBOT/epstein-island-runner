using UnityEngine;
using System.Collections;
using System.Collections.Generic;

// Shots 1-3: pressurised wine jets from each bullet hole (continuous ribbon stream + droplets + splashes + spreading puddle).
// Shot 4: barrel bursts - wine blast, streaks, mist, flash, flying staves and a big puddle.
public class WineBarrel : MonoBehaviour, IWorldState
{
    public GameObject fracturedPrefab;
    int hits;
    static Material dropMat, ribbonMat, mistMat, puddleMat;
    static readonly Color Wine = new Color(0.42f, 0.015f, 0.05f, 1f);
    readonly List<ParticleSystem> streams = new List<ParticleSystem>();
    Transform puddle; float puddleTarget;

    static Material Make(bool additiveSoft, float alpha)
    {
        var baseMat = Res.Load<Material>("FX_WineAlpha");   // real asset: the web build keeps its transparent shader variant
        var m = baseMat != null ? new Material(baseMat) : new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
        m.SetFloat("_Surface", 1); m.SetFloat("_Blend", 0); m.SetOverrideTag("RenderType", "Transparent");
        m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetInt("_ZWrite", 0); m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.renderQueue = 3000;
        var tex = new Texture2D(64, 64); for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
        { float d = Vector2.Distance(new Vector2(x, y), new Vector2(31.5f, 31.5f)) / 32f; float a = additiveSoft ? Mathf.Pow(Mathf.Clamp01(1 - d), 2f) : Mathf.Clamp01((1 - d) * 3f);
          float hl = Mathf.Clamp01(1 - Vector2.Distance(new Vector2(x, y), new Vector2(24, 40)) / 10f) * (additiveSoft ? 0 : 0.8f);
          tex.SetPixel(x, y, new Color(1 + hl, 1 + hl, 1 + hl, a * alpha)); }
        tex.Apply(); m.SetTexture("_BaseMap", tex); m.SetColor("_BaseColor", Wine); return m;
    }
    static void Mats()
    {
        if (dropMat) return;
        dropMat = Make(false, 1f); mistMat = Make(true, 0.35f);
        ribbonMat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")); ribbonMat.SetColor("_BaseColor", Wine);
        puddleMat = new Material(Shader.Find("Universal Render Pipeline/Lit")); puddleMat.SetColor("_BaseColor", new Color(0.18f, 0.005f, 0.02f)); puddleMat.SetFloat("_Smoothness", 0.95f);
    }

    void OnBulletHit(RaycastHit hit)
    {
        hits++;
        if (hits <= 4) WorldState.Changed(netPath, this);
        if (hits <= 3) Pour(hit);
        else if (hits == 4) Burst(hit);
    }

    ParticleSystem PS(string name, Vector3 pos, Quaternion rot, Material mat)
    {
        var go = new GameObject(name); go.transform.SetPositionAndRotation(pos, rot);
        var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = mat;
        var m = ps.main; m.simulationSpace = ParticleSystemSimulationSpace.World; m.startColor = Color.white;
        return ps;
    }
    void Collide(ParticleSystem ps, float bounce, float loss)
    {
        var c = ps.collision; c.enabled = true; c.type = ParticleSystemCollisionType.World; c.bounce = bounce; c.dampen = 0.6f; c.lifetimeLoss = loss; c.quality = ParticleSystemCollisionQuality.Medium; c.sendCollisionMessages = false;
    }
    ParticleSystem Splash(ParticleSystem parent, int count)
    {
        var s = PS("Splash", parent.transform.position, Quaternion.identity, dropMat); s.transform.SetParent(parent.transform, true);
        var m = s.main; m.loop = false; m.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.5f); m.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.8f);
        m.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.035f); m.gravityModifier = 1.5f; m.maxParticles = 3000;
        var e = s.emission; e.rateOverTime = 0; e.SetBursts(new[] { new ParticleSystem.Burst(0, (short)count) });
        var sh = s.shape; sh.shapeType = ParticleSystemShapeType.Hemisphere; sh.radius = 0.02f;
        var r = s.GetComponent<ParticleSystemRenderer>(); r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.06f; r.lengthScale = 1.2f;
        var sub = parent.subEmitters; sub.enabled = true; sub.AddSubEmitter(s, ParticleSystemSubEmitterType.Collision, ParticleSystemSubEmitterProperties.InheritNothing, 0.35f);
        return s;
    }

    void Pour(RaycastHit hit)
    {
        Mats();
        var n = hit.normal; n.y = Mathf.Max(n.y, -0.2f); n.Normalize();
        var jet = PS("WineJet", hit.point + hit.normal * 0.01f, Quaternion.LookRotation(n), dropMat); jet.transform.SetParent(transform, true);
        var m = jet.main; m.loop = true; m.startLifetime = 2.2f; m.startSpeed = new ParticleSystem.MinMaxCurve(3.4f, 3.9f);
        m.startSize = new ParticleSystem.MinMaxCurve(0.035f, 0.055f); m.gravityModifier = 1f; m.maxParticles = 4000;
        var e = jet.emission; e.rateOverTime = 420;
        var sh = jet.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 2.5f; sh.radius = 0.012f;
        var no = jet.noise; no.enabled = true; no.strength = 0.06f; no.frequency = 1.2f; no.scrollSpeed = 1.5f;
        var r = jet.GetComponent<ParticleSystemRenderer>(); r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.05f; r.lengthScale = 2.5f;
        Collide(jet, 0.02f, 1f); Splash(jet, 3);
        // continuous liquid ribbon on top of the droplets
        var rib = PS("WineRibbon", jet.transform.position, jet.transform.rotation, dropMat); rib.transform.SetParent(transform, true);
        var rm = rib.main; rm.loop = true; rm.startLifetime = 1.2f; rm.startSpeed = new ParticleSystem.MinMaxCurve(3.5f, 3.8f); rm.startSize = 0.01f; rm.gravityModifier = 1f; rm.maxParticles = 600;
        var re = rib.emission; re.rateOverTime = 90; var rsh = rib.shape; rsh.shapeType = ParticleSystemShapeType.Cone; rsh.angle = 1; rsh.radius = 0.005f;
        var tr = rib.trails; tr.enabled = true; tr.mode = ParticleSystemTrailMode.Ribbon; tr.ribbonCount = 1; tr.widthOverTrail = new ParticleSystem.MinMaxCurve(0.045f, new AnimationCurve(new Keyframe(0, 0.7f), new Keyframe(1, 1.2f))); tr.dieWithParticles = true; tr.textureMode = ParticleSystemTrailTextureMode.Stretch;
        var rr = rib.GetComponent<ParticleSystemRenderer>(); rr.trailMaterial = ribbonMat; rr.renderMode = ParticleSystemRenderMode.None;
        Collide(rib, 0f, 1f);
        jet.Play(); rib.Play(); streams.Add(jet); streams.Add(rib);
        StartCoroutine(Pressure(jet, rib));
        GrowPuddle(0.55f + 0.25f * hits);
    }
    IEnumerator Pressure(ParticleSystem jet, ParticleSystem rib)
    {
        // pressure drops as the barrel empties: strong jet -> weaker arc -> dribble
        float t = 0, dur = 14f;
        while (t < dur && jet)
        {
            t += Time.deltaTime; float k = Mathf.Pow(1 - t / dur, 0.6f);
            var m = jet.main; m.startSpeed = new ParticleSystem.MinMaxCurve(0.3f + 3.4f * k, 0.5f + 3.6f * k);
            var e = jet.emission; e.rateOverTime = 120 + 300 * k;
            if (rib) { var rm = rib.main; rm.startSpeed = new ParticleSystem.MinMaxCurve(0.35f + 3.3f * k, 0.45f + 3.4f * k); }
            yield return null;
        }
        if (jet) jet.Stop(); if (rib) rib.Stop();
    }
    void GrowPuddle(float size)
    {
        Mats();
        if (!puddle)
        {
            var b = GetComponent<Renderer>().bounds; RaycastHit h; var p = b.center;
            if (Physics.Raycast(b.center, Vector3.down, out h, 5f)) p = h.point;
            var g = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Destroy(g.GetComponent<Collider>()); g.name = "WinePuddle";
            g.GetComponent<Renderer>().sharedMaterial = puddleMat; g.transform.position = p + Vector3.up * 0.005f; g.transform.localScale = new Vector3(0.01f, 0.002f, 0.01f);
            puddle = g.transform; StartCoroutine(PuddleGrow());
        }
        puddleTarget = Mathf.Max(puddleTarget, size);
    }
    IEnumerator PuddleGrow()
    {
        while (puddle)
        {
            var s = puddle.localScale; float x = Mathf.MoveTowards(s.x, puddleTarget, Time.deltaTime * 0.12f);
            puddle.localScale = new Vector3(x, s.y, x * 0.85f); yield return null;
        }
    }

    void Burst(RaycastHit hit)
    {
        Mats();
        var rend = GetComponent<Renderer>(); var center = rend ? rend.bounds.center : transform.position; float rad = rend ? rend.bounds.extents.magnitude * 0.6f : 0.6f;
        foreach (var s in streams) if (s) s.Stop();
        // 1) main wine blast - stretched droplets flung outward
        var b = PS("WineBlast", center, Quaternion.identity, dropMat);
        var m = b.main; m.loop = false; m.duration = 0.15f; m.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 2.2f);
        m.startSpeed = new ParticleSystem.MinMaxCurve(3f, 11f); m.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.12f); m.gravityModifier = 1.3f; m.maxParticles = 6000;
        var e = b.emission; e.rateOverTime = 0; e.SetBursts(new[] { new ParticleSystem.Burst(0f, 2600), new ParticleSystem.Burst(0.05f, 900) });
        var sh = b.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = rad * 0.6f;
        var vel = b.velocityOverLifetime; vel.enabled = true; vel.x = new ParticleSystem.MinMaxCurve(0f, 0f); vel.y = new ParticleSystem.MinMaxCurve(1.5f, 3.5f); vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
        var dr = b.limitVelocityOverLifetime; dr.enabled = true; dr.drag = 0.8f; dr.dampen = 0f;
        var r = b.GetComponent<ParticleSystemRenderer>(); r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.018f; r.lengthScale = 1.1f;
        Collide(b, 0.05f, 0.9f); Splash(b, 4);
        // 2) streaks - long liquid tendrils with ribbons
        var st = PS("WineStreaks", center, Quaternion.identity, dropMat);
        var sm = st.main; sm.loop = false; sm.duration = 0.1f; sm.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.2f); sm.startSpeed = new ParticleSystem.MinMaxCurve(5f, 10f); sm.startSize = 0.02f; sm.gravityModifier = 1.2f; sm.maxParticles = 200;
        var se = st.emission; se.rateOverTime = 0; se.SetBursts(new[] { new ParticleSystem.Burst(0f, 70) });
        var ssh = st.shape; ssh.shapeType = ParticleSystemShapeType.Hemisphere; ssh.radius = rad * 0.4f;
        var tr = st.trails; tr.enabled = true; tr.mode = ParticleSystemTrailMode.PerParticle; tr.lifetime = 0.35f; tr.widthOverTrail = new ParticleSystem.MinMaxCurve(0.06f, new AnimationCurve(new Keyframe(0, 1), new Keyframe(1, 0))); tr.dieWithParticles = true;
        st.GetComponent<ParticleSystemRenderer>().trailMaterial = ribbonMat; Collide(st, 0f, 1f);
        // 3) wine mist cloud
        var mi = PS("WineMist", center, Quaternion.identity, mistMat);
        var mm = mi.main; mm.loop = false; mm.duration = 0.2f; mm.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.4f); mm.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 2.5f); mm.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1.6f); mm.gravityModifier = 0.05f; mm.startRotation = new ParticleSystem.MinMaxCurve(0, 6.28f); mm.maxParticles = 100;
        var me = mi.emission; me.rateOverTime = 0; me.SetBursts(new[] { new ParticleSystem.Burst(0f, 40) });
        var msh = mi.shape; msh.shapeType = ParticleSystemShapeType.Sphere; msh.radius = rad * 0.5f;
        var sz = mi.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 0.5f), new Keyframe(1, 1.6f)));
        var col = mi.colorOverLifetime; col.enabled = true; var gr = new Gradient(); gr.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(0.9f, 0), new GradientAlphaKey(0, 1) }); col.color = gr;
        // 4) flash
        var fl = new GameObject("WineFlash").AddComponent<Light>(); fl.transform.position = center; fl.type = LightType.Point; fl.color = new Color(1f, 0.45f, 0.3f); fl.range = 8f; fl.intensity = 14f; fl.shadows = LightShadows.None;
        StartCoroutine(FadeLight(fl));
        b.Play(); st.Play(); mi.Play();
        Destroy(b.gameObject, 5f); Destroy(st.gameObject, 3f); Destroy(mi.gameObject, 4f);
        // 5) puddle
        GrowPuddle(2.4f); if (puddle) puddle.SetParent(null, true);
        // 6) staves fly apart
        if (fracturedPrefab)
        {
            var f = Instantiate(fracturedPrefab, transform.position, transform.rotation);
            f.transform.localScale = transform.lossyScale;
            foreach (var mr in f.GetComponentsInChildren<MeshRenderer>())
            {
                var bm = GetComponent<MeshRenderer>().sharedMaterials; mr.sharedMaterial = (mr.name.StartsWith("Hoop") && bm.Length > 1) ? bm[1] : bm[0];
                var mc = mr.gameObject.AddComponent<MeshCollider>(); mc.convex = true;
                var pl = GameObject.Find("Player"); if (pl) foreach (var pc in pl.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(mc, pc, true);
                var rb = mr.gameObject.AddComponent<Rigidbody>(); rb.mass = 4f; rb.interpolation = RigidbodyInterpolation.Interpolate; rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                rb.AddExplosionForce(110f, center - hit.normal * 0.3f + Vector3.down * 0.3f, 5f, 0.8f, ForceMode.Impulse);
                rb.AddTorque(Random.insideUnitSphere * 25f, ForceMode.Impulse);
                mr.gameObject.AddComponent<FragmentFade>().life = 4f;
            }
            Destroy(f, 8f);
        }
        var myBounds = rend ? rend.bounds : new Bounds(center, Vector3.one);
        foreach (var rr in GetComponentsInChildren<Renderer>()) rr.enabled = false; foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
        DropAbove(myBounds, this);
        Destroy(gameObject, 6f);
    }

    // ---- multiplayer sync of falling barrels ----
    static readonly Dictionary<string, WineBarrel> registry = new Dictionary<string, WineBarrel>();
    string netPath; float poseSend, lastPose = -10f, fellAt; Vector3 netPos; Quaternion netRot = Quaternion.identity; bool netFollow;
    void Awake() { netPath = Net.PathOf(transform); registry[netPath] = this; }   // id taken before a fall re-parents the barrel
    void Start() { WorldState.Register(netPath, this); }
    bool settledSent;

    // ---- late join: hits so far, and where it came to rest if it fell ----
    public string Save() => hits >= 4 ? "4" : falling ? hits + "|" + WorldState.V(transform.position) + "|" + WorldState.Q(transform.rotation) : hits.ToString();
    public void Load(string s)
    {
        if (hits >= 4 || string.IsNullOrEmpty(s)) return;
        var p = s.Split('|'); int n = WorldState.PI(p[0]);
        if (n >= 4)
        {
            hits = 4; SnapPuddle(2.4f);
            foreach (var rr in GetComponentsInChildren<Renderer>()) rr.enabled = false; foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
            Destroy(gameObject, 0.5f);
            return;
        }
        if (p.Length >= 3)
        {
            falling = true; transform.SetParent(null, true);
            transform.SetPositionAndRotation(WorldState.PV(p[1]), WorldState.PQ(p[2]));
            var rb = GetComponent<Rigidbody>(); if (rb) rb.isKinematic = true;
        }
        if (n > hits) { hits = n; SnapPuddle(0.55f + 0.25f * n); }
    }
    void SnapPuddle(float size)
    {
        GrowPuddle(size);
        if (puddle) puddle.localScale = new Vector3(size, puddle.localScale.y, size * 0.85f);
    }
    void Update()
    {
        if (!falling || hits >= 4) return;
        // the host reports where a fallen barrel came to rest, for players who join later
        if (!netFollow && !settledSent && Time.time - fellAt >= 6f && Net.I != null && Net.I.Online && Net.I.IsHost) { settledSent = true; WorldState.Changed(netPath, this, true); }
        if (netFollow)
        {
            if (Time.time - lastPose > 1.5f && Time.time - fellAt > 1.5f) { var rb0 = GetComponent<Rigidbody>(); if (rb0) rb0.isKinematic = false; netFollow = false; return; }   // host silent: fall locally
            float k = 1f - Mathf.Exp(-20f * Time.deltaTime);
            transform.SetPositionAndRotation(Vector3.Lerp(transform.position, netPos, k), Quaternion.Slerp(transform.rotation, netRot, k));
        }
        else if (Net.I != null && Net.I.Online && Net.I.IsHost && Time.time - fellAt < 6f && (poseSend -= Time.deltaTime) <= 0f)
        {
            poseSend = 1f / 15f; var p = transform.position; var q = transform.rotation;
            Net.I.SendRaw(new Net.Msg { t = "bp", c = netPath, x = p.x, y = p.y, z = p.z, tx = q.x, ty = q.y, tz = q.z, r = q.w });
        }
    }
    public static void ApplyPose(Net.Msg m)
    {
        WineBarrel w; if (!registry.TryGetValue(m.c, out w) || w == null || w.hits >= 4) return;
        w.netPos = new Vector3(m.x, m.y, m.z); w.netRot = new Quaternion(m.tx, m.ty, m.tz, m.r); w.lastPose = Time.time;
        if (!w.falling) w.Fall();   // the host's barrel lost its support before ours did
    }
    public static void RemoteBurst(string path)
    {
        WineBarrel w; if (!registry.TryGetValue(path, out w) || w == null || w.hits >= 4) return;
        var h = new RaycastHit(); h.point = w.transform.position; h.normal = Vector3.up;
        w.hits = 4; w.Burst(h);
    }

    // ---- stacking: barrels resting on a destroyed barrel lose their support and fall (and so does everything stacked on them) ----
    bool falling;
    static void DropAbove(Bounds support, WineBarrel except)
    {
        foreach (var w in FindObjectsByType<WineBarrel>(FindObjectsSortMode.None))
        {
            if (w == except || w.falling || w.hits >= 4) continue;
            var r = w.GetComponent<Renderer>(); if (r == null || !r.enabled) continue;
            var b = r.bounds;
            bool above = b.min.y > support.center.y && b.min.y - support.max.y < 0.35f;
            Vector2 d = new Vector2(b.center.x - support.center.x, b.center.z - support.center.z);
            bool overlaps = Mathf.Abs(d.x) < (b.extents.x + support.extents.x) * 0.85f && Mathf.Abs(d.y) < (b.extents.z + support.extents.z) * 0.85f;
            if (above && overlaps) w.Fall();
        }
    }
    public void Fall()
    {
        if (falling) return; falling = true;
        var r = GetComponent<Renderer>(); var b = r ? r.bounds : new Bounds(transform.position, Vector3.one);
        transform.SetParent(null, true);
        foreach (var mc in GetComponentsInChildren<MeshCollider>()) mc.convex = true;   // a moving rigidbody needs convex colliders
        if (GetComponentInChildren<Collider>() == null) { var bc = gameObject.AddComponent<BoxCollider>(); }
        var rb = GetComponent<Rigidbody>(); if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = false; rb.useGravity = true; rb.mass = 60f; rb.interpolation = RigidbodyInterpolation.Interpolate; rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.AddTorque(Random.insideUnitSphere * 6f, ForceMode.Impulse);
        fellAt = Time.time;
        // multiplayer: only the host simulates the fall; everyone else's barrel follows the host's (same path, same landing)
        if (Net.I != null && Net.I.Online && !Net.I.IsHost) { rb.isKinematic = true; netFollow = true; if (lastPose < fellAt - 0.5f) { netPos = transform.position; netRot = transform.rotation; } }
        DropAbove(b, this);   // whatever sits on this barrel comes down with it
    }

    void OnCollisionEnter(Collision c)
    {
        // a falling barrel that slams down hard bursts
        if (falling && !netFollow && hits < 4 && c.relativeVelocity.magnitude > 7f && c.contactCount > 0)
        {
            var cp = c.GetContact(0); RaycastHit h = new RaycastHit(); h.point = cp.point; h.normal = cp.normal;
            hits = 4; Burst(h);
            if (Net.I != null && Net.I.Online) Net.I.SendRaw(new Net.Msg { t = "act", to = "burst", c = netPath });
            WorldState.Changed(netPath, this, true);
        }
    }
    IEnumerator FadeLight(Light l) { float t = 0; while (t < 0.35f) { t += Time.deltaTime; l.intensity = Mathf.Lerp(14f, 0, t / 0.35f); yield return null; } Destroy(l.gameObject); }
}
