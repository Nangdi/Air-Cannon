using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RenderHeads.Media.AVProVideo;
using UnityEngine;

// 대기 화면 영상을 AVPro로 무한 반복 재생한다 (StreamingAssets/<idleVideoFolder>/ 안의 영상 하나, 이름 무관).
// AVPro 마크가 보이지 않게:
//  - 반복은 MediaPlayer.Loop(네이티브 루프)로만 한다. 끝 이벤트에서 Seek하지 않는다.
//  - DisplayUGUI는 프레임이 없을 때 아무것도 그리지 않게 하고(NoDefaultDisplay), 에디터 아이콘·기본 텍스처를 끈다.
//  - 첫 프레임 전, 파일 없음/열기 실패, NullMediaPlayer(AVPro 로고 프레임)로 떨어진 경우에는 화면을 숨긴다(배경 검정만 보임).
// 리플레이가 재생되는 동안에는 영상을 멈춰 두고, 끝나면 이어서 재생한다.
public class IdleVideoPlayer : MonoBehaviour
{
    private static readonly HashSet<string> VideoExtensions = new HashSet<string>
    {
        ".mp4", ".mov", ".webm", ".avi", ".mkv", ".m4v", ".wmv"
    };

    [SerializeField] private AirCannonController controller;
    [SerializeField] private ClipPlayer clipPlayer;
    [SerializeField] private MediaPlayer mediaPlayer;
    [SerializeField] private DisplayUGUI display;

    private bool frameReady;
    private bool failed;
    private bool pausedForReplay;

    private void Awake()
    {
        if (mediaPlayer != null)
        {
            mediaPlayer.AutoOpen = false;
            mediaPlayer.AutoStart = false;
            mediaPlayer.Loop = true;
            mediaPlayer.AudioMuted = true;
        }
        if (display != null)
        {
            display.Player = mediaPlayer;
            display.NoDefaultDisplay = true;
            display.DisplayInEditor = false;
            display.DefaultTexture = null;
        }
        SetVisible(false);
    }

    private void Start()
    {
        if (mediaPlayer == null || display == null || controller == null)
        {
            Debug.LogError("[AirCannon] IdleVideoPlayer 참조가 비어 있음 - 대기 영상 없음");
            enabled = false;
            return;
        }

        mediaPlayer.Events.AddListener(OnMediaEvent);
        Open(controller.Config.idleVideoFolder);
    }

    private void OnDestroy()
    {
        if (mediaPlayer != null)
            mediaPlayer.Events.RemoveListener(OnMediaEvent);
    }

    // 폴더 안 영상을 이름에 상관없이 찾는다. 여러 개면 이름순 첫 파일을 쓴다.
    private static string FindVideo(string folder)
    {
        if (!Directory.Exists(folder))
        {
            Debug.LogWarning($"[AirCannon] 대기 영상 폴더 없음: {folder}");
            return null;
        }

        var videos = Directory.GetFiles(folder)
            .Where(f => VideoExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (videos.Count == 0)
        {
            Debug.LogWarning($"[AirCannon] 대기 영상 폴더에 영상 없음: {folder}");
            return null;
        }
        if (videos.Count > 1)
            Debug.LogWarning($"[AirCannon] 대기 영상이 {videos.Count}개 - 이름순 첫 파일만 사용: {Path.GetFileName(videos[0])}");
        return videos[0];
    }

    private void Open(string relativeFolder)
    {
        if (string.IsNullOrWhiteSpace(relativeFolder))
        {
            Debug.LogWarning("[AirCannon] idleVideoFolder가 비어 있음 - 대기 영상 없음");
            failed = true;
            return;
        }

        string path = FindVideo(Path.Combine(Application.streamingAssetsPath, relativeFolder));
        if (path == null)
        {
            failed = true;
            return;
        }

        if (!mediaPlayer.OpenMedia(MediaPathType.AbsolutePathOrURL, path, true))
        {
            Debug.LogWarning($"[AirCannon] 대기 영상 열기 실패: {path}");
            failed = true;
            return;
        }
        Debug.Log($"[AirCannon] 대기 영상 재생: {path}");
    }

    private void OnMediaEvent(MediaPlayer player, MediaPlayerEvent.EventType eventType, ErrorCode errorCode)
    {
        switch (eventType)
        {
            case MediaPlayerEvent.EventType.FirstFrameReady:
                // 지원 플레이어가 없으면 AVPro 로고 프레임을 내보내는 NullMediaPlayer로 떨어진다.
                if (player.Player is NullMediaPlayer)
                {
                    Debug.LogWarning("[AirCannon] 이 PC에서 AVPro 재생 플레이어를 만들지 못함 - 대기 영상 숨김");
                    failed = true;
                    player.CloseMedia();
                    break;
                }
                frameReady = true;
                break;
            case MediaPlayerEvent.EventType.Error:
                Debug.LogWarning($"[AirCannon] 대기 영상 오류: {errorCode}");
                failed = true;
                break;
        }
        UpdateVisible();
    }

    private void Update()
    {
        if (failed) return;

        // 리플레이 화면이 덮고 있는 동안은 디코딩을 멈춘다. Stop()으로 닫힌 경우도 따라가도록 상태를 직접 본다.
        bool replaying = clipPlayer != null && clipPlayer.IsPlaying;
        if (replaying && !pausedForReplay)
        {
            pausedForReplay = true;
            mediaPlayer.Pause();
        }
        else if (!replaying && pausedForReplay)
        {
            pausedForReplay = false;
            mediaPlayer.Play();
        }
    }

    private void UpdateVisible()
    {
        SetVisible(frameReady && !failed);
    }

    private void SetVisible(bool visible)
    {
        if (display != null && display.enabled != visible)
            display.enabled = visible;
    }
}
