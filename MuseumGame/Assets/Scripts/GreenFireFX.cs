using UnityEngine;

// Layered green fire: core flames, outer flames, embers and smoke, plus a flickering light.
public class GreenFireFX : MonoBehaviour
{
    public float height = 1.4f;
    Light glow; float baseInt;

    static Material mat, sheet;
    static Material Mat()
    {
        if (mat) return mat;
        mat = Res.Load<Material>("FX_GreenFire");
        return mat;
    }

    ParticleSystem Make(string n, Color a, Color b, float life, float speed, float size, float rate, float radius, float noise, int max, bool add = true, bool flame = false)
    {
        var go = new GameObject(n); go.transform.SetParent(transform, false); go.transform.localPosition = new Vector3(0, height, 0);
        go.transform.localRotation = Quaternion.Euler(-90, 0, 0);
        var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var m = ps.main; m.loop = true; m.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.6f, life); m.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.6f, speed);
        m.startSize = new ParticleSystem.MinMaxCurve(size * 0.6f, size); m.maxParticles = Mathf.Min(max, 50); m.simulationSpace = ParticleSystemSimulationSpace.World;
        m.startRotation = new ParticleSystem.MinMaxCurve(0, 6.28f); m.gravityModifier = -0.05f; var lim = ps.limitVelocityOverLifetime; lim.enabled = false;
        var e = ps.emission; e.rateOverTime = rate;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 8; sh.radius = radius;
        var col = ps.colorOverLifetime; col.enabled = true; var gr = new Gradient();
        gr.SetKeys(new[] { new GradientColorKey(a, 0), new GradientColorKey(b, 0.6f), new GradientColorKey(b * 0.3f, 1) },
                   new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.12f), new GradientAlphaKey(0.7f, 0.5f), new GradientAlphaKey(0, 1) });
        col.color = gr;
        var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1, new AnimationCurve(new Keyframe(0, 0.6f), new Keyframe(0.3f, 1f), new Keyframe(1, 0.1f)));
        var nz = ps.noise; nz.enabled = noise > 0; nz.strength = noise; nz.frequency = 1.6f; nz.scrollSpeed = 1.2f; nz.octaveCount = 2; nz.separateAxes = true; nz.strengthX = noise; nz.strengthY = 0f; nz.strengthZ = noise;
        var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-1.5f, 1.5f);
        var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = Mat(); r.pivot = new Vector3(0, 0.5f, 0);
        if (flame) { if (!sheet) sheet = Res.Load<Material>("FX_GreenFlameSheet"); r.sharedMaterial = sheet; var ts = ps.textureSheetAnimation; ts.enabled = true; ts.numTilesX = 4; ts.numTilesY = 4; ts.frameOverTime = new ParticleSystem.MinMaxCurve(1, new AnimationCurve(new Keyframe(0,0), new Keyframe(1,1))); ts.startFrame = new ParticleSystem.MinMaxCurve(0, 15); ts.cycleCount = 2; m.startRotation = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f); rot.enabled = false; r.alignment = ParticleSystemRenderSpace.View; } r.sortingFudge = add ? 0 : 10;
        ps.Play(); return ps;
    }

    void Start()
    {
        Make("FlameTongues", new Color(0.9f, 1f, 0.85f), new Color(0.3f, 1f, 0.35f), 0.9f, 0.9f, 0.9f, 22, 0.18f, 0.25f, 60, true, true);
        Make("FlameTongues2", new Color(0.5f, 1f, 0.5f), new Color(0.05f, 0.7f, 0.12f), 1.1f, 1.0f, 1.25f, 12, 0.3f, 0.35f, 40, true, true);
        

        var emb = Make("Embers", new Color(0.8f, 1f, 0.6f), new Color(0.2f, 1f, 0.3f), 2.2f, 2.2f, 0.03f, 12, 0.25f, 1.4f, 40);
        var em = emb.main; em.gravityModifier = -0.15f;
        var smoke = Make("Smoke", new Color(0.1f, 0.14f, 0.1f), new Color(0.05f, 0.06f, 0.05f), 3.2f, 0.6f, 0.9f, 4, 0.2f, 0.4f, 30, false);
        smoke.transform.localPosition = new Vector3(0, height + 0.9f, 0);
        glow = GetComponentInChildren<Light>(); if (glow) baseInt = glow.intensity;
    }

    void Update()
    {
        if (!glow) return;
        float t = Time.time * 7f;
        glow.intensity = baseInt * (0.8f + 0.25f * Mathf.PerlinNoise(t, transform.position.x) + 0.1f * Mathf.Sin(t * 2.3f));
    }
}
