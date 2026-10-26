using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 리플레이 타이밍 보정 화면 (F2).
// 발사하면 신호 전 PreSeconds ~ 후 PostSeconds를 녹화해 멈추고, 프레임 단위로 넘겨 보며
// 잘라낼 구간(시작/끝)을 정해 aircannon.json에 저장한다.
// 그래프에는 프레임별 움직임 점수, 감도(임계값), 신호 시점, 움직임 시작 시점, 최근 발들의 움직임 시작 시점을 그린다.
public class ReplayCalibrator : MonoBehaviour
{
    private const float PreSeconds = 1f;
    private const float PostSeconds = 5f;
    private const float WaitTimeoutSeconds = 1f;   // 녹화 끝 프레임/움직임 점수가 늦게 들어올 때 더 기다리는 시간
    private const int HistorySize = 5;
    private const int GraphWidth = 800;
    private const int GraphHeight = 100;
    private const float ThresholdStep = 0.5f;
    private const float DefaultFrameInterval = 1f / 30f;
    private const float ShortDurationWarnSeconds = 0.3f;
    private const float KeyRepeatDelaySeconds = 0.35f;   // 꾹 누를 때 반복이 시작되기까지
    private const float KeyRepeatIntervalSeconds = 0.05f; // 반복 간격 (초당 20번)

    private const string HelpScope = "보정 화면 (F2)";
    // 보정 화면 조작 키. 도움말(F1)에도 이 값이 그대로 나온다.
    private const KeyCode AutoAlignKey = KeyCode.A;
    private const KeyCode ThresholdDownKey = KeyCode.Minus;
    private const KeyCode ThresholdUpKey = KeyCode.Equals;
    private const KeyCode SetStartKey = KeyCode.I;
    private const KeyCode SetEndKey = KeyCode.O;
    private const KeyCode PreviewKey = KeyCode.P;
    private const KeyCode RetakeKey = KeyCode.R;
    private const KeyCode SaveKey = KeyCode.Return;

    private enum State { Closed, Live, Recording, Editing }

    // 한 발의 움직임 기록 (신호 기준 상대 시각)
    private class ShotRecord
    {
        public readonly List<float> times = new List<float>();
        public readonly List<float> scores = new List<float>();

        public bool TryFindOnset(float searchSeconds, float threshold, out float onset)
        {
            for (int i = 0; i < times.Count; i++)
            {
                if (times[i] < 0f) continue;
                if (times[i] > searchSeconds) break;
                if (scores[i] >= threshold)
                {
                    onset = times[i];
                    return true;
                }
            }
            onset = 0f;
            return false;
        }
    }

    [Header("패널")]
    [Tooltip("보정 화면 루트. 이 스크립트는 항상 켜져 있는 오브젝트에 둔다.")]
    [SerializeField] private GameObject panelRoot;

    [Header("화면")]
    [SerializeField] private RawImage preview;
    [Tooltip("비워두면 화면 비율을 맞추지 않음")]
    [SerializeField] private AspectRatioFitter previewFitter;
    [SerializeField] private RawImage graph;
    [SerializeField] private Slider playhead;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text infoText;

    [Header("버튼")]
    [SerializeField] private Button setStartButton;
    [SerializeField] private Button setEndButton;
    [SerializeField] private Button previewButton;
    [SerializeField] private TMP_Text previewLabel;
    [SerializeField] private Button autoAlignButton;
    [SerializeField] private TMP_Text autoAlignLabel;
    [SerializeField] private Button thresholdDownButton;
    [SerializeField] private Button thresholdUpButton;
    [SerializeField] private Button retakeButton;
    [SerializeField] private Button saveButton;
    [SerializeField] private Button closeButton;

    [Header("그래프 색")]
    [SerializeField] private Color graphBackground = new Color(0.07f, 0.07f, 0.09f);
    [SerializeField] private Color tickColor = new Color(0.2f, 0.2f, 0.24f);
    [SerializeField] private Color selectionColor = new Color(0.13f, 0.22f, 0.38f);
    [SerializeField] private Color barColor = new Color(0.5f, 0.55f, 0.65f);
    [SerializeField] private Color barHotColor = new Color(0.95f, 0.7f, 0.25f);
    [SerializeField] private Color thresholdColor = new Color(0.95f, 0.3f, 0.3f);
    [SerializeField] private Color signalColor = Color.white;
    [SerializeField] private Color onsetColor = new Color(1f, 0.75f, 0.2f);
    [SerializeField] private Color historyColor = new Color(0.55f, 0.42f, 0.15f);
    [SerializeField] private Color playheadColor = new Color(0.3f, 0.95f, 0.5f);

    public bool IsOpen => state != State.Closed;

    private State state = State.Closed;
    private AirCannonConfig config;
    private WebcamRingBuffer ringBuffer;
    private Action<string, bool> report;

    // 저장 전까지 config에 쓰지 않는 값
    private bool pendingAutoAlign;
    private float pendingThreshold;

    private double fireTime;
    private readonly List<RenderTexture> frames = new List<RenderTexture>();
    private readonly List<double> frameTimes = new List<double>();
    private readonly List<double> sampleTimes = new List<double>();
    private readonly List<float> sampleScores = new List<float>();
    private readonly List<ShotRecord> history = new List<ShotRecord>();
    private int playheadIndex;
    private int startIndex;
    private int endIndex;
    private bool previewing;
    private float previewTime;
    private float keyRepeatTimer;

    private Texture2D graphTexture;
    private Color32[] graphPixels;
    private readonly StringBuilder sb = new StringBuilder();

    private void Awake()
    {
        if (panelRoot != null) panelRoot.SetActive(false);

        if (playhead != null) playhead.onValueChanged.AddListener(OnPlayheadChanged);
        Bind(setStartButton, SetStart);
        Bind(setEndButton, SetEnd);
        Bind(previewButton, TogglePreview);
        Bind(autoAlignButton, ToggleAutoAlign);
        Bind(thresholdDownButton, () => ChangeThreshold(-ThresholdStep));
        Bind(thresholdUpButton, () => ChangeThreshold(ThresholdStep));
        Bind(retakeButton, Retake);
        Bind(saveButton, Save);
        Bind(closeButton, Close);
    }

    private void OnEnable()
    {
        HotkeyRegistry.Register(this, "발사 신호 / 테스트 발사 키", "녹화 시작 (신호 전 1초 ~ 후 5초)", HelpScope);
        HotkeyRegistry.Register(this, AutoAlignKey.ToString(), "자동 맞춤 켜기/끄기", HelpScope);
        HotkeyRegistry.Register(this, $"{ThresholdDownKey} / {ThresholdUpKey}", "감도 낮추기 / 높이기", HelpScope);
        HotkeyRegistry.Register(this, "← / →", "1프레임 이동 (꾹 누르면 계속, Shift 10프레임) - 편집 중", HelpScope);
        HotkeyRegistry.Register(this, SetStartKey.ToString(), "시작점 지정 - 편집 중", HelpScope);
        HotkeyRegistry.Register(this, SetEndKey.ToString(), "끝점 지정 - 편집 중", HelpScope);
        HotkeyRegistry.Register(this, PreviewKey.ToString(), "실제 배속 미리보기 / 멈춤 - 편집 중", HelpScope);
        HotkeyRegistry.Register(this, RetakeKey.ToString(), "다시 촬영 - 편집 중", HelpScope);
        HotkeyRegistry.Register(this, "Enter", "보정 값 저장 - 편집 중", HelpScope);
    }

    private void OnDisable()
    {
        HotkeyRegistry.Unregister(this);
    }

    private static void Bind(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button != null) button.onClick.AddListener(action);
    }

    private void OnDestroy()
    {
        if (graphTexture != null) Destroy(graphTexture);
    }

    public void Open(AirCannonConfig config, WebcamRingBuffer ringBuffer, Action<string, bool> report)
    {
        this.config = config;
        this.ringBuffer = ringBuffer;
        this.report = report;
        pendingAutoAlign = config.autoAlign;
        pendingThreshold = config.motionThreshold;

        if (graphTexture == null)
        {
            graphTexture = new Texture2D(GraphWidth, GraphHeight, TextureFormat.RGBA32, false)
            {
                name = "CalibrationGraph",
                wrapMode = TextureWrapMode.Clamp,
            };
            graphPixels = new Color32[GraphWidth * GraphHeight];
        }
        if (graph != null) graph.texture = graphTexture;

        ringBuffer.SetCalibrationMode(true, PreSeconds + PostSeconds);
        EnterLive();
        if (panelRoot != null) panelRoot.SetActive(true);
        Report("보정 화면 열림");
    }

    public void Close()
    {
        if (state == State.Closed) return;

        state = State.Closed;
        previewing = false;
        frames.Clear();
        frameTimes.Clear();
        if (preview != null) preview.texture = null;
        if (ringBuffer != null) ringBuffer.SetCalibrationMode(false, 0f);
        if (panelRoot != null) panelRoot.SetActive(false);
        Report("보정 화면 닫힘");
    }

    public void OnFire(double time, string source)
    {
        if (state != State.Live)
        {
            Report($"보정: 무시-{(state == State.Recording ? "녹화" : "편집")} 중 ({source}). 다시 찍으려면 R", true);
            return;
        }
        if (!ringBuffer.IsRunning)
        {
            Report($"보정: 무시-웹캠 없음 또는 버퍼 준비 중 ({source})", true);
            return;
        }

        fireTime = time;
        state = State.Recording;
        RefreshButtons();
        Report($"보정: 발사 인식 ({source}) - {PostSeconds:F0}초 녹화");
    }

    private void Update()
    {
        if (state == State.Closed) return;

        // 에디터에서 플레이 중 스크립트가 다시 컴파일되면 녹화 프레임 목록이 비므로 대기 상태로 되돌린다.
        if (state == State.Editing && frames.Count == 0)
        {
            EnterLive();
            return;
        }

        if (!AirCannonController.IsTypingInInputField())
            HandleKeys();

        switch (state)
        {
            case State.Live: UpdateLive(); break;
            case State.Recording: UpdateRecording(); break;
            case State.Editing: UpdateEditing(); break;
        }
    }

    private void HandleKeys()
    {
        if (Input.GetKeyDown(AutoAlignKey)) ToggleAutoAlign();
        if (Input.GetKeyDown(ThresholdDownKey) || Input.GetKeyDown(KeyCode.KeypadMinus)) ChangeThreshold(-ThresholdStep);
        if (Input.GetKeyDown(ThresholdUpKey) || Input.GetKeyDown(KeyCode.KeypadPlus)) ChangeThreshold(ThresholdStep);

        if (state != State.Editing) return;

        HandleArrowKeys();
        if (Input.GetKeyDown(SetStartKey)) SetStart();
        if (Input.GetKeyDown(SetEndKey)) SetEnd();
        if (Input.GetKeyDown(PreviewKey)) TogglePreview();
        if (Input.GetKeyDown(RetakeKey)) Retake();
        if (Input.GetKeyDown(SaveKey) || Input.GetKeyDown(KeyCode.KeypadEnter)) Save();
    }

    // ←/→: 한 번 누르면 1프레임(Shift 10프레임), 꾹 누르고 있으면 잠시 뒤부터 계속 이동한다.
    private void HandleArrowKeys()
    {
        int direction = Input.GetKey(KeyCode.RightArrow) ? 1 : Input.GetKey(KeyCode.LeftArrow) ? -1 : 0;
        if (direction == 0) return;

        int step = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? 10 : 1;
        if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.RightArrow))
        {
            MovePlayhead(direction * step);
            keyRepeatTimer = KeyRepeatDelaySeconds;
            return;
        }

        keyRepeatTimer -= Time.unscaledDeltaTime;
        while (keyRepeatTimer <= 0f)
        {
            MovePlayhead(direction * step);
            keyRepeatTimer += KeyRepeatIntervalSeconds;
        }
    }

    // ---------- 상태별 화면 ----------

    private void EnterLive()
    {
        state = State.Live;
        previewing = false;
        frames.Clear();
        frameTimes.Clear();
        ringBuffer.Frozen = false;
        RefreshButtons();
    }

    private void UpdateLive()
    {
        ShowTexture(ringBuffer.LatestFrame);
        SetText(statusText, "발사 대기 중 - Space 또는 발사 신호를 보내면 신호 전 1초 ~ 후 5초를 녹화합니다");

        double now = Time.realtimeSinceStartupAsDouble;
        ringBuffer.Motion.GetSamples(now - PreSeconds - PostSeconds, now, sampleTimes, sampleScores);
        DrawGraph(now - PreSeconds - PostSeconds, now, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN);

        sb.Clear();
        sb.Append("현재 움직임 점수 ").Append(sampleScores.Count > 0 ? sampleScores[sampleScores.Count - 1].ToString("F1") : "-");
        sb.Append("  |  감도 ").Append(pendingThreshold.ToString("F1"));
        sb.Append("  |  가만히 있을 때 점수보다 조금 높게 감도를 맞춥니다\n");
        AppendHistory();
        SetText(infoText, sb.ToString());
    }

    private void UpdateRecording()
    {
        ShowTexture(ringBuffer.LatestFrame);
        double now = Time.realtimeSinceStartupAsDouble;
        double end = fireTime + PostSeconds;
        SetText(statusText, $"녹화 중... {Math.Max(0, end - now):F1}초");

        ringBuffer.Motion.GetSamples(fireTime - PreSeconds, end, sampleTimes, sampleScores);
        DrawGraph(fireTime - PreSeconds, end, fireTime, FindOnset(), double.NaN, double.NaN, now);
        SetText(infoText, "");

        if (now < end) return;
        bool complete = ringBuffer.LatestFrameTime >= end && ringBuffer.Motion.LatestTime >= end;
        if (complete || now >= end + WaitTimeoutSeconds)
            FinishRecording();
    }

    private void FinishRecording()
    {
        double from = fireTime - PreSeconds;
        double to = fireTime + PostSeconds;

        ringBuffer.Frozen = true;
        int count = ringBuffer.CollectRange(from, to, frames, frameTimes);
        if (count == 0)
        {
            Report("보정: 녹화된 프레임 없음 - 다시 발사하세요", true);
            EnterLive();
            return;
        }

        ringBuffer.Motion.GetSamples(from, to, sampleTimes, sampleScores);
        var shot = new ShotRecord();
        for (int i = 0; i < sampleTimes.Count; i++)
        {
            shot.times.Add((float)(sampleTimes[i] - fireTime));
            shot.scores.Add(sampleScores[i]);
        }
        history.Add(shot);
        while (history.Count > HistorySize) history.RemoveAt(0);

        // 처음 구간은 지금 설정대로 잘라냈을 때의 구간으로 잡는다.
        double onset = FindOnset();
        double start = pendingAutoAlign && !double.IsNaN(onset)
            ? onset + config.motionLeadSeconds
            : fireTime + config.offsetSeconds;
        double end = start + config.durationSeconds;
        startIndex = IndexAtOrAfter(start);
        endIndex = Mathf.Max(startIndex, IndexAtOrBefore(end));
        playheadIndex = startIndex;

        if (playhead != null)
        {
            playhead.minValue = 0;
            playhead.maxValue = Mathf.Max(1, count - 1);
            playhead.wholeNumbers = true;
            playhead.SetValueWithoutNotify(playheadIndex);
        }

        state = State.Editing;
        RefreshButtons();
        string onsetText = double.IsNaN(onset) ? "못 찾음" : $"{onset - fireTime:+0.00;-0.00}s";
        Report($"보정: 녹화 완료 {count}프레임, 움직임 시작 {onsetText}");
    }

    private void UpdateEditing()
    {
        if (previewing)
        {
            float length = (float)(frameTimes[endIndex] - frameTimes[startIndex] + FrameInterval());
            previewTime += Time.unscaledDeltaTime * config.playbackSpeed;
            if (previewTime >= length) previewTime %= length;

            int index = startIndex;
            while (index + 1 <= endIndex && frameTimes[index + 1] - frameTimes[startIndex] <= previewTime)
                index++;
            SetPlayhead(index);
        }

        ShowTexture(frames[playheadIndex]);
        SetText(statusText, "편집 - ←/→ 1프레임 (꾹 누르면 계속, Shift 10프레임), I 시작점, O 끝점, P 미리보기, Enter 저장, R 다시 촬영");

        double onset = FindOnset();
        DrawGraph(fireTime - PreSeconds, fireTime + PostSeconds, fireTime, onset,
                  frameTimes[startIndex], frameTimes[endIndex], frameTimes[playheadIndex]);

        double interval = FrameInterval();
        double selStart = frameTimes[startIndex];
        double selEnd = frameTimes[endIndex] + interval * 0.5;
        double duration = selEnd - selStart;

        sb.Clear();
        sb.Append($"재생 위치 {Rel(frameTimes[playheadIndex])}s  ({playheadIndex + 1}/{frames.Count}프레임)\n");
        sb.Append($"구간 {Rel(selStart)}s ~ {Rel(selEnd)}s  (길이 {duration:F2}s, 재생 시 {duration / config.playbackSpeed:F1}s x{config.repeatCount})\n");
        sb.Append($"움직임 시작 {(double.IsNaN(onset) ? "못 찾음" : Rel(onset) + "s")}  |  감도 {pendingThreshold:F1}  |  저장 시 ");
        if (pendingAutoAlign && !double.IsNaN(onset))
            sb.Append($"움직임 기준 {selStart - onset:+0.00;-0.00}s부터");
        else if (pendingAutoAlign)
            sb.Append($"신호 기준 {Rel(selStart)}s부터 (움직임 못 찾음)");
        else
            sb.Append($"신호 기준 {Rel(selStart)}s부터");
        sb.Append('\n');
        AppendHistory();
        SetText(infoText, sb.ToString());
    }

    // ---------- 조작 ----------

    private void OnPlayheadChanged(float value)
    {
        if (state != State.Editing) return;
        previewing = false;
        playheadIndex = Mathf.Clamp(Mathf.RoundToInt(value), 0, frames.Count - 1);
        RefreshButtons();
    }

    private void MovePlayhead(int delta)
    {
        previewing = false;
        SetPlayhead(Mathf.Clamp(playheadIndex + delta, 0, frames.Count - 1));
        RefreshButtons();
    }

    private void SetPlayhead(int index)
    {
        playheadIndex = index;
        if (playhead != null) playhead.SetValueWithoutNotify(index);
    }

    private void SetStart()
    {
        if (state != State.Editing) return;
        // 시작점이 끝점을 넘어가면 구간 길이를 유지한 채 끝점을 같이 민다.
        int length = endIndex - startIndex;
        startIndex = playheadIndex;
        if (endIndex < startIndex) endIndex = Mathf.Min(frames.Count - 1, startIndex + length);
    }

    private void SetEnd()
    {
        if (state != State.Editing) return;
        // 끝점이 시작점보다 앞으로 가면 구간 길이를 유지한 채 시작점을 같이 당긴다.
        int length = endIndex - startIndex;
        endIndex = playheadIndex;
        if (startIndex > endIndex) startIndex = Mathf.Max(0, endIndex - length);
    }

    private void TogglePreview()
    {
        if (state != State.Editing) return;
        previewing = !previewing;
        previewTime = 0f;
        if (previewing) SetPlayhead(startIndex);
        RefreshButtons();
    }

    private void ToggleAutoAlign()
    {
        pendingAutoAlign = !pendingAutoAlign;
        RefreshButtons();
    }

    private void ChangeThreshold(float delta)
    {
        pendingThreshold = Mathf.Max(ThresholdStep, pendingThreshold + delta);
    }

    private void Retake()
    {
        if (state != State.Editing) return;
        EnterLive();
        Report("보정: 다시 촬영 대기");
    }

    private void Save()
    {
        if (state != State.Editing)
        {
            Report("보정: 녹화한 뒤에 저장할 수 있음", true);
            return;
        }

        double interval = FrameInterval();
        double selStart = frameTimes[startIndex];
        double selEnd = frameTimes[endIndex] + interval * 0.5;
        double onset = FindOnset();

        config.offsetSeconds = Round3(selStart - fireTime);
        config.durationSeconds = Mathf.Max(0.1f, Round3(selEnd - selStart));
        config.autoAlign = pendingAutoAlign;
        config.motionThreshold = pendingThreshold;
        if (!double.IsNaN(onset))
            config.motionLeadSeconds = Round3(selStart - onset);

        if (!config.Save())
        {
            Report("보정: 저장 실패 (로그 확인)", true);
            return;
        }

        string lead = double.IsNaN(onset) ? "그대로(움직임 못 찾음)" : $"{config.motionLeadSeconds:+0.000;-0.000}s";
        Report($"보정 저장: offset {config.offsetSeconds:+0.000;-0.000}s, 길이 {config.durationSeconds:F3}s, " +
               $"움직임 기준 {lead}, 감도 {config.motionThreshold:F1}, 자동 맞춤 {(config.autoAlign ? "켜짐" : "꺼짐")}");
        if (pendingAutoAlign && double.IsNaN(onset))
            Report("보정: 이번 발에서 움직임을 못 찾음 - 감도를 낮추거나 motionArea를 확인하세요", true);
        if (config.durationSeconds < ShortDurationWarnSeconds)
            Report($"보정: 구간이 {config.durationSeconds:F2}초로 매우 짧음 - 끝점(O)을 확인하세요", true);
    }

    // ---------- 보조 ----------

    private void RefreshButtons()
    {
        bool editing = state == State.Editing;
        SetInteractable(setStartButton, editing);
        SetInteractable(setEndButton, editing);
        SetInteractable(previewButton, editing);
        SetInteractable(retakeButton, editing);
        SetInteractable(saveButton, editing);
        SetText(previewLabel, previewing ? "멈춤 (P)" : "미리보기 (P)");
        SetText(autoAlignLabel, pendingAutoAlign ? "자동 맞춤 켬 (A)" : "자동 맞춤 끔 (A)");
    }

    private static void SetInteractable(Button button, bool value)
    {
        if (button != null) button.interactable = value;
    }

    private static void SetText(TMP_Text text, string value)
    {
        if (text != null && text.text != value) text.text = value;
    }

    private void ShowTexture(Texture texture)
    {
        if (preview == null) return;
        preview.texture = texture;
        if (texture != null && previewFitter != null)
            previewFitter.aspectRatio = (float)texture.width / texture.height;
    }

    // 현재 녹화본에서 감도를 넘는 첫 움직임 (절대 시각). 없으면 NaN
    private double FindOnset()
    {
        for (int i = 0; i < sampleTimes.Count; i++)
        {
            double t = sampleTimes[i];
            if (t < fireTime) continue;
            if (t > fireTime + config.motionSearchSeconds) break;
            if (sampleScores[i] >= pendingThreshold) return t;
        }
        return double.NaN;
    }

    private void AppendHistory()
    {
        if (history.Count == 0)
        {
            sb.Append("최근 발 움직임 시작: 기록 없음");
            return;
        }

        sb.Append("최근 발 움직임 시작(신호 기준): ");
        float min = float.MaxValue, max = float.MinValue;
        int found = 0;
        for (int i = 0; i < history.Count; i++)
        {
            if (history[i].TryFindOnset(config.motionSearchSeconds, pendingThreshold, out float onset))
            {
                sb.Append($"{onset:+0.00;-0.00}  ");
                min = Mathf.Min(min, onset);
                max = Mathf.Max(max, onset);
                found++;
            }
            else
            {
                sb.Append("없음  ");
            }
        }
        if (found >= 2) sb.Append($"(편차 {max - min:F2}s)");
    }

    private double FrameInterval()
    {
        int count = frameTimes.Count;
        return count > 1 ? (frameTimes[count - 1] - frameTimes[0]) / (count - 1) : DefaultFrameInterval;
    }

    private int IndexAtOrAfter(double t)
    {
        for (int i = 0; i < frameTimes.Count; i++)
            if (frameTimes[i] >= t) return i;
        return frameTimes.Count - 1;
    }

    private int IndexAtOrBefore(double t)
    {
        for (int i = frameTimes.Count - 1; i >= 0; i--)
            if (frameTimes[i] <= t) return i;
        return 0;
    }

    private string Rel(double t) => (t - fireTime).ToString("+0.000;-0.000");

    private static float Round3(double v) => (float)Math.Round(v, 3);

    // ---------- 그래프 ----------

    // 시각은 모두 절대 시각. 쓰지 않는 표시는 NaN
    private void DrawGraph(double from, double to, double signal, double onset, double selStart, double selEnd, double playheadTime)
    {
        if (graphTexture == null) return;

        Color32 bg = graphBackground;
        for (int i = 0; i < graphPixels.Length; i++) graphPixels[i] = bg;

        double span = Math.Max(0.001, to - from);
        int X(double t) => Mathf.Clamp((int)((t - from) / span * (GraphWidth - 1)), 0, GraphWidth - 1);

        if (!double.IsNaN(selStart))
            FillColumns(X(selStart), X(selEnd), 0, GraphHeight, selectionColor);

        // 1초 눈금 (신호가 있으면 신호 기준, 없으면 오른쪽 끝 기준)
        double reference = double.IsNaN(signal) ? to : signal;
        for (double t = reference - Math.Floor((reference - from)); t <= to; t += 1.0)
            FillColumns(X(t), X(t), 0, GraphHeight, tickColor);

        float maxScore = 0f;
        for (int i = 0; i < sampleScores.Count; i++) maxScore = Mathf.Max(maxScore, sampleScores[i]);
        float graphMax = Mathf.Max(pendingThreshold * 2.5f, maxScore * 1.1f, 1f);

        for (int i = 0; i < sampleTimes.Count; i++)
        {
            int x0 = X(sampleTimes[i]);
            int x1 = i + 1 < sampleTimes.Count ? Mathf.Max(x0, X(sampleTimes[i + 1]) - 1) : x0;
            int h = Mathf.RoundToInt(Mathf.Clamp01(sampleScores[i] / graphMax) * (GraphHeight - 1));
            FillColumns(x0, x1, 0, h, sampleScores[i] >= pendingThreshold ? barHotColor : barColor);
        }

        int thresholdRow = Mathf.RoundToInt(Mathf.Clamp01(pendingThreshold / graphMax) * (GraphHeight - 1));
        FillRow(thresholdRow, thresholdColor);

        if (!double.IsNaN(signal))
        {
            // 움직임 찾는 범위 끝 (점선)
            int searchX = X(signal + config.motionSearchSeconds);
            for (int y = 0; y < GraphHeight; y += 6) FillColumns(searchX, searchX, y, y + 3, signalColor);

            int top = GraphHeight * 2 / 3;
            for (int i = 0; i < history.Count - (state == State.Editing ? 1 : 0); i++)
            {
                if (history[i].TryFindOnset(config.motionSearchSeconds, pendingThreshold, out float h))
                    FillColumns(X(signal + h), X(signal + h), top, GraphHeight, historyColor);
            }
            FillColumns(X(signal), X(signal) + 1, 0, GraphHeight, signalColor);
        }
        if (!double.IsNaN(onset))
            FillColumns(X(onset), X(onset) + 1, 0, GraphHeight, onsetColor);
        if (!double.IsNaN(playheadTime))
            FillColumns(X(playheadTime), X(playheadTime) + 1, 0, GraphHeight, playheadColor);

        graphTexture.SetPixels32(graphPixels);
        graphTexture.Apply(false);
    }

    private void FillColumns(int x0, int x1, int y0, int y1, Color32 color)
    {
        x0 = Mathf.Clamp(x0, 0, GraphWidth - 1);
        x1 = Mathf.Clamp(x1, 0, GraphWidth - 1);
        y0 = Mathf.Clamp(y0, 0, GraphHeight);
        y1 = Mathf.Clamp(y1, 0, GraphHeight);
        for (int y = y0; y < y1; y++)
        {
            int row = y * GraphWidth;
            for (int x = x0; x <= x1; x++)
                graphPixels[row + x] = color;
        }
    }

    private void FillRow(int y, Color32 color)
    {
        if (y < 0 || y >= GraphHeight) return;
        int row = y * GraphWidth;
        for (int x = 0; x < GraphWidth; x++)
            graphPixels[row + x] = color;
    }

    private void Report(string message, bool warning = false)
    {
        if (report != null) report(message, warning);
        else if (warning) Debug.LogWarning($"[AirCannon] {message}");
        else Debug.Log($"[AirCannon] {message}");
    }
}
