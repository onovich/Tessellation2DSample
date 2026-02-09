using UnityEngine;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
[ExecuteAlways] // ✨ 关键：确保在编辑模式下持续运行
public class VectorShapeCreator : MonoBehaviour {
    public enum ShapeType { Circle, Polygon, Star }

    [Header("Shape Settings")]
    public ShapeType shapeType = ShapeType.Polygon;
    [Range(3, 12)] public int sides = 5;
    public float radius = 1f;
    [Range(3, 12)] public int starPoints = 5;
    [Range(0.1f, 1f)] public float starInnerRatio = 0.5f;
    public Color previewColor = Color.white;

    [Header("Baking Settings")]
    [Tooltip("烘焙时的采样精度。点越多，变形越圆滑。")]
    [Range(60, 720)] public int bakeResolution = 360;

    [Header("Debug")]
    public bool showGizmos = true;

    // 内部缓存
    private Mesh _mesh;
    private MeshFilter _mf;
    private MeshRenderer _mr;

    void OnEnable() {
        InitComponents();
        UpdatePreview();
    }

    // ✨ 核心：当你在 Inspector 修改数值时，Unity 会自动调用这个函数
    void OnValidate() {
        InitComponents();
        UpdatePreview();
    }

    void InitComponents() {
        if (!_mf) _mf = GetComponent<MeshFilter>();
        if (!_mr) _mr = GetComponent<MeshRenderer>();
        if (!_mesh) {
            _mesh = new Mesh();
            _mesh.name = "PreviewShape";
            _mf.mesh = _mesh;
        }

        // 自动赋予一个默认材质，否则你看不到形状
        if (_mr.sharedMaterial == null)
            _mr.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
    }

    // --- 实时预览绘制逻辑 ---
    public void UpdatePreview() {
        if (_mesh == null) return;

        // 1. 生成原始关键点
        Vector2[] keyPoints = GenerateKeyPoints();

        // 2. 重采样到高分辨率 (模拟烘焙后的效果)
        // 这一步很重要，能让你看到最终 Bake 出来是圆的还是方的
        Vector2[] finalVerts = ResamplePoints(keyPoints, bakeResolution);

        // 3. 真正绘制 Mesh
        RenderMesh(finalVerts);
    }

    // --- 核心算法：生成关键点 ---
    Vector2[] GenerateKeyPoints() {
        List<Vector2> points = new List<Vector2>();
        float angleOffset = Mathf.PI / 2; // 让形状朝上

        if (shapeType == ShapeType.Circle) {
            // 预览用的圆形，给个60段就够了，Bake的时候会重采样
            return GenerateRegularPolygon(60, radius, angleOffset);
        } else if (shapeType == ShapeType.Polygon) {
            return GenerateRegularPolygon(sides, radius, angleOffset);
        } else if (shapeType == ShapeType.Star) {
            int count = starPoints * 2;
            float step = 2 * Mathf.PI / count;
            for (int i = 0; i < count; i++) {
                float r = (i % 2 == 0) ? radius : radius * starInnerRatio;
                float angle = i * step + angleOffset;
                points.Add(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * r);
            }
            return points.ToArray();
        }
        return new Vector2[0];
    }

    Vector2[] GenerateRegularPolygon(int n, float r, float offset) {
        Vector2[] pts = new Vector2[n];
        for (int i = 0; i < n; i++) {
            float angle = i * (2 * Mathf.PI / n) + offset;
            pts[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * r;
        }
        return pts;
    }

    // --- 核心算法：重采样 (Flash原理) ---
    public static Vector2[] ResamplePoints(Vector2[] keyPoints, int targetCount) {
        if (keyPoints == null || keyPoints.Length < 3) return new Vector2[targetCount];

        Vector2[] result = new Vector2[targetCount];
        float perimeter = 0;
        float[] segLens = new float[keyPoints.Length];

        for (int i = 0; i < keyPoints.Length; i++) {
            float d = Vector2.Distance(keyPoints[i], keyPoints[(i + 1) % keyPoints.Length]);
            segLens[i] = d;
            perimeter += d;
        }

        float step = perimeter / targetCount;
        float traveled = 0;
        int curSeg = 0;

        for (int i = 0; i < targetCount; i++) {
            // 防止浮点误差导致的死循环
            int safety = 0;
            while (traveled + step > segLens[curSeg] + 0.0001f && safety++ < 100) {
                traveled -= segLens[curSeg];
                curSeg = (curSeg + 1) % keyPoints.Length;
            }

            float t = 0;
            if (segLens[curSeg] > 0.0001f) t = traveled / segLens[curSeg];

            result[i] = Vector2.Lerp(keyPoints[curSeg], keyPoints[(curSeg + 1) % keyPoints.Length], t);
            traveled += step;
        }
        return result;
    }

    // --- 渲染 Mesh ---
    void RenderMesh(Vector2[] polyVerts) {
        int count = polyVerts.Length;
        Vector3[] v = new Vector3[count + 1]; // +1 中心点
        int[] t = new int[count * 3];
        Color[] c = new Color[count + 1];

        // 中心点
        v[0] = Vector3.zero;
        c[0] = previewColor;

        // 周边点
        for (int i = 0; i < count; i++) {
            v[i + 1] = polyVerts[i];
            c[i + 1] = previewColor;

            // 构建扇形
            t[i * 3] = 0;
            t[i * 3 + 1] = i + 1;
            t[i * 3 + 2] = (i + 1) >= count ? 1 : i + 2;
        }

        _mesh.Clear();
        _mesh.vertices = v;
        _mesh.triangles = t;
        _mesh.colors = c;
        _mesh.RecalculateBounds();
    }

    // --- 可视化 Gizmos (可选) ---
    void OnDrawGizmos() {
        if (!showGizmos) return;

        // 画出每一个重采样点，让你看到“密度”
        if (_mesh != null && _mesh.vertices.Length > 0) {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(1, 1, 0, 0.5f); // 半透明黄

            // 跳过中心点，只画边缘
            for (int i = 1; i < _mesh.vertices.Length; i++) {
                Gizmos.DrawSphere(_mesh.vertices[i], 0.02f * radius);
            }
        }
    }

    // --- 烘焙功能 (仅Editor) ---
#if UNITY_EDITOR
    public void BakeShape() {
        // 1. 重新计算一份最终数据
        Vector2[] finalData = ResamplePoints(GenerateKeyPoints(), bakeResolution);

        // 2. 创建 Asset
        VectorShapeAsset asset = ScriptableObject.CreateInstance<VectorShapeAsset>();
        asset.vertices = finalData;
        asset.resolution = bakeResolution;

        // 3. 保存文件对话框
        string path = EditorUtility.SaveFilePanelInProject(
            "Save Vector Shape",
            $"Shape_{shapeType}_{sides}",
            "asset",
            "Save shape data"
        );

        if (string.IsNullOrEmpty(path)) return;

        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();

        // 高亮显示生成的文件
        EditorGUIUtility.PingObject(asset);
        Debug.Log($"<color=green>Shape Baked to: {path}</color>");
    }
#endif
}

// --- Editor 按钮 ---
#if UNITY_EDITOR
[CustomEditor(typeof(VectorShapeCreator))]
public class VectorShapeCreatorEditor : Editor {
    // 1. 定义变量，但不要在这里赋值！
    private GUIStyle _bigButtonStyle;

    public override void OnInspectorGUI() {
        DrawDefaultInspector();
        GUILayout.Space(15);

        // 2. 在绘制时检查：如果是空的，说明是第一次运行，进行初始化
        if (_bigButtonStyle == null) {
            // 安全地获取当前皮肤的 button 样式进行复制
            _bigButtonStyle = new GUIStyle(GUI.skin.button);
            _bigButtonStyle.fontStyle = FontStyle.Bold;
            _bigButtonStyle.fontSize = 12;
            _bigButtonStyle.fixedHeight = 40;
            _bigButtonStyle.normal.textColor = Color.white;
        }

        // 3. 恢复背景色（防止污染其他组件）
        Color originalColor = GUI.backgroundColor;

        GUI.backgroundColor = Color.green;

        // 4. 使用初始化好的 Style
        if (GUILayout.Button("Bake to ScriptableObject", _bigButtonStyle)) {
            ((VectorShapeCreator)target).BakeShape();
        }

        // 恢复颜色
        GUI.backgroundColor = originalColor;
    }
}
#endif