using System.Collections;
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
        UmaAPose.IntoAPose(BoneDictionary[BoneNames.左腕], BoneDictionary[BoneNames.右腕]);

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

        UmaAPose.BackToTPose(BoneDictionary[BoneNames.左腕], BoneDictionary[BoneNames.右腕]);
        
        animator.enabled = true;
        animator.Play(state.shortNameHash, 0, state.normalizedTime);
    }

    private void FixedUpdate()
    {
        if (IsRecording && !IsLive && !ManualSampling)
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
    /// Skips the FixedUpdate sampler so that a caller can drive sampling itself through
    /// <see cref="SampleFrame"/>. Used by <see cref="RecordClipLoop"/>.
    /// </summary>
    public bool ManualSampling { get; set; }

    /// <summary>Records one frame at the current pose. Only valid while <see cref="ManualSampling"/> is set.</summary>
    public void SampleFrame()
    {
        if (!IsRecording) return;
        SaveFrame();
        FrameNumber++;
    }

    /// <summary>
    /// Records exactly one pass over <paramref name="clip"/>.
    ///
    /// The clip is rewound to its start first, then left playing normally with the frame length pinned by
    /// <see cref="Time.captureDeltaTime"/>, so the animator advances exactly one vmd frame per rendered
    /// frame and one sample is taken per frame. For a looping clip the closing frame is copied from the
    /// first frame, so the motion loops exactly; for a one shot clip the end pose is kept instead.
    ///
    /// Rewinding matters because a one shot clip that has already finished is parked on its last frame and
    /// the animator never advances a finished state - a recording started there used to repeat one pose for
    /// every frame. <see cref="Animator.Play(int, int, float)"/> enters the state again, and a state with
    /// "write default values" resets every bone its clip does not animate, which is why seeking used to
    /// wreck a recording (measured: 21 of 52 bones rotating instead of 49). The pose is therefore
    /// snapshotted and put back right after the seek, and one animator evaluation lays the clip's own
    /// curves on top of it: the clip decides the bones it animates, everything else keeps the pose the
    /// viewer was showing.
    ///
    /// The player loop runs one frame per recorded frame, so cloth/hair physics keep working. Physics
    /// driven bones are the one part that cannot be made identical to the first frame.
    /// </summary>
    public IEnumerator RecordClipLoop(AnimationClip clip, int fps, Animator[] animators, Action onFinished = null)
    {
        if (clip == null) throw new ArgumentNullException(nameof(clip));
        if (fps <= 0) throw new ArgumentOutOfRangeException(nameof(fps));

        int totalFrames = Mathf.Max(1, Mathf.RoundToInt(clip.length * fps));
        var layers = SnapshotLayers(animators);
        // Whether the motion loops is decided by the clip, and the sampled motion gets the last word. Unity
        // already marks the uma motions correctly - the stride cycle and the idles report isLooping, the
        // race result animations do not - and the umbrella "_loop" suffix is what the viewer itself goes by
        // (UmaContainerCharacter picks the looping state for those). AnimatorStateInfo.loop is useless here:
        // measured true for every state in this rig, body, tail, position and face alike.
        bool declaredLoop = clip.isLooping
                            || clip.name.IndexOf("_loop", StringComparison.OrdinalIgnoreCase) >= 0;
        var previousSpeeds = new Dictionary<Animator, float>();
        var previousCulling = new Dictionary<Animator, AnimatorCullingMode>();
        foreach (var animator in (animators ?? new Animator[0]).Where(a => a != null).Distinct())
        {
            previousSpeeds[animator] = animator.speed;
            animator.speed = 1f; // normal playback, one pinned frame length per sample

            // A culled animator is not evaluated at all, so a model that is off screen (or a headless
            // run with nothing rendering it) would record a frozen pose.
            previousCulling[animator] = animator.cullingMode;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }

        float previousCaptureDeltaTime = Time.captureDeltaTime;
        Time.captureDeltaTime = 1f / fps;
        Time.fixedDeltaTime = 1f / fps;

        ManualSampling = true;
        int movingBones = 0;
        bool looping = declaredLoop;

        // Rewind to the start of the clip (see the remarks above): the pose is saved, every recorded layer
        // is put back on time 0, the pose is restored and one animator evaluation applies the clip on top.
        var poseBeforeSeek = SnapshotBonePose();
        foreach (var layer in layers)
        {
            layer.Animator.Play(layer.StateHash, layer.Layer, 0f);
        }
        RestoreBonePose(poseBeforeSeek);

        StartRecording();
        try
        {
            // Initialize() disables the animator, resets the pose and re-enables it; depending on where
            // the frame boundary falls the pose can still be that rest pose here, which is the T-pose
            // frame that used to show up at the start of a recording. Force one evaluation so the pose
            // is animation driven before anything is sampled. This is also what puts the clip's own
            // curves back over the pose the seek reset.
            foreach (var layer in layers)
            {
                layer.Animator.Update(0f);
                var state = layer.Animator.GetCurrentAnimatorStateInfo(layer.Layer);
                Debug.Log($"[VMD] layer {layer.Layer} ({layer.Animator.name}) starts at "
                          + $"{state.normalizedTime:F3} of '{clip.name}', playing "
                          + $"{string.Join(", ", layer.Animator.GetCurrentAnimatorClipInfo(layer.Layer).Select(i => i.clip != null ? i.clip.name : "<null>"))}");
            }

            for (int frame = 0; frame <= totalFrames; frame++)
            {
                // One fixed step advances the animation by exactly 1/fps (captureDeltaTime is pinned),
                // and sampling right after it keeps the same phase as the legacy FixedUpdate sampler -
                // sampling after Update instead reads a pose the character's IK has partly reset.
                if (frame > 0) yield return new WaitForFixedUpdate();
                SampleFrame();
            }

            LoopSeam(out float seamRotation, out float seamPosition);
            bool closes = declaredLoop || (seamRotation <= LoopSeamRotation && seamPosition <= LoopSeamPosition);
            Debug.Log($"[VMD] '{clip.name}': {totalFrames + 1} frames, seam rotation {seamRotation:F5} "
                      + $"position {seamPosition:F5}, closed on itself {closes} "
                      + $"(declared loop {declaredLoop}, tolerances {LoopSeamRotation}/{LoopSeamPosition})");
            if (closes) CloseRecordedLoop();
            looping = closes;
            // StopRecording swaps the dictionaries out, so measure the motion before that happens
            movingBones = CountMovingBones();
        }
        finally
        {
            ManualSampling = false;
            Time.captureDeltaTime = previousCaptureDeltaTime;
            foreach (var pair in previousSpeeds)
            {
                if (pair.Key != null) pair.Key.speed = pair.Value;
            }
            foreach (var pair in previousCulling)
            {
                if (pair.Key != null) pair.Key.cullingMode = pair.Value;
            }
            StopRecording();
        }

        Debug.Log($"[VMD] recorded {(looping ? "one loop" : "one pass")} of '{clip.name}': {frameNumberSaved} frames "
                  + $"({clip.length:F3}s @ {fps}fps), {movingBones} bone(s) move, "
                  + (looping ? "last frame repeats the first pose"
                             : "one shot clip, so the last frame is its end pose"));
        if (movingBones == 0)
        {
            Debug.LogWarning("[VMD] the recorded motion contains no bone movement at all - the animation "
                             + "may not be playing (some clips, e.g. a resting idle, really are static).");
        }
        onFinished?.Invoke();
    }

    /// <summary>
    /// How far the last sampled frame may be from the first one and still count as a cycle worth closing,
    /// used only when the clip does not declare itself a loop: a quaternion component delta and a position
    /// delta over every recorded bone. Measured on character 1001: a race result one shot ends 0.82
    /// (rotation) from its start, a running cycle closes to 0.23 but is declared a loop, and a looping
    /// idle closes to 0.10. The thresholds sit below all of those, so this only ever fires for an unmarked
    /// clip that genuinely ends where it started.
    /// </summary>
    private const float LoopSeamRotation = 0.05f;
    private const float LoopSeamPosition = 0.5f;

    /// <summary>
    /// How far the last sampled frame is from the first one, over every recorded bone: the worst quaternion
    /// component delta and the worst position delta. A cyclic animation comes back to where it started, a
    /// one shot ends somewhere else - which is what decides whether forcing the first pose onto the last
    /// frame is a seamless loop or a destroyed ending.
    /// </summary>
    private void LoopSeam(out float worstRotation, out float worstPosition)
    {
        worstRotation = 0f;
        worstPosition = 0f;
        foreach (var pair in positionDictionary)
        {
            var positions = pair.Value;
            if (positions == null || positions.Count < 2) continue;
            worstPosition = Mathf.Max(worstPosition,
                Vector3.Distance(positions[0], positions[positions.Count - 1]));
        }
        foreach (var pair in rotationDictionary)
        {
            var rotations = pair.Value;
            if (rotations == null || rotations.Count < 2) continue;
            worstRotation = Mathf.Max(worstRotation,
                QuaternionDelta(rotations[0], rotations[rotations.Count - 1]));
        }
    }

    private static float QuaternionDelta(Quaternion a, Quaternion b)
    {
        float direct = Mathf.Max(Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y)),
                                 Mathf.Max(Mathf.Abs(a.z - b.z), Mathf.Abs(a.w - b.w)));
        float flipped = Mathf.Max(Mathf.Max(Mathf.Abs(a.x + b.x), Mathf.Abs(a.y + b.y)),
                                  Mathf.Max(Mathf.Abs(a.z + b.z), Mathf.Abs(a.w + b.w)));
        return Mathf.Min(direct, flipped);
    }

    /// <summary>A recorded bone's local pose, so a seek can put back what it resets.</summary>
    private struct BonePose
    {
        public Transform Bone;
        public Vector3 Position;
        public Quaternion Rotation;
    }

    /// <summary>
    /// The local pose of every bone this recorder writes, taken before a seek. Entering an animator state
    /// with "write default values" resets the bones its clip does not animate, and those are exactly the
    /// bones a recording would lose without this - see <see cref="RecordClipLoop"/>.
    /// </summary>
    private List<BonePose> SnapshotBonePose()
    {
        var poses = new List<BonePose>(BoneDictionary.Count);
        foreach (var pair in BoneDictionary)
        {
            if (pair.Value == null) continue;
            poses.Add(new BonePose
            {
                Bone = pair.Value,
                Position = pair.Value.localPosition,
                Rotation = pair.Value.localRotation
            });
        }
        return poses;
    }

    private static void RestoreBonePose(List<BonePose> poses)
    {
        foreach (var pose in poses)
        {
            if (pose.Bone == null) continue;
            pose.Bone.localPosition = pose.Position;
            pose.Bone.localRotation = pose.Rotation;
        }
    }

    /// <summary>
    /// Copies the first sampled frame over the last one for every bone and morph, so the motion closes
    /// exactly instead of merely ending close to where it started.
    /// </summary>
    private void CloseRecordedLoop()
    {
        foreach (BoneNames boneName in BoneDictionary.Keys)
        {
            if (positionDictionary.TryGetValue(boneName, out var positions) && positions.Count > 1)
            {
                positions[positions.Count - 1] = positions[0];
            }
            if (rotationDictionary.TryGetValue(boneName, out var rotations) && rotations.Count > 1)
            {
                rotations[rotations.Count - 1] = rotations[0];
            }
        }

        if (morphRecorder == null) return;
        foreach (var driver in morphRecorder.MorphDrivers.Values)
        {
            var values = driver.ValueList;
            if (values.Count > 1) values[values.Count - 1] = values[0];
        }
    }

    /// <summary>How many bones actually move over the recorded loop (the closing frame is ignored).</summary>
    private int CountMovingBones()
    {
        int moving = 0;
        foreach (BoneNames boneName in BoneDictionary.Keys)
        {
            if (!positionDictionary.TryGetValue(boneName, out var positions)) continue;
            if (positions.Count < 3) continue;

            Vector3 first = positions[0];
            Quaternion firstRotation = rotationDictionary.TryGetValue(boneName, out var rotations) && rotations.Count > 0
                ? rotations[0]
                : Quaternion.identity;

            for (int i = 1; i < positions.Count - 1; i++)
            {
                if (Vector3.Distance(first, positions[i]) > 1e-5f) { moving++; break; }
                if (rotations != null && i < rotations.Count
                    && Quaternion.Angle(firstRotation, rotations[i]) > 0.01f) { moving++; break; }
            }
        }
        return moving;
    }

    /// <summary>Records one loop of whatever the current character is playing.</summary>
    public IEnumerator RecordCurrentLoop(AnimationClip clip, int fps = 30, Action onFinished = null)
    {
        var character = GetComponentInParent<UmaContainerCharacter>();
        var animators = character == null
            ? new Animator[0]
            : new[] { character.UmaAnimator, character.UmaFaceAnimator }.Where(a => a != null).ToArray();
        return RecordClipLoop(clip, fps, animators, onFinished);
    }

    private class RecordedLayer
    {
        public Animator Animator;
        public int Layer;
        /// <summary>Short name hash of the state, which is what <see cref="Animator.Play(int, int, float)"/> takes.</summary>
        public int StateHash;
    }

    /// <summary>
    /// Every layer that currently plays something, so the recording can rewind all of them to the start of
    /// their clip (the uma character drives body, face and camera on several layers).
    /// </summary>
    private static List<RecordedLayer> SnapshotLayers(Animator[] animators)
    {
        var layers = new List<RecordedLayer>();
        foreach (var animator in animators ?? new Animator[0])
        {
            if (animator == null || animator.runtimeAnimatorController == null) continue;
            for (int layer = 0; layer < animator.layerCount; layer++)
            {
                var state = animator.GetCurrentAnimatorStateInfo(layer);
                if (state.fullPathHash == 0) continue;
                layers.Add(new RecordedLayer
                {
                    Animator = animator,
                    Layer = layer,
                    StateHash = state.shortNameHash
                });
            }
        }
        return layers;
    }

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
    /// <param name="keyReductionLevel">1 = every frame, 2 = every other frame, ... 0 (default) keeps
    /// whatever level is already set on the component (see <see cref="SaveLiveVMD"/>).</param>
    public void SaveVMD(string modelName, string filePath, int keyReductionLevel = 0)
    {
        if (IsRecording)
        {
            Debug.Log(transform.name + "VMD保存前にレコーディングをストップしてください。");
            return;
        }

        // The parameter must not share a name with the field: SaveLiveVMD assigns the field and then
        // calls this method, and a shadowing parameter defaulted it back to 1.
        if (keyReductionLevel > 0) { KeyReductionLevel = keyReductionLevel; }
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
                            // the last frame is always keyed: skipping it leaves the motion without its
                            // closing pose, so a looping motion no longer returns to where it started
                            if ((i % KeyReductionLevel) != 0 && i != frameNumberSaved - 1 && boneName != BoneNames.全ての親) { continue; }
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

                var tooLongMorphs = morphRecorderSaved.MorphDrivers.Keys
                    .Where(n => !MorphNaming.FitsVmd(n)).ToList();
                if (tooLongMorphs.Count > 0)
                {
                    Debug.LogWarning($"[VMD] {tooLongMorphs.Count} morph(s) have names longer than {MorphNaming.VmdNameByteLimit} bytes and are left out of this vmd "
                                     + $"(e.g. {string.Join(", ", tooLongMorphs.Take(3))}). Export the model with a name mode whose names fit the vmd field.");
                }
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
            foreach(var pair in boneGhost.GhostDictionary)
            {
                try 
                {
                    if (pair.Value.ghost != null) Destroy(pair.Value.ghost.gameObject);
                } 
                catch (Exception ex) 
                {
                    Debug.LogWarning($"Failed to destroy ghost for {pair.Key}: {ex.Message}");
                }
            }
            
        }
        Destroy(this);
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
                    string morphName = ConvertMorphName(FacialMorphList[i]);
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
                if (Config.Instance.VmdUseEnglishMorphNames && !MorphNaming.ModelAndMotionNamesAgree(Config.Instance.PmxMorphNameMode))
                {
                    Debug.LogWarning("[VMD] PmxMorphNameMode 0 (BlenderCompatible) names morphs longer than a vmd morph name field, "
                                     + "so exported motions cannot drive the exported model. Use mode 3 (Unified) or 2 (Both) for motions.");
                }
                else if (!Config.Instance.VmdUseEnglishMorphNames)
                {
                    Debug.LogWarning("[VMD] Japanese vmd morph names do not match any morph name an exported model can carry, "
                                     + "so Blender will drop the morph keyframes on import. Enable 'english morph names' in the "
                                     + "animation settings to match the exported model.");
                }
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


        /// <summary>
        /// The name this morph gets in the vmd. It has to be the same name <see cref="ModelExporter"/>
        /// wrote into the model, otherwise Blender's mmd_tools silently drops the morph keyframes when
        /// the motion is imported - both sides therefore go through <see cref="MorphNaming"/>.
        /// </summary>
        public string ConvertMorphName(FacialMorph morph)
        {
            string name = morph != null ? morph.name : "";
            string tag = morph != null ? morph.tag : "";

            if (Config.Instance.VmdUseEnglishMorphNames)
            {
                return MorphNaming.VmdName(name, tag, Config.Instance.PmxMorphNameMode);
            }

            // Default behavior: Convert to Japanese MMD standard names
            if (Config.Instance.VmdMorphConvertSetting != null && Config.Instance.VmdMorphConvertSetting.Count > 0)
            {
                var setting = Config.Instance.VmdMorphConvertSetting;
                foreach (var val in setting)
                {
                    foreach (var v in val.UMAMorph)
                    {
                        if(v.Equals(name)) return val.MMDMorph;
                    }
                }
            }

            return MorphNaming.ShortName(name);
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