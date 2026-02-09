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
    private float trackHeight = 100f; // 轨道高度
    private Vector2 scrollPos = Vector2.zero;

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
    private Texture2D keyframeIcon;

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

        // 初始化纹理
        whiteTexture = new Texture2D(1, 1);
        whiteTexture.SetPixel(0, 0, Color.white);
        whiteTexture.Apply();
    }

    private void OnDisable()
    {
        EditorApplication.update -= OnEditorUpdate;
        if (whiteTexture != null) DestroyImmediate(whiteTexture);
        if (keyframeIcon != null) DestroyImmediate(keyframeIcon);
    }

    private void OnEditorUpdate()
    {
        // 拖拽时强制刷新
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

        // 1. 顶部工具栏
        DrawToolbar();

        if (currentAsset == null)
        {
            GUILayout.FlexibleSpace();
            GUILayout.Label("请选择一个 VectorTimelineAsset", EditorStyles.centeredGreyMiniLabel);
            GUILayout.FlexibleSpace();
            return;
        }

        // 2. 计算内容区域
        float maxTime = GetMaxVisibleTime();
        float contentWidth = maxTime * zoom + 100f;

        // 3. 开始滚动视图
        scrollPos = GUILayout.BeginScrollView(scrollPos, false, true, GUILayout.Height(headerHeight + trackHeight + 20));

        // 在滚动视图内部申请一块绘制区域
        Rect contentRect = GUILayoutUtility.GetRect(contentWidth, headerHeight + trackHeight);

        // --- 核心修复：绘制和交互都在这里进行 ---
        DrawTimelineContent(contentRect, maxTime);
        HandleInput(contentRect);

        GUILayout.EndScrollView();

        // 4. 底部属性面板
        DrawInspectorArea();
    }

    // =========================================================
    // 绘图逻辑
    // =========================================================

    void DrawTimelineContent(Rect rect, float maxTime)
    {
        // 背景
        EditorGUI.DrawRect(rect, new Color(0.18f, 0.18f, 0.18f));

        // 标尺背景
        Rect rulerRect = new Rect(rect.x, rect.y, rect.width, headerHeight);
        EditorGUI.DrawRect(rulerRect, new Color(0.22f, 0.22f, 0.22f));

        // 绘制网格和刻度
        Handles.color = new Color(1, 1, 1, 0.2f);
        float timeStep = GetTimeStep();

        for (float t = 0; t <= maxTime; t += timeStep)
        {
            float x = rect.x + TimeToPixel(t);
            bool isMain = Mathf.Abs(t % 1.0f) < 0.001f;
            float h = isMain ? headerHeight : headerHeight * 0.5f;

            // 刻度线
            Handles.color = isMain ? Color.gray : new Color(0.4f, 0.4f, 0.4f);
            Handles.DrawLine(new Vector3(x, rect.y + headerHeight - h), new Vector3(x, rect.y + headerHeight));

            // 垂直参考线
            if (isMain)
            {
                Handles.color = new Color(1, 1, 1, 0.05f);
                Handles.DrawLine(new Vector3(x, rect.y + headerHeight), new Vector3(x, rect.y + rect.height));
                GUI.Label(new Rect(x + 2, rect.y, 40, 20), t.ToString("0"), EditorStyles.miniLabel);
            }
        }

        // 绘制 Duration 线
        if (currentAsset.duration > 0)
        {
            float durX = rect.x + TimeToPixel(currentAsset.duration);
            Handles.color = new Color(0.2f, 0.5f, 1f, 0.8f);
            Handles.DrawLine(new Vector3(durX, rect.y), new Vector3(durX, rect.y + rect.height));
            GUI.Label(new Rect(durX + 5, rect.y + headerHeight + 5, 50, 20), "Loop End", EditorStyles.miniBoldLabel);

            // 遮罩
            Rect maskRect = new Rect(durX, rect.y + headerHeight, rect.width - (durX - rect.x), rect.height - headerHeight);
            EditorGUI.DrawRect(maskRect, new Color(0, 0, 0, 0.3f));
        }

        // 绘制关键帧连线
        float keyY = rect.y + headerHeight + (trackHeight / 2) - 8;
        Handles.color = Color.white;
        for (int i = 0; i < currentAsset.keyframes.Count - 1; i++)
        {
            float x1 = rect.x + TimeToPixel(currentAsset.keyframes[i].time);
            float x2 = rect.x + TimeToPixel(currentAsset.keyframes[i + 1].time);
            Handles.DrawDottedLine(new Vector3(x1, keyY + 8), new Vector3(x2, keyY + 8), 2f);
        }

        // 绘制关键帧节点 (菱形)
        for (int i = 0; i < currentAsset.keyframes.Count; i++)
        {
            var key = currentAsset.keyframes[i];
            float x = rect.x + TimeToPixel(key.time);
            Rect keyRect = new Rect(x - 8, keyY, 16, 16);

            // 颜色状态
            Color c = Color.white;
            if (key.isInstant) c = new Color(1f, 0.5f, 0.5f); // 红色突变
            if (i == selectedKeyIndex) c = Color.cyan;        // 选中青色

            DrawDiamond(keyRect, c);
        }

        // 绘制播放头
        float playheadX = rect.x + TimeToPixel(currentTime);
        Handles.color = Color.red;
        Handles.DrawLine(new Vector3(playheadX, rect.y), new Vector3(playheadX, rect.y + rect.height));
    }

    // 绘制菱形图标
    void DrawDiamond(Rect rect, Color color)
    {
        Color old = GUI.color;
        GUI.color = color;
        // 使用内置的 Button 样式绘制，旋转 45 度模拟菱形，或者直接画 Box
        // 这里用一个简单的 Box，为了清晰可见
        GUI.Box(rect, GUIContent.none, GUI.skin.button);
        GUI.color = old;
    }

    // =========================================================
    // 交互逻辑 (修复版：坐标系一致)
    // =========================================================

    void HandleInput(Rect rect)
    {
        Event e = Event.current;

        // 只有当鼠标在内容区域内，或者正在拖拽操作时才响应
        if (rect.Contains(e.mousePosition) || isDraggingKey || isScrubbing)
        {
            if (e.type == EventType.MouseDown && e.button == 0)
            {
                // 1. 优先检测：点击关键帧
                bool hitKey = false;
                float keyY = rect.y + headerHeight + (trackHeight / 2) - 8;

                for (int i = 0; i < currentAsset.keyframes.Count; i++)
                {
                    float x = rect.x + TimeToPixel(currentAsset.keyframes[i].time);
                    Rect keyRect = new Rect(x - 8, keyY, 16, 16);

                    if (keyRect.Contains(e.mousePosition))
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

                // 2. 如果没点中，则点击空白处 -> 移动播放头
                if (!hitKey)
                {
                    isScrubbing = true;
                    selectedKeyIndex = -1; // 取消选中
                    float localX = e.mousePosition.x - rect.x;
                    currentTime = Mathf.Max(0, PixelToTime(localX));
                    Repaint();
                    UpdateScenePreview();
                    e.Use();
                }
            }

            else if (e.type == EventType.MouseDrag && e.button == 0)
            {
                float localX = e.mousePosition.x - rect.x;
                float t = Mathf.Max(0, PixelToTime(localX));

                if (isDraggingKey && selectedKeyIndex != -1)
                {
                    // 吸附
                    if (!e.shift) t = Mathf.Round(t * 10) / 10f;

                    Undo.RecordObject(currentAsset, "Move Keyframe");
                    var key = currentAsset.keyframes[selectedKeyIndex];
                    key.time = t;
                    currentAsset.keyframes[selectedKeyIndex] = key;
                    EditorUtility.SetDirty(currentAsset);

                    currentTime = t; // 拖动时同步预览
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

            else if (e.type == EventType.MouseUp)
            {
                if (isDraggingKey) { SortKeyframes(); isDraggingKey = false; }
                isScrubbing = false;
            }
        }
    }

    // =========================================================
    // 属性面板 (Inspector)
    // =========================================================

    void DrawInspectorArea()
    {
        GUILayout.Space(10);
        GUILayout.Label("Inspector", EditorStyles.boldLabel);
        GUILayout.BeginVertical(EditorStyles.helpBox);

        // 如果选中了关键帧，显示关键帧详情
        if (selectedKeyIndex != -1 && selectedKeyIndex < currentAsset.keyframes.Count)
        {
            var key = currentAsset.keyframes[selectedKeyIndex];
            EditorGUI.BeginChangeCheck();

            EditorGUILayout.LabelField($"Selected Keyframe ({key.time:F2}s)", EditorStyles.boldLabel);

            float newTime = EditorGUILayout.FloatField("Time", key.time);
            var newShape = (VectorShapeAsset)EditorGUILayout.ObjectField("Shape", key.shapeAsset, typeof(VectorShapeAsset), false);
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

                // 时间改变时重新排序
                if (Mathf.Abs(newTime - key.time) > 0.001f) SortKeyframes();

                EditorUtility.SetDirty(currentAsset);
                UpdateScenePreview();
                Repaint();
            }
        }
        else
        {
            // 全局设置
            EditorGUILayout.LabelField("Global Settings", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            var loopMode = (VectorLoopMode)EditorGUILayout.EnumPopup("Loop Mode", currentAsset.loopMode);
            float dur = EditorGUILayout.FloatField("Total Duration", currentAsset.duration);

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(currentAsset, "Global Settings");
                currentAsset.loopMode = loopMode;
                currentAsset.duration = Mathf.Max(0, dur);
                EditorUtility.SetDirty(currentAsset);
                Repaint();
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label("Select a keyframe to edit properties.", EditorStyles.centeredGreyMiniLabel);
        }

        // 操作按钮
        GUILayout.Space(10);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("+ Add Keyframe", GUILayout.Height(24))) AddKeyframe();

        GUI.enabled = selectedKeyIndex != -1;
        if (GUILayout.Button("- Remove Selected", GUILayout.Height(24))) RemoveKeyframe();
        GUI.enabled = true;
        GUILayout.EndHorizontal();

        GUILayout.EndVertical();
    }

    // =========================================================
    // 辅助 & 工具
    // =========================================================

    void DrawToolbar()
    {
        GUILayout.BeginHorizontal(EditorStyles.toolbar);

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

        GUILayout.Space(10);
        GUILayout.Label("Player:", GUILayout.Width(45));
        var newPlayer = (VectorTimelinePlayer)EditorGUILayout.ObjectField(previewPlayer, typeof(VectorTimelinePlayer), true, GUILayout.Width(120));
        if (newPlayer != previewPlayer) previewPlayer = newPlayer;

        GUILayout.Space(10);
        if (GUILayout.Button("⏮", EditorStyles.toolbarButton, GUILayout.Width(25))) { currentTime = 0; UpdateScenePreview(); }
        bool newPlaying = GUILayout.Toggle(isPlaying, isPlaying ? "⏸" : "▶", EditorStyles.toolbarButton, GUILayout.Width(35));
        if (newPlaying != isPlaying) { isPlaying = newPlaying; lastEditorTime = EditorApplication.timeSinceStartup; }
        if (GUILayout.Button("⏹", EditorStyles.toolbarButton, GUILayout.Width(25))) { isPlaying = false; currentTime = 0; UpdateScenePreview(); }

        GUILayout.FlexibleSpace();
        GUILayout.Label("Zoom:", GUILayout.Width(40));
        zoom = GUILayout.HorizontalSlider(zoom, 10f, 500f, GUILayout.Width(100));
        GUILayout.EndHorizontal();
    }

    float GetMaxVisibleTime()
    {
        float maxT = currentAsset.duration > 0 ? currentAsset.duration : 0f;
        if (currentAsset.keyframes.Count > 0)
        {
            float lastKeyTime = currentAsset.keyframes.Max(k => k.time);
            if (lastKeyTime > maxT) maxT = lastKeyTime;
        }
        return Mathf.Max(maxT, 5.0f);
    }

    float GetTimeStep()
    {
        if (zoom > 300) return 0.1f;
        if (zoom > 100) return 0.5f;
        return 1.0f;
    }

    float TimeToPixel(float time) => time * zoom;
    float PixelToTime(float pixel) => pixel / zoom;

    void InitStyles()
    {
        if (keyframeStyle == null) keyframeStyle = new GUIStyle(GUI.skin.button);
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
        if (currentAsset.keyframes.Count > 0) newKey.shapeAsset = currentAsset.keyframes.Last().shapeAsset;

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
        if (previewPlayer == null) previewPlayer = FindFirstObjectByType<VectorTimelinePlayer>();
        if (previewPlayer != null && currentAsset != null)
        {
            if (previewPlayer.timelineData != currentAsset) previewPlayer.timelineData = currentAsset;
            previewPlayer.Evaluate(currentTime);
            SceneView.RepaintAll();
        }
    }
}