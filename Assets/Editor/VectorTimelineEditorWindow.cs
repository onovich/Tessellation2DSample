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
    private float trackHeight = 100f;
    private Vector2 scrollPos = Vector2.zero;

    // --- 播放状态 ---
    private float currentTime = 0f;
    private bool isPlaying = false;
    private double lastEditorTime;

    // --- 编辑交互状态 ---
    private int selectedKeyIndex = -1;
    private bool isDraggingKey = false;
    private bool isScrubbing = false;
    private bool isDraggingDuration = false;

    // ✨ 新增：用于编辑器预览 PingPong 模式的方向记录
    private bool isReversing = false;

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
        // -----------------------------------------------------------------
        // 场景 A: 运行时 (Runtime) -> 观察者模式
        // -----------------------------------------------------------------
        if (Application.isPlaying)
        {
            if (previewPlayer == null) previewPlayer = FindFirstObjectByType<VectorTimelinePlayer>();

            if (previewPlayer != null && currentAsset != null)
            {
                // 确保数据引用
                if (previewPlayer.timelineData != currentAsset)
                    previewPlayer.timelineData = currentAsset;

                // 读取 Player 的原始时间
                float rawTime = previewPlayer.CurrentTime;
                float dur = currentAsset.GetDuration();

                // 映射红线位置
                if (dur > 0.0001f)
                {
                    switch (currentAsset.loopMode)
                    {
                        case VectorLoopMode.Once:
                            currentTime = Mathf.Clamp(rawTime, 0, dur);
                            break;
                        case VectorLoopMode.Loop:
                            currentTime = Mathf.Repeat(rawTime, dur);
                            break;
                        case VectorLoopMode.PingPong:
                            currentTime = Mathf.PingPong(rawTime, dur);
                            break;
                    }
                }
                else
                {
                    currentTime = rawTime;
                }

                // 强制重绘
                if (previewPlayer.IsPlaying) Repaint();
            }
            return;
        }

        // -----------------------------------------------------------------
        // 场景 B: 编辑器预览 (Editor) -> 控制者模式
        // -----------------------------------------------------------------

        if (isScrubbing || isDraggingKey || isDraggingDuration)
        {
            UpdateScenePreview();
            // 如果用户手动拖拽，重置反向状态，下次播放从正向开始
            isReversing = false;
        }

        if (isPlaying && currentAsset != null)
        {
            double timeNow = EditorApplication.timeSinceStartup;
            double delta = timeNow - lastEditorTime;
            lastEditorTime = timeNow;

            float duration = currentAsset.GetDuration();

            // ✨✨ 核心修复：编辑器预览的 PingPong 逻辑 ✨✨
            if (duration > 0)
            {
                if (currentAsset.loopMode == VectorLoopMode.PingPong)
                {
                    // 1. 根据方向增减时间
                    if (isReversing) currentTime -= (float)delta;
                    else currentTime += (float)delta;

                    // 2. 触顶反弹
                    if (currentTime >= duration)
                    {
                        currentTime = duration; // 修正超出的部分
                        isReversing = true;     // 切换为倒放
                    }
                    // 3. 触底反弹
                    else if (currentTime <= 0)
                    {
                        currentTime = 0;
                        isReversing = false;    // 切换为正放
                    }
                }
                else
                {
                    // 普通 Loop / Once 逻辑
                    isReversing = false; // 确保切模式后不倒放
                    currentTime += (float)delta;

                    if (currentTime > duration)
                    {
                        if (currentAsset.loopMode == VectorLoopMode.Loop)
                            currentTime %= duration;
                        else if (currentAsset.loopMode == VectorLoopMode.Once)
                        {
                            currentTime = duration;
                            isPlaying = false;
                        }
                    }
                }
            }
            else
            {
                // 没有 Duration 时，只能无限增加
                currentTime += (float)delta;
            }

            UpdateScenePreview();
            Repaint();
        }
    }

    private void OnGUI()
    {
        InitStyles();
        DrawToolbar();

        if (currentAsset == null)
        {
            GUILayout.FlexibleSpace();
            GUILayout.Label("请选择一个 VectorTimelineAsset", EditorStyles.centeredGreyMiniLabel);
            GUILayout.FlexibleSpace();
            return;
        }

        float maxVisibleTime = Mathf.Max(GetMaxKeyframeTime(), currentAsset.duration) + 1.0f;
        float contentWidth = maxVisibleTime * zoom + 100f;

        scrollPos = GUILayout.BeginScrollView(scrollPos, false, true, GUILayout.Height(headerHeight + trackHeight + 20));

        Rect contentRect = GUILayoutUtility.GetRect(contentWidth, headerHeight + trackHeight);

        DrawTimelineContent(contentRect, maxVisibleTime);
        HandleInput(contentRect);

        GUILayout.EndScrollView();

        DrawInspectorArea();
    }

    // =========================================================
    // 绘图逻辑
    // =========================================================

    void DrawTimelineContent(Rect rect, float maxTime)
    {
        EditorGUI.DrawRect(rect, new Color(0.18f, 0.18f, 0.18f));

        Rect rulerRect = new Rect(rect.x, rect.y, rect.width, headerHeight);
        EditorGUI.DrawRect(rulerRect, new Color(0.22f, 0.22f, 0.22f));

        Handles.color = new Color(1, 1, 1, 0.2f);
        float timeStep = GetTimeStep();

        for (float t = 0; t <= maxTime; t += timeStep)
        {
            float x = rect.x + TimeToPixel(t);
            bool isMain = Mathf.Abs(t % 1.0f) < 0.001f;
            float h = isMain ? headerHeight : headerHeight * 0.5f;

            Handles.color = isMain ? Color.gray : new Color(0.4f, 0.4f, 0.4f);
            Handles.DrawLine(new Vector3(x, rect.y + headerHeight - h), new Vector3(x, rect.y + headerHeight));

            if (isMain)
            {
                Handles.color = new Color(1, 1, 1, 0.05f);
                Handles.DrawLine(new Vector3(x, rect.y + headerHeight), new Vector3(x, rect.y + rect.height));
                GUI.Label(new Rect(x + 2, rect.y, 40, 20), t.ToString("0"), EditorStyles.miniLabel);
            }
        }

        if (currentAsset.duration > 0)
        {
            float durX = rect.x + TimeToPixel(currentAsset.duration);
            Handles.color = new Color(0.2f, 0.6f, 1f, 0.8f);
            Handles.DrawLine(new Vector3(durX, rect.y), new Vector3(durX, rect.y + rect.height));

            Vector3[] handle = {
                new Vector3(durX - 6, rect.y),
                new Vector3(durX + 6, rect.y),
                new Vector3(durX, rect.y + 12)
            };
            Handles.DrawAAConvexPolygon(handle);
            GUI.Label(new Rect(durX + 5, rect.y + 2, 50, 20), "End", EditorStyles.miniBoldLabel);

            if (rect.width + rect.x > durX)
            {
                Rect maskRect = new Rect(durX, rect.y + headerHeight, (rect.width + rect.x) - durX, rect.height - headerHeight);
                EditorGUI.DrawRect(maskRect, new Color(0, 0, 0, 0.5f));
            }
        }

        float keyY = rect.y + headerHeight + (trackHeight / 2) - 8;
        Handles.color = Color.white;
        for (int i = 0; i < currentAsset.keyframes.Count - 1; i++)
        {
            float x1 = rect.x + TimeToPixel(currentAsset.keyframes[i].time);
            float x2 = rect.x + TimeToPixel(currentAsset.keyframes[i + 1].time);
            Handles.DrawDottedLine(new Vector3(x1, keyY + 8), new Vector3(x2, keyY + 8), 2f);
        }

        for (int i = 0; i < currentAsset.keyframes.Count; i++)
        {
            var key = currentAsset.keyframes[i];
            float x = rect.x + TimeToPixel(key.time);
            Rect keyRect = new Rect(x - 8, keyY, 16, 16);

            Color c = Color.white;
            if (key.isInstant) c = new Color(1f, 0.5f, 0.5f);
            if (i == selectedKeyIndex) c = Color.cyan;
            if (currentAsset.duration > 0 && key.time > currentAsset.duration) c *= 0.5f;

            DrawDiamond(keyRect, c);
        }

        float playheadX = rect.x + TimeToPixel(currentTime);
        Handles.color = new Color(1f, 0.2f, 0.2f, 1f);
        Handles.DrawLine(new Vector3(playheadX, rect.y), new Vector3(playheadX, rect.y + rect.height));
        Vector3[] headHandle = {
            new Vector3(playheadX - 6, rect.y),
            new Vector3(playheadX + 6, rect.y),
            new Vector3(playheadX, rect.y + 12)
        };
        Handles.DrawAAConvexPolygon(headHandle);

        if (Application.isPlaying)
        {
            GUI.Label(new Rect(rect.x + 10, rect.y + headerHeight + 5, 200, 20), "▶ RUNTIME MODE (READ ONLY)", EditorStyles.boldLabel);
        }
    }

    void DrawDiamond(Rect rect, Color color)
    {
        Color old = GUI.color;
        GUI.color = color;
        GUI.Box(rect, GUIContent.none, GUI.skin.button);
        GUI.color = old;
    }

    // =========================================================
    // 交互逻辑
    // =========================================================

    void HandleInput(Rect rect)
    {
        if (Application.isPlaying) return;

        Event e = Event.current;

        if (rect.Contains(e.mousePosition) || isDraggingKey || isScrubbing || isDraggingDuration)
        {
            float localX = e.mousePosition.x - rect.x;
            float mouseTime = Mathf.Max(0, PixelToTime(localX));

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                float pixelMouse = e.mousePosition.x;
                float pixelPlayhead = rect.x + TimeToPixel(currentTime);
                float pixelDuration = rect.x + TimeToPixel(currentAsset.duration);
                float threshold = 8f;

                bool hitPlayhead = Mathf.Abs(pixelMouse - pixelPlayhead) < threshold;
                bool hitDuration = Mathf.Abs(pixelMouse - pixelDuration) < threshold;

                if (hitPlayhead && hitDuration) hitDuration = false;

                bool hitKey = false;
                if (!hitPlayhead && !hitDuration)
                {
                    float keyY = rect.y + headerHeight + (trackHeight / 2) - 8;
                    for (int i = 0; i < currentAsset.keyframes.Count; i++)
                    {
                        float kx = rect.x + TimeToPixel(currentAsset.keyframes[i].time);
                        Rect keyRect = new Rect(kx - 8, keyY, 16, 16);
                        if (keyRect.Contains(e.mousePosition))
                        {
                            selectedKeyIndex = i;
                            isDraggingKey = true;
                            hitKey = true;
                            Repaint();
                            e.Use();
                            break;
                        }
                    }
                }

                if (!hitKey)
                {
                    if (hitPlayhead) { isScrubbing = true; selectedKeyIndex = -1; e.Use(); }
                    else if (hitDuration) { isDraggingDuration = true; Undo.RecordObject(currentAsset, "Modify Duration"); e.Use(); }
                    else { isScrubbing = true; selectedKeyIndex = -1; currentTime = mouseTime; Repaint(); UpdateScenePreview(); e.Use(); }
                }
            }
            else if (e.type == EventType.MouseDrag && e.button == 0)
            {
                if (isDraggingKey && selectedKeyIndex != -1)
                {
                    if (!e.shift) mouseTime = Mathf.Round(mouseTime * 10) / 10f;
                    Undo.RecordObject(currentAsset, "Move Keyframe");
                    var key = currentAsset.keyframes[selectedKeyIndex];
                    key.time = mouseTime;
                    currentAsset.keyframes[selectedKeyIndex] = key;
                    EditorUtility.SetDirty(currentAsset);
                    currentTime = mouseTime; UpdateScenePreview(); e.Use();
                }
                else if (isScrubbing) { currentTime = mouseTime; UpdateScenePreview(); Repaint(); e.Use(); }
                else if (isDraggingDuration)
                {
                    if (!e.shift) mouseTime = Mathf.Round(mouseTime * 10) / 10f;
                    currentAsset.duration = Mathf.Max(0.1f, mouseTime);
                    EditorUtility.SetDirty(currentAsset);
                    Repaint(); UpdateScenePreview(); e.Use();
                }
            }
            else if (e.type == EventType.MouseUp)
            {
                if (isDraggingKey) SortKeyframes();
                isDraggingKey = false; isScrubbing = false; isDraggingDuration = false; e.Use();
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
        GUI.enabled = !Application.isPlaying;

        GUILayout.BeginVertical(EditorStyles.helpBox);

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
                if (Mathf.Abs(newTime - key.time) > 0.001f) SortKeyframes();
                EditorUtility.SetDirty(currentAsset);
                UpdateScenePreview();
                Repaint();
            }
        }
        else
        {
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
        }

        GUILayout.Space(10);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("+ Add Keyframe", GUILayout.Height(24))) AddKeyframe();
        GUI.enabled = !Application.isPlaying && selectedKeyIndex != -1;
        if (GUILayout.Button("- Remove Selected", GUILayout.Height(24))) RemoveKeyframe();
        GUI.enabled = !Application.isPlaying;
        GUILayout.EndHorizontal();

        GUILayout.EndVertical();
        GUI.enabled = true;
    }

    // =========================================================
    // 辅助 & 工具
    // =========================================================

    void DrawToolbar()
    {
        GUILayout.BeginHorizontal(EditorStyles.toolbar);
        GUI.enabled = !Application.isPlaying;
        GUILayout.Label("Asset:", GUILayout.Width(40));
        EditorGUI.BeginChangeCheck();
        var newAsset = (VectorTimelineAsset)EditorGUILayout.ObjectField(currentAsset, typeof(VectorTimelineAsset), false, GUILayout.Width(150));
        if (EditorGUI.EndChangeCheck()) { currentAsset = newAsset; selectedKeyIndex = -1; currentTime = 0; isPlaying = false; }
        GUI.enabled = true;
        GUILayout.Space(10);
        GUILayout.Label("Player:", GUILayout.Width(45));
        var newPlayer = (VectorTimelinePlayer)EditorGUILayout.ObjectField(previewPlayer, typeof(VectorTimelinePlayer), true, GUILayout.Width(120));
        if (newPlayer != previewPlayer) previewPlayer = newPlayer;
        GUILayout.Space(10);
        GUI.enabled = !Application.isPlaying;
        if (GUILayout.Button("⏮", EditorStyles.toolbarButton, GUILayout.Width(25))) { currentTime = 0; UpdateScenePreview(); }

        bool newPlaying = GUILayout.Toggle(isPlaying, isPlaying ? "⏸" : "▶", EditorStyles.toolbarButton, GUILayout.Width(35));
        if (newPlaying != isPlaying)
        {
            isPlaying = newPlaying;
            lastEditorTime = EditorApplication.timeSinceStartup;
            isReversing = false; // ✨ 切换播放时重置方向
        }

        if (GUILayout.Button("⏹", EditorStyles.toolbarButton, GUILayout.Width(25))) { isPlaying = false; currentTime = 0; UpdateScenePreview(); }
        GUI.enabled = true;
        GUILayout.FlexibleSpace();
        GUILayout.Label("Zoom:", GUILayout.Width(40));
        zoom = GUILayout.HorizontalSlider(zoom, 10f, 500f, GUILayout.Width(100));
        GUILayout.EndHorizontal();
    }

    float GetMaxKeyframeTime() => (currentAsset.keyframes.Count == 0) ? 0 : currentAsset.keyframes.Max(k => k.time);
    float GetTimeStep() => (zoom > 300) ? 0.1f : (zoom > 100 ? 0.5f : 1.0f);
    float TimeToPixel(float time) => time * zoom;
    float PixelToTime(float pixel) => pixel / zoom;
    void InitStyles() { if (keyframeStyle == null) keyframeStyle = new GUIStyle(GUI.skin.button); }
    void AddKeyframe()
    {
        Undo.RecordObject(currentAsset, "Add Keyframe");
        var newKey = new TimelineKeyframe { time = currentTime, scale = 1.0f, curve = AnimationCurve.Linear(0, 0, 1, 1), alignOffset = 0, isInstant = false };
        if (currentAsset.keyframes.Count > 0) newKey.shapeAsset = currentAsset.keyframes.Last().shapeAsset;
        currentAsset.keyframes.Add(newKey);
        SortKeyframes(); selectedKeyIndex = currentAsset.keyframes.IndexOf(newKey);
        EditorUtility.SetDirty(currentAsset); Repaint();
    }
    void RemoveKeyframe()
    {
        if (selectedKeyIndex != -1)
        {
            Undo.RecordObject(currentAsset, "Remove Keyframe");
            currentAsset.keyframes.RemoveAt(selectedKeyIndex);
            selectedKeyIndex = -1;
            EditorUtility.SetDirty(currentAsset); Repaint();
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
            previewPlayer.debugTime = currentTime;
            previewPlayer.Evaluate(currentTime);
            SceneView.RepaintAll();
        }
    }
}