using System;
using UnityEngine;
using UnityEngine.UI;

// ReplayClip을 RawImage에 정해진 횟수와 배속으로 반복 재생한다.
public class ClipPlayer : MonoBehaviour
{
    [SerializeField] private RawImage screen;
    [Tooltip("비워두면 화면 비율을 맞추지 않음")]
    [SerializeField] private AspectRatioFitter aspectFitter;
    [Tooltip("재생 중에만 켜는 루트. 비워두면 screen 오브젝트를 켜고 끈다")]
    [SerializeField] private GameObject replayRoot;

    public event Action Finished;
    public bool IsPlaying { get; private set; }

    private ReplayClip clip;
    private int repeatCount;
    private float speed;
    private float playTime;
    private int loopsDone;
    private int frameIndex;

    private void Awake()
    {
        SetVisible(false);
    }

    // repeatCount가 0이면 Stop()이나 다음 Play()까지 계속 반복한다.
    public void Play(ReplayClip clip, int repeatCount, float speed)
    {
        if (clip == null || clip.Count == 0 || screen == null) return;

        this.clip = clip;
        this.repeatCount = repeatCount;
        this.speed = speed;
        playTime = 0f;
        loopsDone = 0;
        frameIndex = 0;

        RenderTexture first = clip.GetFrame(0);
        screen.texture = first;
        if (aspectFitter != null)
            aspectFitter.aspectRatio = (float)first.width / first.height;

        IsPlaying = true;
        SetVisible(true);
    }

    public void Stop()
    {
        IsPlaying = false;
        clip = null;
        if (screen != null) screen.texture = null;
        SetVisible(false);
    }

    private void Update()
    {
        if (!IsPlaying) return;

        playTime += Time.unscaledDeltaTime * speed;
        if (playTime >= clip.Length)
        {
            loopsDone++;
            if (repeatCount > 0 && loopsDone >= repeatCount)
            {
                Stop();
                Finished?.Invoke();
                return;
            }
            playTime %= clip.Length;
            frameIndex = 0;
        }

        while (frameIndex + 1 < clip.Count && clip.GetTime(frameIndex + 1) <= playTime)
            frameIndex++;

        screen.texture = clip.GetFrame(frameIndex);
    }

    private void SetVisible(bool visible)
    {
        GameObject target = replayRoot != null ? replayRoot : (screen != null ? screen.gameObject : null);
        if (target != null) target.SetActive(visible);
    }
}
