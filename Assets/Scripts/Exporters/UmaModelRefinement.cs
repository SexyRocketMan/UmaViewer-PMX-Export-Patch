using LibMMD.Model;
using System.Collections.Generic;
using UnityEngine;
using static LibMMD.Model.Morph;
using static LibMMD.Model.SkinningOperator;

/// <summary>
/// The optional step that makes an exported FBX stand in for the one thing this fork's Blender workflow
/// used to need the uma_addon for: the eye rig.
///
/// The add-on's <i>Refine Structure</i> merges the eyeball out of <c>Eye_L</c>/<c>Eye_R</c> into <c>Head</c>
/// and drives eight shape keys from those bones with drivers, so rotating an eye bone moves the gaze. FBX
/// has no driver, so an FBX that copies that structure would arrive with eyes that cannot be aimed. This
/// step takes the other of the two routes to the same behaviour, the one the format can carry and the one
/// MMD itself uses:
///
/// * the eyeball keeps its <c>Eye_L</c>/<c>Eye_R</c> weights, so rotating the bone rotates the eyeball;
/// * the eye bones are re-aimed the way the add-on aims them - <b>Y out of the face</b> along the gaze, and
///   <b>roll</b> such that the bone's Z points down - so the axes a user rotates are the ones the add-on's
///   drivers read (X = up/down, Z = left/right), and the behaviour is the same without any driver.
///
/// The gaze is taken from the feet and "up" from the head bone, both as the add-on derives them, so the
/// result does not depend on which way the exporter happened to aim the eye bones or on the rig sitting
/// square to the world axes.
///
/// It is deliberately a pure function of the model and its skeleton, in a file of its own, so that an
/// export can be taken with it or without it and nothing else in the exporter has to know it exists.
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
    /// The bones the add-on's <i>Set Bone Collections</i> puts in its hidden <c>Handle</c> collection, and
    /// which its <i>Del</i> deletes outright. They sit exactly on the bone they belong to, hold no weights
    /// and exist so the exporter has somewhere to hang a curve - so a rig without them poses and skins the
    /// same. An FBX has no "hidden", so the only way to give a reader the rig the add-on shows is to leave
    /// them out.
    /// </summary>
    private const string HiddenBoneSuffix = "Handle";

    /// <summary>
    /// The rest of what the add-on hides - <i>Refine Structure</i>'s own list. These are the ground-contact
    /// offsets, the hand attach points and the upper-body control: controls for posing, not joints of the
    /// model, and each of them sits on top of the bone it belongs to, which is what makes its owner's drawn
    /// length come out as a dot when nothing else is under it.
    /// </summary>
    private static readonly string[] HiddenBoneNames =
    {
        "Ankle_offset_L", "Ankle_offset_R", "Toe_offset_L", "Toe_offset_R",
        "Hand_Attach_L", "Hand_Attach_R", "UpBody_Ctrl"
    };

    /// <summary>
    /// Drops the helper bones from the model, moving any weight they carried onto the bone they belong to
    /// (their parent - for a <c>&lt;name&gt;_Handle</c> that is the bone itself). Returns how many went.
    ///
    /// This runs before the skeleton is built, because dropping a bone renumbers the ones after it and the
    /// vertex skinning refers to them by index.
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

        // old index -> new index, for the bones that stay. The skinning and the bone morphs refer to bones
        // by index too, so this one table is what keeps a vertex weighted to the bone it was weighted to.
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

            // The tail is a bone index as well, and it is what the writer reads a bone's direction and
            // length from. Left stale it points at whichever bone moved into that slot - how a rig comes out
            // with its bones aimed at each other's neighbours - so it is renumbered, and where the child it
            // named has been left out it becomes an offset that keeps the same direction and length.
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
                    // both slots landing on the same bone means that bone carries the whole vertex, which
                    // is what a weight of 1 says - leaving the pair split across one bone would leave a
                    // reader with two entries for it and half the weight
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
                    // fold slots that now name the same bone into the first of them
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
    /// Two bones whose weights are (w, 1-w). A bone that was left out leaves its weight on its own slot and
    /// only changes which bone that slot names - the bone it belongs to picks the weight up, which is what
    /// the add-on's Del does when it deletes a collection's bones.
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
    /// Re-aims the eye rig on <paramref name="skeleton"/> and reports on the parts of the add-on's
    /// refinement that no FBX can carry. <paramref name="model"/> is read, never modified: the weights and
    /// morphs it already has are exactly what this route wants.
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
            // The locators are aimed the same way so a rig that follows them keeps the same axes; they
            // carry no weights themselves, so this is cosmetic, but it is what the add-on's rig looks like.
            Aim(skeleton, name, gaze, up);
        }

        report.Notes.Add("bone collections, connected bones, pose-bone visibility and shape-key drivers "
                         + "have no FBX field: an importer has to put those back (the reworked add-on's job)");
        report.Notes.Add("the game's four gaze morphs stay as ordinary shape keys; the add-on consumes them "
                         + "into eight mirrored control keys, which is not needed on this route");
        return report;
    }

    /// <summary>Aims one bone: Y along <paramref name="direction"/>, roll such that Z points away from
    /// <paramref name="up"/> - the add-on's `align_roll(down)`. Both are Unity-space vectors.</summary>
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
    /// Which way the model looks, taken from the feet: the toe sits in front of the ankle on every
    /// humanoid rig, so this does not depend on how the eye bones themselves were aimed. This is the
    /// add-on's <c>gaze_direction</c>, with the same fallback to the model's forward.
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
            // Drop the vertical component, so the gaze is the horizontal way the model faces. The add-on
            // drops its Z because it works in Blender's Z-up space; this side is in Unity's Y-up space, so
            // it is Y that has to go - dropping Z here would leave a mostly vertical vector, which aims the
            // eye bones down instead of forward.
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
    /// Up, in the model's own frame: the head bone points out of the neck, so its direction is up. This is
    /// the add-on's <c>up_direction</c>, and using the rig instead of the world keeps the eye controls'
    /// axes right on a model that is not square to the world.
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

    /// <summary>How many mesh vertices the eye bones actually carry - gaze by bone rotation needs some.</summary>
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
