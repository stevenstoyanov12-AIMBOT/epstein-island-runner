#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.SceneManagement;

// Editor only: pressing Play in the Outside scene also streams in the Inside scene (the museum interior,
// door prompt and portals), like the web build does after the intro. The build uses IntroLoader instead.
public static class EditorInsideAutoLoad
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Load()
    {
        if (!SceneManager.GetSceneByName("Outside").isLoaded || SceneManager.GetSceneByName("Inside").isLoaded) return;
        UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/Inside.unity",
            new LoadSceneParameters(LoadSceneMode.Additive));
    }
}
#endif
