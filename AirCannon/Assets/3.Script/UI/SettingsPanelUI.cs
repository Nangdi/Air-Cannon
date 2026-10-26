using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// ESC 키로 열고 닫는 런타임 설정창.
// gameSettingData.json 의 bool 값을 토글로 노출하고, 바뀌면 즉시 적용 + 저장한다.
// UI는 런타임에 생성하지 않고 씬에 오브젝트로 만들어 인스펙터에서 연결한다.
//
// [bool 설정을 추가하는 방법]
//  1) JsonManager.GameSettingData 에 public bool 필드를 추가한다.
//  2) 씬의 설정창에 Toggle 오브젝트를 하나 만든다.
//  3) 아래 boolSettings 목록에 (필드 이름, Toggle)을 연결한다.
// 저장/복원은 리플렉션으로 처리되므로 이 스크립트를 고칠 필요가 없다.
public class SettingsPanelUI : MonoBehaviour
{
    // gameSettingData.json 의 bool 필드 1개와 씬의 Toggle 1개를 연결하는 항목.
    [Serializable]
    public class BoolSetting
    {
        [Tooltip("GameSettingData 안의 bool 필드 이름 (예: useUnityOnTop)")]
        public string fieldName;
        public Toggle toggle;

        // 런타임에만 쓰는 값들 (직렬화하지 않는다)
        [NonSerialized] public FieldInfo field;
        [NonSerialized] public UnityAction<bool> handler;
    }

    // 스타트팩이 기본으로 즉시 반영해 주는 설정 필드 이름
    private const string UseUnityOnTopField = "useUnityOnTop";
    private const string ShowMouseCursorField = "showMouseCursor";

    [Header("패널")]
    [Tooltip("ESC로 켜고 끌 설정창 루트 오브젝트. 이 스크립트는 항상 켜져 있는 오브젝트(캔버스 등)에 둔다.")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private KeyCode toggleKey = KeyCode.Escape;
    [Tooltip("시작할 때 설정창을 숨긴 상태로 둘지 여부.")]
    [SerializeField] private bool hideOnStart = true;

    [Header("설정 토글 (gameSettingData.json 의 bool 필드)")]
    [SerializeField] private List<BoolSetting> boolSettings = new List<BoolSetting>();

    [Header("적용 대상 (비워두면 자동 탐색)")]
    [SerializeField] private UnityAlwaysOnTop alwaysOnTop;

    // 설정이 바뀔 때 (필드 이름, 새 값)을 알린다. 프로젝트별 반영 처리는 여기에 붙인다.
    public event Action<string, bool> OnSettingChanged;
    // 설정창이 열리거나 닫힐 때 알린다. 디버그 패널(SerialPortLogPanel)이 이것을 따라 함께 열고 닫힌다.
    public event Action<bool> VisibilityChanged;

    public bool IsVisible => panelRoot != null && panelRoot.activeSelf;

    // 데이터 -> UI 반영 중에는 onValueChanged 콜백이 저장을 유발하지 않도록 막는다.
    private bool _syncing;

    private void OnEnable()
    {
        HotkeyRegistry.Register(this, toggleKey.ToString(), "설정창 + 디버그 패널 열기/닫기", "모든 화면");
    }

    private void OnDisable()
    {
        HotkeyRegistry.Unregister(this);
    }

    private void Start()
    {
        if (alwaysOnTop == null)
            alwaysOnTop = FindObjectOfType<UnityAlwaysOnTop>();

        BindFields();
        SyncFromData();

        foreach (var setting in boolSettings)
        {
            if (setting == null || setting.field == null || setting.toggle == null) continue;

            var captured = setting;                 // 클로저가 항목별로 따로 잡히도록 지역 변수에 담는다
            captured.handler = value => OnToggleChanged(captured, value);
            captured.toggle.onValueChanged.AddListener(captured.handler);
        }

        if (hideOnStart)
            SetPanelVisible(false);
    }

    private void OnDestroy()
    {
        foreach (var setting in boolSettings)
        {
            if (setting == null || setting.toggle == null || setting.handler == null) continue;

            setting.toggle.onValueChanged.RemoveListener(setting.handler);
            setting.handler = null;
        }
    }

    private void Update()
    {
        if (Input.GetKeyDown(toggleKey) && panelRoot != null)
            SetPanelVisible(!panelRoot.activeSelf);
    }

    // 인스펙터에 적은 필드 이름을 GameSettingData 의 bool 필드와 연결한다.
    // 오타·타입 불일치·연결 누락은 시작 시점에 로그로 알려준다.
    private void BindFields()
    {
        var type = typeof(GameSettingData);
        var bound = new HashSet<string>();

        foreach (var setting in boolSettings)
        {
            if (setting == null) continue;
            setting.field = null;

            if (string.IsNullOrEmpty(setting.fieldName))
            {
                Debug.LogWarning("[Settings] 필드 이름이 비어 있는 항목이 있습니다.", this);
                continue;
            }

            var field = type.GetField(setting.fieldName, BindingFlags.Public | BindingFlags.Instance);
            if (field == null)
            {
                Debug.LogError($"[Settings] GameSettingData 에 '{setting.fieldName}' 필드가 없습니다.", this);
                continue;
            }
            if (field.FieldType != typeof(bool))
            {
                Debug.LogError($"[Settings] '{setting.fieldName}' 은 bool 이 아니라 {field.FieldType.Name} 입니다. 토글로 다룰 수 없습니다.", this);
                continue;
            }
            if (setting.toggle == null)
            {
                Debug.LogWarning($"[Settings] '{setting.fieldName}' 에 연결된 Toggle 이 없습니다.", this);
                continue;
            }

            setting.field = field;
            bound.Add(field.Name);
        }

        // json 에는 있는데 설정창에 안 나오는 bool 필드를 알려준다.
        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (field.FieldType == typeof(bool) && !bound.Contains(field.Name))
                Debug.LogWarning($"[Settings] bool 필드 '{field.Name}' 에 연결된 토글이 없습니다. 씬에 Toggle 을 만들고 목록에 추가하세요.", this);
        }
    }

    // 설정창 표시/숨김 + 마우스 커서 연동.
    // 창이 보일 때는 조작할 수 있도록 커서를 강제로 표시하고,
    // 창이 닫히면 커서를 설정값(showMouseCursor)대로 되돌린다(숨김이면 같이 숨김).
    public void SetPanelVisible(bool show)
    {
        if (panelRoot != null)
            panelRoot.SetActive(show);

        if (show)
        {
            SyncFromData();          // 열 때 최신 json 값 반영
            Cursor.visible = true;   // 조작을 위해 커서 강제 표시
        }
        else
        {
            RestoreCursor();         // 설정값대로 복원
        }

        VisibilityChanged?.Invoke(show);
    }

    // 현재 showMouseCursor 설정값대로 커서 표시/숨김을 되돌린다.
    private void RestoreCursor()
    {
        bool show = GetValue(ShowMouseCursorField, true);
        if (alwaysOnTop != null) alwaysOnTop.ApplyMouseCursor(show);
        else Cursor.visible = show;
    }

    // 현재 gameSettingData -> 토글 UI 반영
    public void SyncFromData()
    {
        var data = JsonManager.instance != null ? JsonManager.instance.gameSettingData : null;
        if (data == null) return;

        _syncing = true;
        foreach (var setting in boolSettings)
        {
            if (setting == null || setting.field == null || setting.toggle == null) continue;
            setting.toggle.isOn = (bool)setting.field.GetValue(data);
        }
        _syncing = false;
    }

    // gameSettingData 의 bool 값 하나를 읽는다. 필드가 없거나 데이터가 없으면 fallback.
    public bool GetValue(string fieldName, bool fallback = false)
    {
        var data = JsonManager.instance != null ? JsonManager.instance.gameSettingData : null;
        if (data == null) return fallback;

        var field = typeof(GameSettingData).GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
        if (field == null || field.FieldType != typeof(bool)) return fallback;

        return (bool)field.GetValue(data);
    }

    private void OnToggleChanged(BoolSetting setting, bool value)
    {
        if (_syncing) return;

        var jm = JsonManager.instance;
        if (jm == null || setting.field == null) return;

        setting.field.SetValue(jm.gameSettingData, value);
        jm.SaveGameSettingData();               // json 파일에 실시간 저장

        Apply(setting.fieldName, value);
        OnSettingChanged?.Invoke(setting.fieldName, value);
    }

    // 스타트팩이 기본으로 제공하는 설정의 즉시 반영.
    // 프로젝트별 설정은 OnSettingChanged 이벤트를 구독해 처리한다.
    private void Apply(string fieldName, bool value)
    {
        if (fieldName == UseUnityOnTopField && alwaysOnTop != null)
            alwaysOnTop.ApplyAlwaysOnTop(value);

        // showMouseCursor 는 창이 열려 있는 동안 조작을 위해 커서를 계속 표시해야 하므로
        // 실제 반영은 창을 닫을 때 RestoreCursor() 가 설정값대로 처리한다.
    }
}
