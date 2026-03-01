using UnityEngine;
using System.Collections.Generic;
using TriInspector;
#if UNITY_EDITOR
using UnityEditor;
#endif

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
[ExecuteAlways]
public class VectorShapeCreator : MonoBehaviour {
    [Title("Asset Management")]
    [PropertyTooltip("将要写入或读取的目标 Asset")]
    [Required] // Tri-Inspector: 提示不能为空
    public VectorShapeAsset targetAsset;

    [Title("Shape Configuration")]
    [OnValueChanged(nameof(UpdatePreview))] // Tri-Inspector: 值变化时自动刷新
    public VectorShapeType shapeType = VectorShapeType.Polygon;

    [OnValueChanged(nameof(UpdatePreview))]
    [Range(3, 12)]
    public int sides = 5;

    [OnValueChanged(nameof(UpdatePreview))]
    public float radius = 1f;

    [OnValueChanged(nameof(UpdatePreview))]
    [ShowIf(nameof(IsStar))] // Tri-Inspector: 只有是 Star 时才显示
    [Range(3, 12)]
    public int starPoints = 5;

    [OnValueChanged(nameof(UpdatePreview))]
    [ShowIf(nameof(IsStar))]
    [Range(0.1f, 1f)]
    public float starInnerRatio = 0.5f;

    [Title("Baking Settings")]
    [OnValueChanged(nameof(UpdatePreview))]
    [Range(60, 720)]
    public int bakeResolution = 360;

    [Title("Visualization")]
    public Color fillColor = Color.white;
    public bool enableStroke = true;
    public Color strokeColor = Color.black;
    [Min(0)] public float strokeWidth = 0.1f;

    public bool showResolutionGizmos = true;
    [ShowIf(nameof(showResolutionGizmos))]
    public Color gizmoColor = Color.yellow;

    // --- 辅助属性 ---
    private bool IsStar => shapeType == VectorShapeType.Star;

    // --- 内部缓存 ---
    private Mesh _mesh;
    private MeshFilter _mf;
    private MeshRenderer _mr;

    void OnEnable() {
        InitComponents();
        UpdatePreview();
    }

    void InitComponents() {
        if (!_mf) _mf = GetComponent<MeshFilter>();
        if (!_mr) _mr = GetComponent<MeshRenderer>();
        if (!_mesh) { _mesh = new Mesh(); _mesh.name = "PreviewShape"; _mf.mesh = _mesh; }
        if (_mr.sharedMaterial == null) _mr.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
    }

    // =========================================================
    //  核心功能：Bake (保存到 Asset)
    // =========================================================
    [Button(ButtonSizes.Large, "Bake to Asset")]
    [GUIColor(0.2f, 0.8f, 0.2f)] // 绿色按钮
    public void BakeShape() {
        if (targetAsset == null) {
            Debug.LogError("❌ 请先在 Target Asset 槽位中分配一个 ScriptableObject！");
            return;
        }

        // 1. 计算数据
        Vector2[] finalData = ResamplePoints(GenerateKeyPoints(), bakeResolution);

        // 2. 写入数据到 Asset
        targetAsset.vertices = finalData;
        targetAsset.resolution = bakeResolution;

        // 3. 写入参数（为了下次能 Load 回来）
        targetAsset.shapeType = shapeType;
        targetAsset.sides = sides;
        targetAsset.radius = radius;
        targetAsset.starPoints = starPoints;
        targetAsset.starInnerRatio = starInnerRatio;

        // 4. 标记脏数据并保存 (确保 Unity 知道数据变了)
#if UNITY_EDITOR
        EditorUtility.SetDirty(targetAsset);
        AssetDatabase.SaveAssets();
#endif
        Debug.Log($"<color=green>✅ Shape Baked to {targetAsset.name}!</color>");
    }

    // =========================================================
    //  核心功能：Load (从 Asset 读取)
    // =========================================================
    [Button(ButtonSizes.Medium, "Load from Asset")]
    [GUIColor(0.2f, 0.6f, 1.0f)] // 蓝色按钮
    public void LoadShape() {
        if (targetAsset == null) return;

        // 恢复 Inspector 参数
        this.shapeType = targetAsset.shapeType;
        this.sides = targetAsset.sides;
        this.radius = targetAsset.radius;
        this.starPoints = targetAsset.starPoints;
        this.starInnerRatio = targetAsset.starInnerRatio;
        this.bakeResolution = targetAsset.resolution;

        // 刷新预览
        UpdatePreview();
        Debug.Log($"<color=cyan>🔄 Loaded configuration from {targetAsset.name}</color>");
    }

    // =========================================================
    //  预览与 Gizmos
    // =========================================================
    public void UpdatePreview() {
        if (_mesh == null) InitComponents();

        Vector2[] keyPoints = GenerateKeyPoints();
        Vector2[] finalVerts = ResamplePoints(keyPoints, bakeResolution);

        RenderMesh(finalVerts);
    }

    void OnDrawGizmos() {
        if (!showResolutionGizmos || _mesh == null || _mesh.vertices.Length == 0) return;

        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = gizmoColor;

        // ✨ 分辨率可视化：只画边缘点，不画中心点
        // 点的大小设为半径的 1/50，既能看清又不会太大
        float dotSize = radius * 0.02f;

        // vertices[0] 是中心点，跳过
        for (int i = 1; i < _mesh.vertices.Length; i++) {
            Gizmos.DrawSphere(_mesh.vertices[i], dotSize);
        }
    }

    // =========================================================
    // 底层算法  
    // =========================================================
    Vector2[] GenerateKeyPoints() {
        List<Vector2> points = new List<Vector2>();
        float angleOffset = Mathf.PI / 2;

        if (shapeType == VectorShapeType.Circle)
            return GenerateRegularPolygon(60, radius, angleOffset);
        else if (shapeType == VectorShapeType.Polygon)
            return GenerateRegularPolygon(sides, radius, angleOffset);
        else if (shapeType == VectorShapeType.Star) {
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

    public static Vector2[] ResamplePoints(Vector2[] keyPoints, int targetCount) {
        if (keyPoints == null || keyPoints.Length < 3) return new Vector2[targetCount];
        Vector2[] result = new Vector2[targetCount];
        float perimeter = 0;
        float[] segLens = new float[keyPoints.Length];
        for (int i = 0; i < keyPoints.Length; i++) {
            float d = Vector2.Distance(keyPoints[i], keyPoints[(i + 1) % keyPoints.Length]);
            segLens[i] = d; perimeter += d;
        }
        float step = perimeter / targetCount;
        float traveled = 0; int curSeg = 0;
        for (int i = 0; i < targetCount; i++) {
            while (traveled + step > segLens[curSeg] + 0.0001f) {
                traveled -= segLens[curSeg]; curSeg = (curSeg + 1) % keyPoints.Length;
            }
            float t = segLens[curSeg] > 0.0001f ? traveled / segLens[curSeg] : 0;
            result[i] = Vector2.Lerp(keyPoints[curSeg], keyPoints[(curSeg + 1) % keyPoints.Length], t);
            traveled += step;
        }
        return result;
    }

    void RenderMesh(Vector2[] polyVerts) {
        int count = polyVerts.Length;
        int totalVerts = enableStroke ? (3 * count + 1) : (count + 1);
        int totalTris = enableStroke ? (count * 9) : (count * 3);

        Vector3[] v = new Vector3[totalVerts];
        int[] t = new int[totalTris];
        Color[] c = new Color[totalVerts];

        v[0] = Vector3.zero; c[0] = fillColor;

        for (int i = 0; i < count; i++) {
            v[i + 1] = polyVerts[i];
            c[i + 1] = fillColor;

            int next = (i + 1) % count;
            t[i * 3] = 0;
            t[i * 3 + 1] = i + 1;
            t[i * 3 + 2] = next + 1;

            if (enableStroke) {
                Vector2 pPrev = polyVerts[(i - 1 + count) % count];
                Vector2 pNext = polyVerts[(i + 1) % count];
                Vector2 dir = (pNext - pPrev).normalized;
                if (dir == Vector2.zero) dir = Vector2.right;
                Vector2 normal = new Vector2(dir.y, -dir.x);

                Vector2 outerP = polyVerts[i] + normal * strokeWidth;

                v[count + 1 + i] = polyVerts[i];
                c[count + 1 + i] = strokeColor;
                v[2 * count + 1 + i] = outerP;
                c[2 * count + 1 + i] = strokeColor;

                int inner1 = count + 1 + i;
                int inner2 = count + 1 + next;
                int outer1 = 2 * count + 1 + i;
                int outer2 = 2 * count + 1 + next;
                int tIdx = count * 3 + i * 6;

                t[tIdx] = inner1;
                t[tIdx + 1] = outer1;
                t[tIdx + 2] = outer2;
                t[tIdx + 3] = inner1;
                t[tIdx + 4] = outer2;
                t[tIdx + 5] = inner2;
            }
        }

        _mesh.Clear();
        _mesh.vertices = v;
        _mesh.triangles = t;
        _mesh.colors = c;
    }
}