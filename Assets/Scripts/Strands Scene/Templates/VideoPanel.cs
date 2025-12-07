using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using TMPro;

public class VideoPanel : MonoBehaviour, IInteractableUI
{
    [Header("Video")]
    public VideoPlayer videoPlayer;
    public RawImage videoImage;   // optional: if you're using a RenderTexture

    [Header("Play / Pause UI")]
    public Button playPauseButton;
    public Image playPauseIcon;   // icon on the button
    public Sprite playSprite;
    public Sprite pauseSprite;

    [Header("Timeline")]
    public Slider timelineSlider; // 0..1 scrubber

    private bool hasClip => videoPlayer != null && videoPlayer.clip != null;

    // Called by InteractionManager after the panel is created
    public void Init(InteractableItem item, InteractionManager manager)
    {
        if (videoPlayer == null)
            videoPlayer = GetComponent<VideoPlayer>();

        if (videoPlayer == null)
        {
            Debug.LogError($"[{name}] No VideoPlayer assigned.");
            return;
        }

        if (item.videoClip == null)
        {
            Debug.LogWarning($"[{name}] Item {item.name} has no videoClip assigned.");
            return;
        }

        // Assign clip and basic settings
        videoPlayer.clip = item.videoClip;
        videoPlayer.isLooping = false; // we want it to be able to "end"
        videoPlayer.time = 0;
        videoPlayer.Play();

        // Button / icon wiring
        if (playPauseButton != null)
        {
            playPauseButton.onClick.RemoveListener(TogglePlay);
            playPauseButton.onClick.AddListener(TogglePlay);
        }

        if (playPauseIcon == null && playPauseButton != null)
        {
            // Try to auto-grab the button's graphic as icon
            playPauseIcon = playPauseButton.targetGraphic as Image;
        }

        // Slider wiring
        if (timelineSlider != null)
        {
            timelineSlider.minValue = 0f;
            timelineSlider.maxValue = 1f;
            timelineSlider.value = 0f;

            timelineSlider.onValueChanged.RemoveListener(OnSliderChanged);
            timelineSlider.onValueChanged.AddListener(OnSliderChanged);
        }

        UpdateIcon();
    }

    private void OnDisable()
    {
        if (playPauseButton != null)
            playPauseButton.onClick.RemoveListener(TogglePlay);

        if (timelineSlider != null)
            timelineSlider.onValueChanged.RemoveListener(OnSliderChanged);
    }

    private void Update()
    {
        if (!hasClip) return;

        double length = videoPlayer.length;
        if (length > 0 && timelineSlider != null)
        {
            // Keep slider following the video
            float norm = (float)(videoPlayer.time / length);
            // Clamp to [0,1]
            if (norm < 0f) norm = 0f;
            if (norm > 1f) norm = 1f;
            timelineSlider.SetValueWithoutNotify(norm);
        }

        // Detect end (not playing, but reached ~the end)
        if (!videoPlayer.isPlaying && length > 0)
        {
            if (videoPlayer.time >= length - 0.05f)
            {
                // Video ended, ensure icon shows "Play"
                UpdateIcon();
            }
        }
    }

    // ------------------
    // Play / Pause
    // ------------------
    void TogglePlay()
    {
        if (!hasClip) return;

        double length = videoPlayer.length;

        // If at (or very near) the end and pressed Play, restart from 0
        if (!videoPlayer.isPlaying && length > 0 &&
            videoPlayer.time >= length - 0.05f)
        {
            videoPlayer.time = 0;
        }

        if (videoPlayer.isPlaying)
            videoPlayer.Pause();
        else
            videoPlayer.Play();

        UpdateIcon();
    }

    void UpdateIcon()
    {
        if (playPauseIcon == null) return;

        if (videoPlayer != null && videoPlayer.isPlaying)
        {
            if (pauseSprite != null)
                playPauseIcon.sprite = pauseSprite;
        }
        else
        {
            if (playSprite != null)
                playPauseIcon.sprite = playSprite;
        }
    }

    // ------------------
    // Slider scrub
    // ------------------
    void OnSliderChanged(float value)
    {
        if (!hasClip) return;
        double length = videoPlayer.length;
        if (length <= 0) return;

        // Set video time based on slider (0..1)
        double newTime = value * length;
        videoPlayer.time = newTime;

        // If it was at the end and user scrubs back, keep icon state consistent
        UpdateIcon();
    }
}
