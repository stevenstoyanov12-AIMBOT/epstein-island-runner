using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// Brings the autumn tree and the laser-eyed watching bust over from the UnityGame project.
// Places one bust on each of the museum's 4 sides and one tree in the Roman garden.
// Runs once automatically when the project opens; re-run from Tools > Place Statues And Tree.
[InitializeOnLoad]
public static class ImportStatuesAndTree
{
    const string Mat = "Assets/Materials/Statue";
    const string Flag = "MuseumStatuesPlaced_v1";
    static ImportStatuesAndTree()
    {
        if (EditorPrefs.GetBool(Flag)) return;
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorSceneManager.GetActiveScene().path != "Assets/game.unity") return;
            Place(); EditorPrefs.SetBool(Flag, true);
        };
    }

    [MenuItem("Tools/Place Statues And Tree")]
    public static void Place()
    {
        if (!AssetDatabase.IsValidFolder(Mat)) AssetDatabase.CreateFolder("Assets/Materials", "Statue");
        var old = GameObject.Find("WatchingStatues"); if (old) Object.DestroyImmediate(old);
        var oldT = GameObject.Find("AutumnTree"); if (oldT) Object.DestroyImmediate(oldT);
        var marble = M("Marble", new Color(0.9f, 0.88f, 0.83f), 0.6f);
        var stone = M("Stone", new Color(0.62f, 0.6f, 0.57f), 0.2f);
        var root = new GameObject("WatchingStatues").transform;
        // museum footprint ~ x -29.9..28.3, z -25..22.3; busts stand on the plaza facing outward from each side
        var spots = new[] {
            (new Vector3(-1f, 0f, 30f), 0f), (new Vector3(-1f, 0f, -33f), 180f),
            (new Vector3(37f, 0f, -8f), 90f), (new Vector3(-38f, 0f, -12f), 270f) };
        int i = 0;
        foreach (var (p, yaw) in spots)
        {
            var b = Bust("Statue_" + (++i), root, Ground(p), yaw, marble, stone);
            Watch(b.transform);
        }
        var g = GameObject.Find("RomanGarden");
        var tp = g ? g.transform.position + new Vector3(-7f, 0f, 3.5f) : new Vector3(-50f, 0f, 49f);
        Tree(Ground(tp));
        var player = GameObject.Find("Player");
        if (player && !player.GetComponent<GazeTarget>())
        {
            var gt = player.AddComponent<GazeTarget>(); gt.aimOffset = new Vector3(0f, 1f, 0f);
        }
        EditorSceneManager.MarkAllScenesDirty(); EditorSceneManager.SaveOpenScenes();
        Debug.Log("Placed 4 watching statues and the autumn tree.");
    }

    static Vector3 Ground(Vector3 p)
    {
        if (Physics.Raycast(p + Vector3.up * 20f, Vector3.down, out var h, 40f, ~0, QueryTriggerInteraction.Ignore)) p.y = h.point.y;
        return p;
    }

    static Material M(string n, Color c, float sm)
    {
        string path = $"{Mat}/{n}.mat"; var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
        m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", sm); EditorUtility.SetDirty(m); return m;
    }
    static Material Glow(string n, Color c, Texture2D t = null)
    {
        string path = $"{Mat}/{n}.mat"; var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Unlit")); AssetDatabase.CreateAsset(m, path); }
        m.SetColor("_BaseColor", c); if (t) m.SetTexture("_BaseMap", t); m.SetFloat("_Cull", 0f); EditorUtility.SetDirty(m); return m;
    }
    static Mesh LoadMesh(string p) { foreach (var a in AssetDatabase.LoadAllAssetsAtPath(p)) if (a is Mesh m) return m; return null; }
    static Texture2D Tex(string p, bool normal)
    {
        if (normal && AssetImporter.GetAtPath(p) is TextureImporter ti && ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; ti.SaveAndReimport(); }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
    }

    static GameObject Bust(string name, Transform parent, Vector3 pos, float yaw, Material marble, Material stone)
    {
        const float plaza = 0.1f, ped = 1f;
        var root = new GameObject(name).transform; root.SetParent(parent, false); root.position = pos; root.rotation = Quaternion.Euler(0, yaw, 0);
        var pc = GameObject.CreatePrimitive(PrimitiveType.Cube); pc.name = "Pedestal"; pc.transform.SetParent(root, false);
        pc.transform.localPosition = new Vector3(0, plaza + ped / 2f, 0); pc.transform.localScale = new Vector3(0.55f, ped, 0.45f); pc.GetComponent<Renderer>().sharedMaterial = stone;
        var mesh = LoadMesh("Assets/Models/Statues/Bust_Portrait.obj");
        var head = new GameObject("Head").transform; head.SetParent(root, false); head.localPosition = new Vector3(0, plaza + ped, 0);
        var bust = new GameObject("Bust"); bust.transform.SetParent(head, false); bust.transform.localRotation = Quaternion.Euler(0, 180, 0);
        bust.AddComponent<MeshFilter>().sharedMesh = mesh; bust.AddComponent<MeshRenderer>().sharedMaterial = marble;
        if (mesh) bust.AddComponent<MeshCollider>().sharedMesh = mesh;
        var drape = LoadMesh("Assets/Models/Statues/Bust_Portrait_Drape.obj");
        if (drape) { var d = new GameObject("Drape"); d.transform.SetParent(bust.transform, false); d.AddComponent<MeshFilter>().sharedMesh = drape; d.AddComponent<MeshRenderer>().sharedMaterial = marble; }
        return root.gameObject;
    }

    static void Watch(Transform bustRoot)
    {
        var head = bustRoot.Find("Head");
        var iris = Glow("StatueEyes", new Color(0.8f, 0.5f, 0.5f), AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Models/Statues/Textures/statue_eye.png"));
        var pupil = Glow("StatuePupil", new Color(0.01f, 0f, 0f));
        var rim = Glow("StatuePupilRim", new Color(1f, 0.8f, 0.45f));
        var irisCap = Saved("StatueIris", EyeMesh(0.46f, 36, 8, 1.003f, true));
        var rimCap = Saved("StatuePupilRim", EyeMesh(0.19f, 24, 4, 1.006f, false));
        var pupilCap = Saved("StatuePupil", EyeMesh(0.15f, 24, 4, 1.009f, false));
        var eyes = new Transform[2]; var pupils = new Transform[2];
        for (int i = 0; i < 2; i++)
        {
            var eye = new GameObject(i == 0 ? "Eye_L" : "Eye_R").transform; eye.SetParent(head, false);
            eye.localPosition = new Vector3(i == 0 ? -0.033f : 0.031f, 0.5995f, i == 0 ? 0.067f : 0.066f);
            var pv = new GameObject("PupilPivot").transform; pv.SetParent(eye, false);
            Part(pv, "Iris", irisCap, iris); Part(pv, "PupilRim", rimCap, rim); Part(pv, "Pupil", pupilCap, pupil);
            eyes[i] = eye; pupils[i] = pv;
        }
        var gz = bustRoot.gameObject.AddComponent<StatueGaze>();
        gz.head = head; gz.eyes = eyes; gz.pupils = pupils;
        gz.chargeFlipbook = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Models/Statues/Textures/eye_charge.png");
    }
    static void Part(Transform p, string n, Mesh m, Material mat)
    {
        var g = new GameObject(n); g.transform.SetParent(p, false); g.AddComponent<MeshFilter>().sharedMesh = m;
        var r = g.AddComponent<MeshRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off;
    }
    static Mesh Saved(string n, Mesh m)
    {
        string path = $"{Mat}/{n}.asset"; var ex = AssetDatabase.LoadAssetAtPath<Mesh>(path); if (ex) return ex;
        m.name = n; AssetDatabase.CreateAsset(m, path); return m;
    }
    static float EyeSurface(float th) { const float r0 = 0.013f; float z = r0 * Mathf.Cos(th); return r0 * (1f + 0.12f * Mathf.Max(0f, (z - 0.009f) / 0.004f)) * 1.05f; }
    static Mesh EyeMesh(float maxTh, int seg, int rings, float lift, bool uv)
    {
        var v = new List<Vector3>(); var u = new List<Vector2>(); var t = new List<int>();
        for (int r = 0; r <= rings; r++)
        {
            float th = maxTh * r / rings; float rad = (maxTh < Mathf.PI ? EyeSurface(0f) : EyeSurface(th)) * lift;
            for (int s = 0; s <= seg; s++) { float ph = 2f * Mathf.PI * s / seg; v.Add(new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Sin(th) * Mathf.Sin(ph), Mathf.Cos(th)) * rad); u.Add(new Vector2((float)s / seg, 1f - th / Mathf.PI)); }
        }
        for (int r = 0; r < rings; r++) for (int s = 0; s < seg; s++) { int a = r * (seg + 1) + s, b = a + seg + 1; t.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 }); }
        var mm = new Mesh(); mm.SetVertices(v); if (uv) mm.SetUVs(0, u); mm.SetTriangles(t, 0); mm.RecalculateNormals(); mm.RecalculateBounds(); return mm;
    }

    static GameObject AddMesh(Transform parent, string n, string path, Material mat)
    {
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(path); if (!src) { Debug.LogError("Missing " + path); return null; }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(src); go.name = n; go.transform.SetParent(parent, false);
        foreach (var r in go.GetComponentsInChildren<Renderer>()) r.sharedMaterial = mat;
        var mf = go.GetComponentInChildren<MeshFilter>(); return mf ? mf.gameObject : go;
    }
    static void Tree(Vector3 pos)
    {
        const string F = "Assets/Models/Trees";
        var root = new GameObject("AutumnTree"); root.transform.position = pos + Vector3.up * 0.1f;
        var bark = M("Bark", Color.white, 0.1f); bark.SetTexture("_BaseMap", Tex($"{F}/Textures/bark_albedo.png", false)); bark.SetTexture("_BumpMap", Tex($"{F}/Textures/bark_normal.png", true)); bark.EnableKeyword("_NORMALMAP");
        var leaves = M("Leaves", Color.white, 0.3f); leaves.SetTexture("_BaseMap", Tex($"{F}/Textures/leaves_atlas.png", false)); leaves.SetTexture("_BumpMap", Tex($"{F}/Textures/leaves_normal.png", true));
        leaves.EnableKeyword("_NORMALMAP"); leaves.SetFloat("_AlphaClip", 1f); leaves.SetFloat("_Cutoff", 0.5f); leaves.EnableKeyword("_ALPHATEST_ON"); leaves.SetFloat("_Cull", 0f); leaves.doubleSidedGI = true;
        var trunk = AddMesh(root.transform, "Bark", $"{F}/Tree_Bark.fbx", bark);
        if (trunk) trunk.AddComponent<MeshCollider>().sharedMesh = trunk.GetComponent<MeshFilter>().sharedMesh;
        var crown = AddMesh(root.transform, "Leaves", $"{F}/Tree_Leaves.fbx", leaves);
        if (crown)
        {
            var fl = root.AddComponent<FallingLeaves>(); var vs = crown.GetComponent<MeshFilter>().sharedMesh.vertices;
            fl.spawnPoints = new Vector3[vs.Length / 9]; for (int i = 0; i < fl.spawnPoints.Length; i++) fl.spawnPoints[i] = vs[i * 9]; fl.leafMaterial = leaves;
        }
        AddMesh(root.transform, "FallenLeaves", $"{F}/Tree_FallenLeaves.fbx", leaves);
        var grass = M("TreeGrass", Color.white, 0.15f); grass.SetTexture("_BaseMap", Tex($"{F}/Textures/grass.png", false)); grass.SetFloat("_Cull", 0f);
        var tuft = AddMesh(root.transform, "Grass", $"{F}/Tree_Grass.fbx", grass);
        if (tuft) tuft.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        AssetDatabase.SaveAssets();
    }
}
