using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System;
using System.Linq;
using Gallop;

//初期ポーズ(T,Aポーズ)の時点でアタッチ、有効化されている必要がある
public class UnityHumanoidVMDRecorder : MonoBehaviour
{
    public const string FileSavePath = "/../VMDRecords";
    public bool UseParentOfAll = true;
    public bool UseCenterAsParentOfAll = true;
    /// <summary>
    /// 全ての親の座標・回転を絶対座標系で計算する
    /// UseParentOfAllがTrueでないと意味がない
    /// </summary>
    public bool UseAbsoluteCoordinateSystem = false;
    public bool IgnoreInitialPosition = false;
    public bool IgnoreInitialRotation = false;
    /// <summary>
    /// 一部のモデルではMMD上ではセンターが足元にある
    /// Start前に設定されている必要がある
    /// </summary>
    public bool UseBottomCenter = false;
    /// <summary>
    /// Unity上のモーフ名に1.まばたきなど番号が振られている場合、番号を除去する
    /// </summary>
    public bool TrimMorphNumber = false;
    public int KeyReductionLevel = 2;
    public bool IsRecording { get; private set; } = false;
    public int FrameNumber { get; private set; } = 0;
    int frameNumberSaved = 0;
    const float FPSs = 0.03333f;
    const string CenterNameString = "センター";
    const string GrooveNameString = "グルーブ";

    public enum BoneNames
    {
        全ての親, センター, 左足ＩＫ, 右足ＩＫ, 上半身, 上半身2, 首, 頭,
        左肩, 左腕, 左ひじ, 左手首, 右肩, 右腕, 右ひじ, 右手首,
        左親指１, 左親指２, 左人指１, 左人指２, 左人指３, 左中指１, 左中指２, 左中指３,
        左薬指１, 左薬指２, 左薬指３, 左小指１, 左小指２, 左小指３, 右親指１, 右親指２,
        右人指１, 右人指２, 右人指３, 右中指１, 右中指２, 右中指３, 右薬指１, 右薬指２,
        右薬指３, 右小指１, 右小指２, 右小指３, 左足, 右足, 左ひざ, 右ひざ,
        左足首, 右足首, 左足先EX, 右足先EX, None
    }

    // Dictionary for English bone name mapping
    private static readonly Dictionary<BoneNames, string> EnglishBoneNameMap = new Dictionary<BoneNames, string>
    {
        { BoneNames.全ての親, "Position" },
        { BoneNames.センター, "Hip" },
        { BoneNames.上半身, "Spine" },
        { BoneNames.上半身2, "Chest" },
        { BoneNames.頭, "Head" },
        { BoneNames.首, "Neck" },
        { BoneNames.左肩, "Shoulder_L" },
        { BoneNames.右肩, "Shoulder_R" },
        { BoneNames.左腕, "Arm_L" },
        { BoneNames.右腕, "Arm_R" },
        { BoneNames.左ひじ, "Elbow_L" },
        { BoneNames.右ひじ, "Elbow_R" },
        { BoneNames.左手首, "Wrist_L" },
        { BoneNames.右手首, "Wrist_R" },
        { BoneNames.左親指１, "Thumb_01_L" },
        { BoneNames.右親指１, "Thumb_01_R" },
        { BoneNames.左親指２, "Thumb_02_L" },
        { BoneNames.右親指２, "Thumb_02_R" },
        { BoneNames.左人指１, "Index_01_L" },
        { BoneNames.右人指１, "Index_01_R" },
        { BoneNames.左人指２, "Index_02_L" },
        { BoneNames.右人指２, "Index_02_R" },
        { BoneNames.左人指３, "Index_03_L" },
        { BoneNames.右人指３, "Index_03_R" },
        { BoneNames.左中指１, "Middle_01_L" },
        { BoneNames.右中指１, "Middle_01_R" },
        { BoneNames.左中指２, "Middle_02_L" },
        { BoneNames.右中指２, "Middle_02_R" },
        { BoneNames.左中指３, "Middle_03_L" },
        { BoneNames.右中指３, "Middle_03_R" },
        { BoneNames.左薬指１, "Ring_01_L" },
        { BoneNames.右薬指１, "Ring_01_R" },
        { BoneNames.左薬指２, "Ring_02_L" },
        { BoneNames.右薬指２, "Ring_02_R" },
        { BoneNames.左薬指３, "Ring_03_L" },
        { BoneNames.右薬指３, "Ring_03_R" },
        { BoneNames.左小指１, "Pinky_01_L" },
        { BoneNames.右小指１, "Pinky_01_R" },
        { BoneNames.左小指２, "Pinky_02_L" },
        { BoneNames.右小指２, "Pinky_02_R" },
        { BoneNames.左小指３, "Pinky_03_L" },
        { BoneNames.右小指３, "Pinky_03_R" },
        { BoneNames.左足, "Thigh_L" },
        { BoneNames.右足, "Thigh_R" },
        { BoneNames.左ひざ, "Knee_L" },
        { BoneNames.右ひざ, "Knee_R" },
        { BoneNames.左足首, "Ankle_L" },
        { BoneNames.右足首, "Ankle_R" },
        { BoneNames.左足先EX, "Toe_L" },
        { BoneNames.右足先EX, "Toe_R" },
        { BoneNames.左足ＩＫ, "Ankle_L_IK" },
        { BoneNames.右足ＩＫ, "Ankle_R_IK" }
    };

    private string GetBoneNameForExport(BoneNames boneName)
    {
        // Check if we should use English names
        if (Config.Instance.VmdUseEnglishBoneNames)
        {
            if (EnglishBoneNameMap.TryGetValue(boneName, out string englishName))
            {
                return englishName;
            }
            else
            {
                Debug.LogWarning($"Bone name lookup failed for {boneName}");
            }
        }
        
        // Default: Use Japanese names (original behavior)
        string boneNameString = boneName.ToString();
        if (boneName == BoneNames.全ての親 && UseCenterAsParentOfAll)
        {
            boneNameString = CenterNameString;
        }
        if (boneName == BoneNames.センター && UseCenterAsParentOfAll)
        {
            boneNameString = GrooveNameString;
        }
        return boneNameString;
    }

    //コンストラクタにて初期化
    //全てのボーンを名前で引く辞書
    Dictionary<string, Transform> transformDictionary = new Dictionary<string, Transform>();
    public Dictionary<BoneNames, Transform> BoneDictionary { get; private set; }
    Vector3 parentInitialPosition = Vector3.zero;
    Quaternion parentInitialRotation = Quaternion.identity;
    Dictionary<BoneNames, List<Vector3>> positionDictionary = new Dictionary<BoneNames, List<Vector3>>();
    Dictionary<BoneNames, List<Vector3>> positionDictionarySaved = new Dictionary<BoneNames, List<Vector3>>();
    Dictionary<BoneNames, List<Quaternion>> rotationDictionary = new Dictionary<BoneNames, List<Quaternion>>();
    Dictionary<BoneNames, List<Quaternion>> rotationDictionarySaved = new Dictionary<BoneNames, List<Quaternion>>();
    Dictionary<BoneNames, Vector3> _boneInitialLocalPositions = new Dictionary<BoneNames, Vector3>();
    Dictionary<int, bool> visitableDictionary = new Dictionary<int, bool>();
    //ボーン移動量の補正係数
    //この値は大体の値、正確ではない
    const float DefaultBoneAmplifier = 12.5f;

    public Vector3 ParentOfAllOffset = new Vector3(0, 0, 0);
    public Vector3 LeftFootIKOffset = Vector3.zero;
    public Vector3 RightFootIKOffset = Vector3.zero;

    BoneGhost boneGhost;
    public MorphRecorder morphRecorder;
    public MorphRecorder morphRecorderSaved;

    private UmaContainer container;
    float aposeDegress = 38.5f;

    public bool IsLive;

    // finds a bone by trying multiple common naming conventions.
    private Transform FindBone(List<Transform> objs, params string[] possibleNames)
    {
        // Try exact match first
        foreach (var name in possibleNames)
        {
            var found = objs.Find(a => a.name.Equals(name));
            if (found != null) return found;
        }
        
        // Fallback to partial match (contains)
        foreach(var name in possibleNames) 
        {
            var found = objs.Find(a => a.name.Contains(name));
            if (found != null) return found;
        }
        return null;
    }

    public void Initialize()
    {
        Time.fixedDeltaTime = FPSs;
        container = GetComponentInParent<UmaContainer>();
        List<Transform> objs = GetComponentsInChildren<Transform>().ToList();

        // === DIAGNOSTIC TOOL ===
        if (container != null && UmaViewerBuilder.Instance.CurrentUMAContainer.IsMini) 
        {
            Debug.Log($"[VMD Debug] Mini model detected. Dumping all bone names in hierarchy:");
            foreach(var t in objs) 
            {
                Debug.Log($" - {t.name}");
            }
        }

        bool isMini = UmaViewerBuilder.Instance.CurrentUMAContainer.IsMini;
        // New bone mapping, tries multiple common aliases for each bone.
        // Uses some fuckass mappings for the mini-uma hands (they have hl2 style 3 finger hands)
        BoneDictionary = new Dictionary<BoneNames, Transform>()
        {
            { BoneNames.全ての親, transform },
            { BoneNames.センター, FindBone(objs, "Hip", "Hips", "Waist", "Pelvis", "Root") },
            // Map 上半身 to Waist if Spine doesn't exist
            { BoneNames.上半身,   FindBone(objs, "Spine", "Spine_01", "Spine1", "UpperBody", "Waist") },
            { BoneNames.上半身2,  FindBone(objs, "Chest", "Chest_01", "Spine_02", "Spine2", "UpperBody_02") },
            { BoneNames.頭,       FindBone(objs, "Head", "Head_01") },
            { BoneNames.首,       FindBone(objs, "Neck", "Neck_01") },
            
            { BoneNames.左肩,     FindBone(objs, "Shoulder_L", "ShoulderL", "Clavicle_L", "ClavicleL") },
            { BoneNames.右肩,     FindBone(objs, "Shoulder_R", "ShoulderR", "Clavicle_R", "ClavicleR") },
            { BoneNames.左腕,     FindBone(objs, "Arm_L", "ArmL", "UpperArm_L", "UpperArmL", "Arm_01_L") },
            { BoneNames.右腕,     FindBone(objs, "Arm_R", "ArmR", "UpperArm_R", "UpperArmR", "Arm_01_R") },
            { BoneNames.左ひじ,   FindBone(objs, "Elbow_L", "ElbowL", "LowerArm_L", "LowerArmL", "Forearm_L", "ForearmL", "Arm_02_L") },
            { BoneNames.右ひじ,   FindBone(objs, "Elbow_R", "ElbowR", "LowerArm_R", "LowerArmR", "Forearm_R", "ForearmR", "Arm_02_R") },
            { BoneNames.左手首,   FindBone(objs, "Wrist_L", "WristL", "Hand_L", "HandL", "Hand_01_L") },
            { BoneNames.右手首,   FindBone(objs, "Wrist_R", "WristR", "Hand_R", "HandR", "Hand_01_R") },
            
            // --- FINGER MAPPINGS ---
            // jesus christ
            // Thumb
            { BoneNames.左親指１, FindBone(objs, "Thumb_01_L") },
            { BoneNames.右親指１, FindBone(objs, "Thumb_01_R") },
            { BoneNames.左親指２, isMini ? null : FindBone(objs, "Thumb_02_L") },
            { BoneNames.右親指２, isMini ? null : FindBone(objs, "Thumb_02_R") },

            // Index
            { BoneNames.左人指１, FindBone(objs, "Index_01_L") },
            { BoneNames.右人指１, FindBone(objs, "Index_01_R") },
            // Mini: 02 is null, 03 gets the rotation. Normal: standard 02 and 03.
            { BoneNames.左人指２, isMini ? null : FindBone(objs, "Index_02_L", "IndexIntermediate_L") },
            { BoneNames.右人指２, isMini ? null : FindBone(objs, "Index_02_R", "IndexIntermediate_R") },
            { BoneNames.左人指３, FindBone(objs, "Index_03_L") },
            { BoneNames.右人指３, FindBone(objs, "Index_03_R") },

            // Middle (Maps to Ring for mini)
            { BoneNames.左中指１, isMini ? FindBone(objs, "Ring_01_L") : FindBone(objs, "Middle_01_L") },
            { BoneNames.右中指１, isMini ? FindBone(objs, "Ring_01_R") : FindBone(objs, "Middle_01_R") },
            { BoneNames.左中指２, isMini ? null : FindBone(objs, "Middle_02_L") },
            { BoneNames.右中指２, isMini ? null : FindBone(objs, "Middle_02_R") },
            { BoneNames.左中指３, isMini ? FindBone(objs, "Ring_03_L") : FindBone(objs, "Middle_03_L") },
            { BoneNames.右中指３, isMini ? FindBone(objs, "Ring_03_R") : FindBone(objs, "Middle_03_R") },

            // Ring
            { BoneNames.左薬指１, isMini ? FindBone(objs, "Ring_01_L") : FindBone(objs, "Ring_01_L") },
            { BoneNames.右薬指１, isMini ? FindBone(objs, "Ring_01_R") : FindBone(objs, "Ring_01_R") },
            { BoneNames.左薬指２, isMini ? null : FindBone(objs, "Ring_02_L") },
            { BoneNames.右薬指２, isMini ? null : FindBone(objs, "Ring_02_R") },
            { BoneNames.左薬指３, isMini ? FindBone(objs, "Ring_03_L") : FindBone(objs, "Ring_03_L") },
            { BoneNames.右薬指３, isMini ? FindBone(objs, "Ring_03_R") : FindBone(objs, "Ring_03_R") },

            // Pinky (Maps to Ring for mini)
            { BoneNames.左小指１, isMini ? FindBone(objs, "Ring_01_L") : FindBone(objs, "Pinky_01_L") },
            { BoneNames.右小指１, isMini ? FindBone(objs, "Ring_01_R") : FindBone(objs, "Pinky_01_R") },
            { BoneNames.左小指２, isMini ? null : FindBone(objs, "Pinky_02_L") },
            { BoneNames.右小指２, isMini ? null : FindBone(objs, "Pinky_02_R") },
            { BoneNames.左小指３, isMini ? FindBone(objs, "Ring_03_L") : FindBone(objs, "Pinky_03_L") },
            { BoneNames.右小指３, isMini ? FindBone(objs, "Ring_03_R") : FindBone(objs, "Pinky_03_R") },
            
            // Legs & Feet
            { BoneNames.左足,     FindBone(objs, "Thigh_L", "ThighL", "UpperLeg_L", "UpperLegL", "Leg_01_L") },
            { BoneNames.右足,     FindBone(objs, "Thigh_R", "ThighR", "UpperLeg_R", "UpperLegR", "Leg_01_R") },
            { BoneNames.左ひざ,   FindBone(objs, "Knee_L", "KneeL", "LowerLeg_L", "LowerLegL", "Calf_L", "CalfL", "Leg_02_L") },
            { BoneNames.右ひざ,   FindBone(objs, "Knee_R", "KneeR", "LowerLeg_R", "LowerLegR", "Calf_R", "CalfR", "Leg_02_R") },
            { BoneNames.左足首,   FindBone(objs, "Ankle_L", "AnkleL", "Foot_L", "FootL", "Leg_03_L") },
            { BoneNames.右足首,   FindBone(objs, "Ankle_R", "AnkleR", "Foot_R", "FootR", "Leg_03_R") },
            // Toes might not exist in mini-umas, use Ankle as fallback
            { BoneNames.左足先EX, FindBone(objs, "Toe_L", "ToeL", "Toes_L", "ToesL", "Ankle_L") },
            { BoneNames.右足先EX, FindBone(objs, "Toe_R", "ToeR", "Toes_R", "ToesR", "Ankle_R") },
            
            // IK targets (Mapped to feet/ankles as per original logic)
            { BoneNames.左足ＩＫ, FindBone(objs, "Ankle_L", "AnkleL", "Foot_L", "FootL", "Leg_03_L") },
            { BoneNames.右足ＩＫ, FindBone(objs, "Ankle_R", "AnkleR", "Foot_R", "FootR", "Leg_03_R") }
        };

        // === DIAGNOSTIC TOOL PART 2 ===
        foreach (var kvp in BoneDictionary)
        {
            if (kvp.Value == null && kvp.Key != BoneNames.None)
            {
                Debug.LogWarning($"[VMD Warning] Bone '{kvp.Key}' was not found in the hierarchy! It will be skipped during recording.");
            }
        }

        foreach (KeyValuePair<BoneNames, Transform> pair in BoneDictionary)
        {
            if(pair.Value != null) transformDictionary.Add(pair.Key.ToString(), pair.Value);
        }

        var characterContainer = GetComponentInParent<UmaContainerCharacter>();
        var animator = characterContainer.UmaAnimator;
        var state = animator.GetCurrentAnimatorStateInfo(0);
        animator.enabled = false;

        // Set to T-Pose
        characterContainer.ResetBodyPose();
        characterContainer.UpBodyReset();

        // Safety check before rotating arms to A-Pose
        if (BoneDictionary[BoneNames.左腕] != null) BoneDictionary[BoneNames.左腕].Rotate(0, 0, -aposeDegress);
        if (BoneDictionary[BoneNames.右腕] != null) BoneDictionary[BoneNames.右腕].Rotate(0, 0, aposeDegress);

        SetInitialPositionAndRotation();

        foreach (BoneNames boneName in BoneDictionary.Keys)
        {
            if (BoneDictionary[boneName] == null) { continue; }
            positionDictionary.Add(boneName, new List<Vector3>());
            rotationDictionary.Add(boneName, new List<Quaternion>());
        }

        if (BoneDictionary[BoneNames.左足ＩＫ] != null)
            LeftFootIKOffset = Quaternion.Inverse(transform.rotation) * (BoneDictionary[BoneNames.左足ＩＫ].position - transform.position);

        if (BoneDictionary[BoneNames.右足ＩＫ] != null)
            RightFootIKOffset = Quaternion.Inverse(transform.rotation) * (BoneDictionary[BoneNames.右足ＩＫ].position - transform.position);

        boneGhost = new BoneGhost(BoneDictionary, UseBottomCenter);
        morphRecorder = new MorphRecorder(transform);

        if (BoneDictionary[BoneNames.左腕] != null) BoneDictionary[BoneNames.左腕].Rotate(0, 0, aposeDegress);
        if (BoneDictionary[BoneNames.右腕] != null) BoneDictionary[BoneNames.右腕].Rotate(0, 0, -aposeDegress);
        
        animator.enabled = true;
        animator.Play(state.shortNameHash, 0, state.normalizedTime);
    }

    private void FixedUpdate()
    {
        if (IsRecording && !IsLive)
        {
            SaveFrame();
            FrameNumber++;
        }
    }

    bool lastvisable;
    void SaveFrame()
    {
        if (boneGhost != null) { boneGhost.GhostAll(); }
        if (morphRecorder != null) { morphRecorder.RecrodAllMorph(); }

        bool visable = container.LiveVisible;
        if (visitableDictionary.Count == 0)
        {
            lastvisable = visable;
            visitableDictionary.Add(0, visable);
        }
        else if(visable != lastvisable)
        {
            lastvisable = visable;
            visitableDictionary.Add(FrameNumber, visable);
        }

        foreach (BoneNames boneName in BoneDictionary.Keys)
        {

            // Debug fuckery - remove me, hello??
            /*
            if (boneName == BoneNames.首)
            {
                var neckBone = BoneDictionary[boneName];
                var parent = neckBone.parent;
                
                Debug.Log($"[VMD Neck Debug] Frame {FrameNumber}");
                Debug.Log($"  localPosition: {neckBone.localPosition}");
                Debug.Log($"  parent.lossyScale: {parent?.lossyScale}");
                Debug.Log($"  parent.name: {parent?.name}");
                Debug.Log($"  IsMini: {UmaViewerBuilder.Instance.CurrentUMAContainer.IsMini}, BodyScale: {UmaViewerBuilder.Instance.CurrentUMAContainer.BodyScale}");
                
                // What the "true" scaled local position would be
                if (parent != null)
                {
                    var scaledLocal = Vector3.Scale(neckBone.localPosition, parent.lossyScale);
                    Debug.Log($"  scaled local (visual offset): {scaledLocal}");
                }
            }
            */

            if (BoneDictionary[boneName] == null)
            {
                continue;
            }

            if (boneName == BoneNames.右足ＩＫ || boneName == BoneNames.左足ＩＫ)
            {
                Vector3 targetVector = Vector3.zero;
                if (UseCenterAsParentOfAll)
                {
                    if ((!UseAbsoluteCoordinateSystem && transform.parent != null) && IgnoreInitialPosition)
                    {
                        targetVector
                            = Quaternion.Inverse(transform.parent.rotation)
                            * (BoneDictionary[boneName].position - transform.parent.position)
                            - parentInitialPosition;
                    }
                    else if ((!UseAbsoluteCoordinateSystem && transform.parent != null) && !IgnoreInitialPosition)
                    {
                        targetVector
                            = Quaternion.Inverse(transform.parent.rotation)
                            * (BoneDictionary[boneName].position - transform.parent.position);
                    }
                    else if ((UseAbsoluteCoordinateSystem || transform.parent == null) && IgnoreInitialPosition)
                    {
                        targetVector = BoneDictionary[boneName].position - parentInitialPosition;
                    }
                    else if ((UseAbsoluteCoordinateSystem || transform.parent == null) && transform.parent && !IgnoreInitialPosition)
                    {
                        targetVector = BoneDictionary[boneName].position;
                    }
                }
                else
                {
                    targetVector = BoneDictionary[boneName].position - transform.position;
                    targetVector = Quaternion.Inverse(transform.rotation) * targetVector;
                }
                targetVector -= (boneName == BoneNames.左足ＩＫ ? LeftFootIKOffset : RightFootIKOffset);
                Vector3 ikPosition = new Vector3(-targetVector.x, targetVector.y, -targetVector.z);
                positionDictionary[boneName].Add(ikPosition * DefaultBoneAmplifier);
                //回転は全部足首に持たせる
                Quaternion ikRotation = Quaternion.identity;
                rotationDictionary[boneName].Add(ikRotation);
                continue;
            }

            if (boneGhost != null && boneGhost.GhostDictionary.Keys.Contains(boneName))
            {
                if (boneGhost.GhostDictionary[boneName].ghost == null || !boneGhost.GhostDictionary[boneName].enabled)
                {
                    rotationDictionary[boneName].Add(Quaternion.identity);
                    positionDictionary[boneName].Add(Vector3.zero);
                    continue;
                }

                Vector3 boneVector = boneGhost.GhostDictionary[boneName].ghost.localPosition;
                Quaternion boneQuatenion = boneGhost.GhostDictionary[boneName].ghost.localRotation;
                rotationDictionary[boneName].Add(new Quaternion(-boneQuatenion.x, boneQuatenion.y, -boneQuatenion.z, boneQuatenion.w));

                boneVector -= boneGhost.GhostOriginalLocalPositionDictionary[boneName];

                positionDictionary[boneName].Add(new Vector3(-boneVector.x, boneVector.y, -boneVector.z) * DefaultBoneAmplifier);
                continue;
            }

            Quaternion fixedQuatenion = Quaternion.identity;
            Quaternion vmdRotation = Quaternion.identity;

            if (boneName == BoneNames.全ての親 && UseAbsoluteCoordinateSystem)
            {
                fixedQuatenion = BoneDictionary[boneName].rotation;
            }
            else
            {
                fixedQuatenion = BoneDictionary[boneName].localRotation;
            }

            if (boneName == BoneNames.全ての親 && IgnoreInitialRotation)
            {
                fixedQuatenion = BoneDictionary[boneName].localRotation.MinusRotation(parentInitialRotation);
            }

            vmdRotation = new Quaternion(-fixedQuatenion.x, fixedQuatenion.y, -fixedQuatenion.z, fixedQuatenion.w);

            rotationDictionary[boneName].Add(vmdRotation);

            Vector3 fixedPosition = Vector3.zero;
            Vector3 vmdPosition = Vector3.zero;

            if (boneName == BoneNames.全ての親 && UseAbsoluteCoordinateSystem)
            {
                fixedPosition = BoneDictionary[boneName].position;
            }
            else
            {
                fixedPosition = BoneDictionary[boneName].localPosition;
            }

            if (boneName == BoneNames.全ての親 && IgnoreInitialPosition)
            {
                fixedPosition -= parentInitialPosition;
            }

            // Log first frame
            if (FrameNumber == 0 && (boneName == BoneNames.首 || boneName == BoneNames.左肩 || boneName == BoneNames.右肩))
            {
                Debug.Log($"[VMD Frame0] {boneName}: localPos={BoneDictionary[boneName].localPosition}, recorded={(new Vector3(-fixedPosition.x, fixedPosition.y, -fixedPosition.z) * DefaultBoneAmplifier)}");
            }

            vmdPosition = new Vector3(-fixedPosition.x, fixedPosition.y, -fixedPosition.z);

            if (boneName == BoneNames.全ての親)
            {
                positionDictionary[boneName].Add(vmdPosition * DefaultBoneAmplifier + ParentOfAllOffset);
            }
            else
            {
                positionDictionary[boneName].Add(vmdPosition * DefaultBoneAmplifier);
            }

            vmdPosition = new Vector3(-fixedPosition.x, fixedPosition.y, -fixedPosition.z);

            if (boneName == BoneNames.全ての親)
            {
                positionDictionary[boneName].Add(vmdPosition * DefaultBoneAmplifier + ParentOfAllOffset);
            }
            else
            {
                positionDictionary[boneName].Add(vmdPosition * DefaultBoneAmplifier);
            }
        }
    }

    void LiveSaveFrame()
    {
        if (IsRecording && IsLive)
        {
            SaveFrame();
            FrameNumber++;
        }
    }

    void SetInitialPositionAndRotation()
    {
        if (UseAbsoluteCoordinateSystem)
        {
            parentInitialPosition = transform.position;
            parentInitialRotation = transform.rotation;
        }
        else
        {
            parentInitialPosition = transform.localPosition;
            parentInitialRotation = transform.localRotation;
        }
    }

    public static void SetFPS(int fps)
    {
        Time.fixedDeltaTime = 1 / (float)fps;
    }

    /// <summary>
    /// レコーディングを開始または再開
    /// </summary>
    public void StartRecording(bool islive = false)
    {
        SetInitialPositionAndRotation();
        IsRecording = true;

        foreach (var kvp in BoneDictionary)
        {
            if (kvp.Value != null)
            {
                _boneInitialLocalPositions[kvp.Key] = kvp.Value.localPosition;
                Debug.Log($"{kvp} is {_boneInitialLocalPositions[kvp.Key]}");
            }
        }

        IsLive = islive;

        if (islive)
        {
            var director = Gallop.Live.Director.instance;
            director._liveTimelineControl.RecordUma += LiveSaveFrame;
        }
    }

    /// <summary>
    /// レコーディングを一時停止
    /// </summary>
    public void PauseRecording() { IsRecording = false; }

    /// <summary>
    /// レコーディングを終了
    /// </summary>
    public void StopRecording()
    {
        IsRecording = false;
        frameNumberSaved = FrameNumber;
        morphRecorderSaved = morphRecorder;
        FrameNumber = 0;
        positionDictionarySaved = positionDictionary;
        positionDictionary = new Dictionary<BoneNames, List<Vector3>>();
        rotationDictionarySaved = rotationDictionary;
        rotationDictionary = new Dictionary<BoneNames, List<Quaternion>>();
        foreach (BoneNames boneName in BoneDictionary.Keys)
        {
            if (BoneDictionary[boneName] == null) { continue; }

            positionDictionary.Add(boneName, new List<Vector3>());
            rotationDictionary.Add(boneName, new List<Quaternion>());
        }
        morphRecorder = new MorphRecorder(transform);
        
        if (IsLive)
        {
            var director = Gallop.Live.Director.instance;
            director._liveTimelineControl.RecordUma -= LiveSaveFrame;
        }
    }

    /// <summary>
    /// VMDを作成する
    /// 呼び出す際は先にStopRecordingを呼び出すこと
    /// </summary>
    /// <param name="modelName">VMDファイルに記載される専用モデル名</param>
    /// <param name="filePath">保存先の絶対ファイルパス</param>
    public void SaveVMD(string modelName, string filePath)
    {
        if (IsRecording)
        {
            Debug.Log(transform.name + "VMD保存前にレコーディングをストップしてください。");
            return;
        }

        if (KeyReductionLevel <= 0) { KeyReductionLevel = 1; }

        Debug.Log(transform.name + "VMDファイル作成開始");
        //ファイルの書き込み
        using (FileStream fileStream = new FileStream(filePath, FileMode.Create))
        using (BinaryWriter binaryWriter = new BinaryWriter(fileStream))
        {
            try
            {
                const string ShiftJIS = "shift_jis";
                const int intByteLength = 4;

                //ファイルタイプの書き込み
                const int fileTypeLength = 30;
                const string RightFileType = "Vocaloid Motion Data 0002";
                byte[] fileTypeBytes = System.Text.Encoding.GetEncoding(ShiftJIS).GetBytes(RightFileType);
                binaryWriter.Write(fileTypeBytes, 0, fileTypeBytes.Length);
                binaryWriter.Write(new byte[fileTypeLength - fileTypeBytes.Length], 0, fileTypeLength - fileTypeBytes.Length);

                //モデル名の書き込み、Shift_JISで保存
                const int modelNameLength = 20;
                byte[] modelNameBytes = System.Text.Encoding.GetEncoding(ShiftJIS).GetBytes(modelName);
                //モデル名が長すぎたとき
                modelNameBytes = modelNameBytes.Take(Mathf.Min(modelNameLength, modelNameBytes.Length)).ToArray();
                binaryWriter.Write(modelNameBytes, 0, modelNameBytes.Length);
                binaryWriter.Write(new byte[modelNameLength - modelNameBytes.Length], 0, modelNameLength - modelNameBytes.Length);

                //全ボーンフレーム数の書き込み
                void LoopWithBoneCondition(Action<BoneNames, int> action)
                {
                    for (int i = 0; i < frameNumberSaved; i++)
                    {
                        foreach (BoneNames boneName in Enum.GetValues(typeof(BoneNames)))
                        {
                            if ((i % KeyReductionLevel) != 0 && boneName != BoneNames.全ての親) { continue; }
                            if (!BoneDictionary.Keys.Contains(boneName)) { continue; }
                            if (BoneDictionary[boneName] == null) { continue; }
                            if (!UseParentOfAll && boneName == BoneNames.全ての親) { continue; }

                            action(boneName, i);
                        }
                    }
                }
                uint allKeyFrameNumber = 0;
                LoopWithBoneCondition((a, b) => { allKeyFrameNumber++; });
                byte[] allKeyFrameNumberByte = BitConverter.GetBytes(allKeyFrameNumber);
                binaryWriter.Write(allKeyFrameNumberByte, 0, intByteLength);

                //人ボーンの書き込み
                LoopWithBoneCondition((boneName, i) =>
                {
                    const int boneNameLength = 15;
                    string boneNameString = GetBoneNameForExport(boneName);
                    byte[] boneNameBytes = System.Text.Encoding.GetEncoding(ShiftJIS).GetBytes(boneNameString);
                    binaryWriter.Write(boneNameBytes, 0, boneNameBytes.Length);
                    binaryWriter.Write(new byte[boneNameLength - boneNameBytes.Length], 0, boneNameLength - boneNameBytes.Length);

                    byte[] frameNumberByte = BitConverter.GetBytes((ulong)i);
                    binaryWriter.Write(frameNumberByte, 0, intByteLength);

                    Vector3 position = positionDictionarySaved[boneName][i];
                    byte[] positionX = BitConverter.GetBytes(position.x);
                    binaryWriter.Write(positionX, 0, intByteLength);
                    byte[] positionY = BitConverter.GetBytes(position.y);
                    binaryWriter.Write(positionY, 0, intByteLength);
                    byte[] positionZ = BitConverter.GetBytes(position.z);
                    binaryWriter.Write(positionZ, 0, intByteLength);
                    Quaternion rotation = rotationDictionarySaved[boneName][i];
                    byte[] rotationX = BitConverter.GetBytes(rotation.x);
                    binaryWriter.Write(rotationX, 0, intByteLength);
                    byte[] rotationY = BitConverter.GetBytes(rotation.y);
                    binaryWriter.Write(rotationY, 0, intByteLength);
                    byte[] rotationZ = BitConverter.GetBytes(rotation.z);
                    binaryWriter.Write(rotationZ, 0, intByteLength);
                    byte[] rotationW = BitConverter.GetBytes(rotation.w);
                    binaryWriter.Write(rotationW, 0, intByteLength);

                    byte[] interpolateBytes = new byte[64];
                    binaryWriter.Write(interpolateBytes, 0, 64);
                });

                //全モーフフレーム数の書き込み
                morphRecorderSaved.DisableIntron();
                if (TrimMorphNumber) { morphRecorderSaved.TrimMorphNumber(); }
                void LoopWithMorphCondition(Action<string, int> action)
                {
                    for (int i = 0; i < frameNumberSaved; i++)
                    {
                        foreach (string morphName in morphRecorderSaved.MorphDrivers.Keys)
                        {
                            if (morphRecorderSaved.MorphDrivers[morphName].ValueList.Count == 0) { continue; }
                            if (i > morphRecorderSaved.MorphDrivers[morphName].ValueList.Count) { continue; }
                            //変化のない部分は省く
                            if (!morphRecorderSaved.MorphDrivers[morphName].ValueList[i].enabled) { continue; }
                            const int boneNameLength = 15;
                            string morphNameString = morphName.ToString();
                            byte[] morphNameBytes = System.Text.Encoding.GetEncoding(ShiftJIS).GetBytes(morphNameString);
                            //名前が長過ぎた場合書き込まない
                            if (boneNameLength - morphNameBytes.Length < 0) { continue; }

                            action(morphName, i);
                        }
                    }
                }
                uint allMorphNumber = 0;
                LoopWithMorphCondition((a, b) => { allMorphNumber++; });
                byte[] faceFrameCount = BitConverter.GetBytes(allMorphNumber);
                binaryWriter.Write(faceFrameCount, 0, intByteLength);

                //モーフの書き込み
                LoopWithMorphCondition((morphName, i) =>
                {
                    const int boneNameLength = 15;
                    string morphNameString = morphName.ToString();
                    byte[] morphNameBytes = System.Text.Encoding.GetEncoding(ShiftJIS).GetBytes(morphNameString);

                    binaryWriter.Write(morphNameBytes, 0, morphNameBytes.Length);
                    binaryWriter.Write(new byte[boneNameLength - morphNameBytes.Length], 0, boneNameLength - morphNameBytes.Length);

                    byte[] frameNumberByte = BitConverter.GetBytes((ulong)i);
                    binaryWriter.Write(frameNumberByte, 0, intByteLength);

                    byte[] valueByte = BitConverter.GetBytes(morphRecorderSaved.MorphDrivers[morphName].ValueList[i].value);
                    binaryWriter.Write(valueByte, 0, intByteLength);
                });

                //カメラの書き込み
                byte[] cameraFrameCount = BitConverter.GetBytes(0);
                binaryWriter.Write(cameraFrameCount, 0, intByteLength);

                //照明の書き込み
                byte[] lightFrameCount = BitConverter.GetBytes(0);
                binaryWriter.Write(lightFrameCount, 0, intByteLength);

                //セルフシャドウの書き込み
                byte[] selfShadowCount = BitConverter.GetBytes(0);
                binaryWriter.Write(selfShadowCount, 0, intByteLength);

                //IKの書き込み
                //0フレームにキーフレーム一つだけ置く
                byte[] ikCount = BitConverter.GetBytes(visitableDictionary.Count);
                binaryWriter.Write(ikCount, 0, intByteLength);

                foreach(var visable in visitableDictionary)
                {
                    byte[] ikFrameNumber = BitConverter.GetBytes(visable.Key);
                    byte modelDisplay = Convert.ToByte(visable.Value ? 1 : 0);
                    binaryWriter.Write(ikFrameNumber, 0, intByteLength);
                    binaryWriter.Write(modelDisplay);

                    //右足IKと左足IKと右足つま先IKと左足つま先IKの4つ
                    byte[] ikNumber = BitConverter.GetBytes(4);
                    const int IKNameLength = 20;
                    byte[] leftIKName = System.Text.Encoding.GetEncoding(ShiftJIS).GetBytes("左足ＩＫ");
                    byte[] rightIKName = System.Text.Encoding.GetEncoding(ShiftJIS).GetBytes("右足ＩＫ");
                    byte[] leftToeIKName = System.Text.Encoding.GetEncoding(ShiftJIS).GetBytes("左つま先ＩＫ");
                    byte[] rightToeIKName = System.Text.Encoding.GetEncoding(ShiftJIS).GetBytes("右つま先ＩＫ");
                    byte ikOn = Convert.ToByte(1);
                    byte ikOff = Convert.ToByte(0);
                        
                    binaryWriter.Write(ikNumber, 0, intByteLength);
                    binaryWriter.Write(leftIKName, 0, leftIKName.Length);
                    binaryWriter.Write(new byte[IKNameLength - leftIKName.Length], 0, IKNameLength - leftIKName.Length);
                    binaryWriter.Write(ikOff);
                    binaryWriter.Write(leftToeIKName, 0, leftToeIKName.Length);
                    binaryWriter.Write(new byte[IKNameLength - leftToeIKName.Length], 0, IKNameLength - leftToeIKName.Length);
                    binaryWriter.Write(ikOff);
                    binaryWriter.Write(rightIKName, 0, rightIKName.Length);
                    binaryWriter.Write(new byte[IKNameLength - rightIKName.Length], 0, IKNameLength - rightIKName.Length);
                    binaryWriter.Write(ikOff);
                    binaryWriter.Write(rightToeIKName, 0, rightToeIKName.Length);
                    binaryWriter.Write(new byte[IKNameLength - rightToeIKName.Length], 0, IKNameLength - rightToeIKName.Length);
                    binaryWriter.Write(ikOff);
                }
            }
            catch (Exception ex)
            {
                Debug.Log("VMD書き込みエラー" + ex.Message);
            }
            finally
            {
                binaryWriter.Close();
            }
        }
        if (boneGhost != null)
        {   
            // Mini-umas motion export cause an exception here
            try
            {
               foreach(var pair in boneGhost.GhostDictionary)
                {
                    Destroy(pair.Value.ghost.gameObject);
                } 
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to destroy some boneGhost pairs");
            }
            
        }
        Destroy(this);
    }

    /// <summary>
    /// VMDを作成する
    /// 呼び出す際は先にStopRecordingを呼び出すこと
    /// </summary>
    /// <param name="modelName">VMDファイルに記載される専用モデル名</param>
    /// <param name="filePath">保存先の絶対ファイルパス</param>
    /// <param name="keyReductionLevel">キーの書き込み頻度を減らして容量を減らす</param>
    public void SaveVMD(string modelName, int keyReductionLevel = 3)
    {
        string fileName = $"{Application.dataPath}{FileSavePath}/{modelName} {DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss")}.vmd";
        //string.Format("UMA_{0}.vmd", )
        Directory.CreateDirectory(Application.dataPath + FileSavePath);
        KeyReductionLevel = keyReductionLevel;
        SaveVMD(modelName, fileName);
    }

    public void SaveLiveVMD(LiveEntry liveEntry, DateTime time ,string modelName, int keyReductionLevel = 3)
    {
        string fileName = $"{Application.dataPath}{FileSavePath}/Live{liveEntry.MusicId}_{time.ToString("yyyy-MM-dd_HH-mm-ss")}/{modelName}.vmd";
        Directory.CreateDirectory(Path.GetDirectoryName(fileName));
        KeyReductionLevel = keyReductionLevel;
        SaveVMD(modelName, fileName);
    }

    //裏で正規化されたモデル
    //(初期ポーズで各ボーンのlocalRotationがQuaternion.identityのモデル)を疑似的にアニメーションさせる
    class BoneGhost
    {
        public Dictionary<BoneNames, (Transform ghost, bool enabled)> GhostDictionary { get; private set; } = new Dictionary<BoneNames, (Transform ghost, bool enabled)>();
        public Dictionary<BoneNames, Vector3> GhostOriginalLocalPositionDictionary { get; private set; } = new Dictionary<BoneNames, Vector3>();
        public Dictionary<BoneNames, Quaternion> GhostOriginalRotationDictionary { get; private set; } = new Dictionary<BoneNames, Quaternion>();
        public Dictionary<BoneNames, Quaternion> OriginalRotationDictionary { get; private set; } = new Dictionary<BoneNames, Quaternion>();

        public bool UseBottomCenter { get; private set; } = false;

        const string GhostSalt = "Ghost";
        private Dictionary<BoneNames, Transform> boneDictionary = new Dictionary<BoneNames, Transform>();
        float centerOffsetLength = 0;

        public BoneGhost(Dictionary<BoneNames, Transform> boneDictionary, bool useBottomCenter)
        {
            this.boneDictionary = boneDictionary;
            UseBottomCenter = useBottomCenter;

            Dictionary<BoneNames, (BoneNames optionParent1, BoneNames optionParent2, BoneNames necessaryParent)> boneParentDictionary
                = new Dictionary<BoneNames, (BoneNames optionParent1, BoneNames optionParent2, BoneNames necessaryParent)>()
            {
                { BoneNames.センター, (BoneNames.None, BoneNames.None, BoneNames.全ての親) },
                { BoneNames.左足,     (BoneNames.None, BoneNames.None, BoneNames.センター) },
                { BoneNames.左ひざ,   (BoneNames.None, BoneNames.None, BoneNames.左足) },
                { BoneNames.左足首,   (BoneNames.None, BoneNames.None, BoneNames.左ひざ) },
                { BoneNames.左足先EX,   (BoneNames.None, BoneNames.None, BoneNames.左足首) },
                { BoneNames.右足,     (BoneNames.None, BoneNames.None, BoneNames.センター) },
                { BoneNames.右ひざ,   (BoneNames.None, BoneNames.None, BoneNames.右足) },
                { BoneNames.右足首,   (BoneNames.None, BoneNames.None, BoneNames.右ひざ) },
                { BoneNames.右足先EX,   (BoneNames.None, BoneNames.None, BoneNames.右足首) },
                { BoneNames.上半身,   (BoneNames.None, BoneNames.None, BoneNames.センター) },
                { BoneNames.上半身2,  (BoneNames.None, BoneNames.None, BoneNames.上半身) },
                { BoneNames.首,       (BoneNames.上半身2, BoneNames.None, BoneNames.上半身) },
                { BoneNames.頭,       (BoneNames.首, BoneNames.上半身2, BoneNames.上半身) },
                { BoneNames.左肩,     (BoneNames.上半身2, BoneNames.None, BoneNames.上半身) },
                { BoneNames.左腕,     (BoneNames.左肩, BoneNames.上半身2, BoneNames.上半身) },
                { BoneNames.左ひじ,   (BoneNames.None, BoneNames.None, BoneNames.左腕) },
                { BoneNames.左手首,   (BoneNames.None, BoneNames.None, BoneNames.左ひじ) },
                { BoneNames.左親指１, (BoneNames.左手首, BoneNames.None, BoneNames.None) },
                { BoneNames.左親指２, (BoneNames.左親指１, BoneNames.None, BoneNames.None) },
                { BoneNames.左人指１, (BoneNames.左手首, BoneNames.None, BoneNames.None) },
                { BoneNames.左人指２, (BoneNames.左人指１, BoneNames.None, BoneNames.None) },
                { BoneNames.左人指３, (BoneNames.左人指２, BoneNames.None, BoneNames.None) },
                { BoneNames.左中指１, (BoneNames.左手首, BoneNames.None, BoneNames.None) },
                { BoneNames.左中指２, (BoneNames.左中指１, BoneNames.None, BoneNames.None) },
                { BoneNames.左中指３, (BoneNames.左中指２, BoneNames.None, BoneNames.None) },
                { BoneNames.左薬指１, (BoneNames.左手首, BoneNames.None, BoneNames.None) },
                { BoneNames.左薬指２, (BoneNames.左薬指１, BoneNames.None, BoneNames.None) },
                { BoneNames.左薬指３, (BoneNames.左薬指２, BoneNames.None, BoneNames.None) },
                { BoneNames.左小指１, (BoneNames.左手首, BoneNames.None, BoneNames.None) },
                { BoneNames.左小指２, (BoneNames.左小指１, BoneNames.None, BoneNames.None) },
                { BoneNames.左小指３, (BoneNames.左小指２, BoneNames.None, BoneNames.None) },
                { BoneNames.右肩,     (BoneNames.上半身2, BoneNames.None, BoneNames.上半身) },
                { BoneNames.右腕,     (BoneNames.右肩, BoneNames.上半身2, BoneNames.上半身) },
                { BoneNames.右ひじ,   (BoneNames.None, BoneNames.None, BoneNames.右腕) },
                { BoneNames.右手首,   (BoneNames.None, BoneNames.None, BoneNames.右ひじ) },
                { BoneNames.右親指１, (BoneNames.右手首, BoneNames.None, BoneNames.None) },
                { BoneNames.右親指２, (BoneNames.右親指１, BoneNames.None, BoneNames.None) },
                { BoneNames.右人指１, (BoneNames.右手首, BoneNames.None, BoneNames.None) },
                { BoneNames.右人指２, (BoneNames.右人指１, BoneNames.None, BoneNames.None) },
                { BoneNames.右人指３, (BoneNames.右人指２, BoneNames.None, BoneNames.None) },
                { BoneNames.右中指１, (BoneNames.右手首, BoneNames.None, BoneNames.None) },
                { BoneNames.右中指２, (BoneNames.右中指１, BoneNames.None, BoneNames.None) },
                { BoneNames.右中指３, (BoneNames.右中指２, BoneNames.None, BoneNames.None) },
                { BoneNames.右薬指１, (BoneNames.右手首, BoneNames.None, BoneNames.None) },
                { BoneNames.右薬指２, (BoneNames.右薬指１, BoneNames.None, BoneNames.None) },
                { BoneNames.右薬指３, (BoneNames.右薬指２, BoneNames.None, BoneNames.None) },
                { BoneNames.右小指１, (BoneNames.右手首, BoneNames.None, BoneNames.None) },
                { BoneNames.右小指２, (BoneNames.右小指１, BoneNames.None, BoneNames.None) },
                { BoneNames.右小指３, (BoneNames.右小指２, BoneNames.None, BoneNames.None) },
            };

            //Ghostの生成
            foreach (BoneNames boneName in boneDictionary.Keys)
            {
                if (boneName == BoneNames.全ての親 || boneName == BoneNames.左足ＩＫ || boneName == BoneNames.右足ＩＫ)
                {
                    continue;
                }

                if (boneDictionary[boneName] == null)
                {
                    GhostDictionary.Add(boneName, (null, false));
                    continue;
                }

                Transform ghost = new GameObject(boneDictionary[boneName].name + GhostSalt).transform;
                if (boneName == BoneNames.センター && UseBottomCenter)
                {
                    ghost.position = boneDictionary[BoneNames.全ての親].position;
                }
                else
                {
                    ghost.position = boneDictionary[boneName].position;
                }
                GhostDictionary.Add(boneName, (ghost, true));
            }

            //Ghostの親子構造を設定
            foreach (BoneNames boneName in boneDictionary.Keys)
            {
                if (boneName == BoneNames.全ての親 || boneName == BoneNames.左足ＩＫ || boneName == BoneNames.右足ＩＫ)
                {
                    continue;
                }

                if (GhostDictionary[boneName].ghost == null || !GhostDictionary[boneName].enabled)
                {
                    continue;
                }

                if (boneName == BoneNames.センター)
                {
                    GhostDictionary[boneName].ghost.SetParent(boneDictionary[BoneNames.全ての親]);
                    continue;
                }

                if (boneParentDictionary[boneName].optionParent1 != BoneNames.None && boneDictionary[boneParentDictionary[boneName].optionParent1] != null)
                {
                    GhostDictionary[boneName].ghost.SetParent(GhostDictionary[boneParentDictionary[boneName].optionParent1].ghost);
                }
                else if (boneParentDictionary[boneName].optionParent2 != BoneNames.None && boneDictionary[boneParentDictionary[boneName].optionParent2] != null)
                {
                    GhostDictionary[boneName].ghost.SetParent(GhostDictionary[boneParentDictionary[boneName].optionParent2].ghost);
                }
                else if (boneParentDictionary[boneName].necessaryParent != BoneNames.None && boneDictionary[boneParentDictionary[boneName].necessaryParent] != null)
                {
                    GhostDictionary[boneName].ghost.SetParent(GhostDictionary[boneParentDictionary[boneName].necessaryParent].ghost);
                }
                else
                {
                    GhostDictionary[boneName] = (GhostDictionary[boneName].ghost, false);
                }
            }

            //初期状態を保存
            foreach (BoneNames boneName in GhostDictionary.Keys)
            {
                if (GhostDictionary[boneName].ghost == null || !GhostDictionary[boneName].enabled)
                {
                    GhostOriginalLocalPositionDictionary.Add(boneName, Vector3.zero);
                    GhostOriginalRotationDictionary.Add(boneName, Quaternion.identity);
                    OriginalRotationDictionary.Add(boneName, Quaternion.identity);
                }
                else
                {
                    GhostOriginalRotationDictionary.Add(boneName, GhostDictionary[boneName].ghost.rotation);
                    OriginalRotationDictionary.Add(boneName, boneDictionary[boneName].rotation);
                    if (boneName == BoneNames.センター && UseBottomCenter)
                    {
                        GhostOriginalLocalPositionDictionary.Add(boneName, Vector3.zero);
                        continue;
                    }
                    GhostOriginalLocalPositionDictionary.Add(boneName, GhostDictionary[boneName].ghost.localPosition);
                }
            }

            centerOffsetLength = Vector3.Distance(boneDictionary[BoneNames.全ての親].position, boneDictionary[BoneNames.センター].position);
        }

        public void GhostAll()
        {
            foreach (BoneNames boneName in GhostDictionary.Keys)
            {
                if (GhostDictionary[boneName].ghost == null || !GhostDictionary[boneName].enabled) { continue; }
                Quaternion transQuaternion = boneDictionary[boneName].rotation * Quaternion.Inverse(OriginalRotationDictionary[boneName]);
                GhostDictionary[boneName].ghost.rotation = transQuaternion * GhostOriginalRotationDictionary[boneName];
                if (boneName == BoneNames.センター && UseBottomCenter)
                {
                    GhostDictionary[boneName].ghost.position = boneDictionary[boneName].position - centerOffsetLength * GhostDictionary[boneName].ghost.up;
                    continue;
                }
                GhostDictionary[boneName].ghost.position = boneDictionary[boneName].position;
            }
        }
    }

    [Serializable]
    public class MorphRecorder
    {
        public List<FacialMorph> FacialMorphList;
        //キーはunity上のモーフ名
        public Dictionary<string, MorphDriver> MorphDrivers { get; private set; } = new Dictionary<string, MorphDriver>();

        public MorphRecorder(Transform model)
        {
            var facialTarget = model.GetComponentInParent<UmaContainerCharacter>().FaceDrivenKeyTarget;
            Debug.Log($"[Morph Debug] FaceDrivenKeyTarget found: {facialTarget != null}");
            FacialMorphList = new List<FacialMorph>();
            
            if (facialTarget != null)
            {
                Debug.Log($"[Morph Debug] EyeBrowMorphs: {facialTarget.EyeBrowMorphs?.Count ?? 0}");
                Debug.Log($"[Morph Debug] EyeMorphs: {facialTarget.EyeMorphs?.Count ?? 0}");
                Debug.Log($"[Morph Debug] MouthMorphs: {facialTarget.MouthMorphs?.Count ?? 0}");
                FacialMorphList.AddRange(facialTarget.EyeBrowMorphs);
                FacialMorphList.AddRange(facialTarget.EyeMorphs);
                FacialMorphList.AddRange(facialTarget.MouthMorphs);
                Debug.Log($"[Morph Debug] Total FacialMorphList count: {FacialMorphList.Count}");
                for (int i = 0; i < FacialMorphList.Count; i++)
                {
                    string morphName = ConvertMorphName(FacialMorphList[i].name);
                    Debug.Log($"[Morph Debug] Processing morph: {FacialMorphList[i].name} -> {morphName}");

                    if (MorphDrivers.Keys.Contains(morphName))
                    {
                        if (!MorphDrivers[morphName].Morphs.Contains(FacialMorphList[i]))
                        {
                            MorphDrivers[morphName].Morphs.Add(FacialMorphList[i]);
                        }
                    }
                    else
                    {
                        List<FacialMorph> morphList = new List<FacialMorph>();
                        morphList.Add(FacialMorphList[i]);
                        var driver = new MorphDriver(morphList, i);
                        MorphDrivers.Add(morphName, driver);
                    }
                }
                Debug.Log($"[Morph Debug] Final MorphDrivers count: {MorphDrivers.Count}");
                foreach (var kvp in MorphDrivers)
                {
                    Debug.Log($"[Morph Debug]   - {kvp.Key}: {kvp.Value.Morphs.Count} morphs");
                }
            }
            else
            {
                Debug.LogWarning($"Mini UMA detected: skipping facial morph recording (no FaceDrivenKeyTarget)");
                Debug.LogWarning($"[Morph Debug] No FaceDrivenKeyTarget found! Skipping facial morph recording.");
            }
            
        }


        public string ConvertMorphName(string name)
        {
            // Clean the name by removing suffixes like "(WaraiA)[M_Face]"
            string cleanName = name;
            
            int parenIndex = cleanName.IndexOf('(');
            if (parenIndex > 0) 
            {
                cleanName = cleanName.Substring(0, parenIndex);
            }
            
            int bracketIndex = cleanName.IndexOf('[');
            if (bracketIndex > 0) 
            {
                cleanName = cleanName.Substring(0, bracketIndex);
            }

            // If English morph names are enabled, return the cleaned English name
            if (Config.Instance.VmdUseEnglishMorphNames)
            {
                return cleanName;
            }

            // Default behavior: Convert to Japanese MMD standard names
            if (Config.Instance.VmdMorphConvertSetting.Count > 0)
            {
                var setting = Config.Instance.VmdMorphConvertSetting;
                foreach (var val in setting)
                {
                    foreach (var v in val.UMAMorph)
                    {
                        if(v.Equals(name))
                        {
                            return val.MMDMorph;
                        }
                    }
                }
            }
            
            // Fallback to the cleaned name if no conversion is found
            return cleanName;
        }

        public void RecrodAllMorph()
        {
            foreach (MorphDriver morphDriver in MorphDrivers.Values)
            {
                morphDriver.RecordMorph();
            }
        }

        public void TrimMorphNumber()
        {
            string dot = ".";
            Dictionary<string, MorphDriver> morphDriversTemp = new Dictionary<string, MorphDriver>();
            foreach (string morphName in MorphDrivers.Keys)
            {
                //正規表現使うより、dot探して整数か見る
                if (morphName.Contains(dot) && int.TryParse(morphName.Substring(0, morphName.IndexOf(dot)), out int dummy))
                {
                    morphDriversTemp.Add(morphName.Substring(morphName.IndexOf(dot) + 1), MorphDrivers[morphName]);
                    continue;
                }
                morphDriversTemp.Add(morphName, MorphDrivers[morphName]);
            }
            MorphDrivers = morphDriversTemp;
        }

        public void DisableIntron()
        {
            int totalFrames = 0;
            int removedFrames = 0;
            foreach (string morphName in MorphDrivers.Keys)
            {
                for (int i = 0; i < MorphDrivers[morphName].ValueList.Count; i++)
                {
                    totalFrames++;
                    //情報がなければ次へ
                    if (MorphDrivers[morphName].ValueList.Count == 0) { continue; }
                    //今、前、後が同じなら不必要なので無効化
                    if (i > 0
                        && i < MorphDrivers[morphName].ValueList.Count - 1
                        && floatCompare(MorphDrivers[morphName].ValueList[i].value, MorphDrivers[morphName].ValueList[i - 1].value)
                        && floatCompare(MorphDrivers[morphName].ValueList[i].value, MorphDrivers[morphName].ValueList[i + 1].value))
                    {
                        MorphDrivers[morphName].ValueList[i] = (MorphDrivers[morphName].ValueList[i].value, false);
                        removedFrames++;
                    }
                }
            }
            Debug.Log($"[Morph Intron] Total frames: {totalFrames}, Removed: {removedFrames}, Kept: {totalFrames - removedFrames}");
        }

        bool floatCompare(float f1, float f2)
        {
            int a = (int)(f1 * 100);
            int b = (int)(f2 * 100);
            return a == b;
        }

        [Serializable]
        public class MorphDriver
        {
            public List<FacialMorph> Morphs;

            public int MorphIndex { get; private set; }

            public List<(float value, bool enabled)> ValueList = new List<(float value, bool enabled)>();

            public MorphDriver(List<FacialMorph> facialMorph, int morphIndex)
            {
                Morphs = facialMorph;
                MorphIndex = morphIndex;
            }

            public void RecordMorph()
            {
                float val = 0;
                foreach (var morph in Morphs)
                {
                    val += morph.weight;
                }

                if (ValueList.Count < 5)
                {
                    Debug.Log($"[Morph Record] Frame {ValueList.Count}: value={val}, morphs={Morphs.Count}");
                }
                ValueList.Add((Mathf.Clamp(val, -1, 1), true));
            }
        }
    }
}