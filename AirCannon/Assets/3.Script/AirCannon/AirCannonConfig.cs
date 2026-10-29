using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// 움직임 감지 영역 (0~1, 왼쪽 위 기준)
[Serializable]
public class MotionArea
{
    public float x = 0f;
    public float y = 0f;
    public float width = 1f;
    public float height = 1f;
}

// 공기대포 리플레이 설정 (persistentDataPath/aircannon.json).
// 처음 실행할 때 StreamingAssets/aircannon.json 기본값을 복사해 오고, 이후로는 persistent 쪽 파일만 읽고 쓴다.
// 설정창(ESC)과 보정 화면(F2)에서 저장해도 persistent 쪽 파일이 바뀐다.
[Serializable]
public class AirCannonConfig
{
    public const string FileName = "aircannon.json";

    // 웹캠 이름(일부만 적어도 됨). 비우면 첫 번째 웹캠을 쓴다.
    public string cameraName = "";
    public int cameraWidth = 1920;
    public int cameraHeight = 1080;
    public int cameraFps = 30;
    // 좌우 반전 (거울처럼 보이게 할 때)
    public bool mirrorHorizontal = false;
    // 웹캠 영상 회전 (시계 방향, 0/90/180/270). 가로 웹캠을 세로로 세워 달았을 때 90 또는 270
    public int cameraRotation = 90;

    // 이 문자열 중 하나와 같으면 발사로 본다 (대소문자 무시, 앞뒤 공백 무시).
    // 어느 RS232 포트(controllerId)로 들어와도 똑같이 받는다.
    public List<string> triggerMessages = new List<string> { "FIRE", "SHOOT" };

    // 신호를 받은 시각 기준으로 몇 초 뒤부터 잘라낼지. 음수면 신호 이전 장면부터 잘라낸다.
    public float offsetSeconds = 0.3f;
    // 잘라낼 길이(초)
    public float durationSeconds = 1.5f;

    // 자동 맞춤: 신호 뒤 화면 움직임이 처음 커지는 순간을 찾아 그 시각 기준으로 잘라낸다.
    // 못 찾으면 offsetSeconds를 쓴다.
    public bool autoAlign = true;
    // 움직임 시작 기준 몇 초 뒤부터 잘라낼지. 음수면 움직임 이전 장면부터
    public float motionLeadSeconds = -0.3f;
    // 움직임으로 볼 점수 (평균 밝기 차이 x 100). 보정 화면 그래프를 보고 정한다.
    public float motionThreshold = 3f;
    // 신호 후 몇 초 안에서 움직임을 찾을지
    public float motionSearchSeconds = 2f;
    // 움직임을 볼 영역. 대포 앞만 지정하면 관람객 움직임에 덜 흔들린다.
    public MotionArea motionArea = new MotionArea();

    // 반복 재생 횟수. 0이면 다음 발사 신호가 올 때까지 계속 반복한다.
    public int repeatCount = 3;
    // 재생 배속. 0.5 = 0.5배속 슬로모션, 1 = 원래 속도
    public float playbackSpeed = 0.5f;

    // 대기 화면에서 무한 반복할 영상 폴더 (StreamingAssets 기준). 파일 이름은 따지지 않고 폴더 안 영상 하나를 쓴다.
    // 영상이 없으면 검은 화면만 보인다.
    public string idleVideoFolder = "Idle";
    // 리플레이 화면에서 마지막 입력(키, 마우스, 터치, 발사 신호) 후 이 시간(초)이 지나면 대기 화면으로 돌아간다. 0이면 사용 안 함
    public float idleTimeoutSeconds = 30f;

    // 클립을 JPG 연속 이미지로 저장할지 여부 (기본 꺼짐)
    public bool saveClip = false;
    // 저장 폴더. 비우면 persistentDataPath/Clips
    public string saveFolder = "";
    public int jpgQuality = 90;

    public static string FilePath => JsonManager.PreparePersistentFile(FileName);

    // 파일이 없으면 기본값으로 새로 만든다. 깨져 있으면 기본값으로 동작하고 파일은 건드리지 않는다.
    public static AirCannonConfig Load()
    {
        AirCannonConfig config;
        try
        {
            config = JsonManager.LoadData(FilePath, new AirCannonConfig());
        }
        catch (Exception ex)
        {
            Debug.LogError($"[AirCannon] {FileName} 읽기 실패 - 기본값 사용: {ex.Message}");
            config = new AirCannonConfig();
        }

        config.Validate();
        return config;
    }

    public bool Save()
    {
        try
        {
            JsonManager.SaveData(this, FilePath);
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[AirCannon] {FileName} 저장 실패: {ex.Message}");
            return false;
        }
    }

    public void Validate()
    {
        cameraWidth = Mathf.Max(16, cameraWidth);
        cameraHeight = Mathf.Max(16, cameraHeight);
        cameraFps = Mathf.Clamp(cameraFps, 1, 240);
        cameraRotation = ((Mathf.RoundToInt(cameraRotation / 90f) % 4 + 4) % 4) * 90;
        durationSeconds = Mathf.Clamp(durationSeconds, 0.1f, 10f);
        repeatCount = Mathf.Max(0, repeatCount);
        playbackSpeed = Mathf.Clamp(playbackSpeed, 0.05f, 4f);
        motionThreshold = Mathf.Max(0.1f, motionThreshold);
        motionSearchSeconds = Mathf.Clamp(motionSearchSeconds, 0.1f, 5f);
        motionArea ??= new MotionArea();
        motionArea.x = Mathf.Clamp(motionArea.x, 0f, 0.99f);
        motionArea.y = Mathf.Clamp(motionArea.y, 0f, 0.99f);
        motionArea.width = Mathf.Clamp(motionArea.width, 0.01f, 1f - motionArea.x);
        motionArea.height = Mathf.Clamp(motionArea.height, 0.01f, 1f - motionArea.y);
        jpgQuality = Mathf.Clamp(jpgQuality, 1, 100);
        idleTimeoutSeconds = Mathf.Clamp(idleTimeoutSeconds, 0f, 600f);
        idleVideoFolder ??= "";
        triggerMessages ??= new List<string>();
        if (triggerMessages.Count == 0)
            Debug.LogWarning("[AirCannon] triggerMessages가 비어 있음 - 시리얼 발사 신호를 받지 않음");
    }

    public bool IsTrigger(string message)
    {
        if (string.IsNullOrEmpty(message)) return false;

        string trimmed = message.Trim();
        for (int i = 0; i < triggerMessages.Count; i++)
        {
            if (string.Equals(trimmed, triggerMessages[i]?.Trim(), StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public string ResolveSaveFolder()
    {
        return string.IsNullOrWhiteSpace(saveFolder)
            ? Path.Combine(Application.persistentDataPath, "Clips")
            : saveFolder;
    }
}
