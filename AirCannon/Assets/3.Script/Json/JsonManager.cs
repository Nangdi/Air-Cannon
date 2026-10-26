using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// 설정창(SettingsPanelUI)에 노출되는 런타임 설정.
// public bool 필드를 추가하면 설정창에서 토글로 다룰 수 있다.
[Serializable]
public class GameSettingData
{
    // Unity 빌드 창을 항상 맨 위로 띄울지 여부.
    public bool useUnityOnTop;
    // 마우스 커서 표시 여부. false면 커서를 숨긴다(키오스크/전시용).
    public bool showMouseCursor;
}

// 프로젝트별로 실행 중에 바뀌는 데이터를 담는 곳 (Setting.json).
[Serializable]
public class GameDynamicData
{
}

[Serializable]
public class PortConfig
{
    public int controllerId;
    public string com;
    public int baudLate;
    // RS232 사용 여부. false면 해당 컨트롤러는 포트를 열지 않는다.
    public bool enabled = true;
}

[Serializable]
public class PortJson
{
    // 컨트롤러별 전용 포트 목록. 필요한 만큼 port.json 에서 늘려 쓴다.
    public List<PortConfig> ports = new List<PortConfig>
    {
        new PortConfig { controllerId = 1, com = "COM1", baudLate = 19200, enabled = false },
    };
}

[Serializable]
public class TcpJson
{
    // 메인 서버 접속 주소와 포트
    public string host = "127.0.0.1";
    public int port = 5000;
    // 끊겼을 때 재시도 간격(초). 0 이하이면 재연결 시도하지 않음.
    public float reconnectIntervalSeconds = 3f;
    // 송신 메시지 끝에 줄바꿈을 자동으로 붙일지 여부
    public bool appendOutgoingLineEnding = true;
}

public class JsonManager : MonoBehaviour
{
    public static JsonManager instance;
    public GameSettingData gameSettingData = new GameSettingData();
    public PortJson portJson = new PortJson();
    public TcpJson tcpJson = new TcpJson();
    public GameDynamicData gameDynamicData = new GameDynamicData();

    private string gameDataPath;
    private string gameDynamicDataPath;
    private string portPath;
    private string tcpPath;

    public string GameDataPath => gameDataPath;

    // 싱글톤을 초기화하고 JSON 파일에서 런타임 설정 데이터를 불러옵니다.
    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (instance != this)
        {
            Destroy(gameObject);
            return;
        }

        // 운영 설정은 모두 persistentDataPath에서 읽고 쓴다. StreamingAssets에는 기본값 원본만 둔다.
        portPath = PreparePersistentFile("port.json");
        tcpPath = PreparePersistentFile("tcp.json");
        gameDynamicDataPath = PreparePersistentFile("Setting.json");
        gameDataPath = PreparePersistentFile("gameSettingData.json");

        gameSettingData ??= new GameSettingData();
        gameDynamicData ??= new GameDynamicData();
        portJson ??= new PortJson();
        tcpJson ??= new TcpJson();

        gameSettingData = LoadData(gameDataPath, gameSettingData);
        gameDynamicData = LoadData(gameDynamicDataPath, gameDynamicData);
        portJson = LoadData(portPath, portJson);
        tcpJson = LoadData(tcpPath, tcpJson);
    }

    // 현재 게임 설정 데이터를 gameSettingData.json 파일에 저장합니다.
    public void SaveGameSettingData()
    {
        SaveData(gameSettingData, gameDataPath);
    }

    // 디버그 패널에서 바꾼 포트 설정을 port.json 에 저장합니다.
    public void SavePortJson()
    {
        try { SaveData(portJson, portPath); }
        catch (Exception ex) { Debug.LogError($"port.json 저장 실패: {ex.Message}"); }
    }

    // persistentDataPath 쪽 파일 경로를 돌려준다. 파일이 없을 때만 StreamingAssets 원본을 복사해 오고,
    // 이미 있으면 덮어쓰지 않는다 (빌드를 새로 덮어써도 현장 설정이 유지되도록).
    public static string PreparePersistentFile(string fileName)
    {
        string persistentPath = Path.Combine(Application.persistentDataPath, fileName);
        if (File.Exists(persistentPath)) return persistentPath;

        string defaultPath = Path.Combine(Application.streamingAssetsPath, fileName);
        try
        {
            if (File.Exists(defaultPath))
            {
                Directory.CreateDirectory(Application.persistentDataPath);
                File.Copy(defaultPath, persistentPath);
                Debug.Log($"기본 설정 복사: {defaultPath} -> {persistentPath}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"기본 설정 복사 실패 ({fileName}): {ex.Message}");
        }
        return persistentPath;
    }

    // 지정한 경로에 JSON 파일을 생성하거나 덮어씁니다.
    public static void SaveData<T>(T jsonObject, string path) where T : new()
    {
        if (jsonObject == null)
            jsonObject = new T();

        string directoryPath = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directoryPath) && !Directory.Exists(directoryPath))
            Directory.CreateDirectory(directoryPath);

        string json = JsonUtility.ToJson(jsonObject, true);
        File.WriteAllText(path, json);
        Debug.Log($"Saved JSON: {path}");
    }

    // JSON 파일을 읽고, 파일이 없으면 기본값으로 새 파일을 만듭니다.
    // FromJsonOverwrite를 사용해 json에 없는 필드는 인스턴스 기본값을 유지한다.
    // (예: 기존 port.json에 enabled 필드가 없어도 PortConfig.enabled = true 가 보존됨)
    // 파일이 깨져 있으면 기본값으로 동작하고 로그만 남긴다 (파일은 건드리지 않음).
    public static T LoadData<T>(string path, T data) where T : new()
    {
        if (data == null) data = new T();

        try
        {
            if (!File.Exists(path))
            {
                Debug.LogWarning($"JSON file does not exist. Creating a new file: {path}");
                SaveData(data, path);
                return data;
            }

            string json = File.ReadAllText(path);
            JsonUtility.FromJsonOverwrite(json, data);
            Debug.Log($"Loaded JSON: {path}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"JSON 읽기 실패 - 기본값 사용 ({path}): {ex.Message}");
            data = new T();
        }
        return data;
    }
}
