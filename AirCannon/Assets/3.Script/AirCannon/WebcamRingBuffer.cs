using System.Collections.Generic;
using UnityEngine;

// 웹캠 영상을 최근 몇 초만큼 계속 덮어쓰며 GPU 메모리에 보관한다.
// 웹캠이 없거나 끊기면 일정 간격으로 다시 연결을 시도한다.
// 보정 모드에서는 해상도를 낮추는 대신 더 긴 시간을 보관한다.
// 복사할 때 cameraRotation만큼 돌려서 저장한다 (90/270이면 가로 웹캠 영상이 세로 프레임이 된다).
public class WebcamRingBuffer : MonoBehaviour
{
    private const float RetryIntervalSeconds = 3f;
    private const float StallTimeoutSeconds = 5f;   // 이 시간 동안 새 프레임이 없으면 끊긴 것으로 본다
    private const float BufferMarginSeconds = 0.5f; // 캡처 대기 시간 여유분
    private const float FpsHeadroom = 1.25f;        // 요청보다 빠르게 들어오는 카메라 대비
    private const float CalibrationScale = 0.5f;    // 보정 모드 해상도 배율
    private const string RotateShaderPath = "Shaders/WebcamRotateBlit"; // Resources 기준

    private AirCannonConfig config;
    private WebCamTexture webcam;
    private Material rotateMaterial;
    private RenderTexture[] frames;
    private double[] times;
    private int normalCapacity;
    private int capacity;
    private float resolutionScale = 1f;
    private int head;  // 다음에 쓸 위치
    private int count;
    private float retryTimer;
    private double lastFrameTime;
    private bool loggedDevices;
    // 마지막으로 웹캠에 요청한 값. 설정창에서 바뀌면 웹캠을 다시 연다.
    private int requestedWidth, requestedHeight, requestedFps;
    private readonly MotionDetector motion = new MotionDetector();

    public bool IsRunning => webcam != null && webcam.isPlaying && count > 0;
    public double LatestFrameTime => count > 0 ? times[(head - 1 + capacity) % capacity] : double.MinValue;
    public RenderTexture LatestFrame => count > 0 ? frames[(head - 1 + capacity) % capacity] : null;
    public MotionDetector Motion => motion;
    public bool IsCalibrationMode { get; private set; }
    // 멈춘 동안은 새 프레임을 쓰지 않는다. 보정 화면이 버퍼 안의 프레임을 직접 보여 줄 때 쓴다.
    public bool Frozen { get; set; }

    public void Begin(AirCannonConfig config)
    {
        this.config = config;

        // 캡처는 구간이 끝나는 즉시 하므로 구간 길이 + 여유분만 보관하면 된다.
        normalCapacity = CapacityFor(config.durationSeconds + BufferMarginSeconds);
        capacity = normalCapacity;

        TryStartWebcam();
    }

    // 보정 모드: seconds만큼 보관할 수 있게 버퍼를 늘리고 해상도를 낮춘다. 버퍼 내용은 비워진다.
    // 끌 때는 보정에서 바뀌었을 수 있는 durationSeconds로 평소 크기를 다시 계산한다.
    public void SetCalibrationMode(bool on, float seconds)
    {
        IsCalibrationMode = on;
        Frozen = false;
        if (!on) normalCapacity = CapacityFor(config.durationSeconds + BufferMarginSeconds);
        capacity = on ? CapacityFor(seconds + BufferMarginSeconds) : normalCapacity;
        resolutionScale = on ? CalibrationScale : 1f;
        ReleaseFrames();
        head = 0;
        count = 0;
        motion.Reset();
    }

    // 설정창에서 durationSeconds가 바뀌면 평소 버퍼 크기를 다시 맞춘다. 크기가 바뀌면 버퍼 내용은 비워진다.
    public void RefreshCapacity()
    {
        if (config == null || IsCalibrationMode) return;

        int wanted = CapacityFor(config.durationSeconds + BufferMarginSeconds);
        if (wanted == normalCapacity) return;

        normalCapacity = wanted;
        capacity = wanted;
        ReleaseFrames();
        head = 0;
        count = 0;
        motion.Reset();
    }

    // 설정창에서 카메라 해상도/fps가 바뀌었으면 웹캠을 다시 연다.
    public void RestartIfCameraChanged()
    {
        if (config == null) return;
        if (config.cameraWidth == requestedWidth && config.cameraHeight == requestedHeight && config.cameraFps == requestedFps)
            return;

        if (!IsCalibrationMode)
        {
            // fps가 바뀌면 같은 시간을 담는 데 필요한 프레임 수도 바뀐다.
            normalCapacity = CapacityFor(config.durationSeconds + BufferMarginSeconds);
            capacity = normalCapacity;
        }
        ReleaseFrames();
        TryStartWebcam();
    }

    private int CapacityFor(float seconds)
    {
        float fps = Mathf.Max(config.cameraFps, 30) * FpsHeadroom;
        return Mathf.CeilToInt(seconds * fps);
    }

    private void Update()
    {
        if (config == null) return;

        if (webcam == null || !webcam.isPlaying || IsStalled())
        {
            retryTimer -= Time.unscaledDeltaTime;
            if (retryTimer <= 0f)
                TryStartWebcam();
            return;
        }

        if (!webcam.didUpdateThisFrame) return;
        if (webcam.width <= 16 || webcam.height <= 16) return; // 첫 프레임 전에는 16x16으로 보고됨

        if (Frozen)
        {
            lastFrameTime = Time.realtimeSinceStartupAsDouble; // 멈춘 동안 끊김으로 오인하지 않게
            return;
        }

        int quarterTurns = config.cameraRotation / 90;
        bool sideways = quarterTurns % 2 == 1;
        int width = Mathf.Max(16, Mathf.RoundToInt(webcam.width * resolutionScale));
        int height = Mathf.Max(16, Mathf.RoundToInt(webcam.height * resolutionScale));
        EnsureFrames(sideways ? height : width, sideways ? width : height);

        if (quarterTurns != 0 && GetRotateMaterial() is Material material)
        {
            material.SetFloat("_Rotation", quarterTurns);
            material.SetFloat("_MirrorX", config.mirrorHorizontal ? 1f : 0f);
            material.SetFloat("_FlipSourceY", webcam.videoVerticallyMirrored ? 1f : 0f);
            Graphics.Blit(webcam, frames[head], material);
        }
        else
        {
            Vector2 scale = new Vector2(config.mirrorHorizontal ? -1f : 1f, webcam.videoVerticallyMirrored ? -1f : 1f);
            Vector2 offset = new Vector2(config.mirrorHorizontal ? 1f : 0f, webcam.videoVerticallyMirrored ? 1f : 0f);
            Graphics.Blit(webcam, frames[head], scale, offset);
        }

        lastFrameTime = Time.realtimeSinceStartupAsDouble;
        times[head] = lastFrameTime;
        motion.Submit(frames[head], lastFrameTime, config.motionArea);
        head = (head + 1) % capacity;
        if (count < capacity) count++;
    }

    // [start, end] 구간 프레임을 오래된 순서대로 clip에 복사하고 복사한 개수를 돌려준다.
    public int CopyRange(double start, double end, ReplayClip clip)
    {
        clip.Clear();
        if (count == 0) return 0;

        int oldest = (head - count + capacity) % capacity;
        for (int i = 0; i < count; i++)
        {
            int index = (oldest + i) % capacity;
            double t = times[index];
            if (t < start) continue;
            if (t > end) break;
            clip.Add(frames[index], t);
        }

        clip.Finish();
        return clip.Count;
    }

    // [start, end] 구간 프레임을 복사하지 않고 그대로 넘긴다. Frozen 상태에서만 내용이 유지된다.
    public int CollectRange(double start, double end, List<RenderTexture> outFrames, List<double> outTimes)
    {
        outFrames.Clear();
        outTimes.Clear();
        if (count == 0) return 0;

        int oldest = (head - count + capacity) % capacity;
        for (int i = 0; i < count; i++)
        {
            int index = (oldest + i) % capacity;
            double t = times[index];
            if (t < start) continue;
            if (t > end) break;
            outFrames.Add(frames[index]);
            outTimes.Add(t);
        }
        return outFrames.Count;
    }

    // 셰이더를 못 찾으면 null - 회전 없이 복사한다 (프레임 크기는 회전 기준이라 늘어나 보인다).
    private Material GetRotateMaterial()
    {
        if (rotateMaterial != null) return rotateMaterial;

        Shader shader = Resources.Load<Shader>(RotateShaderPath);
        if (shader == null || !shader.isSupported)
        {
            Debug.LogError($"[AirCannon] 회전 셰이더 없음 (Resources/{RotateShaderPath}) - 회전 없이 복사");
            config.cameraRotation = 0;
            return null;
        }
        rotateMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        return rotateMaterial;
    }

    private bool IsStalled()
    {
        return count > 0 && Time.realtimeSinceStartupAsDouble - lastFrameTime > StallTimeoutSeconds;
    }

    private void TryStartWebcam()
    {
        retryTimer = RetryIntervalSeconds;
        StopWebcam();

        WebCamDevice[] devices = WebCamTexture.devices;
        if (devices.Length == 0)
        {
            Debug.LogWarning($"[AirCannon] 웹캠을 찾지 못함 - {RetryIntervalSeconds}초 후 재시도");
            return;
        }

        if (!loggedDevices)
        {
            loggedDevices = true;
            for (int i = 0; i < devices.Length; i++)
                Debug.Log($"[AirCannon] 웹캠 [{i}] {devices[i].name}");
        }

        string deviceName = devices[0].name;
        if (!string.IsNullOrWhiteSpace(config.cameraName))
        {
            bool found = false;
            for (int i = 0; i < devices.Length; i++)
            {
                if (devices[i].name.IndexOf(config.cameraName, System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    deviceName = devices[i].name;
                    found = true;
                    break;
                }
            }
            if (!found)
                Debug.LogWarning($"[AirCannon] '{config.cameraName}' 웹캠 없음 - 첫 번째 웹캠({deviceName}) 사용");
        }

        try
        {
            requestedWidth = config.cameraWidth;
            requestedHeight = config.cameraHeight;
            requestedFps = config.cameraFps;
            webcam = new WebCamTexture(deviceName, config.cameraWidth, config.cameraHeight, config.cameraFps);
            webcam.Play();
            lastFrameTime = Time.realtimeSinceStartupAsDouble;
            Debug.Log($"[AirCannon] 웹캠 시작: {deviceName} (요청 {config.cameraWidth}x{config.cameraHeight}@{config.cameraFps})");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[AirCannon] 웹캠 시작 실패 ({deviceName}): {ex.Message}");
            StopWebcam();
        }
    }

    private void StopWebcam()
    {
        if (webcam != null)
        {
            if (webcam.isPlaying) webcam.Stop();
            Destroy(webcam);
            webcam = null;
        }
        count = 0;
        head = 0;
        motion.Reset();
    }

    private void EnsureFrames(int width, int height)
    {
        if (frames != null && frames.Length == capacity && frames[0].width == width && frames[0].height == height)
            return;

        ReleaseFrames();
        frames = new RenderTexture[capacity];
        times = new double[capacity];
        for (int i = 0; i < capacity; i++)
        {
            frames[i] = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32) { name = $"WebcamRing_{i:000}" };
            frames[i].Create();
        }
        head = 0;
        count = 0;
        motion.Reset();

        float mb = width * height * 4f * capacity / (1024f * 1024f);
        Debug.Log($"[AirCannon] 링 버퍼 {capacity}프레임 할당 ({width}x{height}, 약 {mb:F0}MB)");
    }

    private void ReleaseFrames()
    {
        if (frames == null) return;
        for (int i = 0; i < frames.Length; i++)
        {
            if (frames[i] != null)
            {
                frames[i].Release();
                Destroy(frames[i]);
            }
        }
        frames = null;
    }

    private void OnDestroy()
    {
        StopWebcam();
        ReleaseFrames();
        motion.Dispose();
        if (rotateMaterial != null) Destroy(rotateMaterial);
    }
}
