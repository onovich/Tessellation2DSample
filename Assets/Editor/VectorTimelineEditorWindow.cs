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
    private float panX = 0f;   // 视图偏移量
    private float headerHeight = 30f;
    private float trackHeight = 80f; // 轨道高度增加，方便操作
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

    // --- 核心更新循环 ---
    private void OnEditorUpdate()
    {
        // 只要窗口开着，就尝试同步预览（解决拖动时间轴没反应的问题）
        if (isScrubbing || isDraggingKey)
        {
            UpdateScenePreview();
        }

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

        // 1. 工具栏
        DrawToolbar();

        if (currentAsset == null)
        {
            GUILayout.FlexibleSpace();
            GUILayout.Label("请选择一个 VectorTimelineAsset", EditorStyles.centeredGreyMiniLabel);
            GUILayout.FlexibleSpace();
            return;
        }

        // 2. 时间轴区域
        Rect timelineRect = GUILayoutUtility.GetRect(position.width, headerHeight + trackHeight);
        GUI.Box(timelineRect, GUIContent.none, EditorStyles.helpBox);

        // 处理输入 (缩放、平移、点击)
        HandleTimelineInput(timelineRect);

        // 绘制内容 (限制在区域内)
        GUI.BeginGroup(timelineRect);
        DrawTimelineContent(timelineRect);
        GUI.EndGroup();

        // 3. 属性面板
        DrawInspectorArea();
    }

    // =========================================================
    // 区域绘制
    // =========================================================

    void DrawToolbar()
    {
        GUILayout.BeginHorizontal(EditorStyles.toolbar);

        GUILayout.Label("Target Asset:", GUILayout.Width(80));
        EditorGUI.BeginChangeCheck();
        var newAsset = (VectorTimelineAsset)EditorGUILayout.ObjectField(currentAsset, typeof(VectorTimelineAsset), false, GUILayout.Width(200));
        if (EditorGUI.EndChangeCheck())
        {
            currentAsset = newAsset;
            selectedKeyIndex = -1;
            currentTime = 0;
            isPlaying = false;
        }

        GUILayout.Space(20);
        GUILayout.Label("Preview Player:", GUILayout.Width(90));
        // 这里允许用户手动拖入 Player，如果为空则尝试自动找
        var newPlayer = (VectorTimelinePlayer)EditorGUILayout.ObjectField(previewPlayer, typeof(VectorTimelinePlayer), true, GUILayout.Width(150));
        if (newPlayer != previewPlayer) previewPlayer = newPlayer;

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

    void DrawTimelineContent(Rect rect)
    {
        // 1. 绘制背景网格
        DrawGrid(rect);

        // 2. 绘制标尺 (Ruler)
        DrawRuler(rect);

        // 3. 绘制关键帧
        DrawKeyframes(rect);

        // 4. 绘制播放头
        float playheadX = TimeToPixel(currentTime);
        // 确保播放头在视野内才绘制（可选，但画了也没事）
        Handles.color = Color.red;
        Handles.DrawLine(new Vector3(playheadX, 0), new Vector3(playheadX, rect.height));
        // 绘制一个小帽子
        Vector3[] hat = { new Vector3(playheadX - 5, 0), new Vector3(playheadX + 5, 0), new Vector3(playheadX, 10) };
        Handles.DrawAAConvexPolygon(hat);
    }

    void DrawGrid(Rect rect)
    {
        // 简单的背景色
        Rect bg = new Rect(0, headerHeight, rect.width, rect.height - headerHeight);
        EditorGUI.DrawRect(bg, new Color(0.18f, 0.18f, 0.18f));
    }

    void DrawRuler(Rect rect)
    {
        Rect rulerRect = new Rect(0, 0, rect.width, headerHeight);
        EditorGUI.DrawRect(rulerRect, new Color(0.22f, 0.22f, 0.22f));

        Handles.color = Color.gray;

        // 根据缩放级别决定刻度步长
        // Zoom = 100 (1s=100px) -> Step 0.1s
        // Zoom = 10 (1s=10px) -> Step 1.0s
        float timeStep = 1.0f;
        if (zoom > 500) timeStep = 0.05f;
        else if (zoom > 150) timeStep = 0.1f;
        else if (zoom > 50) timeStep = 0.5f;
        else timeStep = 1.0f;

        // 计算可见区域的起始和结束时间，避免绘制无效区域
        float startTime = PixelToTime(0);
        float endTime = PixelToTime(rect.width);

        // 向下取整到最近的 step
        float t = Mathf.Floor(startTime / timeStep) * timeStep;

        while (t <= endTime)
        {
            float x = TimeToPixel(t);

            // 区分主刻度（整数秒）和次刻度
            bool isMain = Mathf.Abs(t % 1.0f) < 0.001f;
            float h = isMain ? headerHeight : headerHeight * 0.5f;

            Handles.color = isMain ? new Color(0.6f, 0.6f, 0.6f) : new Color(0.4f, 0.4f, 0.4f);
            Handles.DrawLine(new Vector3(x, headerHeight - h), new Vector3(x, headerHeight));

            if (isMain)
            {
                GUI.Label(new Rect(x + 2, 0, 40, 20), t.ToString("0.##"), EditorStyles.miniLabel);
            }

            t += timeStep;
        }
    }

    void DrawKeyframes(Rect rect)
    {
        float keyY = headerHeight + (trackHeight / 2) - 6;

        for (int i = 0; i < currentAsset.keyframes.Count; i++)
        {
            var key = currentAsset.keyframes[i];
            float x = TimeToPixel(key.time);

            // 绘制连线
            if (i < currentAsset.keyframes.Count - 1)
            {
                float nextX = TimeToPixel(currentAsset.keyframes[i + 1].time);
                Handles.color = new Color(0.5f, 0.5f, 0.5f, 0.3f);
                Handles.DrawLine(new Vector3(x, keyY + 6), new Vector3(nextX, keyY + 6));
            }

            Rect keyRect = new Rect(x - 6, keyY, 12, 12);

            Color c = (i == selectedKeyIndex) ? Color.cyan : Color.white;
            if (key.isInstant) c = new Color(1f, 0.4f, 0.4f); // 红色表示突变

            GUI.color = c;
            GUI.Box(keyRect, GUIContent.none, keyframeStyle);
            GUI.color = Color.white;

            // 绘制选中提示
            if (i == selectedKeyIndex)
            {
                GUI.Label(new Rect(x - 10, keyY - 15, 50, 20), $"{key.time:F2}", EditorStyles.whiteMiniLabel);
            }
        }
    }

    // =========================================================
    // 交互逻辑 (缩放、平移、拖拽)
    // =========================================================

    void HandleTimelineInput(Rect rect)
    {
        Event e = Event.current;

        // 1. 缩放 (Scroll Wheel)
        if (e.type == EventType.ScrollWheel && rect.Contains(e.mousePosition))
        {
            // 鼠标位置对应的时间（为了以鼠标为中心缩放）
            float mouseTime = PixelToTime(e.mousePosition.x - rect.x);

            float scrollDelta = -e.delta.y; // 向下滚是负，放大
            float zoomSpeed = zoom * 0.1f;  // 基于当前 zoom 的速度

            float oldZoom = zoom;
            zoom += scrollDelta * zoomSpeed;
            zoom = Mathf.Clamp(zoom, 10f, 1000f); // 限制缩放范围

            // 调整 panX 以保持鼠标指向的时间不变
            // mouseTime = (mouseX - panX) / oldZoom  => panX = mouseX - mouseTime * oldZoom
            // 新 panX = mouseX - mouseTime * newZoom
            float localMouseX = e.mousePosition.x - rect.x;
            panX = localMouseX - mouseTime * zoom;

            e.Use();
            Repaint();
        }

        // 2. 平移 (中键或右键拖拽)
        if (e.type == EventType.MouseDrag && (e.button == 2 || e.button == 1) && rect.Contains(e.mousePosition))
        {
            panX += e.delta.x;
            e.Use();
            Repaint();
        }

        // 3. 左键交互 (关键帧 & 播放头)
        if (rect.Contains(e.mousePosition) || isDraggingKey || isScrubbing)
        {
            Vector2 localPos = e.mousePosition - rect.position; // 转为相对坐标

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                bool hitKey = false;
                for (int i = 0; i < currentAsset.keyframes.Count; i++)
                {
                    float x = TimeToPixel(currentAsset.keyframes[i].time);
                    float y = headerHeight + (trackHeight / 2) - 6;
                    Rect keyRect = new Rect(x - 6, y, 12, 12);

                    if (keyRect.Contains(localPos))
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
                    currentTime = Mathf.Max(0, PixelToTime(localPos.x));
                    Repaint();
                    UpdateScenePreview();
                    e.Use();
                }
            }

            if (e.type == EventType.MouseDrag && e.button == 0)
            {
                float t = Mathf.Max(0, PixelToTime(localPos.x));

                if (isDraggingKey && selectedKeyIndex != -1)
                {
                    if (!e.shift) t = Mathf.Round(t * 10) / 10f; // 吸附

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

        // 全局
        EditorGUILayout.LabelField("Global Settings", EditorStyles.miniBoldLabel);
        EditorGUI.BeginChangeCheck();
        var loopMode = (VectorLoopMode)EditorGUILayout.EnumPopup("Loop Mode", currentAsset.loopMode);
        float dur = EditorGUILayout.FloatField("Total Duration", currentAsset.duration);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(currentAsset, "Change Settings");
            currentAsset.loopMode = loopMode;
            currentAsset.duration = dur;
            EditorUtility.SetDirty(currentAsset);
        }

        GUILayout.Space(10);

        // 操作按钮
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("+ Add Keyframe", GUILayout.Height(24))) AddKeyframe();
        GUI.enabled = selectedKeyIndex != -1;
        if (GUILayout.Button("- Remove Selected", GUILayout.Height(24))) RemoveKeyframe();
        GUI.enabled = true;
        GUILayout.EndHorizontal();

        GUILayout.Space(5);

        // 选中帧属性
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
            }
        }
        else
        {
            GUILayout.Label("No keyframe selected.", EditorStyles.centeredGreyMiniLabel);
        }
        GUILayout.EndVertical();
    }

    // =========================================================
    // 辅助方法
    // =========================================================

    float TimeToPixel(float time) => time * zoom + panX;
    float PixelToTime(float pixel) => (pixel - panX) / zoom;

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
        // 继承上一帧
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
        // 1. 自动寻找 Player (如果丢失)
        if (previewPlayer == null)
            previewPlayer = FindFirstObjectByType<VectorTimelinePlayer>();

        // 2. 强力同步逻辑
        if (previewPlayer != null && currentAsset != null)
        {
            // 只有当 Player 的数据不是当前编辑的数据时，才强制赋值
            // 这样可以避免每帧赋值造成的额外开销，但保证了数据一致性
            if (previewPlayer.timelineData != currentAsset)
                previewPlayer.timelineData = currentAsset;

            // 强制驱动渲染
            previewPlayer.Evaluate(currentTime);

            // 强制刷新 Scene 视图 (解决拖拽不更新的问题)
            SceneView.RepaintAll();
        }
    }
}