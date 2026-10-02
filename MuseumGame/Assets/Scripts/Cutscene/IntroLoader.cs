using System.Collections;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

// Streams the game in with Unity Addressables:
//  1. the small "Select" scene (CHOOSE YOUR TRENCHER) downloads and opens first
//  2. once a character is chosen the van-crash cutscene plays while the "Outside" scene downloads in the background
//  3. after the cutscene (and once Outside is ready) it opens; the museum interior ("Inside") streams in afterwards
public class IntroLoader : MonoBehaviour
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [System.Runtime.InteropServices.DllImport("__Internal")] static extern void KeyGuard_Install();
#endif
    public string selectKey = "Select", outsideKey = "Outside", insideKey = "Inside";
    public static bool InsideLoaded { get; private set; }

    float progress; string status = "Loading";
    bool showLoad = true, loadingInside;
    static IntroLoader instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { InsideLoaded = false; instance = null; }

    void Awake()
    {
        if (instance != null) { Destroy(gameObject); return; }
        instance = this;
        DontDestroyOnLoad(gameObject);
        CrateCutscene.Finished = false;
#if UNITY_WEBGL && !UNITY_EDITOR
        KeyGuard_Install();   // Ctrl+W must not close the tab (Ctrl = crouch)
#endif
    }

    IEnumerator Start()
    {
        yield return Addressables.InitializeAsync();
        // 1. character select
        yield return Download(selectKey, "Loading");
        var select = Addressables.LoadSceneAsync(selectKey, LoadSceneMode.Additive);
        yield return select;
        showLoad = false;
        // the world downloads while the player chooses and the cutscene plays
        var outsideDl = Addressables.DownloadDependenciesAsync(outsideKey);
        while (string.IsNullOrEmpty(CharacterSelect.Chosen)) yield return null;
        // 2. cutscene
        var cs = FindFirstObjectByType<CrateCutscene>();
        if (cs != null) { cs.Play(); yield return null; }
        if (select.Status == AsyncOperationStatus.Succeeded) Addressables.UnloadSceneAsync(select);
        while (cs != null && !CrateCutscene.Finished) yield return null;
        // 3. the world
        showLoad = true; status = "Entering";
        while (!outsideDl.IsDone) { progress = outsideDl.GetDownloadStatus().Percent; yield return null; }
        Addressables.Release(outsideDl);
        var outside = Addressables.LoadSceneAsync(outsideKey, LoadSceneMode.Additive);
        while (!outside.IsDone) { progress = outside.PercentComplete; yield return null; }
        if (outside.Status == AsyncOperationStatus.Succeeded) SceneManager.SetActiveScene(outside.Result.Scene);
        showLoad = false;
        // then the interior, in the background
        loadingInside = true;
        yield return Download(insideKey, "Museum interior");
        var inside = Addressables.LoadSceneAsync(insideKey, LoadSceneMode.Additive);
        yield return inside;
        if (outside.Status == AsyncOperationStatus.Succeeded) SceneManager.SetActiveScene(outside.Result.Scene);   // keep outside lighting/sky
        InsideLoaded = true; loadingInside = false;
    }

    IEnumerator Download(string key, string label)
    {
        status = label; progress = 0f;
        var dl = Addressables.DownloadDependenciesAsync(key);
        while (!dl.IsDone) { progress = dl.GetDownloadStatus().Percent; yield return null; }
        if (dl.Status != AsyncOperationStatus.Succeeded) Debug.LogWarning("IntroLoader: download failed for " + key + ": " + dl.OperationException);
        Addressables.Release(dl);
        progress = 1f;
    }

    void OnGUI()
    {
        if (showLoad)
        {
            GUI.color = Color.black; GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            Bar(Screen.width * 0.3f, Screen.height * 0.6f, Screen.width * 0.4f, 10f, status);
        }
        else if (loadingInside)
            Bar(Screen.width - 240f, Screen.height - 40f, 220f, 6f, "Museum interior");
    }

    void Bar(float x, float y, float w, float h, string label)
    {
        GUI.color = new Color(1f, 1f, 1f, 0.8f);
        GUI.Label(new Rect(x, y - 22f, w, 20f), label + "  " + Mathf.RoundToInt(progress * 100f) + "%");
        GUI.color = new Color(1f, 1f, 1f, 0.25f); GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
        GUI.color = Color.white; GUI.DrawTexture(new Rect(x, y, w * Mathf.Clamp01(progress), h), Texture2D.whiteTexture);
    }
}
