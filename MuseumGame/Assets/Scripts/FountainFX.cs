using UnityEngine;
using System.Collections.Generic;

// Realistic fountain: flowing water curtains off each bowl rim, rippling water surfaces, spout, splashes and mist.
public class FountainFX : MonoBehaviour
{
    static Material dropMat;
    Material surfMat, curtainMat;

    Mesh Curtain(float r0, float r1, float y0, float y1, float pow, int seg = 128, int rows = 24)
    {
        var m = new Mesh(); var v = new List<Vector3>(); var uv = new List<Vector2>(); var n = new List<Vector3>(); var tri = new List<int>();
        for (int j = 0; j <= rows; j++)
        {
            float t = j / (float)rows; float r = r0 + (r1 - r0) * Mathf.Pow(t, pow); float y = Mathf.Lerp(y0, y1, t * t * 0.35f + t * 0.65f);
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f; var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                v.Add(d * r + Vector3.up * y); uv.Add(new Vector2(i / (float)seg, t)); n.Add(d);
            }
        }
        for (int j = 0; j < rows; j++) for (int i = 0; i < seg; i++)
        { int a = j * (seg + 1) + i, b = a + seg + 1; tri.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 }); }
        m.SetVertices(v); m.SetUVs(0, uv); m.SetNormals(n); m.SetTriangles(tri, 0); m.RecalculateBounds(); return m;
    }

    Mesh Disc(float r, float y, int seg = 96, int rings = 24)
    {
        var m = new Mesh(); var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
        v.Add(new Vector3(0, y, 0)); uv.Add(new Vector2(0.5f, 0.5f));
        for (int j = 1; j <= rings; j++) for (int i = 0; i < seg; i++)
        { float a = i / (float)seg * Mathf.PI * 2f; float rr = r * j / rings; v.Add(new Vector3(Mathf.Cos(a) * rr, y, Mathf.Sin(a) * rr)); uv.Add(new Vector2(0.5f + Mathf.Cos(a) * rr / (2 * r), 0.5f + Mathf.Sin(a) * rr / (2 * r))); }
        for (int i = 0; i < seg; i++) tri.AddRange(new[] { 0, 1 + (i + 1) % seg, 1 + i });
        for (int j = 1; j < rings; j++) for (int i = 0; i < seg; i++)
        { int a = 1 + (j - 1) * seg + i, b = 1 + (j - 1) * seg + (i + 1) % seg, c = 1 + j * seg + i, d = 1 + j * seg + (i + 1) % seg; tri.AddRange(new[] { a, b, c, b, d, c }); }
        m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(tri, 0); m.RecalculateNormals(); m.RecalculateBounds(); return m;
    }

    void Part(string name, Mesh mesh, Material mat)
    {
        var go = new GameObject(name); go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh; var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
    }

    ParticleSystem PS(string n, Vector3 lp, Quaternion rot)
    {
        var go = new GameObject(n); go.transform.SetParent(transform, false); go.transform.localPosition = lp; go.transform.localRotation = rot;
        var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var m = ps.main; m.loop = true; m.simulationSpace = ParticleSystemSimulationSpace.World; m.maxParticles = 1500;
        var r = go.GetComponent<ParticleSystemRenderer>(); if (!dropMat) dropMat = Res.Load<Material>("FX_WaterDrop"); r.sharedMaterial = dropMat;
        return ps;
    }

    void Splash(string n, float radius, float y, float rate)
    {
        var ps = PS(n, new Vector3(0, y, 0), Quaternion.Euler(-90, 0, 0));
        var m = ps.main; m.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.55f); m.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.6f);
        m.startSize = new ParticleSystem.MinMaxCurve(0.008f, 0.02f); m.gravityModifier = 1f; m.startColor = new Color(0.8f, 0.88f, 0.95f, 0.35f);
        var e = ps.emission; e.rateOverTime = rate;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = radius; sh.radiusThickness = 0.05f; sh.arc = 360;
        var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.Local; vel.radial = new ParticleSystem.MinMaxCurve(-0.3f, 0.6f);
        var r = ps.GetComponent<ParticleSystemRenderer>(); r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.08f; r.lengthScale = 1.5f;
        ps.Play();
    }

    void Mist(float radius, float y)
    {
        var ps = PS("Mist", new Vector3(0, y, 0), Quaternion.Euler(-90, 0, 0));
        var m = ps.main; m.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 2.5f); m.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.25f);
        m.startSize = new ParticleSystem.MinMaxCurve(0.4f, 0.9f); m.gravityModifier = -0.01f; m.startColor = new Color(0.8f, 0.88f, 0.95f, 0.035f);
        m.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
        var e = ps.emission; e.rateOverTime = 18;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = radius; sh.radiusThickness = 0.1f;
        var col = ps.colorOverLifetime; col.enabled = true; var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.3f), new GradientAlphaKey(0, 1) }); col.color = g;
        ps.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.Billboard;
        ps.Play();
    }

    void Jet(float y)
    {
        var ps = PS("Spout", new Vector3(0, y, 0), Quaternion.Euler(-90, 0, 0));
        var m = ps.main; m.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.6f); m.startSpeed = new ParticleSystem.MinMaxCurve(2.0f, 2.4f);
        m.startSize = new ParticleSystem.MinMaxCurve(0.01f, 0.018f); m.gravityModifier = 1f; m.startColor = new Color(0.8f, 0.88f, 0.95f, 0.4f);
        var e = ps.emission; e.rateOverTime = 260;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 9; sh.radius = 0.015f;
        var r = ps.GetComponent<ParticleSystemRenderer>(); r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.05f; r.lengthScale = 2f;
        ps.Play();
    }

    void Start()
    {
        foreach (Transform c in transform) Destroy(c.gameObject);
        var sh = Shader.Find("Custom/FountainWater");
        surfMat = new Material(sh); surfMat.SetFloat("_Mode", 0); surfMat.SetFloat("_Opacity", 0.78f);
        curtainMat = new Material(sh); curtainMat.SetFloat("_Mode", 1); curtainMat.SetFloat("_Opacity", 0.6f); curtainMat.SetFloat("_Speed", 1.8f);
        curtainMat.renderQueue = 3010;

        // curtains off the rims
        Part("TopCurtain", Curtain(0.985f, 1.25f, 3.14f, 2.0f, 0.6f), curtainMat);
        Part("MidCurtain", Curtain(1.975f, 2.45f, 2.04f, 0.62f, 0.6f), curtainMat);

        // water surfaces (each gets its own ripple ring where the water lands)
        var top = new Material(surfMat); top.SetFloat("_RippleCenterR", 0.2f); Part("TopWater", Disc(0.96f, 3.1f), top);
        var mid = new Material(surfMat); mid.SetFloat("_RippleCenterR", 1.25f); Part("MidWater", Disc(1.92f, 2.0f), mid);
        var bas = new Material(surfMat); bas.SetFloat("_RippleCenterR", 2.45f); Part("BasinWater", Disc(3.6f, 0.62f), bas);

        Jet(3.8f);
        Splash("TopSplash", 1.25f, 2.01f, 220);
        Splash("BasinSplash", 2.45f, 0.63f, 420);
        Mist(2.45f, 0.7f);
    }
}
