using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Builds the intro scene: the 13 s crate cutscene from the Statue Garden project (van, cargo, night street).
// Tools > Build Intro Scene. The IntroLoader streams the game in while it plays.
public static class IntroSceneBuilder
{
    const string ScenePath = "Assets/Scenes/Intro.unity";

    [MenuItem("Tools/Build Intro Scene")]
    public static void Build()
    {
        beamMeshBuilt = false;
        if (!AssetDatabase.IsValidFolder(MaterialFolder)) AssetDatabase.CreateFolder("Assets/Materials", "Intro");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        RenderSettings.skybox = NightSkybox();
        RenderSettings.ambientMode = AmbientMode.Trilight;
        var crates = CrateMaterials();
        CutsceneVan(Vector3.zero, crates);
        var cam = new GameObject("Main Camera");   // black backdrop behind the loading screen once the cutscene ends
        var c = cam.AddComponent<Camera>(); c.clearFlags = CameraClearFlags.SolidColor; c.backgroundColor = Color.black; c.cullingMask = 0;
        cam.AddComponent<AudioListener>();
        new GameObject("IntroLoader").AddComponent<IntroLoader>();
        Bloom();
        EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log("Intro scene built: " + ScenePath);
    }

    const string MaterialFolder = "Assets/Materials/Intro";

    static Texture2D LoadTexture(string path, bool normalMap)
    {
        if (normalMap && AssetImporter.GetAtPath(path) is TextureImporter importer
            && importer.textureType != TextureImporterType.NormalMap)
        {
            importer.textureType = TextureImporterType.NormalMap;
            importer.SaveAndReimport();
        }
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (tex == null)
            Debug.LogError($"Missing texture {path}");
        return tex;
    }

    static GameObject AddMesh(Transform parent, string name, string path, params Material[] materials)
    {
        var mesh = LoadMesh(path);
        if (mesh == null)
        {
            Debug.LogError($"Missing mesh {path}");
            return null;
        }
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterials = materials;
        return go;
    }

    const string CutsceneFolder = "Assets/Models/Cutscene";

    const float CrateHeight = 1.21f;   // including the skids

    // [wood, nails, stencil]

    // [wood, nails, stencil]
    static Material[] CrateMaterials()
    {
        var wood = GetMaterial("CrateWood", Color.white, 0.2f, 0f);
        wood.SetTexture("_BaseMap", LoadTexture($"{CutsceneFolder}/Textures/wood_albedo.png", false));
        wood.SetTexture("_BumpMap", LoadTexture($"{CutsceneFolder}/Textures/wood_normal.png", true));
        wood.EnableKeyword("_NORMALMAP");
        var nails = GetMaterial("Nails", new Color(0.12f, 0.12f, 0.13f), 0.5f, 1f);
        var stencil = GetMaterial("CrateStencil", Color.white, 0.1f, 0f);
        stencil.SetTexture("_BaseMap", LoadTexture($"{CutsceneFolder}/Textures/stencil.png", false));
        stencil.SetFloat("_AlphaClip", 1f);
        stencil.SetFloat("_Cutoff", 0.4f);
        stencil.EnableKeyword("_ALPHATEST_ON");
        stencil.SetFloat("_Cull", (float)CullMode.Off);
        EditorUtility.SetDirty(wood);
        EditorUtility.SetDirty(stencil);
        return new[] { wood, nails, stencil };
    }

    static GameObject Crate(string name, Transform parent, Vector3 position, float yaw, Material[] m,
                            string mesh = "Crate", string stencilMesh = "Crate_Stencil")
    {
        var crate = AddMesh(parent, name, $"{CutsceneFolder}/{mesh}.fbx", m[0], m[1]);
        if (crate == null) return null;
        crate.transform.localPosition = position;
        crate.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        crate.AddComponent<BoxCollider>();
        if (stencilMesh == null) return crate;
        var stencil = AddMesh(crate.transform, "Stencil", $"{CutsceneFolder}/{stencilMesh}.fbx", m[2]);
        if (stencil != null)
            stencil.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        return crate;
    }

    // Closed van cargo area with the player's crate and a few others; the cutscene camera sits in the crate.
    // Blender (x, y, z) maps to Unity (-x, z, -y): the rear doors are at z = 0 and face +Z.

    // Closed van cargo area with the player's crate and a few others; the cutscene camera sits in the crate.
    // Blender (x, y, z) maps to Unity (-x, z, -y): the rear doors are at z = 0 and face +Z.
    static void CutsceneVan(Vector3 position, Material[] crates)
    {
        var root = new GameObject("CutsceneVan");
        root.transform.position = position;

        var steel = GetMaterial("VanSteel", Color.white, 0.35f, 0.4f);
        steel.SetTexture("_BaseMap", LoadTexture($"{CutsceneFolder}/Textures/metal_albedo.png", false));
        steel.SetTexture("_BumpMap", LoadTexture($"{CutsceneFolder}/Textures/metal_normal.png", true));
        steel.EnableKeyword("_NORMALMAP");
        var floor = GetMaterial("VanFloor", new Color(0.45f, 0.4f, 0.36f), 0.15f, 0f);
        floor.SetTexture("_BaseMap", LoadTexture($"{CutsceneFolder}/Textures/wood_albedo.png", false));
        EditorUtility.SetDirty(steel);
        EditorUtility.SetDirty(floor);

        var shell = AddMesh(root.transform, "Cargo", $"{CutsceneFolder}/VanCargo.fbx", steel, floor);
        if (shell != null)  // walls and floor the loose crates can crash into
            shell.AddComponent<MeshCollider>().sharedMesh = shell.GetComponent<MeshFilter>().sharedMesh;
        var doorL = AddMesh(root.transform, "Door_L", $"{CutsceneFolder}/VanDoor_L.fbx", steel);
        var doorR = AddMesh(root.transform, "Door_R", $"{CutsceneFolder}/VanDoor_R.fbx", steel);
        foreach (var (door, x) in new[] { (doorL, 1.2f), (doorR, -1.2f) })
        {
            if (door == null) continue;
            door.transform.localPosition = new Vector3(x, 0f, 0f);
            door.AddComponent<MeshCollider>().sharedMesh = door.GetComponent<MeshFilter>().sharedMesh;
        }

        const float floorTop = 0.024f, lowHeight = 0.36f;
        // the player's crate on two flat crates in the middle of the right-hand side, other cargo around it.
        // The van is exactly two crates wide: right column at x 0.55, left column at x -0.6.
        var cargo = new List<Rigidbody>();
        void Loose(GameObject c, float mass)
        {
            if (c == null) return;
            var rb = c.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.isKinematic = true;   // held in place until the crash
            cargo.Add(rb);
        }
        Loose(Crate("Crate_Low_1", root.transform, new Vector3(0.55f, floorTop, -2.3f), 0f, crates, "Crate_Low", null), 25f);
        Loose(Crate("Crate_Low_2", root.transform, new Vector3(0.55f, floorTop + lowHeight, -2.3f), 0f, crates, "Crate_Low", null), 25f);
        Loose(Crate("Crate_5", root.transform, new Vector3(0.55f, floorTop + 2f * lowHeight, -2.3f), 0f, crates), 80f);
        Loose(Crate("Crate_2", root.transform, new Vector3(0.55f, floorTop, -0.7f), 0f, crates), 60f);
        Loose(Crate("Crate_3", root.transform, new Vector3(-0.6f, floorTop, -0.65f), 0f, crates), 60f);
        Loose(Crate("Crate_Low_3", root.transform, new Vector3(-0.6f, floorTop, -1.8f), 0f, crates, "Crate_Low", null), 25f);

        // the player hides in the front-left corner, in the crate with a missing slat, stacked on two flat
        // crates; from there the rest of the cargo and the rear-door windows are in view
        var stash = new Vector3(-0.6f, floorTop, -2.95f);
        Crate("Crate_Low_P1", root.transform, stash, 0f, crates, "Crate_Low", null);
        Crate("Crate_Low_P2", root.transform, stash + Vector3.up * lowHeight, 0f, crates, "Crate_Low", null);
        var hero = Crate("Crate_Player", root.transform, stash + Vector3.up * 2f * lowHeight, 0f, crates,
                         "Crate_Peek", "Crate_Stencil_Back");
        if (hero == null) return;

        // eye just behind the missing slat, looking down the van toward the cargo and the windows
        var eye = new GameObject("Eye").transform;
        eye.SetParent(hero.transform, false);
        eye.localPosition = new Vector3(0f, 0.73f, 0.33f);
        // aimed a little low so the BATON CORPORATION crate by the doors is in frame under the windows
        eye.LookAt(root.transform.TransformPoint(new Vector3(0.1f, 0.85f, 0f)));

        var camGo = new GameObject("CutsceneCamera");
        camGo.transform.SetParent(root.transform, false);
        var cam = camGo.AddComponent<Camera>();
        cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
        cam.fieldOfView = 72f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 200f;
        cam.enabled = false;

        var cutscene = root.AddComponent<CrateCutscene>();
        cutscene.cutsceneCamera = cam;
        cutscene.eye = eye;
        cutscene.van = root.transform;
        cutscene.cargo = cargo.ToArray();
        // the crates by the doors burst into planks in the crash
        cutscene.breakable = cargo.FindAll(rb => rb.name == "Crate_2" || rb.name == "Crate_3").ToArray();
        cutscene.debrisMaterial = crates[0];
        cutscene.doorLeft = doorL != null ? doorL.transform : null;
        cutscene.doorRight = doorR != null ? doorR.transform : null;
        // the van body is a kinematic rigidbody so loose cargo collides properly while it slides and spins
        var body = root.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        cutscene.vanBody = body;
        NightStreetSet(root.transform, cutscene);
    }

    // The night street behind the van: road, pavements, lit building fronts and lamp posts on looping
    // 20 m segments, and a moon. Van-local: +Z is behind the van.

    // The night street behind the van: road, pavements, lit building fronts and lamp posts on looping
    // 20 m segments, and a moon. Van-local: +Z is behind the van.
    static void NightStreetSet(Transform van, CrateCutscene cutscene)
    {
        const float road = -0.62f;   // road surface below the cargo floor
        const int segments = 6;
        const float segLen = 20f;
        var rng = new System.Random(12);

        var asphalt = GetMaterial("Asphalt", Color.white, 0.25f, 0f);
        asphalt.SetTexture("_BaseMap", LoadTexture($"{CutsceneFolder}/Textures/asphalt.png", false));
        asphalt.SetTextureScale("_BaseMap", new Vector2(1f, 5f));
        var pavement = GetMaterial("Pavement", new Color(0.22f, 0.22f, 0.23f), 0.15f, 0f);
        // building fronts at night: dark brick with lit windows, drawn self-lit so the windows glow
        var facade = Glow("Facade", Color.white, LoadTexture($"{CutsceneFolder}/Textures/facade_albedo.png", false));
        var poleMat = GetMaterial("LampPole", new Color(0.08f, 0.08f, 0.09f), 0.4f, 0.8f);
        var lampGlow = Glow("LampGlow", new Color(1f, 0.85f, 0.6f));
        EditorUtility.SetDirty(asphalt);

        // the street is not parented to the van, so the van can slide and spin across it in the crash
        var root = new GameObject("NightStreet").transform;
        root.SetPositionAndRotation(van.position, van.rotation);
        var segs = new Transform[segments];
        var lamps = new List<Light>();
        var block = new MaterialPropertyBlock();
        for (int i = 0; i < segments; i++)
        {
            var seg = new GameObject($"Segment{i}").transform;
            seg.SetParent(root, false);
            seg.localPosition = new Vector3(0f, 0f, i * segLen);
            segs[i] = seg;
            NoCollider(Part(PrimitiveType.Cube, "Road", seg, new Vector3(0f, road - 0.05f, segLen / 2f), new Vector3(8f, 0.1f, segLen), asphalt));
            foreach (int side in new[] { -1, 1 })
            {
                NoCollider(Part(PrimitiveType.Cube, "Pavement", seg, new Vector3(side * 6.5f, road + 0.07f, segLen / 2f), new Vector3(5f, 0.24f, segLen), pavement));
                // two or three buildings per side, of different heights
                float z = 0f;
                while (z < segLen - 1f)
                {
                    float w = Mathf.Min(segLen - z, 5f + (float)rng.NextDouble() * 5f);
                    float h = 6f + (float)rng.NextDouble() * 12f;
                    var b = NoCollider(Part(PrimitiveType.Cube, "Building", seg,
                        new Vector3(side * 13f, road + h / 2f, z + w / 2f), new Vector3(8f, h, w - 0.3f), facade));
                    block.SetVector("_BaseMap_ST", new Vector4(w / 8f, h / 16f, (float)rng.NextDouble(), 0f));
                    b.GetComponent<MeshRenderer>().SetPropertyBlock(block);
                    z += w;
                }
            }
            // single lamp posts at random spots: a random side of the road, unevenly spaced,
            // and now and then a dark stretch with none at all
            int lampCount = rng.NextDouble() < 0.2 ? 0 : rng.NextDouble() < 0.3 ? 2 : 1;
            for (int k = 0; k < lampCount; k++)
            {
                int ls = rng.NextDouble() < 0.5 ? -1 : 1;
                float lz = lampCount == 1 ? 2f + (float)rng.NextDouble() * 16f
                                          : (k == 0 ? 1.5f + (float)rng.NextDouble() * 6f : 11f + (float)rng.NextDouble() * 7f);
                var lampRoot = new GameObject("LampPost").transform;
                lampRoot.SetParent(seg, false);
                lampRoot.localPosition = new Vector3(ls * 4.4f, road, lz);
                NoCollider(Part(PrimitiveType.Cylinder, "Pole", lampRoot, new Vector3(0f, 2.6f, 0f), new Vector3(0.14f, 2.6f, 0.14f), poleMat));
                NoCollider(Part(PrimitiveType.Cube, "Arm", lampRoot, new Vector3(-ls * 0.6f, 5.15f, 0f), new Vector3(1.3f, 0.08f, 0.08f), poleMat));
                NoCollider(Part(PrimitiveType.Sphere, "Lamp", lampRoot, new Vector3(-ls * 1.15f, 5.02f, 0f), new Vector3(0.35f, 0.18f, 0.35f), lampGlow));
                Halo(lampRoot, new Vector3(-ls * 1.15f, 4.95f, 0f), 2.2f, new Color(1f, 0.75f, 0.45f, 0.8f));
                var light = new GameObject("LampLight").AddComponent<Light>();
                light.transform.SetParent(lampRoot, false);
                light.transform.localPosition = new Vector3(-ls * 1.15f, 4.9f, 0f);
                light.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                light.type = LightType.Spot;
                light.spotAngle = 110f;
                light.range = 12f;
                light.intensity = 25f;
                light.color = new Color(1f, 0.72f, 0.42f);
                light.shadows = LightShadows.None;
                lamps.Add(light);
            }
        }
        var street = root.gameObject.AddComponent<NightStreet>();
        street.segments = segs;
        street.segmentLength = segLen;
        street.lampLights = lamps.ToArray();

        // just the headlights of a car far behind (no body: at night that's all you'd see)
        var headGlow = Glow("HeadlightGlow", Color.white);
        var car = new GameObject("DistantHeadlights").transform;
        car.SetParent(root, false);
        car.localPosition = new Vector3(-0.9f, road, 26f);
        foreach (int side in new[] { -1, 1 })
        {
            NoCollider(Part(PrimitiveType.Sphere, "Headlight", car, new Vector3(side * 0.75f, 0.65f, 0f), new Vector3(0.35f, 0.22f, 0.1f), headGlow)).GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            Halo(car, new Vector3(side * 0.75f, 0.65f, -0.3f), 3.4f, new Color(0.95f, 0.97f, 1f, 1f));
            Beam(car, new Vector3(side * 0.75f, 0.65f, -0.2f), 16f);
        }
        var beams = new GameObject("Beams").AddComponent<Light>();
        beams.transform.SetParent(car, false);
        beams.transform.localPosition = new Vector3(0f, 0.7f, -0.2f);
        beams.transform.localRotation = Quaternion.Euler(4f, 180f, 0f);  // along the road toward the van
        beams.type = LightType.Spot;
        beams.spotAngle = 45f;
        beams.range = 40f;                                                 // floods the road up to the van
        beams.intensity = 70f;
        beams.color = new Color(0.9f, 0.94f, 1f);
        beams.shadows = LightShadows.Soft;                                 // so it only gets in through the windows
        cutscene.distantCar = car;

        var moon = new GameObject("Moon").AddComponent<Light>();
        moon.transform.SetParent(root, false);
        moon.transform.localRotation = Quaternion.Euler(32f, 200f, 0f);
        moon.type = LightType.Directional;
        moon.color = new Color(0.55f, 0.65f, 1f);
        moon.intensity = 0.35f;
        moon.shadows = LightShadows.Soft;
        moon.enabled = false;

        cutscene.street = street;
        cutscene.moon = moon;
        cutscene.nightSky = NightSkybox();
    }

    // A head model on a 1 m pedestal, facing the start camera. Painted heads carry vertex colours (shown with a
    // URP particle shader, which multiplies by them) and two lens quads textured from the reference drawing.

    // Scene-wide bloom: only very bright (HDR) things - the lasers, headlights - spill a soft glow around them.
    static void Bloom()
    {
        if (!AssetDatabase.IsValidFolder(MaterialFolder)) AssetDatabase.CreateFolder("Assets/Materials", "Intro");
        string path = $"{MaterialFolder}/PostFX.asset";
        AssetDatabase.DeleteAsset(path);
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(profile, path);
        var bloom = profile.Add<UnityEngine.Rendering.Universal.Bloom>(true);
        bloom.threshold.Override(1.1f);
        bloom.intensity.Override(1.6f);
        bloom.scatter.Override(0.7f);
        bloom.tint.Override(new Color(1f, 0.9f, 0.9f));
        AssetDatabase.AddObjectToAsset(bloom, profile);
        AssetDatabase.SaveAssets();
        var volume = new GameObject("PostFX").AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 10f;
        volume.sharedProfile = profile;
    }

    // Self-lit material: unlit, so it shows at full brightness in the dark whatever the lighting.
    // Star-filled panoramic skybox with the Milky Way, a moon and the nebula (tools/blender_cutscene/night_sky.py).

    // Self-lit material: unlit, so it shows at full brightness in the dark whatever the lighting.
    // Star-filled panoramic skybox with the Milky Way, a moon and the nebula (tools/blender_cutscene/night_sky.py).
    static Material NightSkybox()
    {
        string texPath = $"{CutsceneFolder}/Textures/night_sky.png";
        if (AssetImporter.GetAtPath(texPath) is TextureImporter importer)
        {
            // full resolution and no mipmaps so the stars stay sharp pinpoints
            bool changed = importer.maxTextureSize != 4096 || importer.mipmapEnabled
                           || importer.textureCompression != TextureImporterCompression.CompressedHQ;
            importer.maxTextureSize = 4096;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.wrapModeU = TextureWrapMode.Repeat;
            importer.wrapModeV = TextureWrapMode.Clamp;
            if (changed) importer.SaveAndReimport();
        }
        string path = $"{MaterialFolder}/NightSky.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        var shader = Shader.Find("Skybox/Panoramic");
        if (m == null)
        {
            m = new Material(shader);
            AssetDatabase.CreateAsset(m, path);
        }
        m.shader = shader;
        m.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(texPath));
        m.SetFloat("_Mapping", 1f);      // latitude-longitude layout
        m.SetFloat("_ImageType", 0f);    // full 360 degrees
        m.SetFloat("_Exposure", 1f);
        m.SetFloat("_Rotation", 0f);
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material Glow(string name, Color color, Texture2D texture = null)
    {
        string path = $"{MaterialFolder}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        var unlit = Shader.Find("Universal Render Pipeline/Unlit");
        if (m == null)
        {
            m = new Material(unlit);
            AssetDatabase.CreateAsset(m, path);
        }
        m.shader = unlit;
        m.SetColor("_BaseColor", color);
        if (texture != null) m.SetTexture("_BaseMap", texture);
        EditorUtility.SetDirty(m);
        return m;
    }

    // Soft glow halo around a light source: a camera-facing quad with a radial-gradient texture.

    // Soft glow halo around a light source: a camera-facing quad with a radial-gradient texture.
    static void Halo(Transform parent, Vector3 localPos, float size, Color color)
    {
        string path = $"{MaterialFolder}/Halo_{ColorUtility.ToHtmlStringRGB(color)}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Sprites/Default"));
            AssetDatabase.CreateAsset(m, path);
        }
        m.mainTexture = LoadTexture($"{CutsceneFolder}/Textures/glow.png", false);
        m.color = color;
        EditorUtility.SetDirty(m);
        var q = NoCollider(Part(PrimitiveType.Quad, "Halo", parent, localPos, Vector3.one * size, m));
        var r = q.GetComponent<MeshRenderer>();
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        q.AddComponent<Billboard>();
    }

    // A visible headlight beam: an open cone of faint light through the night haze, pointing down the
    // road toward the van (-Z), bright at the lamp and fading to nothing at its far end.

    static GameObject NoCollider(GameObject go)
    {
        Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }

    // Turn the bust into the watching statue: glowing eyes and the gaze/laser behaviour (StatueGaze).

    static void MeshPart(Transform parent, string name, Mesh mesh, Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
    }

    // A bust mesh (base at its origin, facing -Z) on a 1 m stone pedestal standing on the plaza.

    static GameObject Part(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Material mat)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }

    static Material GetMaterial(string name, Color color, float smoothness, float metallic)
    {
        if (!AssetDatabase.IsValidFolder(MaterialFolder))
            AssetDatabase.CreateFolder("Assets/Materials", "Intro");

        string assetPath = $"{MaterialFolder}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
        if (mat == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, assetPath);
        }
        mat.SetColor("_BaseColor", color);
        mat.color = color;
        mat.SetFloat("_Smoothness", smoothness);
        mat.SetFloat("_Metallic", metallic);
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();
        return mat;
    }

    // A visible headlight beam: an open cone of faint light through the night haze, pointing down the
    // road toward the van (-Z), bright at the lamp and fading to nothing at its far end.
    static bool beamMeshBuilt;

    static void Beam(Transform parent, Vector3 localPos, float length)
    {
        const int sides = 16;
        var verts = new List<Vector3>();
        var colors = new List<Color>();
        var tris = new List<int>();
        for (int ring = 0; ring < 2; ring++)
        {
            float r = ring == 0 ? 0.12f : 2.2f;
            float z = ring == 0 ? 0f : -length;
            var c = new Color(0.95f, 0.97f, 1f, ring == 0 ? 0.22f : 0f);
            for (int i = 0; i < sides; i++)
            {
                float a = i * Mathf.PI * 2f / sides;
                verts.Add(new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r * 0.55f - (ring == 0 ? 0f : 0.6f), z));
                colors.Add(c);
            }
        }
        for (int i = 0; i < sides; i++)
        {
            int n = (i + 1) % sides;
            tris.AddRange(new[] { i, sides + i, sides + n, i, sides + n, n });
        }
        var mesh = new Mesh { name = "HeadlightBeam" };
        mesh.SetVertices(verts);
        mesh.SetColors(colors);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        // both beams share one mesh asset; rebuild it only once per scene build
        string meshPath = $"{MaterialFolder}/HeadlightBeam.asset";
        var saved = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        if (saved == null || !beamMeshBuilt)
        {
            AssetDatabase.DeleteAsset(meshPath);
            AssetDatabase.CreateAsset(mesh, meshPath);
            beamMeshBuilt = true;
        }
        else
            mesh = saved;

        string matPath = $"{MaterialFolder}/HeadlightBeam.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (m == null)
        {
            m = new Material(Shader.Find("Sprites/Default"));  // vertex-coloured, transparent, two-sided
            AssetDatabase.CreateAsset(m, matPath);
        }
        var go = new GameObject("Beam");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r2 = go.AddComponent<MeshRenderer>();
        r2.sharedMaterial = m;
        r2.shadowCastingMode = ShadowCastingMode.Off;
        r2.receiveShadows = false;
    }

    static Mesh SavedMesh(string name, Mesh mesh)
    {
        string path = $"{MaterialFolder}/{name}.asset";
        AssetDatabase.DeleteAsset(path);
        mesh.name = name;
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    static Mesh LoadMesh(string path)
    {
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
            if (asset is Mesh mesh)
                return mesh;
        return null;
    }
}
