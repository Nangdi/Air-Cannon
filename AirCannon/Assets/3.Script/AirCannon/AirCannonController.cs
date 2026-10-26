using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

// 발사 신호(RS232 또는 테스트 키)를 받으면 신호 시각 + offset부터 duration만큼의 웹캠 영상을 잘라
// 정해진 횟수와 배속으로 반복 재생한다.
// 자동 맞춤이 켜져 있으면 신호 뒤 화면 움직임이 시작된 시각 + motionLead를 기준으로 잘라낸다.
// 보정 화면이 열려 있는 동안에는 발사 신호를 보정 화면으로 넘긴다.
// 디버그 패널이 먼저 수신 이벤트를 구독해야 [Receive] 줄 다음에 해석 결과가 찍히므로 늦게 실행한다.
[DefaultExecutionOrder(100)]
public class AirCannonController : MonoBehaviour
{
    private const float FrameWaitTimeoutSeconds = 0.5f; // 구간 끝 프레임이 늦게 들어올 때 더 기다리는 시간
    private const float MotionWaitTimeoutSeconds = 0.5f; // 움직임 점수가 늦게 들어올 때 더 기다리는 시간

    [SerializeField] private WebcamRingBuffer ringBuffer;
    [SerializeField] private ClipPlayer clipPlayer;
    [Tooltip("비워두면 클립을 저장하지 않음")]
    [SerializeField] private ClipSaver clipSaver;
    [Tooltip("비워두면 패널에 표시하지 않음 (콘솔 로그만 남김)")]
    [SerializeField] private SerialPortLogPanel debugPanel;
    [Tooltip("비워두면 보정 화면을 쓰지 않음")]
    [SerializeField] private ReplayCalibrator calibrator;
    [Tooltip("테스트용 발사 키. None이면 사용하지 않음")]
    [SerializeField] private KeyCode testFireKey = KeyCode.Space;
    [Tooltip("보정 화면을 열고 닫는 키")]
    [SerializeField] private KeyCode calibrationKey = KeyCode.F2;

    // 대기화면 연결용. 리플레이가 시작되면/정해진 횟수를 다 돌면 호출된다.
    public event Action ReplayStarted;
    public event Action ReplayFinished;

    public AirCannonConfig Config => config;

    private AirCannonConfig config;
    private readonly ReplayClip clip = new ReplayClip();
    private bool isCapturing;
    private SerialPortManager serial;

    private void Awake()
    {
        config = AirCannonConfig.Load();
    }

    private void OnEnable()
    {
        if (testFireKey != KeyCode.None)
            HotkeyRegistry.Register(this, testFireKey.ToString(), "테스트 발사 (발사 신호와 똑같이 동작)", "모든 화면");
        if (calibrator != null && calibrationKey != KeyCode.None)
            HotkeyRegistry.Register(this, calibrationKey.ToString(), "리플레이 타이밍 보정 화면 열기/닫기", "모든 화면");
    }

    private void OnDisable()
    {
        HotkeyRegistry.Unregister(this);
    }

    private void Start()
    {
        if (ringBuffer == null || clipPlayer == null)
        {
            Debug.LogError("[AirCannon] ringBuffer 또는 clipPlayer 참조가 비어 있음");
            enabled = false;
            return;
        }

        ringBuffer.Begin(config);
        clipPlayer.Finished += OnReplayFinished;

        serial = SerialPortManager.Instance;
        if (serial != null)
            serial.OnDataReceived += OnSerialData;
        else
            Debug.LogWarning("[AirCannon] SerialPortManager 없음 - 테스트 키로만 발사 가능");
    }

    private void OnDestroy()
    {
        if (serial != null)
            serial.OnDataReceived -= OnSerialData;
        if (clipPlayer != null)
            clipPlayer.Finished -= OnReplayFinished;
        clip.Release();
    }

    private void Update()
    {
        if (IsTypingInInputField()) return;

        if (testFireKey != KeyCode.None && Input.GetKeyDown(testFireKey))
            Fire(testFireKey.ToString());

        if (calibrator != null && calibrationKey != KeyCode.None && Input.GetKeyDown(calibrationKey))
            ToggleCalibration();
    }

    // 설정창에서 config 값을 바꾼 뒤 호출한다. 범위를 다시 맞추고, 버퍼 크기와 웹캠 요청값에 반영하고, aircannon.json에 저장한다.
    public void ApplyConfigChange(string description)
    {
        config.Validate();
        ringBuffer.RefreshCapacity();
        ringBuffer.RestartIfCameraChanged();
        if (config.Save())
            Report($"설정 변경: {description}");
    }

    private void ToggleCalibration()
    {
        if (calibrator.IsOpen)
        {
            calibrator.Close();
            return;
        }

        StopAllCoroutines();
        isCapturing = false;
        clipPlayer.Stop();
        calibrator.Open(config, ringBuffer, Report);
    }

    // 어느 포트로 들어왔는지는 구분하지 않는다 (로그에만 남김).
    private void OnSerialData(int controllerId, string data)
    {
        if (!config.IsTrigger(data))
        {
            Report($"무시-문자열: Ctrl {controllerId} '{data}'");
            return;
        }

        Fire($"Ctrl {controllerId}");
    }

    private void Fire(string source)
    {
        if (calibrator != null && calibrator.IsOpen)
        {
            calibrator.OnFire(Time.realtimeSinceStartupAsDouble, source);
            return;
        }
        if (isCapturing)
        {
            Report($"무시-캡처 중 ({source})");
            return;
        }
        if (!ringBuffer.IsRunning)
        {
            Report($"무시-웹캠 없음 ({source})", true);
            return;
        }

        double fireTime = Time.realtimeSinceStartupAsDouble;
        Report($"발사 인식 ({source})");
        StartCoroutine(CaptureRoutine(fireTime));
    }

    private IEnumerator CaptureRoutine(double fireTime)
    {
        isCapturing = true;

        double fixedStart = fireTime + config.offsetSeconds;
        double fixedEnd = fixedStart + config.durationSeconds;
        double start = fixedStart;
        bool clipReady = false;
        string alignNote = "고정 offset";

        if (config.autoAlign && ringBuffer.Motion.IsAvailable)
        {
            MotionDetector motion = ringBuffer.Motion;
            double searchEnd = fireTime + config.motionSearchSeconds;
            double deadline = searchEnd + MotionWaitTimeoutSeconds;
            bool found = false;
            double onset = 0;

            while (true)
            {
                if (motion.TryFindOnset(fireTime, searchEnd, config.motionThreshold, out onset))
                {
                    found = true;
                    break;
                }
                if (motion.LatestTime >= searchEnd || Time.realtimeSinceStartupAsDouble >= deadline)
                    break;

                // 찾는 동안 고정 구간이 버퍼에서 밀려나지 않도록 먼저 복사해 둔다.
                if (!clipReady && ringBuffer.LatestFrameTime >= fixedEnd)
                    clipReady = ringBuffer.CopyRange(fixedStart, fixedEnd, clip) > 0;

                yield return null;
            }

            if (found)
            {
                start = onset + config.motionLeadSeconds;
                clipReady = false;
                alignNote = $"자동 맞춤: 움직임 {onset - fireTime:+0.00;-0.00}s";
            }
            else
            {
                alignNote = $"자동 맞춤 실패({config.motionSearchSeconds:F1}s 안에 점수 {config.motionThreshold:F1} 넘는 움직임 없음) - 고정 offset";
            }
        }

        int frameCount;
        if (clipReady)
        {
            frameCount = clip.Count;
        }
        else
        {
            double end = start + config.durationSeconds;
            while (Time.realtimeSinceStartupAsDouble < end)
                yield return null;

            // 구간 끝 이후의 프레임이 한 장이라도 들어와야 구간이 다 채워진 것이다.
            double frameDeadline = end + FrameWaitTimeoutSeconds;
            while (ringBuffer.LatestFrameTime < end && Time.realtimeSinceStartupAsDouble < frameDeadline)
                yield return null;

            frameCount = ringBuffer.CopyRange(start, end, clip);
        }
        isCapturing = false;

        if (frameCount == 0)
        {
            Report($"재생 안 함: 구간에 프레임 없음 ({alignNote})", true);
            yield break;
        }

        Report($"재생: {frameCount}f {clip.Length:F2}s x{config.repeatCount} @{config.playbackSpeed} ({alignNote})");
        clipPlayer.Play(clip, config.repeatCount, config.playbackSpeed);
        ReplayStarted?.Invoke();

        if (config.saveClip && clipSaver != null)
            clipSaver.Save(clip, config.ResolveSaveFolder(), config.jpgQuality);
    }

    // 발사 신호 해석 결과를 콘솔과 디버그 패널에 함께 남긴다.
    private void Report(string message, bool warning = false)
    {
        string line = $"[AirCannon] {message}";
        if (warning) Debug.LogWarning(line);
        else Debug.Log(line);

        if (debugPanel != null) debugPanel.Log(line);
    }

    public static bool IsTypingInInputField()
    {
        GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        return selected != null && selected.GetComponent<TMP_InputField>() != null;
    }

    private void OnReplayFinished()
    {
        ReplayFinished?.Invoke();
    }
}
