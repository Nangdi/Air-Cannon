using System.Collections.Generic;
using UnityEngine;

// 링 버퍼에서 잘라낸 1회분 리플레이 프레임.
// RenderTexture는 재사용 풀로 두고, 다음 캡처 때 덮어쓴다.
public class ReplayClip
{
    private const float FallbackFrameInterval = 1f / 30f;

    private readonly List<RenderTexture> pool = new List<RenderTexture>();
    private readonly List<float> times = new List<float>(); // 첫 프레임 기준 상대 시각(초)
    private double baseTime;

    public int Count => times.Count;
    // 마지막 프레임이 보여지는 시간까지 포함한 클립 길이(초)
    public float Length { get; private set; }

    public RenderTexture GetFrame(int index) => pool[index];
    public float GetTime(int index) => times[index];

    public void Clear()
    {
        times.Clear();
        Length = 0f;
    }

    public void Add(Texture source, double absoluteTime)
    {
        int index = times.Count;
        if (index == 0) baseTime = absoluteTime;

        RenderTexture target = EnsurePoolFrame(index, source.width, source.height);
        Graphics.Blit(source, target);
        times.Add((float)(absoluteTime - baseTime));
    }

    public void Finish()
    {
        if (times.Count == 0)
        {
            Length = 0f;
            return;
        }

        float last = times[times.Count - 1];
        float interval = times.Count > 1 ? last / (times.Count - 1) : FallbackFrameInterval;
        Length = last + Mathf.Max(interval, 0.001f);
    }

    public void Release()
    {
        for (int i = 0; i < pool.Count; i++)
        {
            if (pool[i] != null)
            {
                pool[i].Release();
                Object.Destroy(pool[i]);
            }
        }
        pool.Clear();
        Clear();
    }

    private RenderTexture EnsurePoolFrame(int index, int width, int height)
    {
        while (pool.Count <= index)
            pool.Add(null);

        RenderTexture rt = pool[index];
        if (rt != null && (rt.width != width || rt.height != height))
        {
            rt.Release();
            Object.Destroy(rt);
            rt = null;
        }

        if (rt == null)
        {
            rt = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32) { name = $"ReplayClip_{index:000}" };
            rt.Create();
            pool[index] = rt;
        }
        return rt;
    }
}
