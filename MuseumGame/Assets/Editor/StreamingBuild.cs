using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

// Addressables web build:
//   player = Intro scene only (cutscene + IntroLoader) -> starts almost immediately
//   Addressables groups "Outside" and "Inside" (each scene + what it pulls in; shared assets de-duplicated
//   by Addressables), streamed from the build's StreamingAssets/aa while the cutscene plays.
// Tools > Setup Addressables, Tools > Build WebGPU (Addressables)
public static class StreamingBuild
{
    const string Intro = "Assets/Scenes/Intro.unity", Outside = "Assets/Scenes/Outside.unity", Inside = "Assets/Scenes/Inside.unity";
    public const string OutDir = "Build/WebGPU";

    const string Select = "Assets/Scenes/Select.unity";

    [MenuItem("Tools/Setup Addressables")]
    public static void Setup()
    {
        var s = AddressableAssetSettingsDefaultObject.GetSettings(true);
        Entry(s, "Select", Select);
        Entry(s, "Outside", Outside);
        Entry(s, "Inside", Inside);
        // drop the groups of the older layout
        foreach (var n in new[] { "Shared", "OutsideAssets", "InsideAssets" }) { var og = s.FindGroup(n); if (og != null) s.RemoveGroup(og); }
        // Every asset goes into a group named after the set of scenes that use it (Select / Outside / Inside), in ~40 MB bundles,
        // so each scene downloads only what it needs and shared assets exist once.
        var scenes = new[] { Select, Outside, Inside };
        var mask = new System.Collections.Generic.Dictionary<string, int>();
        for (int i = 0; i < scenes.Length; i++) foreach (var p in Deps(scenes[i])) { int m; mask.TryGetValue(p, out m); mask[p] = m | (1 << i); }
        var byMask = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<string>>();
        foreach (var kv in mask) { System.Collections.Generic.List<string> l; if (!byMask.TryGetValue(kv.Value, out l)) byMask[kv.Value] = l = new System.Collections.Generic.List<string>(); l.Add(kv.Key); }
        foreach (var g in new System.Collections.Generic.List<AddressableAssetGroup>(s.groups)) if (g != null && g.Name.StartsWith("G") && g.Name.Length == 2 && !byMask.ContainsKey(g.Name[1] - '0')) s.RemoveGroup(g);
        foreach (var kv in byMask) Chunked(s, "G" + kv.Key, kv.Value);
        EditorUtility.SetDirty(s);
        AssetDatabase.SaveAssets();
    }

    static System.Collections.Generic.List<string> Deps(string scene)
    {
        var l = new System.Collections.Generic.List<string>();
        foreach (var p in AssetDatabase.GetDependencies(scene, true))
            if (p != scene && p.StartsWith("Assets/") && !p.EndsWith(".cs") && !p.EndsWith(".unity") && !p.EndsWith(".dll") && !p.Contains("/Editor/")) l.Add(p);
        return l;
    }

    static void Chunked(AddressableAssetSettings s, string name, System.Collections.Generic.List<string> paths)
    {
        var g = s.FindGroup(name) ?? s.CreateGroup(name, false, false, true, null, typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));
        foreach (var old in new System.Collections.Generic.List<AddressableAssetEntry>(g.entries)) g.RemoveAssetEntry(old, false);   // drop assets no longer used by the scenes
        var bs = g.GetSchema<BundledAssetGroupSchema>();
        bs.Compression = BundledAssetGroupSchema.BundleCompressionMode.LZ4;
        bs.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackTogetherByLabel;
        bs.BuildPath.SetVariableByName(s, AddressableAssetSettings.kLocalBuildPath);
        bs.LoadPath.SetVariableByName(s, AddressableAssetSettings.kLocalLoadPath);
        long size = 0; int chunk = 0; const long Limit = 40L * 1024 * 1024;
        foreach (var p in paths)
        {
            long len = 0; try { len = new System.IO.FileInfo(p).Length; } catch { }
            if (size > 0 && size + len > Limit) { chunk++; size = 0; }
            size += len;
            var e = s.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(p), g);
            if (e == null) continue;
            e.labels.Clear();
            string label = name + "_" + chunk; s.AddLabel(label); e.SetLabel(label, true);
        }
    }

    static void Entry(AddressableAssetSettings s, string name, string path)
    {
        var g = s.FindGroup(name) ?? s.CreateGroup(name, false, false, true, null, typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));
        var b = g.GetSchema<BundledAssetGroupSchema>();
        b.Compression = BundledAssetGroupSchema.BundleCompressionMode.LZ4;
        b.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackTogether;
        b.BuildPath.SetVariableByName(s, AddressableAssetSettings.kLocalBuildPath);
        b.LoadPath.SetVariableByName(s, AddressableAssetSettings.kLocalLoadPath);
        var e = s.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(path), g);
        e.address = name;
    }

    [MenuItem("Tools/Build WebGPU (Addressables)")]
    public static void BuildPlayer()
    {
        ResRegistryBuilder.Update();
        Setup();
        AddressableAssetSettings.CleanPlayerContent();
        AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult r);
        if (!string.IsNullOrEmpty(r.Error)) { Debug.LogError("Addressables build failed: " + r.Error); return; }
        PlayerSettings.WebGL.maximumMemorySize = 4096;   // the streamed museum needs more than the 2 GB default
        PlayerSettings.WebGL.initialMemorySize = 256;
        PlayerSettings.stripEngineCode = false;   // the streamed scenes use engine modules the tiny intro scene does not
        PlayerSettings.SetManagedStrippingLevel(UnityEditor.Build.NamedBuildTarget.WebGL, ManagedStrippingLevel.Minimal);
        var opts = new BuildPlayerOptions { scenes = new[] { Intro }, locationPathName = OutDir, target = BuildTarget.WebGL, options = BuildOptions.None };
        var report = BuildPipeline.BuildPlayer(opts);
        Debug.Log("WebGPU addressables build: " + report.summary.result + " " + report.summary.totalSize / (1024 * 1024) + " MB");
    }

    [MenuItem("Tools/Build WebGPU DEV (debug names)")]
    public static void BuildDev()
    {
        PlayerSettings.WebGL.maximumMemorySize = 4096; PlayerSettings.WebGL.initialMemorySize = 256;
        PlayerSettings.stripEngineCode = false; PlayerSettings.SetManagedStrippingLevel(UnityEditor.Build.NamedBuildTarget.WebGL, ManagedStrippingLevel.Minimal);
        var opts = new BuildPlayerOptions { scenes = new[] { Intro }, locationPathName = "Build/WebGPU_dev", target = BuildTarget.WebGL, options = BuildOptions.Development };
        var report = BuildPipeline.BuildPlayer(opts);
        Debug.Log("DEV build: " + report.summary.result);
    }
}
