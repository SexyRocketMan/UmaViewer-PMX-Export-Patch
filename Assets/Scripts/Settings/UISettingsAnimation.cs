using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UISettingsAnimation : MonoBehaviour
{
    static UmaViewerBuilder Builder => UmaViewerBuilder.Instance;

    public TextMeshProUGUI TitleText;
    public TextMeshProUGUI ProgressText;
    public Slider ProgressSlider;
    public Button PlayButton;
    public TextMeshProUGUI SpeedText;
    public Slider SpeedSlider;
    public Button VMDButton;

    /// <summary>
    /// Recording quality row: how many sampled frames are skipped when the vmd is written (1 = every
    /// frame). Both controls are optional.
    /// </summary>
    [SerializeField] private Slider _keyReduction;
    [SerializeField] private TextMeshProUGUI _keyReductionText;

    /// <summary>
    /// Shows what <see cref="Config.json"/> currently says, so the row cannot disagree with what a
    /// recording will do. Called from <see cref="UmaViewerUI.Start"/>.
    /// </summary>
    public void ApplySettings()
    {
        int level = Mathf.Max(1, Config.Instance.VmdKeyReductionLevel);
        if (_keyReduction != null) _keyReduction.SetValueWithoutNotify(level);
        if (_keyReductionText != null) _keyReductionText.text = KeyReductionLabel(level);
    }

    /// <summary>
    /// Slider callback for Config.VmdKeyReductionLevel. 1 records every sampled frame, 2 every other
    /// frame, and so on - fewer keys, smaller vmd.
    /// </summary>
    public void ChangeVmdKeyReduction(float level)
    {
        int value = Mathf.Clamp(Mathf.RoundToInt(level), 1, 10);
        if (_keyReductionText != null) _keyReductionText.text = KeyReductionLabel(value);
        if (Config.Instance.VmdKeyReductionLevel == value) return;
        Config.Instance.VmdKeyReductionLevel = value;
        Debug.Log($"[VMD] recordings will key every {value} frame(s)");
        Config.Instance.UpdateConfig(false);
    }

    static string KeyReductionLabel(int level)
        => level <= 1 ? "Key reduction: every frame" : $"Key reduction: every {level} frames";

    internal void UpdateAnimationInfo(UmaContainerCharacter umaContainer)
    {
        if (umaContainer.OverrideController["clip_2"].name != "clip_2")
        {
            bool isLoop = umaContainer.OverrideController["clip_2"].name.Contains("_loop");
            var AnimeState = umaContainer.UmaAnimator.GetCurrentAnimatorStateInfo(0);
            var AnimeClip = umaContainer.OverrideController["clip_2"];
            if (AnimeClip && umaContainer.UmaAnimator.speed != 0)
            {
                var normalizedTime = (isLoop) ? Mathf.Repeat(AnimeState.normalizedTime, 1) : Mathf.Min(AnimeState.normalizedTime, 1);
                TitleText.text = AnimeClip.name;
                ProgressText.text = string.Format("{0} / {1}", ToFrameFormat(normalizedTime * AnimeClip.length, AnimeClip.frameRate), ToFrameFormat(AnimeClip.length, AnimeClip.frameRate));
                ProgressSlider.SetValueWithoutNotify(normalizedTime);
            }
        }
    }

    public void Pause()
    {
        var container = Builder.CurrentUMAContainer;
        if (!container || !container.UmaAnimator) return;
        if (Builder.OverrideController.animationClips.Length == 0) return;

        var animator = container.UmaAnimator;
        var animator_face = container.UmaFaceAnimator;
        var animator_cam = Builder.AnimationCameraAnimator;
        var AnimeState = animator.GetCurrentAnimatorStateInfo(0);
        var state = animator.speed > 0f;
        if (state)
        {
            animator.speed = 0;
            if (animator_face)
                animator_face.speed = 0;
            animator_cam.speed = 0;
        }
        else if (AnimeState.normalizedTime < 1f)
        {
            animator.speed = SpeedSlider.value;
            animator_cam.speed = SpeedSlider.value;
            if (animator_face)
                animator_face.speed = SpeedSlider.value;
        }
        else
        {
            animator.speed = SpeedSlider.value;
            animator.Play(0, 0, 0);
            animator.Play(0, 2, 0);
            animator_cam.speed = SpeedSlider.value;
            animator_cam.Play(0, -1, 0);
            if (animator_face)
            {
                animator_face.speed = SpeedSlider.value;
                animator_face.Play(0, 0, 0);
                animator_face.Play(0, 1, 0);
            }
        }
    }

    public void ChangeProgress(float val)
    {
        var container = Builder.CurrentUMAContainer;
        if (!container) return;
        var animator = container.UmaAnimator;
        var animator_face = container.UmaFaceAnimator;
        var animator_cam = Builder.AnimationCameraAnimator;
        if (animator != null)
        {
            var AnimeClip = container.OverrideController["clip_2"];

            // Pause and Seek;
            animator.speed = 0;
            animator.Play(0, 0, val);
            animator.Play(0, 2, val);
            if (animator_cam.runtimeAnimatorController)
            {
                animator_cam.speed = 0;
                animator_cam.Play(0, -1, val);
            }
            if (animator_face)
            {
                animator_face.speed = 0;
                animator_face.Play(0, 0, val);
                animator_face.Play(0, 1, val);
            }

            ProgressText.text = string.Format("{0} / {1}", ToFrameFormat(val * AnimeClip.length, AnimeClip.frameRate), ToFrameFormat(AnimeClip.length, AnimeClip.frameRate));
        }
    }

    public void ChangeSpeed(float val)
    {
        var container = Builder.CurrentUMAContainer;
        SpeedText.text = string.Format("Speed: {0:F2}", val);

        if (!container || !container.UmaAnimator) return;

        container.UmaAnimator.speed = val;
        Builder.AnimationCameraAnimator.speed = val;
        if (container.UmaFaceAnimator)
        {
            container.UmaFaceAnimator.speed = val;
        }
    }

    public void EnableRootMotion(bool enable)
    {
        var container = Builder.CurrentUMAContainer;
        if (!container || !container.UmaAnimator) return;
        container.UmaAnimator.SetLayerWeight(2, enable ? 1 : 0);
    }

    public void EnableEnglishVmdBoneNames(bool enable)
    {
        Config.Instance.VmdUseEnglishBoneNames = enable;
        Debug.Log($"{(enable ? "English" : "Japanese")} bone names will be used for exported vmds now");
        Config.Instance.UpdateConfig(false);
    }

    public void EnableEnglishVmdMorphNames(bool enable)
    {
        Config.Instance.VmdUseEnglishMorphNames = enable;
        Debug.Log($"{(enable ? "English" : "Japanese")} morph names will be used for exported vmds now");
        Config.Instance.UpdateConfig(false);
    }

    public static string ToFrameFormat(float time, float frameRate)
    {
        int frames = Mathf.FloorToInt(time % 1 * frameRate);
        int seconds = (int)time;
        int minute = seconds % 3600 / 60;
        seconds = seconds % 3600 % 60;
        return string.Format("{0:D2}m:{1:D2}s:{2:D2}f", minute, seconds, frames);
    }
}
