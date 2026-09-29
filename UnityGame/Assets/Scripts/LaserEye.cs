using System.Collections.Generic;
using UnityEngine;

// All the visuals for one of the statue's laser eyes: a Blender-rendered charge-up flipbook and a thin aim line while it charges, the beam itself - a
// museum-security-style laser: a dead-straight white-hot core inside a red glow and a wide faint red haze, all
// drawn as additive HDR light so the scene's bloom makes it shine. StatueGaze drives it; it holds no game logic.
public class LaserEye
{
    readonly Transform eye;
    readonly LineRenderer core, glow, haze, aim;
    readonly ParticleSystem sparks, smoke;
    readonly Light eyeLight, impactLight;
    readonly Transform flare, impactFlare;
    readonly Material flareMat, impactFlareMat;
    float beamLength, lastScorch;
    // charge-up: a 4x4 flipbook rendered in Blender (tools/blender_eyes/charge_flipbook.py), played over the eye
    readonly Transform chargeQuad;
    readonly Material chargeMat;
    readonly float noiseSeed = Random.Range(0f, 100f);   // each eye flickers differently

    static Texture2D softDot, beamTex, scorchTex;
    static Material scorchMat;
    static readonly List<(Transform t, Material m, float born)> scorches = new List<(Transform, Material, float)>();
    const int MaxScorches = 60;
    const float ScorchLife = 10f;

    public LaserEye(Transform eye, Texture2D chargeFlipbook)
    {
        this.eye = eye;
        MakeTextures();
        var root = new GameObject("Laser").transform;
        root.SetParent(eye, false);

        // three layers, widest first; HDR colours (above 1) are what the bloom picks up
        haze = Line(root, "BeamHaze", Additive(beamTex, new Color(1.2f, 0.05f, 0.03f, 1f)), new Color(1f, 1f, 1f, 0.35f));
        glow = Line(root, "BeamGlow", Additive(beamTex, new Color(6f, 0.35f, 0.15f, 1f)), Color.white);
        core = Line(root, "BeamCore", Additive(beamTex, new Color(8f, 6f, 5.5f, 1f)), Color.white);
        aim = Line(root, "AimLine", Additive(softDot, new Color(2f, 0.1f, 0.05f, 1f)), new Color(1f, 1f, 1f, 0.5f));
        aim.positionCount = 2;

        chargeQuad = Quad(root, "ChargeFlipbook", out chargeMat);
        chargeMat = Additive(chargeFlipbook != null ? chargeFlipbook : softDot, new Color(3f, 3f, 3f, 1f));
        chargeMat.mainTextureScale = new Vector2(0.25f, 0.25f);
        chargeQuad.GetComponent<MeshRenderer>().sharedMaterial = chargeMat;
        chargeQuad.localPosition = new Vector3(0f, 0f, 0.025f);   // just in front of the eye
        chargeQuad.localScale = Vector3.one * 0.08f;
        chargeQuad.gameObject.SetActive(false);

        flare = Quad(root, "EyeFlare", out flareMat);
        flare.localPosition = new Vector3(0f, 0f, 0.03f);          // just in front of the face, not inside it
        eyeLight = new GameObject("EyeLight").AddComponent<Light>();
        eyeLight.transform.SetParent(root, false);
        eyeLight.transform.localPosition = new Vector3(0f, 0f, 0.04f);
        eyeLight.type = LightType.Point;
        eyeLight.range = 0.6f;
        eyeLight.color = new Color(1f, 0.2f, 0.1f);
        eyeLight.intensity = 0f;

        var impact = new GameObject("Impact").transform;       // in world space; moved every frame
        impact.SetParent(root, false);
        impactFlare = Quad(impact, "ImpactFlare", out impactFlareMat);
        impactLight = new GameObject("ImpactLight").AddComponent<Light>();
        impactLight.transform.SetParent(impact, false);
        impactLight.type = LightType.Point;
        impactLight.range = 2.5f;
        impactLight.color = new Color(1f, 0.45f, 0.15f);
        impactLight.intensity = 0f;

        sparks = Particles(impact, "Sparks", true);
        var m = sparks.main;
        m.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
        m.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 5f);
        m.startSize = new ParticleSystem.MinMaxCurve(0.008f, 0.02f);
        m.gravityModifier = 1.2f;
        m.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.9f, 0.5f), new Color(1f, 0.4f, 0.1f));
        var sh = sparks.shape;
        sh.shapeType = ParticleSystemShapeType.Cone;
        sh.angle = 55f;
        sh.radius = 0.02f;
        var sr = sparks.GetComponent<ParticleSystemRenderer>();
        sr.renderMode = ParticleSystemRenderMode.Stretch;      // streaks, not dots
        sr.velocityScale = 0.04f;
        sr.lengthScale = 1.5f;
        var col = sparks.colorOverLifetime;
        col.enabled = true;
        col.color = Gradient(new Color(1f, 0.95f, 0.7f), new Color(1f, 0.25f, 0.05f), 1f, 0f);
        var bounce = sparks.collision;
        bounce.enabled = true;
        bounce.type = ParticleSystemCollisionType.World;
        bounce.bounce = 0.35f;
        bounce.lifetimeLoss = 0.3f;

        smoke = Particles(impact, "Smoke", true);
        m = smoke.main;
        m.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2f);
        m.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.5f);
        m.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.2f);
        m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        m.gravityModifier = -0.08f;                            // rises
        m.startColor = new Color(0.25f, 0.22f, 0.2f, 0.35f);
        sh = smoke.shape;
        sh.shapeType = ParticleSystemShapeType.Cone;
        sh.angle = 20f;
        var grow = smoke.sizeOverLifetime;
        grow.enabled = true;
        grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 2.5f));
        FadeOut(smoke);

        // no glow on the face itself: only the beams come out of the eyes
        flare.gameObject.SetActive(false);
        eyeLight.enabled = false;
        SetIdle();
    }

    // Glow of the eye alone: 0 = dim ember, 1 = white-hot.
    public void Idle(float heat)
    {
        SetFlare(flare, flareMat, 0.02f + 0.02f * heat, Color.Lerp(new Color(1f, 0.15f, 0.05f, 0.35f), new Color(1f, 0.6f, 0.4f, 0.8f), heat));
        eyeLight.intensity = 0.15f + 0.5f * heat;
    }

    public void SetIdle()
    {
        Idle(0f);
        core.enabled = glow.enabled = haze.enabled = aim.enabled = false;
        chargeQuad.gameObject.SetActive(false);
        SetEmission(sparks, 0f);
        SetEmission(smoke, 0f);
        impactLight.intensity = 0f;
        impactFlare.gameObject.SetActive(false);
        beamLength = 0f;
    }

    // Charging, k from 0 to 1: only a thin aim line, which steadies and brightens as the charge completes.
    public void Charge(float k, Vector3 target)
    {
        // play the Blender charge animation over the eye: frame 0 at the start, frame 15 just before firing
        int frame = Mathf.Min(15, Mathf.FloorToInt(k * 16f));
        chargeMat.mainTextureOffset = new Vector2((frame % 4) * 0.25f, 0.75f - (frame / 4) * 0.25f);
        chargeQuad.gameObject.SetActive(true);

        // aim line: a faint, jittering thread that narrows and steadies as the charge completes
        aim.enabled = Random.value > 0.25f * (1f - k);      // flickers early on
        aim.widthMultiplier = Mathf.Lerp(0.02f, 0.004f, k);
        var c = new Color(1f, Mathf.Lerp(0.15f, 0.6f, k), 0.1f, Mathf.Lerp(0.15f, 0.8f, k));
        aim.startColor = c;
        aim.endColor = new Color(c.r, c.g, c.b, c.a * 0.4f);
        var jitter = Random.insideUnitSphere * 0.05f * (1f - k);
        aim.SetPosition(0, eye.position);
        aim.SetPosition(1, target + jitter);
    }

    // The beam, from this eye to `hit` (world). `age` is seconds since it fired; `fade` 1 = full, 0 = gone.
    public void Beam(Vector3 hit, Vector3 normal, bool hitSomething, float age, float fade)
    {
        aim.enabled = false;
        chargeQuad.gameObject.SetActive(false);
        var from = eye.position;
        var full = hit - from;
        // the beam lunges out in a tenth of a second instead of appearing all at once
        beamLength = Mathf.MoveTowards(beamLength, full.magnitude, 120f * Time.deltaTime);
        float len = Mathf.Min(beamLength, full.magnitude);
        var dir = full.normalized;
        var to = from + dir * len;

        // dead straight and steady, like a security laser; just a faint hum in its brightness
        float flicker = 0.95f + 0.1f * Mathf.PerlinNoise(Time.time * 25f, noiseSeed);
        float snap = Mathf.Exp(-age * 12f);                    // the extra-bright flash as it fires
        haze.enabled = false;                                 // pure lasers: no haze
        foreach (var l in new[] { core, glow })
        {
            l.SetPosition(0, from);
            l.SetPosition(1, to);
            l.enabled = true;
            l.material.mainTextureOffset = new Vector2(-Time.time * 2f, 0f);   // a slow shimmer along it
        }
        core.widthMultiplier = (0.006f + 0.01f * snap) * flicker * fade;
        glow.widthMultiplier = (0.02f + 0.03f * snap) * flicker * fade;

        SetFlare(flare, flareMat, (0.16f + 0.25f * snap) * flicker * fade, new Color(1f, 0.85f, 0.75f, 1f));
        eyeLight.intensity = (4f + 6f * snap) * fade;
        eyeLight.range = 1.5f;

        bool landed = hitSomething && len >= full.magnitude - 0.01f && fade > 0.3f;
        // pure lasers: no sparks, smoke or glow where it lands, just the burn mark
        if (!landed) return;
        var impact = impactFlare.parent;
        impact.position = hit + normal * 0.02f;
        impact.rotation = Quaternion.LookRotation(normal);
        // no burn marks: the lasers leave nothing behind
    }

    // Burn marks left where the beam dragged across things. They glow orange-hot at first, then cool and fade.
    static void Scorch(Vector3 at, Vector3 normal)
    {
        if (scorchMat == null)
        {
            scorchMat = new Material(Shader.Find("Sprites/Default")) { mainTexture = scorchTex };
        }
        Transform t;
        Material m;
        if (scorches.Count >= MaxScorches)
        {
            (t, m, _) = scorches[0];
            scorches.RemoveAt(0);
        }
        else
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = "Scorch";
            Object.DestroyImmediate(q.GetComponent<Collider>());   // now, so this frame's beam can't hit it
            m = new Material(scorchMat);
            var r = q.GetComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            t = q.transform;
        }
        t.position = at + normal * 0.005f;
        t.rotation = Quaternion.LookRotation(-normal) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
        t.localScale = Vector3.one * Random.Range(0.12f, 0.2f);
        scorches.Add((t, m, Time.time));
    }

    // Called once per frame by StatueGaze: ages the scorch marks.
    public static void UpdateScorches()
    {
        foreach (var (t, m, born) in scorches)
        {
            float age = Time.time - born;
            float hot = Mathf.Exp(-age * 2.5f);
            float alpha = Mathf.Clamp01(1f - (age - ScorchLife * 0.6f) / (ScorchLife * 0.4f));
            m.color = new Color(Mathf.Lerp(0.05f, 1f, hot), Mathf.Lerp(0.04f, 0.45f, hot), Mathf.Lerp(0.03f, 0.1f, hot), 0.85f * alpha);
            t.gameObject.SetActive(alpha > 0f);
        }
    }

    // --- helpers ------------------------------------------------------------------------------------

    static LineRenderer Line(Transform parent, string name, Material material, Color color)
    {
        var l = new GameObject(name).AddComponent<LineRenderer>();
        l.transform.SetParent(parent, false);
        l.material = material;
        l.textureMode = LineTextureMode.Tile;
        l.positionCount = 2;
        l.startColor = color;
        l.endColor = new Color(color.r, color.g, color.b, color.a * 0.85f);
        l.numCapVertices = 0;
        l.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        l.receiveShadows = false;
        l.enabled = false;
        return l;
    }

    // Additive, HDR-tinted material: adds light on top of whatever is behind it, like a real beam.
    static Material Additive(Texture2D tex, Color hdr)
    {
        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null)
            return new Material(Shader.Find("Sprites/Default")) { mainTexture = tex, color = hdr };
        var m = new Material(shader);
        m.SetTexture("_BaseMap", tex);
        m.SetColor("_BaseColor", hdr);
        m.SetFloat("_Surface", 1f);                                   // transparent
        m.SetFloat("_Blend", 2f);                                     // additive
        m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
        m.SetFloat("_ZWrite", 0f);
        m.SetFloat("_Cull", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        return m;
    }

    static Transform Quad(Transform parent, string name, out Material mat)
    {
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        q.name = name;
        Object.Destroy(q.GetComponent<Collider>());
        q.transform.SetParent(parent, false);
        mat = new Material(Shader.Find("Sprites/Default")) { mainTexture = softDot };
        var r = q.GetComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        q.AddComponent<Billboard>();
        return q.transform;
    }

    static void SetFlare(Transform t, Material m, float size, Color c)
    {
        t.localScale = Vector3.one * size;
        m.color = c;
    }

    static ParticleSystem Particles(Transform parent, string name, bool world)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var m = ps.main;
        m.loop = true;
        m.playOnAwake = true;
        m.maxParticles = 400;
        m.simulationSpace = world ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
        var e = ps.emission;
        e.rateOverTime = 0f;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.material = world && name == "Sparks" ? Additive(softDot, new Color(3f, 2f, 1.2f, 1f))
                                                 : new Material(Shader.Find("Sprites/Default")) { mainTexture = softDot };
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ps.Play();
        return ps;
    }

    static void SetEmission(ParticleSystem ps, float rate)
    {
        var e = ps.emission;
        e.rateOverTime = rate;
    }

    static void FadeOut(ParticleSystem ps)
    {
        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = Gradient(Color.white, Color.white, 1f, 0f);
    }

    static ParticleSystem.MinMaxGradient Gradient(Color a, Color b, float alphaA, float alphaB)
    {
        var g = new UnityEngine.Gradient();
        g.SetKeys(new[] { new GradientColorKey(a, 0f), new GradientColorKey(b, 1f) },
                  new[] { new GradientAlphaKey(alphaA, 0f), new GradientAlphaKey(alphaA, 0.6f), new GradientAlphaKey(alphaB, 1f) });
        return new ParticleSystem.MinMaxGradient(g);
    }

    static void MakeTextures()
    {
        if (softDot != null) return;
        // soft round dot for flares, sparks and smoke
        softDot = new Texture2D(64, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
            {
                float r = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(32f, 32f)) / 32f;
                float a = Mathf.Pow(Mathf.Clamp01(1f - r), 2.2f);
                softDot.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        softDot.Apply();

        // beam: soft falloff across its width (v) and bright energy pulses along its length (u)
        beamTex = new Texture2D(128, 32, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat };
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 128; x++)
            {
                float across = Mathf.Abs((y + 0.5f) / 32f - 0.5f) * 2f;
                float falloff = Mathf.Pow(Mathf.Clamp01(1f - across), 1.6f);
                float along = x / 128f;
                float pulse = 0.92f + 0.08f * Mathf.Sin(along * Mathf.PI * 2f * 3f);
                beamTex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(falloff * pulse)));
            }
        beamTex.Apply();

        // scorch mark: ragged dark blot, lighter at the rim
        scorchTex = new Texture2D(64, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
            {
                var p = new Vector2(x - 32f, y - 32f) / 32f;
                float ang = Mathf.Atan2(p.y, p.x);
                float edge = 0.75f + 0.15f * Mathf.Sin(ang * 5f + 1f) + 0.1f * Mathf.PerlinNoise(x * 0.2f, y * 0.2f);
                float a = Mathf.Clamp01((edge - p.magnitude) * 3f) * (0.7f + 0.3f * Mathf.PerlinNoise(x * 0.35f + 7f, y * 0.35f));
                scorchTex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        scorchTex.Apply();
    }
}
