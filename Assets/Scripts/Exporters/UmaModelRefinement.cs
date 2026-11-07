using LibMMD.Model;
using System.Collections.Generic;
using UnityEngine;
using static LibMMD.Model.Morph;
using static LibMMD.Model.SkinningOperator;

/// <summary>
/// Stands in for the one part of the uma_addon's Blender refinement an FBX cannot carry: the add-on drives eight eye shape
/// keys from the eye bones with drivers, and FBX has no drivers. The eyeball keeps its <c>Eye_L</c>/<c>Eye_R</c> weights and
/// the eye bones are re-aimed the way the add-on aims them - <b>Y out of the face</b> along the gaze, roll such that Z points
/// down (X = up/down, Z = left/right) - so rotating a bone moves the gaze without any driver.
/// </summary>
public static class UmaModelRefinement
{
    /// <summary>The bones the add-on re-aims, and the locators that go with them.</summary>
    private static readonly string[] EyeBones = { "Eye_L", "Eye_R" };
    private static readonly string[] EyeHelpers =
    {
        "Eye_locator_L", "Eye_locator_R", "Eye_target_locator_L", "Eye_target_locator_R"
    };

    /// <summary>
    /// The add-on hides these in its <c>Handle</c> collection and <i>Del</i> deletes them: they sit on the bone they belong to,
    /// carry no weights, and an FBX has no "hidden".
    /// </summary>
    private const string HiddenBoneSuffix = "Handle";

    /// <summary>
    /// The rest of what the add-on's <i>Refine Structure</i> hides: ground-contact offsets, hand attach points and the upper-body
    /// control - controls for posing, not joints.
    /// </summary>
    private static readonly string[] HiddenBoneNames =
    {
        "Ankle_offset_L", "Ankle_offset_R", "Toe_offset_L", "Toe_offset_R",
        "Hand_Attach_L", "Hand_Attach_R", "UpBody_Ctrl"
    };

    /// <summary>
    /// Drops the helper bones, moving any weight they carried onto the bone they belong to (their parent - for a
    /// <c>&lt;name&gt;_Handle</c> that is the bone itself). Returns how many went. Runs before the skeleton is built, because
    /// dropping a bone renumbers the ones after it and vertex skinning refers to them by index.
    /// </summary>
    public static int OmitHiddenBones(RawMMDModel model)
    {
        LibMMD.Model.Bone[] bones = model.Bones;
        if (bones == null || bones.Length == 0) return 0;

        // replacement[i] = the index that stands in for bone i once the dropped ones are gone
        int[] replacement = new int[bones.Length];
        bool[] dropped = new bool[bones.Length];
        for (int i = 0; i < bones.Length; i++)
        {
            string name = bones[i].Name;
            dropped[i] = name != null &&
                         (name.EndsWith(HiddenBoneSuffix, System.StringComparison.Ordinal) ||
                          System.Array.IndexOf(HiddenBoneNames, name) >= 0);
            replacement[i] = i;
        }
        int droppedCount = 0;
        for (int i = 0; i < bones.Length; i++)
        {
            if (!dropped[i]) continue;
            droppedCount++;
            // walk up to the first ancestor that stays; a handle's parent is the bone it belongs to
            int ancestor = bones[i].ParentIndex;
            int guard = 0;
            while (ancestor >= 0 && ancestor < bones.Length && dropped[ancestor] && guard++ < bones.Length)
            {
                ancestor = bones[ancestor].ParentIndex;
            }
            replacement[i] = ancestor >= 0 && ancestor < bones.Length ? ancestor : -1;
        }
        if (droppedCount == 0) return 0;

        // old index -> new index for the bones that stay; skinning and bone morphs refer to bones by index,
        // so this one table is what keeps a vertex weighted to the bone it was weighted to.
        int[] newIndex = new int[bones.Length];
        for (int i = 0; i < newIndex.Length; i++) newIndex[i] = -1;
        List<LibMMD.Model.Bone> kept = new List<LibMMD.Model.Bone>();
        for (int i = 0; i < bones.Length; i++)
        {
            if (dropped[i]) continue;
            newIndex[i] = kept.Count;
            kept.Add(bones[i]);
        }
        int[] finalIndex = new int[bones.Length];
        for (int i = 0; i < bones.Length; i++)
        {
            int resolved = replacement[i];
            finalIndex[i] = resolved >= 0 && resolved < bones.Length ? newIndex[resolved] : -1;
        }

        RepointSkinning(model.Vertices, finalIndex);
        RepointBoneMorphs(model.Morphs, finalIndex);

        for (int i = 0; i < bones.Length; i++)
        {
            if (dropped[i]) continue;
            int parent = bones[i].ParentIndex;
            bones[i].ParentIndex = parent >= 0 && parent < bones.Length ? finalIndex[parent] : -1;

            // the tail is a bone index too and sets the bone's direction and length: left stale it points at
            // whichever bone moved into that slot, so a dropped child becomes an offset of the same direction
            LibMMD.Model.Bone.ChildBone tail = bones[i].ChildBoneVal;
            if (tail == null || !tail.ChildUseId) continue;
            int oldTail = tail.Index;
            int mapped = oldTail >= 0 && oldTail < bones.Length ? finalIndex[oldTail] : -1;
            if (mapped >= 0 && mapped != newIndex[i])
            {
                tail.Index = mapped;
                continue;
            }
            Vector3 offset = oldTail >= 0 && oldTail < bones.Length
                ? bones[oldTail].Position - bones[i].Position
                : Vector3.zero;
            tail.ChildUseId = false;
            tail.Offset = offset;
        }

        model.Bones = kept.ToArray();
        return droppedCount;
    }

    /// <summary>A bone morph drives a bone by index, so it needs the same renumbering.</summary>
    private static void RepointBoneMorphs(Morph[] morphs, int[] finalIndex)
    {
        foreach (Morph morph in morphs ?? new Morph[0])
        {
            foreach (MorphData data in morph.MorphDatas ?? new MorphData[0])
            {
                if (!(data is BoneMorphData bone)) continue;
                if (bone.BoneIndex < 0 || bone.BoneIndex >= finalIndex.Length) continue;
                if (finalIndex[bone.BoneIndex] >= 0) bone.BoneIndex = finalIndex[bone.BoneIndex];
            }
        }
    }

    /// <summary>Points every vertex's skinning at the new index of the bone it referenced.</summary>
    private static void RepointSkinning(Vertex[] vertices, int[] finalIndex)
    {
        foreach (Vertex vertex in vertices ?? new Vertex[0])
        {
            SkinningOperator skinning = vertex.SkinningOperator;
            if (skinning == null || skinning.Param == null) continue;
            switch (skinning.Type)
            {
                case SkinningType.SkinningBdef1:
                {
                    Bdef1 bdef1 = (Bdef1)skinning.Param;
                    bdef1.BoneId = Resolve(bdef1.BoneId, finalIndex);
                    break;
                }
                case SkinningType.SkinningBdef2:
                {
                    Bdef2 bdef2 = (Bdef2)skinning.Param;
                    ResolvePair(bdef2.BoneId, finalIndex);
                    // both slots on one bone means that bone carries the whole vertex: a split pair would leave
                    // a reader with two entries for it and half the weight
                    if (bdef2.BoneId[0] == bdef2.BoneId[1]) bdef2.BoneWeight = 1f;
                    break;
                }
                case SkinningType.SkinningBdef4:
                {
                    Bdef4 bdef4 = (Bdef4)skinning.Param;
                    for (int slot = 0; slot < 4; slot++)
                    {
                        int bone = bdef4.BoneId[slot];
                        if (bone < 0 || bone >= finalIndex.Length || finalIndex[bone] < 0) continue;
                        bdef4.BoneId[slot] = finalIndex[bone];
                    }
                    // fold slots that ended up naming the same bone into the first of them
                    for (int slot = 0; slot < 4; slot++)
                    {
                        for (int other = slot + 1; other < 4; other++)
                        {
                            if (bdef4.BoneId[other] != bdef4.BoneId[slot]) continue;
                            bdef4.BoneWeight[slot] += bdef4.BoneWeight[other];
                            bdef4.BoneWeight[other] = 0f;
                        }
                    }
                    break;
                }
                case SkinningType.SkinningSdef:
                {
                    Sdef sdef = (Sdef)skinning.Param;
                    ResolvePair(sdef.BoneId, finalIndex);
                    if (sdef.BoneId[0] == sdef.BoneId[1]) sdef.BoneWeight = 1f;
                    break;
                }
            }
        }
    }

    private static int Resolve(int bone, int[] finalIndex)
    {
        if (bone < 0 || bone >= finalIndex.Length) return bone;
        return finalIndex[bone] >= 0 ? finalIndex[bone] : bone;
    }

    /// <summary>
    /// Two bones whose weights are (w, 1-w). A dropped bone leaves its weight on its own slot and only changes which bone the
    /// slot names, so the bone it belonged to picks the weight up - what the add-on's Del does.
    /// </summary>
    private static void ResolvePair(int[] bones, int[] finalIndex)
    {
        bones[0] = Resolve(bones[0], finalIndex);
        bones[1] = Resolve(bones[1], finalIndex);
    }

    /// <summary>What the step did, and what it could not do because the format has no field for it.</summary>
    public sealed class Report
    {
        public bool Applied;
        public bool GazeFromFeet;
        public bool UpFromHead;
        public int EyeBonesAimed;
        public int EyeSkinnedVertices;
        public int HiddenBonesOmitted;
        public readonly List<string> Notes = new List<string>();

        public bool EyesCanBeAimed
        {
            get { return EyeSkinnedVertices > 0; }
        }

        public string Summary()
        {
            if (!Applied) return "uma refinement: off";
            List<string> lines = new List<string>();
            if (HiddenBonesOmitted > 0)
            {
                lines.Add("uma refinement: left out " + HiddenBonesOmitted + " bones the addon hides "
                          + "(its Handle collection and Refine Structure's own hidden list) - an FBX has no "
                          + "hidden, so they are simply absent; any weight they carried moved to the bone "
                          + "they belong to");
            }
            lines.Add("uma refinement: aimed " + EyeBonesAimed + " eye bones at the gaze ("
                      + (GazeFromFeet ? "from the feet" : "fallback direction") + ", "
                      + (UpFromHead ? "up from the head bone" : "up fallback") + ")");
            lines.Add("   eyeball vertices on Eye_L/Eye_R: " + EyeSkinnedVertices
                      + (EyesCanBeAimed
                          ? " - rotating either eye bone moves the gaze, no driver needed"
                          : " - WARNING: the eyeball is not skinned to the eye bones, so rotating them will not move it"));
            foreach (string note in Notes)
            {
                lines.Add("   " + note);
            }
            return string.Join("\n", lines.ToArray());
        }
    }

    /// <summary>
    /// Re-aims the eye rig on <paramref name="skeleton"/> and reports the parts of the refinement no FBX can carry.
    /// <paramref name="model"/> is read, never modified: its weights and morphs are what this route wants.
    /// </summary>
    public static Report Apply(RawMMDModel model, FbxExporter.Skeleton skeleton)
    {
        Report report = new Report();
        report.Applied = true;

        report.EyeSkinnedVertices = CountEyeSkinnedVertices(model, skeleton);

        Vector3 gaze;
        report.GazeFromFeet = TryGazeFromFeet(skeleton, out gaze);
        if (!report.GazeFromFeet) gaze = Vector3.forward;

        Vector3 up;
        report.UpFromHead = TryHeadDirection(skeleton, out up);
        if (!report.UpFromHead) up = Vector3.up;

        foreach (string name in EyeBones)
        {
            report.EyeBonesAimed += Aim(skeleton, name, gaze, up) ? 1 : 0;
        }
        foreach (string name in EyeHelpers)
        {
            // aimed too, so a rig following them keeps the same axes (cosmetic: these carry no weights)
            Aim(skeleton, name, gaze, up);
        }

        report.Notes.Add("bone collections, connected bones, pose-bone visibility and shape-key drivers "
                         + "have no FBX field: an importer has to put those back (the reworked add-on's job)");
        report.Notes.Add("the game's four gaze morphs stay as ordinary shape keys; the add-on consumes them "
                         + "into eight mirrored control keys, which is not needed on this route");
        return report;
    }

    /// <summary>
    /// Aims one bone: Y along <paramref name="direction"/>, roll so Z points away from <paramref name="up"/> - the
    /// add-on's `align_roll(down)`. Both are Unity-space vectors.
    /// </summary>
    private static bool Aim(FbxExporter.Skeleton skeleton, string name, Vector3 direction, Vector3 up)
    {
        int index = skeleton.IndexOf(name);
        if (index < 0) return false;
        FbxExporter.Skeleton.Bone bone = skeleton.Bones[index];
        bone.Direction = direction;
        bone.Rest = FbxExporter.Skeleton.RestMatrix(bone.Head, direction, up);
        return true;
    }

    /// <summary>
    /// Which way the model looks, taken from the feet: the toe sits in front of the ankle on every humanoid rig, so this does
    /// not depend on how the eye bones were aimed. The add-on's <c>gaze_direction</c>, same fallback.
    /// </summary>
    private static bool TryGazeFromFeet(FbxExporter.Skeleton skeleton, out Vector3 gaze)
    {
        Vector3 sum = Vector3.zero;
        int found = 0;
        foreach (string side in new[] { "L", "R" })
        {
            int toe = skeleton.IndexOf("Toe_" + side);
            int ankle = skeleton.IndexOf("Ankle_" + side);
            if (toe < 0 || ankle < 0) continue;
            Vector3 direction = skeleton.Bones[toe].Head - skeleton.Bones[ankle].Head;
            // drop the vertical component so the gaze is the horizontal way the model faces. The add-on drops Z because it works Z-up
            // in Blender; this side is Y-up, so dropping Z here would leave a mostly vertical vector and aim the eyes down.
            direction.y = 0f;
            if (direction.sqrMagnitude < 1e-10f) continue;
            sum += direction.normalized;
            found++;
        }
        if (found == 0 || sum.sqrMagnitude < 1e-10f)
        {
            gaze = Vector3.forward;
            return false;
        }
        gaze = sum.normalized;
        return true;
    }

    /// <summary>
    /// Up, in the model's own frame: the head bone points out of the neck, so its direction is up - the add-on's
    /// <c>up_direction</c>, which keeps the eye axes right on a model not square to the world.
    /// </summary>
    private static bool TryHeadDirection(FbxExporter.Skeleton skeleton, out Vector3 up)
    {
        int head = skeleton.IndexOf("Head");
        if (head < 0)
        {
            up = Vector3.up;
            return false;
        }
        up = skeleton.Bones[head].Direction;
        if (up.sqrMagnitude < 1e-10f)
        {
            up = Vector3.up;
            return false;
        }
        up.Normalize();
        return true;
    }

    /// <summary>How many mesh vertices the eye bones carry - gaze by bone rotation needs some.</summary>
    private static int CountEyeSkinnedVertices(RawMMDModel model, FbxExporter.Skeleton skeleton)
    {
        int left = skeleton.IndexOf("Eye_L");
        int right = skeleton.IndexOf("Eye_R");
        if (left < 0 && right < 0) return 0;

        int count = 0;
        foreach (Vertex vertex in model.Vertices ?? new Vertex[0])
        {
            SkinningOperator skinning = vertex.SkinningOperator;
            if (skinning == null || skinning.Param == null) continue;
            bool eye = false;
            switch (skinning.Type)
            {
                case SkinningType.SkinningBdef1:
                    eye = IsEye(((Bdef1)skinning.Param).BoneId, left, right);
                    break;
                case SkinningType.SkinningBdef2:
                    eye = IsEye(((Bdef2)skinning.Param).BoneId, left, right);
                    break;
                case SkinningType.SkinningBdef4:
                    eye = IsEye(((Bdef4)skinning.Param).BoneId, left, right);
                    break;
                case SkinningType.SkinningSdef:
                    eye = IsEye(((Sdef)skinning.Param).BoneId, left, right);
                    break;
            }
            if (eye) count++;
        }
        return count;
    }

    private static bool IsEye(int bone, int left, int right)
    {
        return bone == left || bone == right;
    }

    private static bool IsEye(int[] bones, int left, int right)
    {
        foreach (int bone in bones)
        {
            if (bone == left || bone == right) return true;
        }
        return false;
    }
}
