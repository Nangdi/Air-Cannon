using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;

// F1로 켜고 끄는 단축키 도움말 패널. 기본은 숨김.
// HotkeyRegistry에 등록된 키를 화면(scope)별로 묶어 보여 준다.
public class HotkeyHelpPanel : MonoBehaviour
{
    private const string Scope = "모든 화면";

    [Tooltip("F1로 켜고 끌 패널 루트. 이 스크립트는 항상 켜져 있는 오브젝트(캔버스 등)에 둔다.")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private TMP_Text bodyText;
    [SerializeField] private KeyCode toggleKey = KeyCode.F1;

    private bool dirty = true;
    private readonly StringBuilder sb = new StringBuilder();

    private void OnEnable()
    {
        HotkeyRegistry.Register(this, toggleKey.ToString(), "단축키 도움말 열기/닫기", Scope);
        HotkeyRegistry.Changed += MarkDirty;
        dirty = true;
    }

    private void OnDisable()
    {
        HotkeyRegistry.Changed -= MarkDirty;
        HotkeyRegistry.Unregister(this);
    }

    private void Start()
    {
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    private void Update()
    {
        if (Input.GetKeyDown(toggleKey) && panelRoot != null)
        {
            panelRoot.SetActive(!panelRoot.activeSelf);
            dirty = true;
        }

        if (dirty && panelRoot != null && panelRoot.activeSelf)
            Rebuild();
    }

    private void MarkDirty() => dirty = true;

    private void Rebuild()
    {
        dirty = false;
        if (bodyText == null) return;

        // 등록 순서를 지키면서 화면별로 묶는다.
        var scopes = new List<string>();
        var groups = new Dictionary<string, List<HotkeyRegistry.Entry>>();
        foreach (var e in HotkeyRegistry.Entries)
        {
            string scope = string.IsNullOrEmpty(e.scope) ? Scope : e.scope;
            if (!groups.TryGetValue(scope, out var list))
            {
                list = new List<HotkeyRegistry.Entry>();
                groups[scope] = list;
                scopes.Add(scope);
            }
            list.Add(e);
        }

        sb.Clear();
        foreach (string scope in scopes)
        {
            sb.Append("<color=#7FA8FF><b>").Append(scope).Append("</b></color>\n");
            foreach (var e in groups[scope])
                sb.Append("  <b>").Append(e.key).Append("</b><pos=32%>").Append(e.description).Append('\n');
            sb.Append('\n');
        }
        bodyText.text = sb.Length > 0 ? sb.ToString() : "등록된 단축키 없음";
    }
}
