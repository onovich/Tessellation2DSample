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
        if (isScrubbing || isDraggingKey || isDraggingDuration) UpdateScenePreview();

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
        DrawToolbar();

        if (currentAsset == null)
        {
            GUILayout.FlexibleSpace();
            GUILayout.Label("请选择一个 VectorTimelineAsset", EditorStyles.centeredGreyMiniLabel);
            GUILayout.FlexibleSpace();
            return;
        }

        // 保证视图至少能显示 Duration 之后的一点区域
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
        // 背景
        EditorGUI.DrawRect(rect, new Color(0.18f, 0.18f, 0.18f));

        // 标尺背景
        Rect rulerRect = new Rect(rect.x, rect.y, rect.width, headerHeight);
        EditorGUI.DrawRect(rulerRect, new Color(0.22f, 0.22f, 0.22f));

        // 绘制刻度
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

        // 绘制 Duration 线
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

        // 绘制关键帧连线
        float keyY = rect.y + headerHeight + (trackHeight / 2) - 8;
        Handles.color = Color.white;
        for (int i = 0; i < currentAsset.keyframes.Count - 1; i++)
        {
            float x1 = rect.x + TimeToPixel(currentAsset.keyframes[i].time);
            float x2 = rect.x + TimeToPixel(currentAsset.keyframes[i + 1].time);
            Handles.DrawDottedLine(new Vector3(x1, keyY + 8), new Vector3(x2, keyY + 8), 2f);
        }

        // 绘制关键帧节点
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

        // 绘制播放头
        float playheadX = rect.x + TimeToPixel(currentTime);
        Handles.color = new Color(1f, 0.2f, 0.2f, 1f);
        Handles.DrawLine(new Vector3(playheadX, rect.y), new Vector3(playheadX, rect.y + rect.height));
        Vector3[] headHandle = {
            new Vector3(playheadX - 6, rect.y),
            new Vector3(playheadX + 6, rect.y),
            new Vector3(playheadX, rect.y + 12)
        };
        Handles.DrawAAConvexPolygon(headHandle);
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
        Event e = Event.current;

        if (rect.Contains(e.mousePosition) || isDraggingKey || isScrubbing || isDraggingDuration)
        {
            // 修复点：localX 和 mouseTime 提到最外层计算
            float localX = e.mousePosition.x - rect.x;
            float mouseTime = Mathf.Max(0, PixelToTime(localX));

            // --- Mouse Down ---
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
                    if (hitPlayhead)
                    {
                        isScrubbing = true;
                        selectedKeyIndex = -1;
                        e.Use();
                    }
                    else if (hitDuration)
                    {
                        isDraggingDuration = true;
                        Undo.RecordObject(currentAsset, "Modify Duration");
                        e.Use();
                    }
                    else
                    {
                        isScrubbing = true;
                        selectedKeyIndex = -1;
                        currentTime = mouseTime;
                        Repaint();
                        UpdateScenePreview();
                        e.Use();
                    }
                }
            }

            // --- Mouse Drag ---
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

                    currentTime = mouseTime;
                    UpdateScenePreview();
                    e.Use();
                }
                else if (isScrubbing)
                {
                    currentTime = mouseTime;
                    UpdateScenePreview();
                    Repaint();
                    e.Use();
                }
                else if (isDraggingDuration)
                {
                    if (!e.shift) mouseTime = Mathf.Round(mouseTime * 10) / 10f;
                    currentAsset.duration = Mathf.Max(0.1f, mouseTime);
                    EditorUtility.SetDirty(currentAsset);
                    Repaint();
                    UpdateScenePreview();
                    e.Use();
                }
            }

            // --- Mouse Up ---
            else if (e.type == EventType.MouseUp)
            {
                if (isDraggingKey) SortKeyframes();
                isDraggingKey = false;
                isScrubbing = false;
                isDraggingDuration = false;
                e.Use();
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

    float GetMaxKeyframeTime()
    {
        if (currentAsset.keyframes.Count == 0) return 0f;
        return currentAsset.keyframes.Max(k => k.time);
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