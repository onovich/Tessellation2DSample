using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

public class VectorTimelineEditorWindow : EditorWindow
{
    // --- 数据引用 ---
    private VectorTimelineAsset currentAsset;
    private VectorTimelinePlayer previewPlayer;

    // --- 视图状态 ---
    private float zoom = 100f; // 1秒 = 100像素
    private float headerHeight = 30f;
    private float trackHeight = 60f;
    private float sidebarWidth = 0f;

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
        // 自动查找场景中的播放器
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

    // --- 核心更新循环 ---
    private void OnEditorUpdate()
    {
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

        // 1. 顶部工具栏 (智能加载发生在这里)
        DrawToolbar();

        if (currentAsset == null)
        {
            GUILayout.FlexibleSpace();
            var style = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter, fontSize = 14 };
            GUILayout.Label("请在顶部选择一个 Timeline Asset 进行编辑", style);
            GUILayout.FlexibleSpace();
            return;
        }

        // 2. 绘制时间轴区域
        Rect timelineRect = GUILayoutUtility.GetRect(position.width, headerHeight + trackHeight + 20);
        GUI.Box(timelineRect, GUIContent.none, EditorStyles.helpBox);

        // 处理输入事件
        HandleTimelineEvents(timelineRect);

        if (Event.current.type == EventType.Repaint)
        {
            DrawTimelineContent(timelineRect);
        }

        // 3. 属性面板 (自动保存发生在这里)
        DrawInspectorArea();
    }

    // =========================================================
    // 区域 1: 工具栏 (实现智能加载)
    // =========================================================

    void DrawToolbar()
    {
        GUILayout.BeginHorizontal(EditorStyles.toolbar);

        GUILayout.Label("Target Asset:", GUILayout.Width(80));

        // ✨ 智能加载逻辑：
        // 只要 ObjectField 的返回值变了，我们就认为用户切换了 Asset
        EditorGUI.BeginChangeCheck();
        var newAsset = (VectorTimelineAsset)EditorGUILayout.ObjectField(currentAsset, typeof(VectorTimelineAsset), false, GUILayout.Width(200));
        if (EditorGUI.EndChangeCheck())
        {
            if (newAsset != currentAsset)
            {
                currentAsset = newAsset;
                OnAssetChanged(); // 触发切换逻辑
            }
        }

        GUILayout.Space(20);
        GUILayout.Label("Preview Player:", GUILayout.Width(90));
        previewPlayer = (VectorTimelinePlayer)EditorGUILayout.ObjectField(previewPlayer, typeof(VectorTimelinePlayer), true, GUILayout.Width(150));

        GUILayout.FlexibleSpace();

        // 播放控制
        if (GUILayout.Button("⏮", EditorStyles.toolbarButton, GUILayout.Width(30)))
        { currentTime = 0; UpdateScenePreview(); }

        bool newPlaying = GUILayout.Toggle(isPlaying, isPlaying ? "⏸" : "▶", EditorStyles.toolbarButton, GUILayout.Width(40));
        if (newPlaying != isPlaying)
        {
            isPlaying = newPlaying;
            lastEditorTime = EditorApplication.timeSinceStartup;
        }

        if (GUILayout.Button("⏹", EditorStyles.toolbarButton, GUILayout.Width(30)))
        { isPlaying = false; currentTime = 0; UpdateScenePreview(); }

        GUILayout.Space(10);
        string totalTime = currentAsset ? currentAsset.GetDuration().ToString("F2") : "0.00";
        GUILayout.Label($"{currentTime:F2}s / {totalTime}s", GUILayout.Width(100));

        GUILayout.EndHorizontal();
    }

    // 切换 Asset 时重置状态
    void OnAssetChanged()
    {
        selectedKeyIndex = -1;
        currentTime = 0;
        isPlaying = false;
        // 如果需要，这里可以自动保存上一个 Asset (其实不需要，因为修改时已经实时保存了)
        Repaint();
    }

    // =========================================================
    // 区域 2: 时间轴绘制 (保持不变)
    // =========================================================
    // ... (此处 DrawTimelineContent 代码与上一版完全一致，为节省篇幅省略，请保留上一版的实现) ...
    // 如果你需要完整代码，我可以重新贴这部分，但逻辑没变。

    void DrawTimelineContent(Rect rect)
    {
        GUI.BeginGroup(rect);
        // ... (复制上一版的 DrawTimelineContent 实现) ...
        // 简写示意：
        Rect rulerRect = new Rect(sidebarWidth, 0, rect.width - sidebarWidth, headerHeight);
        GUI.color = new Color(0.2f, 0.2f, 0.2f); GUI.DrawTexture(rulerRect, whiteTexture); GUI.color = Color.white;

        // 绘制刻度...
        Handles.color = Color.gray;
        float maxTime = PixelToTime(rect.width);
        for (float t = 0; t <= maxTime; t += 0.1f)
        {
            float x = TimeToPixel(t);
            bool isSecond = Mathf.Abs(t % 1.0f) < 0.001f;
            float h = isSecond ? 15 : 5;
            Handles.DrawLine(new Vector3(x, headerHeight - h), new Vector3(x, headerHeight));
            if (isSecond) GUI.Label(new Rect(x, 0, 30, 20), t.ToString("0"));
        }

        // 绘制关键帧...
        for (int i = 0; i < currentAsset.keyframes.Count; i++)
        {
            var key = currentAsset.keyframes[i];
            float x = TimeToPixel(key.time);
            Rect keyRect = new Rect(x - 6, headerHeight + 24, 12, 12);
            GUI.color = (i == selectedKeyIndex) ? Color.cyan : Color.white;
            if (key.isInstant) GUI.color = Color.red;
            GUI.Box(keyRect, "", "Button");
        }
        GUI.color = Color.white;

        // 绘制播放头...
        float px = TimeToPixel(currentTime);
        Handles.color = Color.red;
        Handles.DrawLine(new Vector3(px, 0), new Vector3(px, rect.height));

        GUI.EndGroup();
    }


    // =========================================================
    // 区域 3: 交互事件 (实现拖拽自动保存)
    // =========================================================

    void HandleTimelineEvents(Rect rect)
    {
        Event e = Event.current;
        Vector2 mousePos = e.mousePosition;

        if (rect.Contains(mousePos) || isDraggingKey || isScrubbing)
        {
            // Mouse Down
            if (e.type == EventType.MouseDown && e.button == 0)
            {
                bool hitKey = false;
                for (int i = 0; i < currentAsset.keyframes.Count; i++)
                {
                    float x = TimeToPixel(currentAsset.keyframes[i].time);
                    Rect keyRect = new Rect(x - 6 + rect.x, headerHeight + 24 + rect.y, 12, 12); // 坐标校准

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
                if (!hitKey)
                {
                    isScrubbing = true;
                    selectedKeyIndex = -1;
                    currentTime = Mathf.Max(0, PixelToTime(mousePos.x - rect.x));
                    Repaint();
                    UpdateScenePreview();
                    e.Use();
                }
            }

            // Mouse Drag
            if (e.type == EventType.MouseDrag && e.button == 0)
            {
                float t = Mathf.Max(0, PixelToTime(mousePos.x - rect.x));

                if (isDraggingKey && selectedKeyIndex != -1)
                {
                    if (!e.shift) t = Mathf.Round(t * 10) / 10f; // 吸附

                    // ✨ 自动保存关键：Undo.RecordObject
                    Undo.RecordObject(currentAsset, "Move Keyframe");

                    var key = currentAsset.keyframes[selectedKeyIndex];
                    key.time = t;
                    currentAsset.keyframes[selectedKeyIndex] = key;

                    // 标记 Dirty，确保写入磁盘
                    EditorUtility.SetDirty(currentAsset);

                    currentTime = t;
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

            // Mouse Up
            if (e.type == EventType.MouseUp)
            {
                if (isDraggingKey) { SortKeyframes(); isDraggingKey = false; }
                isScrubbing = false;
                e.Use();
            }
        }
    }

    // =========================================================
    // 区域 4: 属性面板 (实现修改自动保存)
    // =========================================================

    void DrawInspectorArea()
    {
        GUILayout.Space(10);
        GUILayout.Label("Inspector", EditorStyles.boldLabel);
        GUILayout.BeginVertical(EditorStyles.helpBox);

        // --- 全局设置 ---
        EditorGUILayout.LabelField("Global Settings", EditorStyles.miniBoldLabel);

        // ✨ 自动保存关键：BeginChangeCheck -> EndChangeCheck
        EditorGUI.BeginChangeCheck();

        var loopMode = (VectorLoopMode)EditorGUILayout.EnumPopup("Loop Mode", currentAsset.loopMode);
        float dur = EditorGUILayout.FloatField("Total Duration (0=Auto)", currentAsset.duration);

        if (EditorGUI.EndChangeCheck())
        {
            // 只有当值真的改变时，才执行保存逻辑
            Undo.RecordObject(currentAsset, "Change Global Settings");
            currentAsset.loopMode = loopMode;
            currentAsset.duration = dur;
            EditorUtility.SetDirty(currentAsset); // 存盘标记
        }

        GUILayout.Space(10);

        // --- 关键帧操作 ---
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("+ Add Keyframe", GUILayout.Height(24))) AddKeyframe();
        GUI.enabled = selectedKeyIndex != -1;
        if (GUILayout.Button("- Remove Selected", GUILayout.Height(24))) RemoveKeyframe();
        GUI.enabled = true;
        GUILayout.EndHorizontal();

        GUILayout.Space(5);

        // --- 选中帧属性 ---
        if (selectedKeyIndex != -1 && selectedKeyIndex < currentAsset.keyframes.Count)
        {
            var key = currentAsset.keyframes[selectedKeyIndex];

            // ✨ 自动保存关键：包裹整个属性块
            EditorGUI.BeginChangeCheck();

            float newTime = EditorGUILayout.FloatField("Time", key.time);
            var newShape = (VectorShapeAsset)EditorGUILayout.ObjectField("Shape Asset", key.shapeAsset, typeof(VectorShapeAsset), false);
            float newScale = EditorGUILayout.Slider("Scale", key.scale, 0f, 2f);
            bool newInstant = EditorGUILayout.Toggle("Is Instant", key.isInstant);
            int newOffset = EditorGUILayout.IntSlider("Align Offset", key.alignOffset, 0, 360);
            AnimationCurve newCurve = EditorGUILayout.CurveField("Transition Curve", key.curve);

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

                EditorUtility.SetDirty(currentAsset); // 存盘标记
                UpdateScenePreview();
            }
        }
        else
        {
            GUILayout.Label("Select a keyframe to edit.", EditorStyles.centeredGreyMiniLabel);
        }
        GUILayout.EndVertical();
    }

    // =========================================================
    // 辅助方法
    // =========================================================

    float TimeToPixel(float time) => time * zoom + sidebarWidth;
    float PixelToTime(float pixel) => (pixel - sidebarWidth) / zoom;

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
        // 继承上一帧的 Shape
        if (currentAsset.keyframes.Count > 0)
        {
            var last = currentAsset.keyframes[currentAsset.keyframes.Count - 1];
            newKey.shapeAsset = last.shapeAsset;
        }
        currentAsset.keyframes.Add(newKey);
        SortKeyframes();
        selectedKeyIndex = currentAsset.keyframes.IndexOf(newKey);
        EditorUtility.SetDirty(currentAsset); // 自动保存
        Repaint();
    }

    void RemoveKeyframe()
    {
        if (selectedKeyIndex != -1)
        {
            Undo.RecordObject(currentAsset, "Remove Keyframe");
            currentAsset.keyframes.RemoveAt(selectedKeyIndex);
            selectedKeyIndex = -1;
            EditorUtility.SetDirty(currentAsset); // 自动保存
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
        if (previewPlayer != null && currentAsset != null)
        {
            if (previewPlayer.timelineData != currentAsset)
                previewPlayer.timelineData = currentAsset;
            previewPlayer.Evaluate(currentTime);
            SceneView.RepaintAll();
        }
    }
}