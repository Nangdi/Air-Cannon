using System;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 설정창(ESC) 안의 공기대포 리플레이 설정 묶음.
// aircannon.json 의 숫자 필드는 -/+ 버튼으로, bool 필드는 토글로 바꾸고, 바꾸는 즉시 적용 + 저장한다.
// fieldName 은 AirCannonConfig 의 필드 이름이며 한 단계 안쪽 필드는 "motionArea.x" 처럼 적는다.
// 이 오브젝트가 켜질 때(설정창을 열 때) 현재 값으로 다시 표시하므로 보정 화면에서 저장한 값도 반영된다.
public class AirCannonSettingsUI : MonoBehaviour
{
    [Serializable]
    public class NumberRow
    {
        public string fieldName;
        public float step = 0.1f;
        public float min;
        public float max = 10f;
        [Tooltip("표시 형식 (예: 0.00, +0.00;-0.00, 0)")]
        public string format = "0.00";
        public TMP_Text valueText;
        public Button minusButton;
        public Button plusButton;

        [NonSerialized] public FieldInfo owner; // 한 단계 안쪽 필드일 때 바깥 필드
        [NonSerialized] public FieldInfo field;
    }

    [Serializable]
    public class BoolRow
    {
        public string fieldName;
        public Toggle toggle;

        [NonSerialized] public FieldInfo owner;
        [NonSerialized] public FieldInfo field;
    }

    [SerializeField] private AirCannonController controller;
    [SerializeField] private List<NumberRow> numberRows = new List<NumberRow>();
    [SerializeField] private List<BoolRow> boolRows = new List<BoolRow>();

    private bool bound;
    private bool syncing;

    private void Awake()
    {
        Bind();
    }

    private void OnEnable()
    {
        Refresh();
    }

    private void Bind()
    {
        if (bound) return;
        bound = true;

        foreach (var row in numberRows)
        {
            if (row == null || !Resolve(row.fieldName, out row.owner, out row.field)) continue;
            if (row.field.FieldType != typeof(float) && row.field.FieldType != typeof(int))
            {
                Debug.LogError($"[AirCannon] 설정 '{row.fieldName}' 은 숫자가 아니라 {row.field.FieldType.Name} 입니다.", this);
                row.field = null;
                continue;
            }
            var captured = row;
            if (row.minusButton != null) row.minusButton.onClick.AddListener(() => Step(captured, -1));
            if (row.plusButton != null) row.plusButton.onClick.AddListener(() => Step(captured, 1));
        }

        foreach (var row in boolRows)
        {
            if (row == null || !Resolve(row.fieldName, out row.owner, out row.field)) continue;
            if (row.field.FieldType != typeof(bool))
            {
                Debug.LogError($"[AirCannon] 설정 '{row.fieldName}' 은 bool 이 아니라 {row.field.FieldType.Name} 입니다.", this);
                row.field = null;
                continue;
            }
            var captured = row;
            if (row.toggle != null) row.toggle.onValueChanged.AddListener(value => OnToggle(captured, value));
        }
    }

    // "a" 또는 "a.b" 를 AirCannonConfig 필드로 찾는다.
    private bool Resolve(string path, out FieldInfo owner, out FieldInfo field)
    {
        owner = null;
        field = null;
        if (string.IsNullOrEmpty(path))
        {
            Debug.LogWarning("[AirCannon] 설정 항목 이름이 비어 있음", this);
            return false;
        }

        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
        string[] parts = path.Split('.');
        Type type = typeof(AirCannonConfig);
        if (parts.Length == 2)
        {
            owner = type.GetField(parts[0], flags);
            type = owner?.FieldType;
        }
        field = type?.GetField(parts[parts.Length - 1], flags);

        if (field == null || parts.Length > 2)
        {
            Debug.LogError($"[AirCannon] AirCannonConfig 에 '{path}' 필드가 없습니다.", this);
            field = null;
            return false;
        }
        return true;
    }

    private static object Target(AirCannonConfig config, FieldInfo owner)
    {
        return owner == null ? config : owner.GetValue(config);
    }

    public void Refresh()
    {
        AirCannonConfig config = controller != null ? controller.Config : null;
        if (config == null) return;

        syncing = true;
        foreach (var row in numberRows)
        {
            if (row == null || row.field == null || row.valueText == null) continue;
            float value = Convert.ToSingle(row.field.GetValue(Target(config, row.owner)));
            row.valueText.text = value.ToString(row.format);
        }
        foreach (var row in boolRows)
        {
            if (row == null || row.field == null || row.toggle == null) continue;
            row.toggle.isOn = (bool)row.field.GetValue(Target(config, row.owner));
        }
        syncing = false;
    }

    private void Step(NumberRow row, int direction)
    {
        AirCannonConfig config = controller != null ? controller.Config : null;
        if (config == null || row.field == null) return;

        object target = Target(config, row.owner);
        float value = Convert.ToSingle(row.field.GetValue(target)) + row.step * direction;
        value = Mathf.Clamp((float)Math.Round(value, 3), row.min, row.max);

        if (row.field.FieldType == typeof(int)) row.field.SetValue(target, Mathf.RoundToInt(value));
        else row.field.SetValue(target, value);

        controller.ApplyConfigChange($"{row.fieldName} = {value.ToString(row.format)}");
        Refresh(); // Validate 로 다시 잘린 값까지 표시
    }

    private void OnToggle(BoolRow row, bool value)
    {
        if (syncing) return;
        AirCannonConfig config = controller != null ? controller.Config : null;
        if (config == null || row.field == null) return;

        row.field.SetValue(Target(config, row.owner), value);
        controller.ApplyConfigChange($"{row.fieldName} = {value}");
    }
}
