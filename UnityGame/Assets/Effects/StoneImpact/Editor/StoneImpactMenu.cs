using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class StoneImpactMenu
{
    // New scene with camera, sun and the tester, then straight into Play.
    [MenuItem("Tools/Stone Impact Test")]
    static void Open()
    {
        if (EditorApplication.isPlaying) { EditorApplication.isPlaying = false; return; }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        var cam = Camera.main;
        if (cam != null) cam.gameObject.AddComponent<StoneImpactTester>();
        EditorApplication.isPlaying = true;
    }
}
