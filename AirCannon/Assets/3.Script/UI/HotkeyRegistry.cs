using System;
using System.Collections.Generic;

// 운영·개발용 단축키 목록. 각 기능 스크립트가 실제로 쓰는 키와 설명을 등록하고,
// F1 도움말 패널(HotkeyHelpPanel)은 등록된 목록을 그대로 보여 준다.
// 키를 바꾸거나 추가해도 안내가 어긋나지 않도록 도움말에 키를 직접 적지 않는다.
public static class HotkeyRegistry
{
    public struct Entry
    {
        public object owner;
        public string key;
        public string description;
        public string scope; // 어느 화면에서 동작하는지 (예: "모든 화면", "보정 화면")
    }

    private static readonly List<Entry> entries = new List<Entry>();

    public static IReadOnlyList<Entry> Entries => entries;
    public static event Action Changed;

    public static void Register(object owner, string key, string description, string scope)
    {
        if (owner == null || string.IsNullOrEmpty(key)) return;
        entries.Add(new Entry { owner = owner, key = key, description = description, scope = scope });
        Changed?.Invoke();
    }

    // owner가 등록한 키를 모두 지운다. 다시 등록하기 전이나 OnDisable에서 부른다.
    public static void Unregister(object owner)
    {
        if (entries.RemoveAll(e => e.owner == owner) > 0)
            Changed?.Invoke();
    }
}
