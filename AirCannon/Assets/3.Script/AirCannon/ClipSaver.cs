using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

// 리플레이 클립을 폴더 하나에 JPG 연속 이미지(frame_000.jpg ...)와 clip.json(프레임 시각)으로 저장한다.
// GPU 읽기는 비동기, JPG 인코딩과 파일 쓰기는 백그라운드 스레드에서 처리해 화면이 멈추지 않게 한다.
public class ClipSaver : MonoBehaviour
{
    [Serializable]
    private class ClipInfo
    {
        public string savedAt;
        public int width;
        public int height;
        public List<float> frameTimes = new List<float>();
    }

    public void Save(ReplayClip clip, string rootFolder, int jpgQuality)
    {
        if (clip == null || clip.Count == 0) return;

        if (!SystemInfo.supportsAsyncGPUReadback)
        {
            Debug.LogWarning("[AirCannon] 이 GPU는 AsyncGPUReadback을 지원하지 않음 - 클립 저장 건너뜀");
            return;
        }

        string folder = Path.Combine(rootFolder, DateTime.Now.ToString("yyyyMMdd_HHmmss_fff"));
        try
        {
            Directory.CreateDirectory(folder);

            var info = new ClipInfo
            {
                savedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                width = clip.GetFrame(0).width,
                height = clip.GetFrame(0).height,
            };
            for (int i = 0; i < clip.Count; i++)
                info.frameTimes.Add(clip.GetTime(i));
            File.WriteAllText(Path.Combine(folder, "clip.json"), JsonUtility.ToJson(info, true));
        }
        catch (Exception ex)
        {
            Debug.LogError($"[AirCannon] 클립 저장 폴더 생성 실패 ({folder}): {ex.Message}");
            return;
        }

        // 요청 시점의 내용이 읽히므로, 이후 클립 풀이 덮어써져도 저장 내용은 바뀌지 않는다.
        for (int i = 0; i < clip.Count; i++)
        {
            RenderTexture rt = clip.GetFrame(i);
            string path = Path.Combine(folder, $"frame_{i:000}.jpg");
            int width = rt.width;
            int height = rt.height;
            AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32, request => OnReadback(request, path, width, height, jpgQuality));
        }

        Debug.Log($"[AirCannon] 클립 저장 시작: {folder} ({clip.Count}프레임)");
    }

    private static void OnReadback(AsyncGPUReadbackRequest request, string path, int width, int height, int quality)
    {
        if (request.hasError)
        {
            Debug.LogError($"[AirCannon] 프레임 읽기 실패: {path}");
            return;
        }

        byte[] pixels = request.GetData<byte>().ToArray();
        Task.Run(() =>
        {
            try
            {
                byte[] jpg = ImageConversion.EncodeArrayToJPG(pixels, GraphicsFormat.R8G8B8A8_UNorm, (uint)width, (uint)height, 0, quality);
                File.WriteAllBytes(path, jpg);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AirCannon] 프레임 저장 실패 ({path}): {ex.Message}");
            }
        });
    }
}
