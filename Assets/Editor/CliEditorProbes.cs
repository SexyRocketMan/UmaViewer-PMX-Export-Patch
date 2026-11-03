using Gallop;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UmaMusumeAudio;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The command line's diagnostic probes that need the editor.
///
/// Everything the CLI does to a model - export, record, scan, dump, screenshot - is ordinary runtime work and
/// lives in `Assets/Scripts/Cli/` so that a built player can do it too. These five are the exception: they read
/// an animation's curves and its serialized properties, which is `UnityEditor` API, and a runtime assembly
/// cannot reference it at all. So they live here, in the editor assembly, and install themselves into
/// <see cref="CliEditorProbeHooks"/> when the editor loads - including after the domain reload that entering
/// play mode causes, which is exactly when a run needs them.
/// </summary>
public static class CliEditorProbes
{
    private const string Tag = "[UmaHeadlessExport]";

    [InitializeOnLoadMethod]
    private static void Install()
    {
        CliEditorProbeHooks.DumpFacePipeline = DumpFacePipeline;
        CliEditorProbeHooks.DumpTearBundles = DumpTearBundles;
        CliEditorProbeHooks.DumpClipSerialized = DumpClipSerialized;
        CliEditorProbeHooks.DumpTearAnimator = DumpTearAnimator;
        CliEditorProbeHooks.DumpShaderFacts = DumpShaderFacts;
        CliEditorProbeHooks.DumpShaderEffectData = DumpShaderEffectData;
    }

    public static void DumpTearBundles()
    {
        var main = UmaViewerMain.Instance;
        if (main == null) return;
        foreach (var pair in main.AbList)
        {
            if (pair.Key.IndexOf("tear", StringComparison.OrdinalIgnoreCase) < 0) continue;
            AssetBundle bundle = null;
            try
            {
                bundle = UmaAssetManager.LoadAssetBundle(pair.Value, true, false);
            }
            catch (Exception exception)
            {
                Debug.Log($"{Tag} [tears] bundle '{pair.Key}' failed: {exception.GetType().Name}");
                continue;
            }
            if (bundle == null) continue;
            UnityEngine.Object[] assets = bundle.LoadAllAssets();
            Debug.Log($"{Tag} [tears] bundle '{pair.Key}': {assets.Length} asset(s)");
            foreach (UnityEngine.Object asset in assets)
            {
                if (asset == null) continue;
                var clip = asset as AnimationClip;
                if (clip != null)
                {
                    Debug.Log($"{Tag} [tears]   AnimationClip '{clip.name}' legacy={clip.legacy} "
                              + $"length={clip.length:F3} bindings={AnimationUtility.GetCurveBindings(clip).Length} "
                              + $"humanMotion={clip.humanMotion} motionCurves={clip.hasMotionCurves}");
                    continue;
                }
                var controller = asset as RuntimeAnimatorController;
                if (controller != null)
                {
                    Debug.Log($"{Tag} [tears]   controller '{asset.name}' "
                              + $"clips={string.Join(",", controller.animationClips.Select(c => c != null ? c.name : "null"))}");
                    continue;
                }
                Debug.Log($"{Tag} [tears]   {asset.GetType().Name} '{asset.name}'");
            }
        }
    }

    public static void DumpClipSerialized(string label, AnimationClip clip)
    {
        try
        {
            var serialized = new SerializedObject(clip);
            SerializedProperty iterator = serialized.GetIterator();
            int printed = 0;
            while (iterator.NextVisible(true) && printed < 60)
            {
                if (iterator.depth > 2) continue;
                if (iterator.propertyType == SerializedPropertyType.Generic && iterator.propertyPath.Contains(".")) continue;
                string detail;
                switch (iterator.propertyType)
                {
                    case SerializedPropertyType.Integer:
                    case SerializedPropertyType.Enum:
                    case SerializedPropertyType.Boolean:
                    case SerializedPropertyType.Float:
                        detail = iterator.ToString() == null ? iterator.propertyType.ToString() : iterator.propertyType.ToString();
                        break;
                    default:
                        detail = iterator.propertyType.ToString();
                        break;
                }
                string size = iterator.isArray ? $"[{iterator.arraySize}]" : "";
                Debug.Log($"{Tag} {label}     field {iterator.propertyPath} = {detail}{size}");
                printed++;
            }
        }
        catch (Exception exception)
        {
            Debug.Log($"{Tag} {label}     serialized dump failed: {exception.GetType().Name}");
        }
    }

    public static void DumpTearAnimator(string label, Animator animator, GameObject part)
    {
        if (animator == null)
        {
            Debug.Log($"{Tag} {label} no Animator");
            return;
        }
        var runtime = animator.runtimeAnimatorController;
        Debug.Log($"{Tag} {label} animator enabled={animator.enabled} "
                  + $"controller='{(runtime != null ? runtime.name : "none")}' "
                  + $"clips={(runtime != null ? runtime.animationClips.Length : 0)}");

        // a *humanoid* clip has no generic curve bindings at all - its motion lives in muscle curves, and what
        // maps them onto this skeleton is the avatar. `bindings=0` therefore says nothing about whether the clip
        // animates anything, which is exactly the trap this probe fell into first time round.
        Avatar avatar = animator.avatar;
        if (avatar == null)
        {
            Debug.Log($"{Tag} {label} avatar: NONE - a humanoid clip cannot be applied without one");
        }
        else
        {
            Debug.Log($"{Tag} {label} avatar '{avatar.name}' valid={avatar.isValid} human={avatar.isHuman}");
            if (avatar.isHuman)
            {
                HumanBone[] human = avatar.humanDescription.human;
                Debug.Log($"{Tag} {label}   human bones mapped: {human.Length}");
                foreach (HumanBone bone in human)
                {
                    Debug.Log($"{Tag} {label}     {bone.humanName} -> '{bone.boneName}'");
                }
            }
        }
        if (runtime == null) return;
        AnimatorOverrideController overrides = runtime as AnimatorOverrideController;
        if (overrides != null && overrides.runtimeAnimatorController != null)
        {
            Debug.Log($"{Tag} {label}   override over '{overrides.runtimeAnimatorController.name}'");
        }
        // the override only replaces the one state it is named for; the rest of the base controller - which is
        // where a chain that bends down the cheek would have to live - is a different set of clips entirely
        RuntimeAnimatorController baseController = overrides != null && overrides.runtimeAnimatorController != null
            ? overrides.runtimeAnimatorController
            : runtime;
        if (baseController != null && !ReferenceEquals(baseController, runtime))
        {
            AnimationClip[] baseClips = baseController.animationClips;
            Debug.Log($"{Tag} {label}   base controller '{baseController.name}' clips={baseClips.Length}");
            foreach (AnimationClip baseClip in baseClips)
            {
                if (baseClip == null) continue;
                EditorCurveBinding[] baseBindings = AnimationUtility.GetCurveBindings(baseClip);
                Debug.Log($"{Tag} {label}     base clip '{baseClip.name}' length={baseClip.length:F3} "
                          + $"bindings={baseBindings.Length}");
                for (int b = 0; b < baseBindings.Length && b < 8; b++)
                {
                    Debug.Log($"{Tag} {label}       {baseBindings[b].path} | {baseBindings[b].type.Name} | "
                              + $"{baseBindings[b].propertyName}");
                }
            }
        }
        var animatorController = baseController as UnityEditor.Animations.AnimatorController;
        if (animatorController != null)
        {
            foreach (var layer in animatorController.layers)
            {
                Debug.Log($"{Tag} {label}   layer '{layer.name}' states={layer.stateMachine.states.Length}");
                foreach (var state in layer.stateMachine.states)
                {
                    Debug.Log($"{Tag} {label}     state '{state.state.name}' "
                              + $"clip='{(state.state.motion != null ? state.state.motion.name : "none")}'");
                }
            }
        }
        foreach (AnimationClip clip in runtime.animationClips)
        {
            if (clip == null) continue;
            EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
            Debug.Log($"{Tag} {label}   clip '{clip.name}' length={clip.length:F3} "
                      + $"loop={clip.isLooping} bindings={bindings.Length} humanMotion={clip.humanMotion} "
                      + $"motionCurves={clip.hasMotionCurves} genericRoot={clip.hasGenericRootTransform}");
            for (int b = 0; b < bindings.Length && b < 12; b++)
            {
                EditorCurveBinding binding = bindings[b];
                AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                string keys = curve == null || curve.length == 0
                    ? "no keys"
                    : $"keys={curve.length} first={curve.keys[0].value:F3} last={curve.keys[curve.length - 1].value:F3}";
                Debug.Log($"{Tag} {label}     {binding.path} | {binding.type.Name} | {binding.propertyName} | {keys}");
            }
            if (bindings.Length > 12)
            {
                Debug.Log($"{Tag} {label}     ... {bindings.Length - 12} more bindings");
            }
            // GetCurveBindings only reports generic curves. A clip can still carry data the API does not list
            // (muscle curves, additive data), so the serialized fields are printed too rather than trusted.
            DumpClipSerialized(label, clip);
        }
    }

    public static void DumpShaderFacts(UmaContainer container)
    {
        Debug.Log("[shaders] ---- eye/face/cheek materials on the loaded model ----");
        var seen = new HashSet<string>();
        foreach (Renderer renderer in container.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null) continue;
            // sharedMaterials, not materials: the latter instantiates a copy per call and leaks it
            foreach (Material material in renderer.sharedMaterials)
            {
                if (material == null || material.shader == null) continue;
                string shaderName = material.shader.name;
                bool facial = UmaHeadlessExport.Contains(shaderName, "eye") || UmaHeadlessExport.Contains(shaderName, "face") || UmaHeadlessExport.Contains(shaderName, "cheek")
                              || UmaHeadlessExport.Contains(material.name, "eye") || UmaHeadlessExport.Contains(material.name, "face");
                if (!facial) continue;
                // several renderers share one material; print each distinct one once
                if (!seen.Add(shaderName + "|" + material.name)) continue;

                int propertyCount = material.shader.GetPropertyCount();
                Debug.Log($"[shaders] '{material.name}' on '{renderer.name}' uses shader '{shaderName}', "
                          + $"{propertyCount} properties");
                for (int i = 0; i < propertyCount; i++)
                {
                    string property = material.shader.GetPropertyName(i);
                    var type = material.shader.GetPropertyType(i);
                    string value;
                    try
                    {
                        switch (type)
                        {
                            case ShaderPropertyType.Float:
                            case ShaderPropertyType.Range:
                                value = material.GetFloat(property).ToString("0.0000"); break;
                            case ShaderPropertyType.Int:
                                value = material.GetInt(property).ToString(); break;
                            case ShaderPropertyType.Color:
                                var colour = material.GetColor(property);
                                value = $"({colour.r:F3},{colour.g:F3},{colour.b:F3},{colour.a:F3})"; break;
                            case ShaderPropertyType.Vector:
                                var vector = material.GetVector(property);
                                value = $"({vector.x:F3},{vector.y:F3},{vector.z:F3},{vector.w:F3})"; break;
                            case ShaderPropertyType.Texture:
                                var texture = material.GetTexture(property);
                                value = texture != null ? texture.name : "<none>"; break;
                            default:
                                value = "?"; break;
                        }
                    }
                    catch (Exception exception)
                    {
                        value = "(" + exception.GetType().Name + ")";
                    }
                    Debug.Log($"[shaders]    {property} [{type}] = {value}");
                }
            }
        }

        DumpShaderEffectData("head", container.HeadShaderEffectData);
        DumpShaderEffectData("body", container.BodyShaderEffectData);

        // The shipped eye shader has a _WetTex slot and a _WetRate that nothing in this viewer writes, and no
        // exported character carries a wet texture - so the teary state may be a wet overlay the game assigns at
        // runtime rather than only a _Limit drop. Name scan only, no bundle loading: whether such assets exist at
        // all is the question, and it is answerable from the asset list. Counted by part rather than listed,
        // because "wet" turns out to match thousands of them (every texture has a wet variant).
        var viewer = UmaViewerMain.Instance;
        if (viewer != null)
        {
            var wet = viewer.AbList.Keys.Where(name => UmaHeadlessExport.Contains(name, "wet")).ToList();
            Debug.Log($"[shaders] asset names containing 'wet': {wet.Count}");
            foreach (var part in new[] { "eyehi", "eye", "face", "cheek", "tear", "hair", "body", "attach" })
            {
                Debug.Log($"[shaders]   wet+{part}: {wet.Count(name => UmaHeadlessExport.Contains(name, part))}");
            }
            foreach (var name in wet.Where(name => UmaHeadlessExport.Contains(name, "eye")).Take(10))
            {
                Debug.Log($"[shaders]   eye wet: {name}");
            }
        }

        // A keyframed teary threshold would be a material curve on some clip in a motion bundle. Capped and
        // fully guarded: these are large bundles and a failure in here must cost the probe, not the export.
        // Character motions are looked at, not the first eight bundles: alphabetically those are campaign
        // backgrounds, which carry a background clip and nothing facial.
        int scanned = 0, clips = 0, materialCurves = 0, highlightCurves = 0;
        var main = UmaViewerMain.Instance;
        if (main != null)
        {
            var candidates = main.AbMotions.Where(entry => UmaHeadlessExport.Contains(entry.Name, "chara")).ToList();
            Debug.Log($"[shaders] motion scan: {candidates.Count} character motion bundle(s) of "
                      + $"{main.AbMotions.Count} available, sampling 8");
            foreach (var entry in candidates.Take(8))
            {
                try
                {
                    var bundle = UmaAssetManager.LoadAssetBundle(entry, true, false);
                    if (bundle == null) continue;
                    scanned++;
                    var loaded = bundle.LoadAllAssets<AnimationClip>();
                    int before = clips;
                    foreach (var clip in loaded)
                    {
                        if (clip == null) continue;
                        clips++;
                        foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                        {
                            if (binding.propertyName.IndexOf("aterial", StringComparison.Ordinal) < 0) continue;
                            materialCurves++;
                            if (binding.propertyName.IndexOf("_Limit", StringComparison.Ordinal) < 0
                                && binding.propertyName.IndexOf("_High", StringComparison.Ordinal) < 0) continue;
                            highlightCurves++;
                            Debug.Log($"[shaders] MATERIAL CURVE '{entry.Name}' clip '{clip.name}' "
                                      + $"{binding.path} -> {binding.propertyName}");
                        }
                    }
                    Debug.Log($"[shaders]   motion '{entry.Name}': {clips - before} clip(s)");
                }
                catch (Exception exception)
                {
                    Debug.Log($"[shaders] skipping motion '{entry.Name}': "
                              + $"{exception.GetType().Name}: {exception.Message}");
                }
            }
        }
        else
        {
            Debug.Log("[shaders] no UmaViewerMain instance, skipped the motion scan");
        }
        Debug.Log($"[shaders] motion scan: {scanned} bundle(s), {clips} clip(s), {materialCurves} material "
                  + $"curve(s), {highlightCurves} on _Limit/_High");
        Debug.Log("[shaders] done");
    }

    public static void DumpShaderEffectData(string label, CharaShaderEffectData data)
    {
        if (data == null)
        {
            Debug.Log($"[shaders] chara_shader_effect ({label}): none");
            return;
        }
        var serialized = new SerializedObject(data);
        var settings = serialized.FindProperty("_settingDataArray");
        int count = settings != null && settings.isArray ? settings.arraySize : 0;
        Debug.Log($"[shaders] chara_shader_effect ({label}) '{data.name}': {count} setting(s)");
        for (int i = 0; i < count; i++)
        {
            var element = settings.GetArrayElementAtIndex(i);
            var type = element.FindPropertyRelative("Type");
            var target = element.FindPropertyRelative("TargetMaterial");
            var parameters = element.FindPropertyRelative("ParamArray");
            var curves = element.FindPropertyRelative("ParamCurveArray");

            string kind = "?";
            if (type != null && type.enumNames != null
                && type.enumValueIndex >= 0 && type.enumValueIndex < type.enumNames.Length)
            {
                kind = type.enumNames[type.enumValueIndex];
            }
            string targetName = target != null && target.objectReferenceValue != null
                ? target.objectReferenceValue.name
                : "none";
            var values = new List<string>();
            if (parameters != null && parameters.isArray)
            {
                for (int p = 0; p < parameters.arraySize; p++)
                    values.Add(parameters.GetArrayElementAtIndex(p).intValue.ToString());
            }
            int curveCount = curves != null && curves.isArray ? curves.arraySize : 0;
            Debug.Log($"[shaders]   [{i}] {kind} material '{targetName}' "
                      + $"params=[{string.Join(",", values)}] curves={curveCount}");
        }
    }

    public static void DumpFacePipeline(GameObject root, string stem)
    {
        // once only: this sits in the stage loop, and without the guard it wrote a four gigabyte log
        if (UmaHeadlessExport.FaceDumped) return;
        UmaHeadlessExport.FaceDumped = true;

        foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            string lower = renderer.name.ToLowerInvariant();
            if (!lower.Contains("face") && !lower.Contains("head")) continue;

            Debug.Log($"{Tag} [face] {stem} renderer '{renderer.name}' mesh '{renderer.sharedMesh?.name}'");

            var mesh = renderer.sharedMesh;
            if (mesh != null)
            {
                var normals = mesh.normals;
                if (normals != null && normals.Length > 0)
                {
                    float min = float.MaxValue, max = 0f, total = 0f;
                    foreach (var n in normals)
                    {
                        float length = n.magnitude;
                        if (length < min) min = length;
                        if (length > max) max = length;
                        total += length;
                    }
                    float mean = total / normals.Length;
                    Debug.Log($"{Tag} [face] normals: {normals.Length} entries, length min {min:F6} mean {mean:F6} "
                              + $"max {max:F6}");
                }

                var colours = mesh.colors;
                if (colours == null || colours.Length == 0)
                {
                    Debug.Log($"{Tag} [face] vertex colours: none on the mesh");
                }
                else
                {
                    Vector4 min = new Vector4(float.MaxValue, float.MaxValue, float.MaxValue, float.MaxValue);
                    Vector4 max = new Vector4(float.MinValue, float.MinValue, float.MinValue, float.MinValue);
                    foreach (var c in colours)
                    {
                        min = Vector4.Min(min, c);
                        max = Vector4.Max(max, c);
                    }
                if (normals != null && normals.Length > 0)
                {
                    Vector3 sum = Vector3.zero;
                    foreach (var n in normals) sum += n;
                    Vector3 mean = (sum / normals.Length).normalized;
                    var bins = new int[19];
                    foreach (var n in normals)
                        bins[Mathf.Clamp((int)(Vector3.Angle(n, mean) / 5f), 0, 18)]++;
                    var parts = new List<string>();
                    for (int i = 0; i < bins.Length; i++)
                        if (bins[i] > 0) parts.Add($"{i * 5}-{i * 5 + 5}:{bins[i]}");
                    Debug.Log($"{Tag} [face] normal fingerprint: mean "
                              + $"({mean.x:F4},{mean.y:F4},{mean.z:F4}) angles to mean -> "
                              + string.Join(" ", parts));
                }
                    Vector4 colourTotal = Vector4.zero;
                    foreach (var c in colours) colourTotal += (Vector4)c;
                    Vector4 meanColour = colourTotal / colours.Length;
                    Debug.Log($"{Tag} [face] vertex colours: {colours.Length} entries, "
                              + $"r {min.x:F3}-{max.x:F3} g {min.y:F3}-{max.y:F3} "
                              + $"b {min.z:F3}-{max.z:F3} a {min.w:F3}-{max.w:F3}");
                    // the four means separately: the export packs this colour into the pmx's third
                    // extra uv as (r,g,b,a), so which channel is the blue is a question of order,
                    // and r and b measure close enough on this model that blue's mean alone cannot
                    // tell them apart
                    Debug.Log($"{Tag} [face] colour channel means: r {meanColour.x:F4} g {meanColour.y:F4} "
                              + $"b {meanColour.z:F4} a {meanColour.w:F4}");
                }
            }

            UmaHeadlessExport.DumpMeshArrays(renderer, mesh, root);

            foreach (var material in renderer.sharedMaterials)
            {
                if (material == null) continue;
                Debug.Log($"{Tag} [face] material '{material.name}' shader "
                          + $"'{(material.shader != null ? material.shader.name : "<none>")}'");
                var keywords = material.shaderKeywords;
                Debug.Log($"{Tag} [face]   enabled keywords: "
                          + (keywords == null || keywords.Length == 0
                              ? "<none>"
                              : string.Join(", ", keywords)));

                foreach (string property in material.GetTexturePropertyNames())
                {
                    var texture = material.GetTexture(property);
                    Debug.Log($"{Tag} [face]   texture {property} = "
                              + $"{(texture == null ? "<null>" : $"'{texture.name}' {texture.width}x{texture.height}")}");
                }

                var shader = material.shader;
                if (shader == null) continue;
                int count = ShaderUtil.GetPropertyCount(shader);
                for (int i = 0; i < count; i++)
                {
                    var type = ShaderUtil.GetPropertyType(shader, i);
                    string property = ShaderUtil.GetPropertyName(shader, i);
                    if (type == ShaderUtil.ShaderPropertyType.Float
                        || type == ShaderUtil.ShaderPropertyType.Range)
                    {
                        Debug.Log($"{Tag} [face]   float {property} = {material.GetFloat(property):F6}");
                    }
                    else if (type == ShaderUtil.ShaderPropertyType.Color)
                    {
                        var colour = material.GetColor(property);
                        Debug.Log($"{Tag} [face]   colour {property} = "
                                  + $"{colour.r:F3},{colour.g:F3},{colour.b:F3},{colour.a:F3}");
                    }
                }
            }
        }
    }

}