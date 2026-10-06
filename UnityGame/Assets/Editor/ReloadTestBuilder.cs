using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

// Tools > Build Reload Test: tjr playing the Blender-made pistol reload (tools/anim_gen/pistol_reload.py) on loop.
// Play: Space restarts, 1/2/3 = speed 1x / 0.5x / 0.25x, mouse drag orbits, wheel zooms.
public static class ReloadTestBuilder
{
    const string ClipFbx = "Assets/Animations/PistolReload.fbx";
    const string CharFbx = "Assets/Models/Characters/tjr.fbx";
    const string CtrlPath = "Assets/Animations/ReloadTest.controller";
    const string ScenePath = "Assets/Scenes/ReloadTest.unity";

    [MenuItem("Tools/Build Reload Test")]
    public static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Humanoid(CharFbx, false);
        Humanoid(ClipFbx, true);
        var clip = AssetDatabase.LoadAllAssetsAtPath(ClipFbx).OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview"));
        var ctrl = AnimatorController.CreateAnimatorControllerAtPathWithClip(CtrlPath, clip);

        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.transform.localScale = Vector3.one * 2f;

        var ch = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(CharFbx));
        var anim = ch.GetComponent<Animator>() ?? ch.AddComponent<Animator>();
        anim.runtimeAnimatorController = ctrl;
        anim.applyRootMotion = false;

        // pistol stand-in in the right hand
        var hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
        if (hand != null)
        {
            var gun = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(gun.GetComponent<Collider>());
            gun.name = "GunProxy";
            gun.transform.SetParent(hand, false);
            gun.transform.position = hand.position + ch.transform.forward * 0.08f + Vector3.up * 0.03f;
            gun.transform.rotation = Quaternion.LookRotation(ch.transform.forward, Vector3.up);
            gun.transform.localScale = Vector3.one;
            gun.transform.localScale = new Vector3(0.035f, 0.035f, 0.19f) / Mathf.Max(hand.lossyScale.x, 1e-4f);
        }

        var cam = Camera.main.gameObject;
        cam.AddComponent<ReloadViewer>().target = anim;
        cam.transform.position = new Vector3(1.4f, 1.5f, 2.2f);
        cam.transform.LookAt(new Vector3(0f, 1.2f, 0f));

        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), ScenePath);
        Debug.Log($"Reload test built: {clip.name} {clip.length:0.00}s");
    }

    static void Humanoid(string path, bool isClip)
    {
        var imp = AssetImporter.GetAtPath(path) as ModelImporter;
        if (imp == null) { Debug.LogError("Missing " + path); return; }
        imp.animationType = ModelImporterAnimationType.Human;
        imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        if (isClip)
        {
            var clips = imp.defaultClipAnimations;
            foreach (var c in clips)
            {
                c.name = "PistolReload";
                c.loopTime = true;     // loops here for easy viewing; the game plays it once
                c.lockRootRotation = c.lockRootHeightY = c.lockRootPositionXZ = true;
                c.keepOriginalOrientation = c.keepOriginalPositionY = c.keepOriginalPositionXZ = true;
            }
            imp.clipAnimations = clips;
        }
        imp.SaveAndReimport();
    }
}
