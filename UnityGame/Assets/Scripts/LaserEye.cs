using System.Collections.Generic;
using UnityEngine;

// All the visuals for one of the statue's laser eyes: the charge-up (sparks swirling in, a pulsing flare,
// a tightening aim line, the eye lighting up the face), the beam itself (white-hot core in a red glow with
// energy pulses running along it and a heat shimmer), and what happens where it lands (sparks, smoke,
// flickering light and scorch marks that slowly fade). StatueGaze drives it; it holds no game logic.
public class LaserEye
{
    readonly Transform eye;
    readonly LineRenderer core, glow, aim;
    readonly ParticleSystem charge, sparks, smoke;
    readonly Light eyeLight, impactLight;
    readonly Transform flare, impactFlare;
    readonly Material flareMat, impactFlareMat;
    float beamLength, lastScorch;
    readonly float noiseSeed = Random.Range(0f, 100f);   // each eye flickers differently

    static Texture2D softDot, beamTex, scorchTex;
    static Material scorchMat;
    static readonly List<(Transform t, Material m, float born)> scorches = new List<(Transform, Material, float)>();
    const int MaxScorches = 60;
    const float ScorchLife = 10f;

    public LaserEye(Transform eye)
    {
        this.eye = eye;
        MakeTextures();
        var root = new GameObject("Laser").transform;
        root.SetParent(eye, false);

        glow = Line(root, "BeamGlow", beamTex, new Color(1f, 0.1f, 0.05f, 0.55f));
        core = Line(root, "BeamCore", beamTex, new Color(1f, 0.95f, 0.85f, 1f));
        aim = Line(root, "AimLine", softDot, new Color(1f, 0.2f, 0.1f, 0.5f));
        aim.positionCount = 2;

        flare = Quad(root, "EyeFlare", out flareMat);
        flare.localPosition = new Vector3(0f, 0f, 0.03f);          // just in front of the face, not inside it
        eyeLight = new GameObject("EyeLight").AddComponent<Light>();
        eyeLight.transform.SetParent(root, false);
        eyeLight.transform.localPosition = new Vector3(0f, 0f, 0.04f);
        eyeLight.type = LightType.Point;
        eyeLight.range = 0.6f;
        eyeLight.color = new Color(1f, 0.2f, 0.1f);
        eyeLight.intensity = 0f;

        charge = Particles(root, "ChargeSparks", false);
        var m = charge.main;
        m.startLifetime = 0.35f;
        m.startSpeed = -1f;                                     // emitted on a sphere, flying inward to the eye
        m.startSize = new ParticleSystem.MinMaxCurve(0.006f, 0.016f);
        m.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.25f, 0.1f), new Color(1f, 0.7f, 0.4f));
        m.simulationSpace = ParticleSystemSimulationSpace.Local;
        var sh = charge.shape;
        sh.shapeType = ParticleSystemShapeType.Sphere;
        sh.radius = 0.35f;
        sh.radiusThickness = 0f;
        var orbit = charge.velocityOverLifetime;             // a swirl as they fall in
        orbit.enabled = true;
        orbit.orbitalZ = 6f;
        orbit.space = ParticleSystemSimulationSpace.Local;
        FadeOut(charge);

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
        m = sparks.main;
        m.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
        m.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 5f);
        m.startSize = new ParticleSystem.MinMaxCurve(0.008f, 0.02f);
        m.gravityModifier = 1.2f;
        m.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.9f, 0.5f), new Color(1f, 0.4f, 0.1f));
        sh = sparks.shape;
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
        core.enabled = glow.enabled = aim.enabled = false;
        SetEmission(charge, 0f);
        SetEmission(sparks, 0f);
        SetEmission(smoke, 0f);
        impactLight.intensity = 0f;
        impactFlare.gameObject.SetActive(false);
        beamLength = 0f;
    }

    // Charging, k from 0 to 1: sparks pour in faster, the flare pulses quicker and whiter, the aim line tightens.
    public void Charge(float k, Vector3 target)
    {
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * Mathf.Lerp(6f, 40f, k * k));
        SetFlare(flare, flareMat, Mathf.Lerp(0.04f, 0.13f, k) * (0.8f + 0.3f * pulse),
                 Color.Lerp(new Color(1f, 0.2f, 0.08f, 0.6f), new Color(1f, 0.9f, 0.8f, 1f), k * k));
        eyeLight.intensity = Mathf.Lerp(0.5f, 4f, k) * (0.8f + 0.4f * pulse);
        eyeLight.range = Mathf.Lerp(0.5f, 1.2f, k);
        SetEmission(charge, Mathf.Lerp(20f, 160f, k));

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
        SetEmission(charge, 0f);
        var from = eye.position;
        var full = hit - from;
        // the beam lunges out in a tenth of a second instead of appearing all at once
        beamLength = Mathf.MoveTowards(beamLength, full.magnitude, 120f * Time.deltaTime);
        float len = Mathf.Min(beamLength, full.magnitude);
        var dir = full.normalized;
        var to = from + dir * len;

        float flicker = 0.85f + 0.3f * Mathf.PerlinNoise(Time.time * 30f, noiseSeed);
        float snap = Mathf.Exp(-age * 12f);                    // the extra-bright flash as it fires
        WobbleLine(core, from, to, 0.004f, Time.time);
        WobbleLine(glow, from, to, 0.012f, Time.time + 3f);
        core.enabled = glow.enabled = true;
        core.widthMultiplier = (0.018f + 0.03f * snap) * flicker * fade;
        glow.widthMultiplier = (0.09f + 0.2f * snap) * flicker * Mathf.Sqrt(fade);
        foreach (var l in new[] { core, glow })                // energy pulses racing along the beam
            l.material.mainTextureOffset = new Vector2(-Time.time * 6f, 0f);

        SetFlare(flare, flareMat, (0.16f + 0.25f * snap) * flicker * fade, new Color(1f, 0.85f, 0.75f, 1f));
        eyeLight.intensity = (4f + 6f * snap) * fade;
        eyeLight.range = 1.5f;

        bool landed = hitSomething && len >= full.magnitude - 0.01f && fade > 0.3f;
        impactFlare.gameObject.SetActive(landed);
        SetEmission(sparks, landed ? 90f * fade : 0f);
        SetEmission(smoke, landed ? 14f * fade : 0f);
        impactLight.intensity = landed ? (3f + 3f * Random.value) * fade : 0f;
        if (!landed) return;
        var impact = impactFlare.parent;
        impact.position = hit + normal * 0.02f;
        impact.rotation = Quaternion.LookRotation(normal);
        SetFlare(impactFlare, impactFlareMat, (0.25f + 0.15f * Random.value) * fade, new Color(1f, 0.75f, 0.45f, 1f));
        if (Time.time - lastScorch > 0.06f)
        {
            lastScorch = Time.time;
            Scorch(hit, normal);
        }
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

    static void WobbleLine(LineRenderer l, Vector3 from, Vector3 to, float amp, float t)
    {
        int n = l.positionCount;
        var d = to - from;
        var side = Vector3.Cross(d.normalized, Vector3.up);
        if (side.sqrMagnitude < 0.01f) side = Vector3.right;
        side.Normalize();
        var up = Vector3.Cross(side, d.normalized);
        for (int i = 0; i < n; i++)
        {
            float f = i / (float)(n - 1);
            float w = amp * Mathf.Sin(f * Mathf.PI);           // heat shimmer, strongest mid-beam
            l.SetPosition(i, from + d * f + side * Mathf.Sin(t * 37f + i * 1.7f) * w + up * Mathf.Sin(t * 29f + i * 2.3f) * w);
        }
    }

    static LineRenderer Line(Transform parent, string name, Texture2D tex, Color color)
    {
        var l = new GameObject(name).AddComponent<LineRenderer>();
        l.transform.SetParent(parent, false);
        l.material = new Material(Shader.Find("Sprites/Default")) { mainTexture = tex };
        l.textureMode = LineTextureMode.Tile;
        l.positionCount = 12;
        l.startColor = color;
        l.endColor = new Color(color.r, color.g, color.b, color.a * 0.85f);
        l.numCapVertices = 4;
        l.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        l.receiveShadows = false;
        l.enabled = false;
        return l;
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
        r.material = new Material(Shader.Find("Sprites/Default")) { mainTexture = softDot };
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
                float pulse = 0.75f + 0.25f * Mathf.Sin(along * Mathf.PI * 2f * 3f) + 0.15f * Mathf.Sin(along * Mathf.PI * 2f * 7f);
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
