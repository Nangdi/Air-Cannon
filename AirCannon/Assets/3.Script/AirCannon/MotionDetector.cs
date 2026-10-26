using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

// 웹캠 프레임을 작게 줄여 직전 프레임과의 밝기 차이(움직임 점수)를 계산하고 최근 기록을 보관한다.
// GPU 읽기는 비동기라 점수는 1~3프레임 늦게 들어오지만, 각 점수에는 원래 프레임 시각이 붙는다.
// 점수 = 감지 영역 픽셀들의 평균 밝기 차이 x 100 (0 ~ 100)
public class MotionDetector
{
    private const int Width = 64;
    private const int Height = 36;
    private const int HistoryCapacity = 1024; // 30fps 기준 약 30초

    private readonly double[] times = new double[HistoryCapacity];
    private readonly float[] scores = new float[HistoryCapacity];
    private int head;
    private int count;

    private RenderTexture small;
    private float[] prevLuma;
    private bool hasPrev;
    private int generation; // Reset 이전에 요청한 읽기 결과는 버린다
    private bool disposed;

    public bool IsAvailable => SystemInfo.supportsAsyncGPUReadback;
    public double LatestTime => count > 0 ? times[(head - 1 + HistoryCapacity) % HistoryCapacity] : double.MinValue;

    // area: 감지 영역 (0~1, 왼쪽 위 기준)
    public void Submit(Texture frame, double time, MotionArea area)
    {
        if (disposed || !IsAvailable) return;

        if (small == null)
        {
            small = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGB32) { name = "MotionSmall" };
            small.Create();
        }

        Graphics.Blit(frame, small);
        int requestGeneration = generation;
        MotionArea areaCopy = area;
        AsyncGPUReadback.Request(small, 0, TextureFormat.RGBA32, request =>
        {
            if (disposed || requestGeneration != generation) return;
            if (request.hasError) return;
            OnReadback(request.GetData<byte>(), time, areaCopy);
        });
    }

    // 웹캠이 다시 연결되거나 해상도가 바뀌면 직전 프레임 비교를 끊는다.
    public void Reset()
    {
        generation++;
        hasPrev = false;
    }

    public void Dispose()
    {
        disposed = true;
        if (small != null)
        {
            small.Release();
            Object.Destroy(small);
            small = null;
        }
    }

    // [from, to] 구간에서 점수가 threshold 이상인 첫 프레임 시각
    public bool TryFindOnset(double from, double to, float threshold, out double onset)
    {
        int oldest = (head - count + HistoryCapacity) % HistoryCapacity;
        for (int i = 0; i < count; i++)
        {
            int index = (oldest + i) % HistoryCapacity;
            double t = times[index];
            if (t < from) continue;
            if (t > to) break;
            if (scores[index] >= threshold)
            {
                onset = t;
                return true;
            }
        }
        onset = 0;
        return false;
    }

    public void GetSamples(double from, double to, List<double> outTimes, List<float> outScores)
    {
        outTimes.Clear();
        outScores.Clear();
        int oldest = (head - count + HistoryCapacity) % HistoryCapacity;
        for (int i = 0; i < count; i++)
        {
            int index = (oldest + i) % HistoryCapacity;
            double t = times[index];
            if (t < from) continue;
            if (t > to) break;
            outTimes.Add(t);
            outScores.Add(scores[index]);
        }
    }

    private void OnReadback(NativeArray<byte> data, double time, MotionArea area)
    {
        if (data.Length < Width * Height * 4) return;

        prevLuma ??= new float[Width * Height];

        // 읽어 온 행 0은 화면 아래쪽이다. area는 위쪽 기준이므로 뒤집어서 범위를 구한다.
        int x0 = Mathf.Clamp(Mathf.FloorToInt(area.x * Width), 0, Width - 1);
        int x1 = Mathf.Clamp(Mathf.CeilToInt((area.x + area.width) * Width), x0 + 1, Width);
        int top = Mathf.Clamp(Mathf.FloorToInt(area.y * Height), 0, Height - 1);
        int bottom = Mathf.Clamp(Mathf.CeilToInt((area.y + area.height) * Height), top + 1, Height);
        int row0 = Height - bottom;
        int row1 = Height - top;

        float sum = 0f;
        int pixels = 0;
        for (int y = 0; y < Height; y++)
        {
            bool rowInArea = y >= row0 && y < row1;
            for (int x = 0; x < Width; x++)
            {
                int p = y * Width + x;
                int b = p * 4;
                float luma = (data[b] * 0.299f + data[b + 1] * 0.587f + data[b + 2] * 0.114f) / 255f;
                if (hasPrev && rowInArea && x >= x0 && x < x1)
                {
                    sum += Mathf.Abs(luma - prevLuma[p]);
                    pixels++;
                }
                prevLuma[p] = luma;
            }
        }

        float score = hasPrev && pixels > 0 ? sum / pixels * 100f : 0f;
        hasPrev = true;

        times[head] = time;
        scores[head] = score;
        head = (head + 1) % HistoryCapacity;
        if (count < HistoryCapacity) count++;
    }
}
