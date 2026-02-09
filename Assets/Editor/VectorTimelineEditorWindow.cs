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

    // --- GUI 绘制入口 ---
    private void OnGUI()
    {
        InitStyles();
        DrawToolbar();

        if (currentAsset == null)
        {
            EditorGUILayout.HelpBox("请在上方选择一个 VectorTimelineAsset 开始编辑。", MessageType.Info);
            return;
        }

        // 绘制时间轴区域
        Rect timelineRect = GUILayoutUtility.GetRect(position.width, headerHeight + trackHeight + 20);
        GUI.Box(timelineRect, GUIContent.none, EditorStyles.helpBox);

        // 处理输入事件 (修复了你之前的 ProcessEvents 报错，逻辑都在这里)
        HandleTimelineEvents(timelineRect);

        if (Event.current.type == EventType.Repaint)
        {
            DrawTimelineContent(timelineRect);
        }

        DrawInspectorArea();
    }

    // =========================================================
    // 区域绘制逻辑
    // =========================================================

    void DrawToolbar()
    {
        GUILayout.BeginHorizontal(EditorStyles.toolbar);

        GUILayout.Label("Target Asset:", GUILayout.Width(80));
        var newAsset = (VectorTimelineAsset)EditorGUILayout.ObjectField(currentAsset, typeof(VectorTimelineAsset), false, GUILayout.Width(200));
        if (newAsset != currentAsset)
        {
            currentAsset = newAsset;
            selectedKeyIndex = -1;
            currentTime = 0;
            isPlaying = false;
        }

        GUILayout.Space(20);
        GUILayout.Label("Preview Player:", GUILayout.Width(90));
        previewPlayer = (VectorTimelinePlayer)EditorGUILayout.ObjectField(previewPlayer, typeof(VectorTimelinePlayer), true, GUILayout.Width(150));

        GUILayout.FlexibleSpace();

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
        GUI.BeginGroup(rect);

        // 1. 标尺
        Rect rulerRect = new Rect(sidebarWidth, 0, rect.width - sidebarWidth, headerHeight);
        GUI.color = new Color(0.2f, 0.2f, 0.2f);
        GUI.DrawTexture(rulerRect, whiteTexture);
        GUI.color = Color.white;

        Handles.color = Color.gray;
        float maxTime = PixelToTime(rect.width);

        for (float t = 0; t <= maxTime; t += 0.1f)
        {
            float x = TimeToPixel(t);
            bool isSecond = Mathf.Abs(t % 1.0f) < 0.001f;
            bool isHalf = Mathf.Abs(t % 0.5f) < 0.001f;

            float h = isSecond ? 15 : (isHalf ? 10 : 5);
            Handles.DrawLine(new Vector3(x, headerHeight - h), new Vector3(x, headerHeight));

            if (isSecond) GUI.Label(new Rect(x + 2, 0, 30, 20), t.ToString("0"), EditorStyles.miniLabel);
        }

        // 2. 轨道背景
        Rect trackRect = new Rect(sidebarWidth, headerHeight, rect.width - sidebarWidth, trackHeight);
        GUI.color = new Color(0.15f, 0.15f, 0.15f);
        GUI.DrawTexture(trackRect, whiteTexture);
        GUI.color = Color.white;

        // 3. 关键帧
        for (int i = 0; i < currentAsset.keyframes.Count; i++)
        {
            var key = currentAsset.keyframes[i];
            float x = TimeToPixel(key.time);
            float y = headerHeight + (trackHeight / 2) - 6;

            if (i < currentAsset.keyframes.Count - 1)
            {
                float nextX = TimeToPixel(currentAsset.keyframes[i + 1].time);
                Handles.color = new Color(0.5f, 0.5f, 0.5f, 0.5f);
                Handles.DrawLine(new Vector3(x, y + 6), new Vector3(nextX, y + 6));
            }

            Rect keyRect = new Rect(x - 6, y, 12, 12);
            Color keyColor = (i == selectedKeyIndex) ? Color.cyan : Color.white;
            if (key.isInstant) keyColor = new Color(1f, 0.4f, 0.4f);

            GUI.color = keyColor;
            if (keyframeStyle != null) GUI.Box(keyRect, GUIContent.none, keyframeStyle);
            else GUI.Box(keyRect, "", "Button");
            GUI.color = Color.white;
        }

        // 4. Playhead
        float playheadX = TimeToPixel(currentTime);
        Handles.color = Color.red;
        Handles.DrawLine(new Vector3(playheadX, 0), new Vector3(playheadX, rect.height));

        GUI.EndGroup();
    }

    void HandleTimelineEvents(Rect rect)
    {
        Event e = Event.current;
        Vector2 mousePos = e.mousePosition;

        if (rect.Contains(mousePos) || isDraggingKey || isScrubbing)
        {
            if (e.type == EventType.MouseDown && e.button == 0)
            {
                bool hitKey = false;
                for (int i = 0; i < currentAsset.keyframes.Count; i++)
                {
                    float x = TimeToPixel(currentAsset.keyframes[i].time);
                    float y = headerHeight + (trackHeight / 2) - 6;
                    Rect keyRect = new Rect(x - 6 + rect.x, y + rect.y, 12, 12);

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
                    float localX = mousePos.x - rect.x;
                    currentTime = Mathf.Max(0, PixelToTime(localX));
                    Repaint();
                    UpdateScenePreview();
                    e.Use();
                }
            }

            if (e.type == EventType.MouseDrag && e.button == 0)
            {
                float localX = mousePos.x - rect.x;
                float t = Mathf.Max(0, PixelToTime(localX));

                if (isDraggingKey && selectedKeyIndex != -1)
                {
                    if (!e.shift) t = Mathf.Round(t * 10) / 10f;
                    Undo.RecordObject(currentAsset, "Move Keyframe");
                    var key = currentAsset.keyframes[selectedKeyIndex];
                    key.time = t;
                    currentAsset.keyframes[selectedKeyIndex] = key;
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

            if (e.type == EventType.MouseUp)
            {
                if (isDraggingKey) { SortKeyframes(); isDraggingKey = false; }
                isScrubbing = false;
                e.Use();
            }
        }
    }

    void DrawInspectorArea()
    {
        GUILayout.Space(10);
        GUILayout.Label("Inspector", EditorStyles.boldLabel);
        GUILayout.BeginVertical(EditorStyles.helpBox);

        EditorGUILayout.LabelField("Global Settings", EditorStyles.miniBoldLabel);
        EditorGUI.BeginChangeCheck();
        var loopMode = (VectorLoopMode)EditorGUILayout.EnumPopup("Loop Mode", currentAsset.loopMode);
        float dur = EditorGUILayout.FloatField("Total Duration (0=Auto)", currentAsset.duration);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(currentAsset, "Change Global Settings");
            currentAsset.loopMode = loopMode;
            currentAsset.duration = dur;
        }

        GUILayout.Space(10);
        EditorGUILayout.LabelField("Keyframe Settings", EditorStyles.miniBoldLabel);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("+ Add Keyframe", GUILayout.Height(24))) AddKeyframe();
        GUI.enabled = selectedKeyIndex != -1;
        if (GUILayout.Button("- Remove Selected", GUILayout.Height(24))) RemoveKeyframe();
        GUI.enabled = true;
        GUILayout.EndHorizontal();

        GUILayout.Space(5);

        if (selectedKeyIndex != -1 && selectedKeyIndex < currentAsset.keyframes.Count)
        {
            EditorGUI.BeginChangeCheck();
            var key = currentAsset.keyframes[selectedKeyIndex];

            float newTime = EditorGUILayout.FloatField("Time", key.time);
            var newShape = (VectorShapeAsset)EditorGUILayout.ObjectField("Shape Asset", key.shapeAsset, typeof(VectorShapeAsset), false);
            float newScale = EditorGUILayout.Slider("Scale", key.scale, 0f, 2f);
            bool newInstant = EditorGUILayout.Toggle("Is Instant (Jump)", key.isInstant);
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
                UpdateScenePreview();
            }
        }
        else
        {
            GUILayout.Label("Select a keyframe on timeline to edit properties.", EditorStyles.centeredGreyMiniLabel);
        }
        GUILayout.EndVertical();
    }

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
        if (currentAsset.keyframes.Count > 0)
        {
            var last = currentAsset.keyframes[currentAsset.keyframes.Count - 1];
            newKey.shapeAsset = last.shapeAsset;
        }
        currentAsset.keyframes.Add(newKey);
        SortKeyframes();
        selectedKeyIndex = currentAsset.keyframes.IndexOf(newKey);
        Repaint();
    }

    void RemoveKeyframe()
    {
        if (selectedKeyIndex != -1)
        {
            Undo.RecordObject(currentAsset, "Remove Keyframe");
            currentAsset.keyframes.RemoveAt(selectedKeyIndex);
            selectedKeyIndex = -1;
            Repaint();
        }
    }

    void SortKeyframes()
    {
        var selectedKey = (selectedKeyIndex != -1 && selectedKeyIndex < currentAsset.keyframes.Count)
            ? currentAsset.keyframes[selectedKeyIndex] : default;
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