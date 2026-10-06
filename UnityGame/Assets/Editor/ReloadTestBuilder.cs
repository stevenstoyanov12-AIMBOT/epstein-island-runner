using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Places tjr playing the Blender-made pistol reload (tools/anim_gen/pistol_reload.py) on loop; used by
// Tools > Build Statue Scene. In Play: T restarts, 1/2/3 = speed 1x / 0.5x / 0.25x.
public static class ReloadTestBuilder
{
    const string ClipFbx = "Assets/Animations/PistolReload.fbx";
    const string CharFbx = "Assets/Models/Characters/tjr.fbx";
    const string CtrlPath = "Assets/Animations/ReloadTest.controller";

    public static GameObject Place(Vector3 pos, float yaw)
    {
        Humanoid(CharFbx, false);
        Humanoid(ClipFbx, true);
        var clip = AssetDatabase.LoadAllAssetsAtPath(ClipFbx).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview"));
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(CharFbx);
        if (clip == null || model == null) { Debug.LogError("Missing reload clip or tjr model"); return null; }
        var ctrl = AnimatorController.CreateAnimatorControllerAtPathWithClip(CtrlPath, clip);

        var ch = (GameObject)PrefabUtility.InstantiatePrefab(model);
        ch.name = "ReloadDemo_tjr";
        ch.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        // the model comes in at the wrong size: scale it to 1.8 m tall
        var rs = ch.GetComponentsInChildren<Renderer>();
        if (rs.Length > 0)
        {
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            if (b.size.y > 1e-5f) ch.transform.localScale *= 1.8f / b.size.y;
        }
        var anim = ch.GetComponent<Animator>();
        if (anim == null) anim = ch.AddComponent<Animator>();
        anim.runtimeAnimatorController = ctrl;
        anim.applyRootMotion = false;

        var hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
        if (hand != null)
        {
            var gun = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(gun.GetComponent<Collider>());
            gun.name = "GunProxy";
            gun.transform.SetParent(hand, true);
            gun.transform.position = hand.position + ch.transform.forward * 0.08f + Vector3.up * 0.03f;
            gun.transform.rotation = Quaternion.LookRotation(ch.transform.forward, Vector3.up);
            var ls = hand.lossyScale.x;
            gun.transform.localScale = new Vector3(0.035f, 0.035f, 0.19f) / Mathf.Max(Mathf.Abs(ls), 1e-6f);
        }
        ch.AddComponent<ReloadDemo>();
        return ch;
    }

    // Public-domain revolver reload (github.com/ZenXChaos/ThirdPersonShooter-AnimationSets): open, load (x3), close, on loop.
    public static GameObject PlaceRevolver(Vector3 pos, float yaw)
    {
        string dir = "Assets/Animations/Revolver/";
        var names = new[] { "RevolverReloadInit", "RevolverReloadLoop", "RevolverReloadEnd" };
        var clips = new AnimationClip[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            var path = dir + names[i] + ".fbx";
            Humanoid(path, true, names[i]);
            clips[i] = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview"));
            if (clips[i] == null) { Debug.LogError("Missing " + path); return null; }
        }
        var ctrl = AnimatorController.CreateAnimatorControllerAtPath(dir + "RevolverReloadTest.controller");
        var sm = ctrl.layers[0].stateMachine;
        var init = sm.AddState("Init"); init.motion = clips[0];
        var load = sm.AddState("Load"); load.motion = clips[1];
        var end = sm.AddState("End"); end.motion = clips[2];
        sm.defaultState = init;
        void Next(AnimatorState a, AnimatorState b, float exit) { var t = a.AddTransition(b); t.hasExitTime = true; t.exitTime = exit; t.duration = 0.1f; }
        Next(init, load, 0.95f);
        Next(load, end, 2.9f);        // three rounds
        Next(end, init, 0.95f);

        var ch = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(CharFbx));
        ch.name = "ReloadDemo_Revolver";
        ch.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        var rs = ch.GetComponentsInChildren<Renderer>();
        if (rs.Length > 0) { var bb = rs[0].bounds; foreach (var r in rs) bb.Encapsulate(r.bounds); if (bb.size.y > 1e-5f) ch.transform.localScale *= 1.8f / bb.size.y; }
        var anim = ch.GetComponent<Animator>();
        if (anim == null) anim = ch.AddComponent<Animator>();
        anim.runtimeAnimatorController = ctrl;
        anim.applyRootMotion = false;
        ch.AddComponent<ReloadDemo>();
        return ch;
    }

    static void Humanoid(string path, bool isClip, string clipName)
    {
        var imp = AssetImporter.GetAtPath(path) as ModelImporter;
        if (imp == null) { Debug.LogError("Missing " + path); return; }
        imp.animationType = ModelImporterAnimationType.Human;
        imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        var clips = imp.defaultClipAnimations;
        foreach (var c in clips)
        {
            c.name = clipName;
            c.loopTime = clipName.EndsWith("Loop");
            c.lockRootRotation = c.lockRootHeightY = c.lockRootPositionXZ = true;
            c.keepOriginalOrientation = c.keepOriginalPositionY = c.keepOriginalPositionXZ = true;
        }
        imp.clipAnimations = clips;
        imp.SaveAndReimport();
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
