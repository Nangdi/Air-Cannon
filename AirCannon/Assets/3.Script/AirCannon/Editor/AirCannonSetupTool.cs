using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// 현재 씬에 리플레이용 오브젝트(AirCannon + Canvas_Replay)와 보정 화면(Canvas_Calibration)을 만들고 참조를 연결한다.
// 이미 있으면 덮어쓰지 않는다.
public static class AirCannonSetupTool
{
    private const string RootName = "AirCannon";
    private const string CanvasName = "Canvas_Replay";
    private static readonly Vector2 ReferenceResolution = new Vector2(1920, 1080);
    private static readonly Color BackgroundColor = Color.black;

    // 보정 화면
    private const string CalibrationCanvasName = "Canvas_Calibration";
    private const int CalibrationSortingOrder = 105; // 설정창(100) 위, RS232 디버그 패널(110) 아래
    private const string BoldFontPath = "Assets/10.Font/defaultFont/NotoSansKR-Bold SDF.asset";
    private const string MediumFontPath = "Assets/10.Font/defaultFont/NotoSansKR-Medium SDF.asset";
    private static readonly Color BgColor = new Color(0.04f, 0.04f, 0.05f, 0.97f);
    private static readonly Color CardColor = new Color(0.18f, 0.19f, 0.23f);
    private static readonly Color AccentColor = new Color(0.2f, 0.45f, 0.9f);
    private static readonly Color TextColor = Color.white;
    private static readonly Color SubTextColor = new Color(0.7f, 0.72f, 0.78f);

    [MenuItem("Tools/AirCannon/Create Replay Objects")]
    private static void CreateReplayObjects()
    {
        if (GameObject.Find(RootName) != null || GameObject.Find(CanvasName) != null)
        {
            Debug.LogWarning($"[AirCannon] {RootName} 또는 {CanvasName}이 이미 있어 만들지 않음");
            return;
        }

        int uiLayer = LayerMask.NameToLayer("UI");

        // 캔버스
        var canvasGo = new GameObject(CanvasName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        canvasGo.layer = uiLayer;
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 0;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = ReferenceResolution;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

        var panel = CreateChild("Panel_Replay", CreateFrame(canvasGo.transform, uiLayer), uiLayer);
        Stretch(panel);

        var background = CreateChild("Background", panel, uiLayer);
        Stretch(background);
        var bgImage = background.gameObject.AddComponent<Image>();
        bgImage.color = BackgroundColor;
        bgImage.raycastTarget = false;

        var screen = CreateChild("Screen", panel, uiLayer);
        Stretch(screen);
        var rawImage = screen.gameObject.AddComponent<RawImage>();
        rawImage.raycastTarget = false;
        var fitter = screen.gameObject.AddComponent<AspectRatioFitter>();
        fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        fitter.aspectRatio = ReferenceResolution.x / ReferenceResolution.y;

        // 동작 오브젝트
        var root = new GameObject(RootName);
        var ringBuffer = root.AddComponent<WebcamRingBuffer>();
        var player = root.AddComponent<ClipPlayer>();
        var saver = root.AddComponent<ClipSaver>();
        var controller = root.AddComponent<AirCannonController>();

        var playerSo = new SerializedObject(player);
        playerSo.FindProperty("screen").objectReferenceValue = rawImage;
        playerSo.FindProperty("aspectFitter").objectReferenceValue = fitter;
        playerSo.FindProperty("replayRoot").objectReferenceValue = panel.gameObject;
        playerSo.ApplyModifiedPropertiesWithoutUndo();

        var controllerSo = new SerializedObject(controller);
        controllerSo.FindProperty("ringBuffer").objectReferenceValue = ringBuffer;
        controllerSo.FindProperty("clipPlayer").objectReferenceValue = player;
        controllerSo.FindProperty("clipSaver").objectReferenceValue = saver;
        controllerSo.FindProperty("debugPanel").objectReferenceValue = Object.FindObjectOfType<SerialPortLogPanel>(true);
        controllerSo.ApplyModifiedPropertiesWithoutUndo();

        Undo.RegisterCreatedObjectUndo(canvasGo, "Create Replay Objects");
        Undo.RegisterCreatedObjectUndo(root, "Create Replay Objects");
        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;
        Debug.Log("[AirCannon] 리플레이 오브젝트 생성 완료");
    }

    [MenuItem("Tools/AirCannon/Create Calibration Objects")]
    private static void CreateCalibrationObjects()
    {
        var root = GameObject.Find(RootName);
        if (root == null)
        {
            Debug.LogWarning($"[AirCannon] {RootName}이 없음 - 먼저 Create Replay Objects 실행");
            return;
        }
        if (GameObject.Find(CalibrationCanvasName) != null || root.GetComponent<ReplayCalibrator>() != null)
        {
            Debug.LogWarning($"[AirCannon] {CalibrationCanvasName} 또는 ReplayCalibrator가 이미 있어 만들지 않음");
            return;
        }

        var bold = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(BoldFontPath);
        var medium = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(MediumFontPath);
        int uiLayer = LayerMask.NameToLayer("UI");

        var canvasGo = new GameObject(CalibrationCanvasName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.layer = uiLayer;
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = CalibrationSortingOrder;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = ReferenceResolution;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

        var panel = CreateChild("Panel_Calibration", CreateFrame(canvasGo.transform, uiLayer), uiLayer);
        Stretch(panel);

        var background = CreateChild("Background", panel, uiLayer);
        Stretch(background);
        background.gameObject.AddComponent<Image>().color = BgColor; // 아래 화면 클릭을 막으므로 raycast 유지

        var title = CreateText("Title", panel, uiLayer, bold, 40, TextColor, TextAlignmentOptions.MidlineLeft,
                               "리플레이 타이밍 보정  <size=60%>(F2 닫기)</size>");
        Place(title.rectTransform, new Vector2(0, 505), new Vector2(1800, 56));

        var status = CreateText("Status", panel, uiLayer, medium, 24, SubTextColor, TextAlignmentOptions.MidlineLeft, "발사 대기 중");
        Place(status.rectTransform, new Vector2(0, 460), new Vector2(1800, 36));

        var previewArea = CreateChild("PreviewArea", panel, uiLayer);
        Place(previewArea, new Vector2(0, 145), new Vector2(1067, 580));
        var previewBg = previewArea.gameObject.AddComponent<Image>();
        previewBg.color = Color.black;
        previewBg.raycastTarget = false;

        var previewRt = CreateChild("Preview", previewArea, uiLayer);
        Stretch(previewRt);
        var preview = previewRt.gameObject.AddComponent<RawImage>();
        preview.raycastTarget = false;
        var previewFitter = previewRt.gameObject.AddComponent<AspectRatioFitter>();
        previewFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        previewFitter.aspectRatio = ReferenceResolution.x / ReferenceResolution.y;

        var legend = CreateText("Legend", panel, uiLayer, medium, 18, SubTextColor, TextAlignmentOptions.MidlineRight,
                                "<color=#FFFFFF>흰 선</color> 신호   <color=#FFBF33>주황 선</color> 움직임 시작   " +
                                "<color=#4DF280>초록 선</color> 재생 위치   <color=#F24D4D>빨간 선</color> 감도   " +
                                "<color=#5C8FE6>파란 영역</color> 잘라낼 구간   점선 움직임 찾는 범위 끝");
        Place(legend.rectTransform, new Vector2(0, -158), new Vector2(1600, 24));

        var graphRt = CreateChild("Graph", panel, uiLayer);
        Place(graphRt, new Vector2(0, -245), new Vector2(1600, 150));
        var graph = graphRt.gameObject.AddComponent<RawImage>();
        graph.raycastTarget = false;

        var resources = new DefaultControls.Resources
        {
            standard = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"),
            background = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd"),
            knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd"),
        };
        var sliderGo = DefaultControls.CreateSlider(resources);
        sliderGo.name = "Slider_Playhead";
        sliderGo.transform.SetParent(panel, false);
        SetLayerRecursive(sliderGo, uiLayer);
        Place((RectTransform)sliderGo.transform, new Vector2(0, -345), new Vector2(1600, 30));
        var slider = sliderGo.GetComponent<Slider>();
        slider.wholeNumbers = true;

        var info = CreateText("Info", panel, uiLayer, medium, 19, TextColor, TextAlignmentOptions.TopLeft, "");
        info.enableWordWrapping = false;
        info.overflowMode = TextOverflowModes.Ellipsis;
        Place(info.rectTransform, new Vector2(0, -420), new Vector2(1600, 112));

        var group = CreateChild("Group_Buttons", panel, uiLayer);
        Place(group, new Vector2(0, -505), new Vector2(1600, 54));
        var layout = group.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 12;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = true;

        var setStart = CreateButton("Button_SetStart", group, uiLayer, bold, resources.standard, CardColor, "시작점 (I)", out _);
        var setEnd = CreateButton("Button_SetEnd", group, uiLayer, bold, resources.standard, CardColor, "끝점 (O)", out _);
        var previewButton = CreateButton("Button_Preview", group, uiLayer, bold, resources.standard, CardColor, "미리보기 (P)", out var previewLabel);
        var autoAlign = CreateButton("Button_AutoAlign", group, uiLayer, bold, resources.standard, CardColor, "자동 맞춤 켬 (A)", out var autoAlignLabel);
        var thresholdDown = CreateButton("Button_ThresholdDown", group, uiLayer, bold, resources.standard, CardColor, "감도 - (-)", out _);
        var thresholdUp = CreateButton("Button_ThresholdUp", group, uiLayer, bold, resources.standard, CardColor, "감도 + (=)", out _);
        var retake = CreateButton("Button_Retake", group, uiLayer, bold, resources.standard, CardColor, "다시 촬영 (R)", out _);
        var save = CreateButton("Button_Save", group, uiLayer, bold, resources.standard, AccentColor, "저장 (Enter)", out _);
        var close = CreateButton("Button_Close", group, uiLayer, bold, resources.standard, CardColor, "닫기 (F2)", out _);

        var calibrator = root.AddComponent<ReplayCalibrator>();
        var so = new SerializedObject(calibrator);
        so.FindProperty("panelRoot").objectReferenceValue = panel.gameObject;
        so.FindProperty("preview").objectReferenceValue = preview;
        so.FindProperty("previewFitter").objectReferenceValue = previewFitter;
        so.FindProperty("graph").objectReferenceValue = graph;
        so.FindProperty("playhead").objectReferenceValue = slider;
        so.FindProperty("statusText").objectReferenceValue = status;
        so.FindProperty("infoText").objectReferenceValue = info;
        so.FindProperty("setStartButton").objectReferenceValue = setStart;
        so.FindProperty("setEndButton").objectReferenceValue = setEnd;
        so.FindProperty("previewButton").objectReferenceValue = previewButton;
        so.FindProperty("previewLabel").objectReferenceValue = previewLabel;
        so.FindProperty("autoAlignButton").objectReferenceValue = autoAlign;
        so.FindProperty("autoAlignLabel").objectReferenceValue = autoAlignLabel;
        so.FindProperty("thresholdDownButton").objectReferenceValue = thresholdDown;
        so.FindProperty("thresholdUpButton").objectReferenceValue = thresholdUp;
        so.FindProperty("retakeButton").objectReferenceValue = retake;
        so.FindProperty("saveButton").objectReferenceValue = save;
        so.FindProperty("closeButton").objectReferenceValue = close;
        so.ApplyModifiedPropertiesWithoutUndo();

        var controller = root.GetComponent<AirCannonController>();
        if (controller != null)
        {
            var controllerSo = new SerializedObject(controller);
            controllerSo.FindProperty("calibrator").objectReferenceValue = calibrator;
            controllerSo.ApplyModifiedPropertiesWithoutUndo();
        }

        Undo.RegisterCreatedObjectUndo(canvasGo, "Create Calibration Objects");
        EditorSceneManager.MarkSceneDirty(root.scene);
        Debug.Log("[AirCannon] 보정 화면 생성 완료");
    }

    // 설정창(ESC)에 넣을 aircannon.json 항목. 헤더는 fieldName 없이 label만 둔다.
    private struct SettingDef
    {
        public string label, field, format;
        public float step, min, max;
        public bool isBool, isHeader;

        public static SettingDef Header(string label) => new SettingDef { label = label, isHeader = true };
        public static SettingDef Bool(string label, string field) => new SettingDef { label = label, field = field, isBool = true };
        public static SettingDef Number(string label, string field, float step, float min, float max, string format) =>
            new SettingDef { label = label, field = field, step = step, min = min, max = max, format = format };
    }

    private static readonly SettingDef[] SettingDefs =
    {
        SettingDef.Header("리플레이"),
        SettingDef.Number("시작 시점 (신호 기준, 초)", "offsetSeconds", 0.05f, -2f, 5f, "+0.00;-0.00"),
        SettingDef.Number("길이 (초)", "durationSeconds", 0.1f, 0.1f, 10f, "0.00"),
        SettingDef.Number("반복 횟수 (0 = 다음 발사까지)", "repeatCount", 1f, 0f, 20f, "0"),
        SettingDef.Number("재생 배속 (0.5 = 절반 속도)", "playbackSpeed", 0.05f, 0.05f, 4f, "0.00"),
        SettingDef.Bool("좌우 반전", "mirrorHorizontal"),
        SettingDef.Bool("클립 JPG 저장", "saveClip"),
        SettingDef.Number("JPG 품질 (1~100)", "jpgQuality", 5f, 1f, 100f, "0"),
        SettingDef.Header("카메라 (바꾸면 웹캠을 다시 엶)"),
        SettingDef.Number("요청 해상도 폭", "cameraWidth", 160f, 320f, 3840f, "0"),
        SettingDef.Number("요청 해상도 높이", "cameraHeight", 90f, 180f, 2160f, "0"),
        SettingDef.Number("요청 fps", "cameraFps", 5f, 5f, 120f, "0"),
        SettingDef.Header("자동 맞춤 (움직임 감지)"),
        SettingDef.Bool("자동 맞춤 사용", "autoAlign"),
        SettingDef.Number("시작 시점 (움직임 기준, 초)", "motionLeadSeconds", 0.05f, -3f, 3f, "+0.00;-0.00"),
        SettingDef.Number("감도 (움직임 점수)", "motionThreshold", 0.5f, 0.5f, 50f, "0.0"),
        SettingDef.Number("움직임 찾는 시간 (초)", "motionSearchSeconds", 0.1f, 0.1f, 5f, "0.0"),
        SettingDef.Number("감지 영역 왼쪽 (0~1)", "motionArea.x", 0.05f, 0f, 0.99f, "0.00"),
        SettingDef.Number("감지 영역 위쪽 (0~1)", "motionArea.y", 0.05f, 0f, 0.99f, "0.00"),
        SettingDef.Number("감지 영역 폭 (0~1)", "motionArea.width", 0.05f, 0.01f, 1f, "0.00"),
        SettingDef.Number("감지 영역 높이 (0~1)", "motionArea.height", 0.05f, 0.01f, 1f, "0.00"),
    };

    private const string SettingsGroupName = "Group_AirCannon";
    private const float SettingsPanelWidth = 640f;
    private const float SettingRowHeight = 34f;

    [MenuItem("Tools/AirCannon/Add Settings Rows")]
    private static void AddSettingsRows()
    {
        var settingsUi = Object.FindObjectOfType<SettingsPanelUI>(true);
        var controller = Object.FindObjectOfType<AirCannonController>(true);
        if (settingsUi == null || controller == null)
        {
            Debug.LogWarning("[AirCannon] SettingsPanelUI 또는 AirCannonController가 씬에 없음");
            return;
        }

        var panelProp = new SerializedObject(settingsUi).FindProperty("panelRoot");
        var panelGo = panelProp.objectReferenceValue as GameObject;
        if (panelGo == null)
        {
            Debug.LogWarning("[AirCannon] 설정창 panelRoot가 비어 있음");
            return;
        }
        var panel = (RectTransform)panelGo.transform;
        if (panel.Find(SettingsGroupName) != null)
        {
            Debug.LogWarning($"[AirCannon] {SettingsGroupName}이 이미 있어 만들지 않음");
            return;
        }

        var bold = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(BoldFontPath);
        var medium = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(MediumFontPath);
        var sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        var checkSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Checkmark.psd");
        int uiLayer = LayerMask.NameToLayer("UI");

        // 항목이 늘어나므로 패널을 넓히고 높이는 내용에 맞춘다.
        Undo.RecordObject(panel, "Add Settings Rows");
        panel.sizeDelta = new Vector2(SettingsPanelWidth, panel.sizeDelta.y);
        if (panel.GetComponent<ContentSizeFitter>() == null)
        {
            var fitter = Undo.AddComponent<ContentSizeFitter>(panelGo);
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        var group = CreateChild(SettingsGroupName, panel, uiLayer);
        var groupLayout = group.gameObject.AddComponent<VerticalLayoutGroup>();
        groupLayout.spacing = 6;
        groupLayout.childControlWidth = true;
        groupLayout.childControlHeight = true;
        groupLayout.childForceExpandWidth = true;
        groupLayout.childForceExpandHeight = false;

        var ui = group.gameObject.AddComponent<AirCannonSettingsUI>();
        var so = new SerializedObject(ui);
        so.FindProperty("controller").objectReferenceValue = controller;
        var numberRows = so.FindProperty("numberRows");
        var boolRows = so.FindProperty("boolRows");

        foreach (var def in SettingDefs)
        {
            if (def.isHeader)
            {
                var header = CreateText("Header_" + def.label, group, uiLayer, bold, 20, AccentColor, TextAlignmentOptions.BottomLeft, def.label);
                AddLayoutElement(header.gameObject, 32, -1);
                continue;
            }

            string rowName = "Row_" + def.field.Replace(".", "_");
            var row = CreateChild(rowName, group, uiLayer);
            AddLayoutElement(row.gameObject, SettingRowHeight, -1);
            var rowLayout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 8;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = true;

            var label = CreateText("Label", row, uiLayer, medium, 18, TextColor, TextAlignmentOptions.MidlineLeft, def.label);
            label.enableWordWrapping = false;
            AddLayoutElement(label.gameObject, -1, -1).flexibleWidth = 1;

            if (def.isBool)
            {
                var toggle = CreateToggle(row, uiLayer, sprite, checkSprite);
                int i = boolRows.arraySize++;
                var item = boolRows.GetArrayElementAtIndex(i);
                item.FindPropertyRelative("fieldName").stringValue = def.field;
                item.FindPropertyRelative("toggle").objectReferenceValue = toggle;
            }
            else
            {
                var minus = CreateButton("Button_Minus", row, uiLayer, bold, sprite, CardColor, "-", out _);
                AddLayoutElement(minus.gameObject, -1, 44);
                var value = CreateText("Value", row, uiLayer, medium, 18, TextColor, TextAlignmentOptions.Center, "0.00");
                AddLayoutElement(value.gameObject, -1, 100);
                var plus = CreateButton("Button_Plus", row, uiLayer, bold, sprite, CardColor, "+", out _);
                AddLayoutElement(plus.gameObject, -1, 44);

                int i = numberRows.arraySize++;
                var item = numberRows.GetArrayElementAtIndex(i);
                item.FindPropertyRelative("fieldName").stringValue = def.field;
                item.FindPropertyRelative("step").floatValue = def.step;
                item.FindPropertyRelative("min").floatValue = def.min;
                item.FindPropertyRelative("max").floatValue = def.max;
                item.FindPropertyRelative("format").stringValue = def.format;
                item.FindPropertyRelative("valueText").objectReferenceValue = value;
                item.FindPropertyRelative("minusButton").objectReferenceValue = minus;
                item.FindPropertyRelative("plusButton").objectReferenceValue = plus;
            }
        }
        so.ApplyModifiedPropertiesWithoutUndo();

        Undo.RegisterCreatedObjectUndo(group.gameObject, "Add Settings Rows");
        EditorSceneManager.MarkSceneDirty(panel.gameObject.scene);
        Debug.Log("[AirCannon] 설정창에 리플레이 설정 추가 완료");
    }

    private static Toggle CreateToggle(Transform parent, int layer, Sprite sprite, Sprite checkSprite)
    {
        var rt = CreateChild("Toggle", parent, layer);
        AddLayoutElement(rt.gameObject, -1, 44);
        var toggle = rt.gameObject.AddComponent<Toggle>();

        var bg = CreateChild("Background", rt, layer);
        Place(bg, Vector2.zero, new Vector2(28, 28));
        var bgImage = bg.gameObject.AddComponent<Image>();
        bgImage.sprite = sprite;
        bgImage.type = Image.Type.Sliced;
        bgImage.color = CardColor;

        var check = CreateChild("Checkmark", bg, layer);
        Stretch(check);
        var checkImage = check.gameObject.AddComponent<Image>();
        checkImage.sprite = checkSprite;
        checkImage.color = TextColor;
        checkImage.raycastTarget = false;

        toggle.targetGraphic = bgImage;
        toggle.graphic = checkImage;
        return toggle;
    }

    private static LayoutElement AddLayoutElement(GameObject go, float height, float width)
    {
        var le = go.AddComponent<LayoutElement>();
        if (height >= 0) { le.minHeight = height; le.preferredHeight = height; }
        if (width >= 0) { le.minWidth = width; le.preferredWidth = width; }
        return le;
    }

    private static TextMeshProUGUI CreateText(string name, Transform parent, int layer, TMP_FontAsset font, float size,
                                              Color color, TextAlignmentOptions alignment, string text)
    {
        var rt = CreateChild(name, parent, layer);
        var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) tmp.font = font;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.alignment = alignment;
        tmp.text = text;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static Button CreateButton(string name, Transform parent, int layer, TMP_FontAsset font, Sprite sprite,
                                       Color color, string label, out TextMeshProUGUI labelText)
    {
        var rt = CreateChild(name, parent, layer);
        var image = rt.gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.type = Image.Type.Sliced;
        image.color = color;
        var button = rt.gameObject.AddComponent<Button>();
        button.targetGraphic = image;

        labelText = CreateText("Label", rt, layer, font, 22, TextColor, TextAlignmentOptions.Center, label);
        labelText.enableAutoSizing = true;
        labelText.fontSizeMin = 14;
        labelText.fontSizeMax = 22;
        labelText.enableWordWrapping = false;
        Stretch(labelText.rectTransform);
        return button;
    }

    private static void Place(RectTransform rt, Vector2 position, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = position;
        rt.sizeDelta = size;
    }

    private static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform)
            SetLayerRecursive(child.gameObject, layer);
    }

    private static RectTransform CreateChild(string name, Transform parent, int layer)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = layer;
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    // 기준 해상도 크기의 고정 틀. 비율이 다른 화면에서 남는 부분은 카메라 배경색으로 채운다.
    private static RectTransform CreateFrame(Transform canvas, int layer)
    {
        var frame = CreateChild("Frame", canvas, layer);
        Place(frame, Vector2.zero, ReferenceResolution);
        return frame;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
