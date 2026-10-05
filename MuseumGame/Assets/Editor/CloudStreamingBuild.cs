using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.PackageManager;
using UnityEngine;
using UnityEngine.Rendering;

// Build > Cloud Streaming (Linux): the Linux x86_64 player that runs on the Vast cards.
// Vulkan only, 1280x720 windowed, CLOUD_STREAMING define (enables CloudStreamHost), output CloudBuild/.
// First run installs com.unity.webrtc if missing; Linux Build Support (IL2CPP or Mono) comes from Unity Hub.
// Switches the active target to Linux (big reimport); Build > Back To WebGL switches back.
public static class CloudStreamingBuild
{
    const string Define = "CLOUD_STREAMING";
    const string OutDir = "CloudBuild";
    const string Exe = "Museum.x86_64";

    [MenuItem("Build/Cloud Streaming (Linux)")]
    public static void Build()
    {
        if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneLinux64))
        {
            EditorUtility.DisplayDialog("Cloud build",
                "Linux Build Support is not installed.\nUnity Hub > Installs > 6000.6 > Add modules > Linux Build Support (IL2CPP and Mono).",
                "OK");
            return;
        }
        if (!HasPackage("com.unity.webrtc"))
        {
            Client.Add("com.unity.webrtc");
            EditorUtility.DisplayDialog("Cloud build", "Installing com.unity.webrtc. Run Build > Cloud Streaming again once it has imported.", "OK");
            return;
        }

        var named = NamedBuildTarget.Standalone;
        var defines = PlayerSettings.GetScriptingDefineSymbols(named).Split(';').Where(d => d.Length > 0).ToList();
        if (!defines.Contains(Define))
        {
            defines.Add(Define);
            PlayerSettings.SetScriptingDefineSymbols(named, string.Join(";", defines));
        }
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneLinux64, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneLinux64, new[] { GraphicsDeviceType.Vulkan });
        PlayerSettings.defaultScreenWidth = 1280;
        PlayerSettings.defaultScreenHeight = 720;
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.resizableWindow = false;
        PlayerSettings.runInBackground = true;
        PlayerSettings.visibleInBackground = true;
        PlayerSettings.usePlayerLog = true;

        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneLinux64)
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneLinux64);

        // Addressables have to be built for Linux too (bundles are per platform). Uses the active profile:
        // make sure its load paths are Local for this build, the cards should not pull bundles from R2.
        var addr = System.Type.GetType("UnityEditor.AddressableAssets.Settings.AddressableAssetSettings, Unity.Addressables.Editor");
        addr?.GetMethod("BuildPlayerContent", System.Type.EmptyTypes)?.Invoke(null, null);

        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        Directory.CreateDirectory(OutDir);
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = Path.Combine(OutDir, Exe),
            target = BuildTarget.StandaloneLinux64,
            targetGroup = BuildTargetGroup.Standalone,
            options = BuildOptions.None,
        });
        Debug.Log($"Cloud build: {report.summary.result}, {report.summary.totalSize / (1024 * 1024)} MB -> {OutDir}/{Exe}");
        if (report.summary.result == BuildResult.Succeeded)
            EditorUtility.RevealInFinder(OutDir);
    }

    [MenuItem("Build/Back To WebGL")]
    public static void BackToWebGL()
    {
        EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WebGL, BuildTarget.WebGL);
    }

    static bool HasPackage(string name)
    {
        var req = Client.List(true);
        while (!req.IsCompleted) System.Threading.Thread.Sleep(20);
        return req.Status == StatusCode.Success && req.Result.Any(p => p.name == name);
    }
}
