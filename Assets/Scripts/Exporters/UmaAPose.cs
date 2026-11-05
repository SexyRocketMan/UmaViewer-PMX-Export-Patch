using System;
using UnityEngine;

/// <summary>
/// The A-pose recorded motions are authored against: <see cref="UnityHumanoidVMDRecorder"/> builds its ghost pose by
/// rotating both upper arms <see cref="Degrees"/> degrees down from the T-pose, so every arm rotation in a vmd is relative
/// to it. <see cref="Enter"/> exports the model in that same pose, unless <see cref="Config.PmxAPoseRestPose"/> is off.
/// </summary>
public static class UmaAPose
{
    const string Tag = "[Export]";

    /// <summary>How far the upper arms rotate down from the T-pose. Shared with the recorder.</summary>
    public const float Degrees = 38.5f;

    // Same candidates, same order as the recorder's arm-bone lookup; the two must stay in step.
    static readonly string[] LeftArmNames = { "Arm_L", "ArmL", "UpperArm_L", "UpperArmL", "Arm_01_L" };
    static readonly string[] RightArmNames = { "Arm_R", "ArmR", "UpperArm_R", "UpperArmR", "Arm_01_R" };

    /// <summary>Rotates both arms into the A-pose - the left negative, the right positive, like the recorder.</summary>
    public static void IntoAPose(Transform leftArm, Transform rightArm)
    {
        if (leftArm != null) leftArm.Rotate(0, 0, -Degrees);
        if (rightArm != null) rightArm.Rotate(0, 0, Degrees);
    }

    /// <summary>Undoes <see cref="IntoAPose"/>.</summary>
    public static void BackToTPose(Transform leftArm, Transform rightArm)
    {
        if (leftArm != null) leftArm.Rotate(0, 0, Degrees);
        if (rightArm != null) rightArm.Rotate(0, 0, -Degrees);
    }

    /// <summary>
    /// Exports inside the A-pose rest pose for the lifetime of the returned scope, then restores the arm rotations and animator
    /// state, leaving the viewer as it was.
    /// </summary>
    public static Scope Enter(UmaContainerCharacter container)
    {
        return new Scope(container);
    }

    /// <summary>See <see cref="Enter"/>.</summary>
    public sealed class Scope : IDisposable
    {
        readonly Animator _animator;
        readonly Transform _leftArm;
        readonly Transform _rightArm;
        readonly Quaternion _leftRotation;
        readonly Quaternion _rightRotation;
        readonly int _stateHash;
        readonly float _normalizedTime;
        bool _disposed;

        internal Scope(UmaContainerCharacter container)
        {
            if (container == null || Config.Instance == null || !Config.Instance.PmxAPoseRestPose) return;

            var bones = container.GetComponentsInChildren<Transform>(true);
            _leftArm = FindArm(bones, LeftArmNames);
            _rightArm = FindArm(bones, RightArmNames);
            if (_leftArm == null || _rightArm == null)
            {
                Debug.LogWarning($"{Tag} A-pose rest pose was requested, but this model has no "
                                 + $"{LeftArmNames[0]}/{RightArmNames[0]} bone to rotate - exporting the plain rest pose");
                _leftArm = _rightArm = null;
                return;
            }

            _leftRotation = _leftArm.localRotation;
            _rightRotation = _rightArm.localRotation;

            // Freeze the animator first, or its next update overwrites the hand-set pose with the playing clip.
            _animator = container.UmaAnimator;
            if (_animator != null)
            {
                var state = _animator.GetCurrentAnimatorStateInfo(0);
                _stateHash = state.shortNameHash;
                _normalizedTime = state.normalizedTime;
                _animator.enabled = false;
            }

            // ExportModel has already called Animator.Rebind(), so the rig sits in the T-pose rest anyway; only the arms are touched
            IntoAPose(_leftArm, _rightArm);

            Debug.Log($"{Tag} exporting in the A-pose rest pose: both upper arms rotated {Degrees} degrees down, "
                      + "the pose recorded motions are relative to");
        }

        static Transform FindArm(Transform[] bones, string[] names)
        {
            foreach (var name in names)
            {
                foreach (var bone in bones)
                {
                    if (bone.name == name) return bone;
                }
            }
            return null;
        }

        public void Dispose()
        {
            if (_leftArm == null || _disposed) return;
            _disposed = true;

            _leftArm.localRotation = _leftRotation;
            _rightArm.localRotation = _rightRotation;
            if (_animator != null)
            {
                _animator.enabled = true;
                if (_stateHash != 0) _animator.Play(_stateHash, 0, _normalizedTime);
            }
        }
    }
}
