#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class VectorShapeSceneEditor {
    private static int selectedNodeIndex = -1;
    private static bool isDraggingNewNode = false;

    // 静态构造，在代码编译后自动把绘制逻辑挂载到 Scene 视图，完全不干扰 TriInspector
    static VectorShapeSceneEditor() {
        SceneView.duringSceneGui -= OnSceneGUI;
        SceneView.duringSceneGui += OnSceneGUI;
    }

    private static void OnSceneGUI(SceneView sceneView) {
        GameObject currentSelected = Selection.activeGameObject;
        if (currentSelected == null) return;

        VectorShapeCreator creator = currentSelected.GetComponent<VectorShapeCreator>();
        if (creator == null || creator.shapeType != VectorShapeType.BezierPath || !creator.enableBezierEdit) return;

        // ✨ 关键修复 1：隐藏 Unity 默认的移动工具，防止箭头抢占鼠标焦点
        Tools.current = Tool.None;

        Event e = Event.current;
        Transform handleTransform = creator.transform;

        // ✨ 关键修复 2：在 Layout 阶段正确注册被动控制权，防止点击空白处取消选中当前物体
        int controlID = GUIUtility.GetControlID("BezierEditor".GetHashCode(), FocusType.Passive);
        if (e.type == EventType.Layout) {
            HandleUtility.AddDefaultControl(controlID);
        }

        // ==========================================
        // A. 绘制贝塞尔曲线 (绿色预览线)
        // ==========================================
        if (creator.bezierNodes.Count >= 2) {
            Handles.color = Color.green;
            int segments = creator.isClosed ? creator.bezierNodes.Count : creator.bezierNodes.Count - 1;
            for (int i = 0; i < segments; i++) {
                BezierNode p1 = creator.bezierNodes[i];
                BezierNode p2 = creator.bezierNodes[(i + 1) % creator.bezierNodes.Count];

                Vector3 wP1 = handleTransform.TransformPoint(p1.position);
                Vector3 wP2 = handleTransform.TransformPoint(p2.position);
                Vector3 wC1 = handleTransform.TransformPoint(p1.position + p1.controlOut);
                Vector3 wC2 = handleTransform.TransformPoint(p2.position + p2.controlIn);
                Handles.DrawBezier(wP1, wP2, wC1, wC2, Color.green, null, 2f);
            }
        }

        // ==========================================
        // B. 交互处理：节点与控制柄
        // ==========================================
        for (int i = 0; i < creator.bezierNodes.Count; i++) {
            BezierNode node = creator.bezierNodes[i];
            Vector3 worldPos = handleTransform.TransformPoint(node.position);
            Vector3 worldIn = handleTransform.TransformPoint(node.position + node.controlIn);
            Vector3 worldOut = handleTransform.TransformPoint(node.position + node.controlOut);

            float handleSize = HandleUtility.GetHandleSize(worldPos) * 0.1f;

            // 绘制控制柄辅助线
            if (i == selectedNodeIndex) {
                Handles.color = Color.gray;
                Handles.DrawLine(worldPos, worldIn);
                Handles.DrawLine(worldPos, worldOut);
            }

            // --- 锚点移动 ---
            Handles.color = (i == selectedNodeIndex) ? Color.white : Color.cyan;
            EditorGUI.BeginChangeCheck();
            Vector3 newPos = Handles.FreeMoveHandle(worldPos, handleSize, Vector3.zero, Handles.SphereHandleCap);
            if (EditorGUI.EndChangeCheck()) {
                Undo.RecordObject(creator, "Move Anchor");
                node.position = handleTransform.InverseTransformPoint(newPos);
                selectedNodeIndex = i;
                creator.UpdatePreview();
            }

            // --- 锚点快捷键操作 (需在锚点附近点击) ---
            if (e.type == EventType.MouseDown && e.button == 0) {
                float dist = Vector2.Distance(e.mousePosition, HandleUtility.WorldToGUIPoint(worldPos));
                if (dist < 15f) { // 命中锚点
                    if (e.control || e.command) { // Ctrl/Cmd 删除
                        Undo.RecordObject(creator, "Delete Node");
                        creator.bezierNodes.RemoveAt(i);
                        selectedNodeIndex = -1;
                        creator.UpdatePreview();
                        e.Use();
                        return;
                    } else if (e.shift) { // Shift 变尖角
                        Undo.RecordObject(creator, "Reset Tangents");
                        node.controlIn = Vector2.zero;
                        node.controlOut = Vector2.zero;
                        selectedNodeIndex = i;
                        creator.UpdatePreview();
                        e.Use();
                    } else { // 选中
                        selectedNodeIndex = i;
                        e.Use();
                    }
                }
            }

            // --- 控制柄移动 ---
            if (i == selectedNodeIndex) {
                EditorGUI.BeginChangeCheck();
                Handles.color = Color.yellow;
                Vector3 newIn = Handles.FreeMoveHandle(worldIn, handleSize * 0.8f, Vector3.zero, Handles.CubeHandleCap);
                if (EditorGUI.EndChangeCheck()) {
                    Undo.RecordObject(creator, "Move Handle In");
                    node.controlIn = (Vector2)handleTransform.InverseTransformPoint(newIn) - node.position;
                    if (!e.alt) node.controlOut = -node.controlIn; // PS 逻辑：不按 Alt 键则对称
                    creator.UpdatePreview();
                }

                EditorGUI.BeginChangeCheck();
                Handles.color = Color.yellow;
                Vector3 newOut = Handles.FreeMoveHandle(worldOut, handleSize * 0.8f, Vector3.zero, Handles.CubeHandleCap);
                if (EditorGUI.EndChangeCheck()) {
                    Undo.RecordObject(creator, "Move Handle Out");
                    node.controlOut = (Vector2)handleTransform.InverseTransformPoint(newOut) - node.position;
                    if (!e.alt) node.controlIn = -node.controlOut;
                    creator.UpdatePreview();
                }
            }
        }

        // ==========================================
        // C. 空白处交互：创建新节点 (点击并拖拽拉出控制柄)
        // ==========================================
        if (e.type == EventType.MouseDown && e.button == 0) {
            // 确保没有点在任何 Handle 上
            if (GUIUtility.hotControl == 0 || GUIUtility.hotControl == controlID) {
                Vector2 mousePos = HandleUtility.GUIPointToWorldRay(e.mousePosition).origin;
                Vector2 localMousePos = handleTransform.InverseTransformPoint(mousePos);

                Undo.RecordObject(creator, "Add Node");
                BezierNode newNode = new BezierNode(localMousePos);
                newNode.controlIn = Vector2.zero;
                newNode.controlOut = Vector2.zero;
                creator.bezierNodes.Add(newNode);

                selectedNodeIndex = creator.bezierNodes.Count - 1;
                isDraggingNewNode = true;

                GUIUtility.hotControl = controlID; // 抢占焦点准备拖拽
                creator.UpdatePreview();
                e.Use();
            }
        } else if (e.type == EventType.MouseDrag && e.button == 0 && isDraggingNewNode) {
            if (selectedNodeIndex >= 0 && selectedNodeIndex < creator.bezierNodes.Count) {
                Vector2 mousePos = HandleUtility.GUIPointToWorldRay(e.mousePosition).origin;
                Vector2 localMousePos = handleTransform.InverseTransformPoint(mousePos);

                BezierNode activeNode = creator.bezierNodes[selectedNodeIndex];
                Undo.RecordObject(creator, "Drag Tangents");

                activeNode.controlOut = localMousePos - activeNode.position;
                activeNode.controlIn = -activeNode.controlOut; // 对称拉出

                creator.UpdatePreview();
                e.Use();
            }
        } else if (e.type == EventType.MouseUp && e.button == 0) {
            if (isDraggingNewNode) {
                isDraggingNewNode = false;
                GUIUtility.hotControl = 0;
                e.Use();
            }
        }
    }
}
#endif