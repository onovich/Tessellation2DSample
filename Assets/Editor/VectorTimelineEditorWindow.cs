using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

public class VectorTimelineEditorWindow : EditorWindow
{
    // --- 数据引用 ---
    private VectorTimelineAsset currentAsset;
    private VectorTimelinePlayer previewPlayer;

    // --- 视图配置 ---
    private float zoom = 100f; // 1秒 = 100像素
    private float headerHeight = 30f;
    private float trackHeight = 80f;
    private Vector2 scrollPos = Vector2.zero; // 滚动条位置

    // --- 播放状态 ---
    private float currentTime = 0f;
    private bool isPlaying = false;
    private double lastEditorTime;

    // --- 编辑交互状态 ---
    private int selectedKeyIndex = -1;
    private bool isDraggingKey = false;
    private bool isScrubbing = false;

    // --- 样式缓存 ---
    private GUIStyle keyframeStyle;
    private Texture2D whiteTexture;

    [MenuItem("Window/Vector Morph/Timeline Editor")]
    public static void ShowWindow()
    {
        GetWindow<VectorTimelineEditorWindow>("Vector Timeline");
    }

    private void OnEnable()
    {
        if (previewPlayer == null)
            previewPlayer = FindFirstObjectByType<VectorTimelinePlayer>();

        EditorApplication.update += OnEditorUpdate;

        whiteTexture = new Texture2D(1, 1);
        whiteTexture.SetPixel(0, 0, Color.white);
        whiteTexture.Apply();
    }

    private void OnDisable()
    {
        EditorApplication.update -= OnEditorUpdate;
        if (whiteTexture != null) DestroyImmediate(whiteTexture);
    }

    private void OnEditorUpdate()
    {
        // 拖拽时强制刷新预览
        if (isScrubbing || isDraggingKey) UpdateScenePreview();

        if (isPlaying && currentAsset != null)
        {
            double timeNow = EditorApplication.timeSinceStartup;
            double delta = timeNow - lastEditorTime;
            lastEditorTime = timeNow;

            currentTime += (float)delta;

            float duration = currentAsset.GetDuration();
            if (duration > 0)
            {
                if (currentTime > duration)
                {
                    if (currentAsset.loopMode == VectorLoopMode.Loop) currentTime %= duration;
                    else if (currentAsset.loopMode == VectorLoopMode.Once) { currentTime = duration; isPlaying = false; }
                }
            }
            UpdateScenePreview();
            Repaint();
        }
    }

    private void OnGUI()
    {
        InitStyles();

        // 1. 工具栏 (包含 Zoom 滑块)
        DrawToolbar();

        if (currentAsset == null)
        {
            GUILayout.FlexibleSpace();
            GUILayout.Label("请选择一个 VectorTimelineAsset", EditorStyles.centeredGreyMiniLabel);
            GUILayout.FlexibleSpace();
            return;
        }

        // 2. 计算内容总宽度
        // 规则：取 (Duration) 和 (最后一个关键帧 + 1秒) 的最大值，保证能显示完
        float maxTime = GetMaxVisibleTime();
        float contentWidth = maxTime * zoom + 50f; // +50f 留一点余量

        // 3. 滚动视图区域
        // 使用 GUILayout.BeginScrollView 自动处理滚动条
        scrollPos = GUILayout.BeginScrollView(scrollPos, false, true,
            GUILayout.Height(headerHeight + trackHeight + 20)); // +20 是滚动条的高度

        // 在 ScrollView 内部，我们需要保留一个足够大的空间
        // 使用 GUILayoutUtility.GetRect 占位，或者直接绘制
        Rect contentRect = GUILayoutUtility.GetRect(contentWidth, headerHeight + trackHeight);

        // 绘制背景
        GUI.BeginGroup(contentRect);
        DrawTimelineContent(contentRect, maxTime);
        GUI.EndGroup();

        // 处理输入 (坐标系是相对于 contentRect 的，也就是 ScrollView 内部坐标)
        HandleInput(contentRect);

        GUILayout.EndScrollView();

        // 4. 属性面板
        DrawInspectorArea();
    }

    // =========================================================
    // 逻辑计算
    // =========================================================

    float GetMaxVisibleTime()
    {
        float maxT = currentAsset.duration > 0 ? currentAsset.duration : 0f;
        if (currentAsset.keyframes.Count > 0)
        {
            float lastKeyTime = currentAsset.keyframes.Max(k => k.time);
            if (lastKeyTime > maxT) maxT = lastKeyTime;
        }
        // 如果都没设置，默认显示 5 秒
        return Mathf.Max(maxT, 5.0f);
    }

    float TimeToPixel(float time) => time * zoom;
    float PixelToTime(float pixel) => pixel / zoom;

    // =========================================================
    // 绘图
    // =========================================================

    void DrawToolbar()
    {
        GUILayout.BeginHorizontal(EditorStyles.toolbar);

        // Asset
        GUILayout.Label("Asset:", GUILayout.Width(40));
        EditorGUI.BeginChangeCheck();
        var newAsset = (VectorTimelineAsset)EditorGUILayout.ObjectField(currentAsset, typeof(VectorTimelineAsset), false, GUILayout.Width(150));
        if (EditorGUI.EndChangeCheck())
        {
            currentAsset = newAsset;
            selectedKeyIndex = -1;
            currentTime = 0;
            isPlaying = false;
        }

        // Player
        GUILayout.Space(10);
        GUILayout.Label("Player:", GUILayout.Width(45));
        var newPlayer = (VectorTimelinePlayer)EditorGUILayout.ObjectField(previewPlayer, typeof(VectorTimelinePlayer), true, GUILayout.Width(120));
        if (newPlayer != previewPlayer) previewPlayer = newPlayer;

        GUILayout.Space(10);

        // Play Controls
        if (GUILayout.Button("⏮", EditorStyles.toolbarButton, GUILayout.Width(25))) { currentTime = 0; UpdateScenePreview(); }

        bool newPlaying = GUILayout.Toggle(isPlaying, isPlaying ? "⏸" : "▶", EditorStyles.toolbarButton, GUILayout.Width(35));
        if (newPlaying != isPlaying)
        {
            isPlaying = newPlaying;
            lastEditorTime = EditorApplication.timeSinceStartup;
        }

        if (GUILayout.Button("⏹", EditorStyles.toolbarButton, GUILayout.Width(25))) { isPlaying = false; currentTime = 0; UpdateScenePreview(); }

        // Time Label
        GUILayout.Space(5);
        GUILayout.Label($"{currentTime:F2}s", GUILayout.Width(50));

        GUILayout.FlexibleSpace();

        // Zoom Slider
        GUILayout.Label("Zoom:", GUILayout.Width(40));
        zoom = GUILayout.HorizontalSlider(zoom, 10f, 500f, GUILayout.Width(100));

        GUILayout.EndHorizontal();
    }

    void DrawTimelineContent(Rect rect, float maxTime)
    {
        // 1. 背景
        Rect bgRect = new Rect(0, 0, rect.width, rect.height);
        EditorGUI.DrawRect(bgRect, new Color(0.18f, 0.18f, 0.18f));

        // 2. 标尺背景
        Rect rulerRect = new Rect(0, 0, rect.width, headerHeight);
        EditorGUI.DrawRect(rulerRect, new Color(0.22f, 0.22f, 0.22f));

        // 3. 绘制刻度
        Handles.color = Color.gray;

        // 动态刻度步长
        float timeStep = 1.0f;
        if (zoom > 300) timeStep = 0.1f;
        else if (zoom > 100) timeStep = 0.5f;
        else timeStep = 1.0f;

        for (float t = 0; t <= maxTime; t += timeStep)
        {
            float x = TimeToPixel(t);
            bool isMain = Mathf.Abs(t % 1.0f) < 0.001f;
            float h = isMain ? headerHeight : headerHeight * 0.5f;

            Handles.color = isMain ? new Color(0.6f, 0.6f, 0.6f) : new Color(0.4f, 0.4f, 0.4f);
            Handles.DrawLine(new Vector3(x, headerHeight - h), new Vector3(x, headerHeight));
            // 垂直参考线
            if (isMain)
            {
                Handles.color = new Color(1, 1, 1, 0.05f);
                Handles.DrawLine(new Vector3(x, headerHeight), new Vector3(x, rect.height));
                GUI.Label(new Rect(x + 2, 0, 40, 20), t.ToString("0"), EditorStyles.miniLabel);
            }
        }

        // 4. 绘制关键帧
        DrawKeyframes(rect);

        // 5. 绘制 Duration 线 (如果设置了)
        if (currentAsset.duration > 0)
        {
            float durX = TimeToPixel(currentAsset.duration);
            Handles.color = Color.blue;
            Handles.DrawLine(new Vector3(durX, 0), new Vector3(durX, rect.height));
            GUI.Label(new Rect(durX + 2, headerHeight + 5, 50, 20), "End", EditorStyles.miniLabel);

            // 绘制 Duration 之后的遮罩 (表示无效区域)
            if (rect.width > durX)
            {
                Rect maskRect = new Rect(durX, headerHeight, rect.width - durX, rect.height - headerHeight);
                EditorGUI.DrawRect(maskRect, new Color(0, 0, 0, 0.3f));
            }
        }

        // 6. 绘制播放头
        // 限制播放头不超出显示范围
        float playheadX = TimeToPixel(currentTime);
        Handles.color = Color.red;
        Handles.DrawLine(new Vector3(playheadX, 0), new Vector3(playheadX, rect.height));
        Vector3[] hat = { new Vector3(playheadX - 5, 0), new Vector3(playheadX + 5, 0), new Vector3(playheadX, 10) };
        Handles.color = Color.red;
        Handles.DrawAAConvexPolygon(hat);
    }

    void DrawKeyframes(Rect rect)
    {
        float keyY = headerHeight + (trackHeight / 2) - 6;

        for (int i = 0; i < currentAsset.keyframes.Count; i++)
        {
            var key = currentAsset.keyframes[i];
            float x = TimeToPixel(key.time);

            // 连线
            if (i < currentAsset.keyframes.Count - 1)
            {
                float nextX = TimeToPixel(currentAsset.keyframes[i + 1].time);
                Handles.color = new Color(0.5f, 0.5f, 0.5f, 0.3f);
                Handles.DrawLine(new Vector3(x, keyY + 6), new Vector3(nextX, keyY + 6));
            }

            Rect keyRect = new Rect(x - 6, keyY, 12, 12);
            Color c = (i == selectedKeyIndex) ? Color.cyan : Color.white;
            if (key.isInstant) c = new Color(1f, 0.4f, 0.4f);

            GUI.color = c;
            GUI.Box(keyRect, GUIContent.none, keyframeStyle);
            GUI.color = Color.white;
        }
    }

    // =========================================================
    // 交互处理
    // =========================================================

    void HandleInput(Rect rect)
    {
        Event e = Event.current;
        Vector2 mousePos = e.mousePosition; // 相对于 ScrollView 内容的坐标

        if (rect.Contains(mousePos) || isDraggingKey || isScrubbing)
        {
            // --- 鼠标左键按下 ---
            if (e.type == EventType.MouseDown && e.button == 0)
            {
                // 1. 检查是否点中关键帧
                bool hitKey = false;
                for (int i = 0; i < currentAsset.keyframes.Count; i++)
                {
                    float x = TimeToPixel(currentAsset.keyframes[i].time);
                    float y = headerHeight + (trackHeight / 2) - 6;
                    Rect keyRect = new Rect(x - 6, y, 12, 12);

                    if (keyRect.Contains(mousePos))
                    {
                        selectedKeyIndex = i;
                        isDraggingKey = true;
                        isScrubbing = false;
                        hitKey = true;
                        Repaint();
                        UpdateScenePreview();
                        e.Use();
                        break;
                    }
                }

                // 2. 没点中关键帧 -> 拖动播放头
                if (!hitKey)
                {
                    isScrubbing = true;
                    selectedKeyIndex = -1;
                    float t = PixelToTime(mousePos.x);
                    currentTime = Mathf.Max(0, t); // 限制最小为0
                    Repaint();
                    UpdateScenePreview();
                    e.Use();
                }
            }

            // --- 鼠标拖拽 ---
            if (e.type == EventType.MouseDrag && e.button == 0)
            {
                float t = PixelToTime(mousePos.x);
                t = Mathf.Max(0, t); // 限制不能拖到负数

                if (isDraggingKey && selectedKeyIndex != -1)
                {
                    if (!e.shift) t = Mathf.Round(t * 10) / 10f; // 吸附

                    Undo.RecordObject(currentAsset, "Move Keyframe");
                    var key = currentAsset.keyframes[selectedKeyIndex];
                    key.time = t;
                    currentAsset.keyframes[selectedKeyIndex] = key;
                    EditorUtility.SetDirty(currentAsset);

                    currentTime = t; // 拖关键帧时，播放头跟随，方便看效果
                    UpdateScenePreview();
                    e.Use();
                }
                else if (isScrubbing)
                {
                    currentTime = t;
                    UpdateScenePreview();
                    Repaint();
                    e.Use();
                }
            }

            // --- 鼠标松开 ---
            if (e.type == EventType.MouseUp)
            {
                if (isDraggingKey) { SortKeyframes(); isDraggingKey = false; }
                isScrubbing = false;
            }
        }
    }

    // =========================================================
    // 属性面板
    // =========================================================

    void DrawInspectorArea()
    {
        GUILayout.Space(10);
        GUILayout.Label("Inspector", EditorStyles.boldLabel);
        GUILayout.BeginVertical(EditorStyles.helpBox);

        // Global Settings
        EditorGUILayout.LabelField("Global Settings", EditorStyles.miniBoldLabel);
        EditorGUI.BeginChangeCheck();
        var loopMode = (VectorLoopMode)EditorGUILayout.EnumPopup("Loop Mode", currentAsset.loopMode);
        float dur = EditorGUILayout.FloatField("Total Duration (0=Auto)", currentAsset.duration);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(currentAsset, "Change Settings");
            currentAsset.loopMode = loopMode;
            currentAsset.duration = Mathf.Max(0, dur);
            EditorUtility.SetDirty(currentAsset);
            Repaint(); // 刷新 Duration 线的位置
        }

        GUILayout.Space(10);

        // Buttons
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("+ Add Keyframe", GUILayout.Height(24))) AddKeyframe();
        GUI.enabled = selectedKeyIndex != -1;
        if (GUILayout.Button("- Remove Selected", GUILayout.Height(24))) RemoveKeyframe();
        GUI.enabled = true;
        GUILayout.EndHorizontal();

        GUILayout.Space(5);

        // Keyframe Properties
        if (selectedKeyIndex != -1 && selectedKeyIndex < currentAsset.keyframes.Count)
        {
            var key = currentAsset.keyframes[selectedKeyIndex];
            EditorGUI.BeginChangeCheck();

            float newTime = EditorGUILayout.FloatField("Time", key.time);
            var newShape = (VectorShapeAsset)EditorGUILayout.ObjectField("Shape", key.shapeAsset, typeof(VectorShapeAsset), false);
            float newScale = EditorGUILayout.Slider("Scale", key.scale, 0f, 2f);
            bool newInstant = EditorGUILayout.Toggle("Is Instant", key.isInstant);
            int newOffset = EditorGUILayout.IntSlider("Align Offset", key.alignOffset, 0, 360);
            AnimationCurve newCurve = EditorGUILayout.CurveField("Curve", key.curve);

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(currentAsset, "Modify Keyframe");
                key.time = Mathf.Max(0, newTime);
                key.shapeAsset = newShape;
                key.scale = newScale;
                key.isInstant = newInstant;
                key.alignOffset = newOffset;
                key.curve = newCurve;

                currentAsset.keyframes[selectedKeyIndex] = key;
                if (Mathf.Abs(newTime - key.time) > 0.001f) SortKeyframes();
                EditorUtility.SetDirty(currentAsset);
                UpdateScenePreview();
                Repaint(); // 刷新关键帧位置
            }
        }
        else
        {
            GUILayout.Label("Select a keyframe to edit.", EditorStyles.centeredGreyMiniLabel);
        }
        GUILayout.EndVertical();
    }

    // =========================================================
    // Helpers
    // =========================================================

    void InitStyles()
    {
        if (keyframeStyle == null) keyframeStyle = new GUIStyle(GUI.skin.box);
    }

    void AddKeyframe()
    {
        Undo.RecordObject(currentAsset, "Add Keyframe");
        var newKey = new TimelineKeyframe
        {
            time = currentTime,
            scale = 1.0f,
            curve = AnimationCurve.Linear(0, 0, 1, 1),
            alignOffset = 0,
            isInstant = false
        };
        if (currentAsset.keyframes.Count > 0)
        {
            var last = currentAsset.keyframes[currentAsset.keyframes.Count - 1];
            newKey.shapeAsset = last.shapeAsset;
        }
        currentAsset.keyframes.Add(newKey);
        SortKeyframes();
        selectedKeyIndex = currentAsset.keyframes.IndexOf(newKey);
        EditorUtility.SetDirty(currentAsset);
        Repaint();
    }

    void RemoveKeyframe()
    {
        if (selectedKeyIndex != -1)
        {
            Undo.RecordObject(currentAsset, "Remove Keyframe");
            currentAsset.keyframes.RemoveAt(selectedKeyIndex);
            selectedKeyIndex = -1;
            EditorUtility.SetDirty(currentAsset);
            Repaint();
        }
    }

    void SortKeyframes()
    {
        var selectedKey = (selectedKeyIndex != -1) ? currentAsset.keyframes[selectedKeyIndex] : default;
        currentAsset.keyframes = currentAsset.keyframes.OrderBy(k => k.time).ToList();
        if (selectedKeyIndex != -1) selectedKeyIndex = currentAsset.keyframes.IndexOf(selectedKey);
    }

    void UpdateScenePreview()
    {
        if (previewPlayer == null)
            previewPlayer = FindFirstObjectByType<VectorTimelinePlayer>();

        if (previewPlayer != null && currentAsset != null)
        {
            if (previewPlayer.timelineData != currentAsset)
                previewPlayer.timelineData = currentAsset;

            previewPlayer.Evaluate(currentTime);
            SceneView.RepaintAll();
        }
    }
}