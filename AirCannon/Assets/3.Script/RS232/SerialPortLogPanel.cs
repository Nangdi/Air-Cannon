using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 게임씬에 미리 만들어진 Canvas/UI 위에서 동작하는 로그 패널.
// Inspector에서 텍스트/스크롤뷰/버튼을 직접 연결한다.
// 설정창(SettingsPanelUI)이 있으면 설정창의 열림/닫힘을 그대로 따라간다 (같은 ESC 키).
public class SerialPortLogPanel : MonoBehaviour
{
    private const string DisabledOption = "사용 안 함";
    private const string MissingSuffix = " (연결 안 됨)";

    [Header("연결 상태 텍스트 (인덱스 0~4 = Ctrl 1~5)")]
    [SerializeField] private TMP_Text[] connectionStatusTexts;

    [Header("포트 선택 드롭다운 (인덱스 0~4 = Ctrl 1~5)")]
    [SerializeField] private TMP_Dropdown[] portDropdowns;
    [Tooltip("COM 포트 목록을 새로 읽고 모든 포트를 다시 연다")]
    [SerializeField] private Button reconnectButton;

    [Header("로그 표시")]
    [SerializeField] private TMP_Text logText;
    [SerializeField] private ScrollRect logScrollRect;

    [Header("버튼")]
    [SerializeField] private Button clearButton;

    [Header("수동 송신 (열린 모든 포트로 보냄, 비워두면 표시하지 않음)")]
    [SerializeField] private TMP_InputField sendMessageInput;
    [SerializeField] private Button sendButton;

    [Header("표시/숨김")]
    [SerializeField] private GameObject togglePanel;
    [Tooltip("비워두면 자동 탐색. 있으면 설정창과 함께 열고 닫는다.")]
    [SerializeField] private SettingsPanelUI settingsPanel;
    [Tooltip("설정창이 없을 때만 쓰는 키")]
    [SerializeField] private KeyCode toggleKey = KeyCode.Escape;
    [Tooltip("시작할 때 로그 창을 숨긴 상태로 둘지 여부.")]
    [SerializeField] private bool hideOnStart = true;

    [Header("동작 설정")]
    [SerializeField] private int maxLogLines = 200;

    [Header("상태 색상")]
    [SerializeField] private Color openColor = new Color(0.4f, 1f, 0.4f);
    [SerializeField] private Color closeColor = new Color(1f, 0.4f, 0.4f);
    [SerializeField] private Color disabledColor = new Color(0.7f, 0.7f, 0.7f);

    private readonly Queue<string> logLines = new Queue<string>();
    private readonly StringBuilder builder = new StringBuilder();
    // 드롭다운 항목별 COM 이름 (null = 사용 안 함). 인덱스는 portDropdowns와 같다.
    private readonly List<List<string>> dropdownValues = new List<List<string>>();

    private SerialPortManager manager;
    private bool subscribed;
    private bool syncingDropdowns;

    private void OnEnable()
    {
        if (settingsPanel == null)
            HotkeyRegistry.Register(this, toggleKey.ToString(), "RS232 디버그 패널 열기/닫기", "모든 화면");
    }

    private void OnDisable()
    {
        HotkeyRegistry.Unregister(this);
    }

    private void Awake()
    {
        if (settingsPanel == null)
            settingsPanel = FindObjectOfType<SettingsPanelUI>(true);
    }

    private void Start()
    {
        if (clearButton != null) clearButton.onClick.AddListener(ClearLog);
        if (sendButton != null) sendButton.onClick.AddListener(SendManual);
        if (sendMessageInput != null) sendMessageInput.onSubmit.AddListener(OnSendSubmit);
        if (reconnectButton != null) reconnectButton.onClick.AddListener(Reconnect);
        BindDropdowns();
        TrySubscribe();
        RefreshAllStatus();
        RefreshPortDropdowns();

        if (settingsPanel != null)
        {
            settingsPanel.VisibilityChanged += OnSettingsVisibilityChanged;
            if (togglePanel != null) togglePanel.SetActive(settingsPanel.IsVisible);
        }
        else if (hideOnStart && togglePanel != null)
        {
            togglePanel.SetActive(false);
        }
    }

    private void Update()
    {
        // SerialPortManager.Instance가 늦게 만들어지는 경우 대비
        if (!subscribed) TrySubscribe();

        if (settingsPanel == null && Input.GetKeyDown(toggleKey) && togglePanel != null)
            SetVisible(!togglePanel.activeSelf);
    }

    private void OnDestroy()
    {
        if (clearButton != null) clearButton.onClick.RemoveListener(ClearLog);
        if (sendButton != null) sendButton.onClick.RemoveListener(SendManual);
        if (sendMessageInput != null) sendMessageInput.onSubmit.RemoveListener(OnSendSubmit);
        if (reconnectButton != null) reconnectButton.onClick.RemoveListener(Reconnect);
        if (settingsPanel != null) settingsPanel.VisibilityChanged -= OnSettingsVisibilityChanged;
        if (!subscribed || manager == null) return;
        manager.OnDataReceived -= HandleReceived;
        manager.OnDataSent -= HandleSent;
        manager.OnConnectionChanged -= HandleConnectionChanged;
        subscribed = false;
    }

    private void OnSettingsVisibilityChanged(bool visible) => SetVisible(visible);

    private void SetVisible(bool visible)
    {
        if (togglePanel == null) return;
        togglePanel.SetActive(visible);
        if (visible) RefreshPortDropdowns(); // 열 때 지금 PC의 COM 목록으로 갱신
    }

    private void TrySubscribe()
    {
        if (subscribed) return;
        manager = SerialPortManager.Instance;
        if (manager == null) return;

        manager.OnDataReceived += HandleReceived;
        manager.OnDataSent += HandleSent;
        manager.OnConnectionChanged += HandleConnectionChanged;
        subscribed = true;
        RefreshAllStatus();
    }

    private void HandleReceived(int id, string data) => AppendLog($"[Receive][Ctrl {id}] {data}");
    private void HandleSent(int id, string data) => AppendLog($"[Send][Ctrl {id}] {data}");

    private void HandleConnectionChanged(int id, bool isOpen)
    {
        UpdateStatus(id, isOpen, IsEnabled(id));
        AppendLog($"[Connect][Ctrl {id}] {(isOpen ? "OPEN" : "CLOSE")}");
    }

    private static PortConfig FindConfig(int controllerId)
    {
        var ports = JsonManager.instance != null ? JsonManager.instance.portJson?.ports : null;
        return ports?.Find(p => p.controllerId == controllerId);
    }

    private bool IsEnabled(int controllerId)
    {
        var cfg = FindConfig(controllerId);
        return cfg == null || cfg.enabled;
    }

    private void RefreshAllStatus()
    {
        var portJson = JsonManager.instance != null ? JsonManager.instance.portJson : null;
        if (portJson == null || portJson.ports == null) return;

        foreach (var cfg in portJson.ports)
        {
            bool open = manager != null && manager.IsControllerOpen(cfg.controllerId);
            UpdateStatus(cfg.controllerId, open, cfg.enabled);
        }
    }

    private void UpdateStatus(int controllerId, bool isOpen, bool enabled)
    {
        if (connectionStatusTexts == null) return;
        int idx = controllerId - 1;
        if (idx < 0 || idx >= connectionStatusTexts.Length) return;
        var t = connectionStatusTexts[idx];
        if (t == null) return;

        var cfg = FindConfig(controllerId);
        string com = cfg != null ? cfg.com : "";
        int baud = cfg != null ? cfg.baudLate : 0;

        string state;
        Color color;
        if (!enabled) { state = "DISABLED"; color = disabledColor; }
        else if (isOpen) { state = "OPEN"; color = openColor; }
        else { state = "CLOSE"; color = closeColor; }

        t.text = $"Ctrl {controllerId}  {com}  {baud}bps  [{state}]";
        t.color = color;
    }

    // ---------- 포트 선택 ----------

    private void BindDropdowns()
    {
        if (portDropdowns == null) return;
        for (int i = 0; i < portDropdowns.Length; i++)
        {
            dropdownValues.Add(new List<string>());
            if (portDropdowns[i] == null) continue;
            int controllerId = i + 1;
            portDropdowns[i].onValueChanged.AddListener(index => OnPortSelected(controllerId, index));
        }
    }

    // 지금 PC의 COM 포트 목록으로 드롭다운을 다시 채우고 저장된 값을 선택해 둔다.
    // 저장된 포트가 지금 PC에 없으면 "(연결 안 됨)"을 붙여 현재 값이 보이게 한다.
    private void RefreshPortDropdowns()
    {
        if (portDropdowns == null) return;

        string[] names;
        try { names = SerialPort.GetPortNames(); }
        catch (Exception ex)
        {
            AppendLog($"[Port] COM 목록 읽기 실패: {ex.Message}");
            names = new string[0];
        }
        Array.Sort(names, StringComparer.OrdinalIgnoreCase);

        syncingDropdowns = true;
        for (int i = 0; i < portDropdowns.Length; i++)
        {
            var dropdown = portDropdowns[i];
            if (dropdown == null) continue;

            var cfg = FindConfig(i + 1);
            dropdown.gameObject.SetActive(cfg != null);
            if (cfg == null) continue;

            var values = dropdownValues[i];
            var labels = new List<string>();
            values.Clear();
            values.Add(null);
            labels.Add(DisabledOption);
            int selected = 0;
            bool savedFound = false;
            foreach (string name in names)
            {
                if (cfg.enabled && string.Equals(name, cfg.com, StringComparison.OrdinalIgnoreCase))
                {
                    selected = values.Count;
                    savedFound = true;
                }
                values.Add(name);
                labels.Add(name);
            }
            if (cfg.enabled && !savedFound && !string.IsNullOrWhiteSpace(cfg.com))
            {
                selected = values.Count;
                values.Add(cfg.com);
                labels.Add(cfg.com + MissingSuffix);
            }

            dropdown.ClearOptions();
            dropdown.AddOptions(labels);
            dropdown.SetValueWithoutNotify(selected);
            dropdown.RefreshShownValue();
        }
        syncingDropdowns = false;
    }

    private void OnPortSelected(int controllerId, int index)
    {
        if (syncingDropdowns) return;
        var values = dropdownValues[controllerId - 1];
        if (index < 0 || index >= values.Count) return;

        string com = values[index];
        AppendLog($"[Port][Ctrl {controllerId}] {(com ?? DisabledOption)} 선택 - 저장 후 다시 연결");
        if (manager != null) manager.SetPort(controllerId, com);
        RefreshAllStatus();
        RefreshPortDropdowns();
    }

    // COM 목록을 새로 읽고 모든 포트를 다시 연다.
    public void Reconnect()
    {
        AppendLog("[Port] 재연결");
        if (manager != null) manager.ReconnectAll();
        RefreshAllStatus();
        RefreshPortDropdowns();
    }

    // ---------- 로그 ----------

    private void AppendLog(string line)
    {
        string formatted = $"{DateTime.Now:HH:mm:ss} {line}";
        logLines.Enqueue(formatted);
        while (logLines.Count > maxLogLines) logLines.Dequeue();

        builder.Clear();
        foreach (var l in logLines) builder.AppendLine(l);
        if (logText != null) logText.text = builder.ToString();

        // ContentSizeFitter 재계산 후, Content가 Viewport를 넘었을 때만 끝으로 스크롤.
        // 항상 0으로 강제하면 로그가 적어 Content가 Viewport보다 짧을 때 위로 튕겨 올라가는 현상이 생긴다.
        if (logScrollRect != null && logScrollRect.content != null && logScrollRect.viewport != null)
        {
            Canvas.ForceUpdateCanvases();
            if (logScrollRect.content.rect.height > logScrollRect.viewport.rect.height)
            {
                logScrollRect.verticalNormalizedPosition = 0f;
            }
        }
    }

    // 다른 기능이 수신 데이터 해석 결과 등을 패널에 남길 때 사용한다.
    public void Log(string line) => AppendLog(line);

    private void OnSendSubmit(string _) => SendManual();

    // 열린 모든 포트로 메시지를 직접 송신한다 (장비 응답 확인용).
    public void SendManual()
    {
        if (sendMessageInput == null) return;

        string message = sendMessageInput.text;
        if (string.IsNullOrWhiteSpace(message)) return;

        int sent = manager != null ? manager.SendAll(message) : 0; // 송신 로그는 OnDataSent로 남는다
        if (sent == 0)
            AppendLog($"[Send] 열린 포트가 없어 송신 안 함: {message}");
    }

    public void ClearLog()
    {
        logLines.Clear();
        if (logText != null) logText.text = string.Empty;
    }
}
