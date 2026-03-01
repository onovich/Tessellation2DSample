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
    [Required]
    public VectorShapeAsset targetAsset;

    [Title("Shape Configuration")]
    [OnValueChanged(nameof(UpdatePreview))]
    public VectorShapeType shapeType = VectorShapeType.Polygon;

    [OnValueChanged(nameof(UpdatePreview))]
    [PropertyTooltip("决定形状（特别是描边和贝塞尔曲线）是否首尾相连")]
    public bool isClosed = true;

    [OnValueChanged(nameof(UpdatePreview))]
    [ShowIf(nameof(IsPolygonOrStar))]
    [Range(3, 12)]
    public int sides = 5;

    [OnValueChanged(nameof(UpdatePreview))]
    [HideIf(nameof(IsBezier))]
    public float radius = 1f;

    [OnValueChanged(nameof(UpdatePreview))]
    [ShowIf(nameof(IsStar))]
    [Range(3, 12)]
    public int starPoints = 5;

    [OnValueChanged(nameof(UpdatePreview))]
    [ShowIf(nameof(IsStar))]
    [Range(0.1f, 1f)]
    public float starInnerRatio = 0.5f;

    [Title("Bezier Configuration")]
    [ShowIf(nameof(IsBezier))]
    [PropertyTooltip("在 Scene 视图中编辑。勾选后可使用鼠标点击交互。")]
    public bool enableBezierEdit = false;

    // ✨ 新增：把初始化按钮移回主脚本，使用 TriInspector 渲染
    [ShowIf(nameof(IsBezier))]
    [Button(ButtonSizes.Medium, "Initialize Basic Curve")]
    [GUIColor(1f, 0.8f, 0.4f)]
    public void InitBasicCurve() {
#if UNITY_EDITOR
        UnityEditor.Undo.RecordObject(this, "Init Curve");
#endif
        bezierNodes.Clear();
        bezierNodes.Add(new BezierNode(new Vector2(-1, 0)) { controlIn = new Vector2(-0.5f, 0), controlOut = new Vector2(0.5f, 0) });
        bezierNodes.Add(new BezierNode(new Vector2(1, 0)) { controlIn = new Vector2(-0.5f, 0), controlOut = new Vector2(0.5f, 0) });
        UpdatePreview();
    }

    [ShowIf(nameof(IsBezier))]
    [ListDrawerSettings(AlwaysExpanded = true)]
    public List<BezierNode> bezierNodes = new List<BezierNode>();

    [Title("Baking Settings")]
    [OnValueChanged(nameof(UpdatePreview))]
    [Range(10, 720)]
    public int bakeResolution = 360;

    [Title("Visualization & Style")]
    [OnValueChanged(nameof(UpdatePreview))]
    public Color fillColor = Color.white;

    [OnValueChanged(nameof(UpdatePreview))]
    public bool enableStroke = true;

    [OnValueChanged(nameof(UpdatePreview))]
    [ShowIf(nameof(enableStroke))]
    public Color strokeColor = Color.black;

    [OnValueChanged(nameof(UpdatePreview))]
    [ShowIf(nameof(enableStroke))]
    [Min(0f)] public float strokeWidth = 0.1f;

    public bool showResolutionGizmos = true;
    [ShowIf(nameof(showResolutionGizmos))]
    public Color gizmoColor = Color.yellow;

    // --- 辅助属性 ---
    private bool IsBezier => shapeType == VectorShapeType.BezierPath;
    private bool IsStar => shapeType == VectorShapeType.Star;
    private bool IsPolygonOrStar => shapeType == VectorShapeType.Polygon || shapeType == VectorShapeType.Star;

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
    // ✨ 核心功能：Bake (保存到 Asset)
    // =========================================================
    [Button(ButtonSizes.Large, "Bake to Asset")]
    [GUIColor(0.2f, 0.8f, 0.2f)]
    public void BakeShape() {
        if (targetAsset == null) {
            Debug.LogError("❌ 请先在 Target Asset 槽位中分配一个 ScriptableObject！");
            return;
        }

        Vector2[] finalData = ResamplePoints(GenerateKeyPoints(), bakeResolution);

        targetAsset.vertices = finalData;
        targetAsset.resolution = bakeResolution;

        targetAsset.shapeType = shapeType;
        targetAsset.isClosed = isClosed;
        targetAsset.sides = sides;
        targetAsset.radius = radius;
        targetAsset.starPoints = starPoints;
        targetAsset.starInnerRatio = starInnerRatio;

        // 深拷贝贝塞尔节点数据
        targetAsset.bezierNodes.Clear();
        foreach (var node in bezierNodes) {
            targetAsset.bezierNodes.Add(new BezierNode(node.position) {
                controlIn = node.controlIn,
                controlOut = node.controlOut
            });
        }

#if UNITY_EDITOR
        EditorUtility.SetDirty(targetAsset);
        AssetDatabase.SaveAssets();
#endif
        Debug.Log($"<color=green>✅ Shape Baked to {targetAsset.name}!</color>");
    }

    // =========================================================
    // ✨ 核心功能：Load (从 Asset 读取)
    // =========================================================
    [Button(ButtonSizes.Medium, "Load from Asset")]
    [GUIColor(0.2f, 0.6f, 1.0f)]
    public void LoadShape() {
        if (targetAsset == null) return;

        this.shapeType = targetAsset.shapeType;
        this.isClosed = targetAsset.isClosed;
        this.sides = targetAsset.sides;
        this.radius = targetAsset.radius;
        this.starPoints = targetAsset.starPoints;
        this.starInnerRatio = targetAsset.starInnerRatio;
        this.bakeResolution = targetAsset.resolution;

        this.bezierNodes.Clear();
        if (targetAsset.bezierNodes != null) {
            foreach (var node in targetAsset.bezierNodes) {
                this.bezierNodes.Add(new BezierNode(node.position) {
                    controlIn = node.controlIn,
                    controlOut = node.controlOut
                });
            }
        }

        UpdatePreview();
        Debug.Log($"<color=cyan>🔄 Loaded configuration from {targetAsset.name}</color>");
    }

    // =========================================================
    // ✨ 预览与渲染
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

        float dotSize = 0.02f;
        // vertices[0] 是中心点，跳过。只画内圈边缘点以显示分辨率
        int pointCount = bakeResolution;
        if (_mesh.vertices.Length > pointCount) {
            for (int i = 1; i <= pointCount; i++) {
                Gizmos.DrawSphere(_mesh.vertices[i], dotSize);
            }
        }
    }

    // =========================================================
    // 底层算法：生成关键点
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
        } else if (shapeType == VectorShapeType.BezierPath) {
            return GenerateBezierPath();
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

    Vector2[] GenerateBezierPath() {
        if (bezierNodes == null || bezierNodes.Count < 2) return new Vector2[0];

        List<Vector2> points = new List<Vector2>();
        int segments = isClosed ? bezierNodes.Count : bezierNodes.Count - 1;
        int samplesPerSegment = 30; // 内部高精度采样，供后续重采样使用

        for (int i = 0; i < segments; i++) {
            BezierNode p1 = bezierNodes[i];
            BezierNode p2 = bezierNodes[(i + 1) % bezierNodes.Count];

            Vector2 p0_pos = p1.position;
            Vector2 p1_pos = p1.position + p1.controlOut;
            Vector2 p2_pos = p2.position + p2.controlIn;
            Vector2 p3_pos = p2.position;

            for (int j = 0; j <= samplesPerSegment; j++) {
                // 防止段与段之间首尾相连时出现重复点
                if (j == samplesPerSegment && (i < segments - 1 || isClosed)) continue;

                float t = j / (float)samplesPerSegment;
                points.Add(CalculateCubicBezierPoint(t, p0_pos, p1_pos, p2_pos, p3_pos));
            }
        }
        return points.ToArray();
    }

    Vector2 CalculateCubicBezierPoint(float t, Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3) {
        float u = 1 - t;
        float tt = t * t;
        float uu = u * u;
        float uuu = uu * u;
        float ttt = tt * t;

        Vector2 p = uuu * p0;
        p += 3 * uu * t * p1;
        p += 3 * u * tt * p2;
        p += ttt * p3;
        return p;
    }

    // =========================================================
    // 底层算法：等距重采样与网格构建
    // =========================================================
    public static Vector2[] ResamplePoints(Vector2[] keyPoints, int targetCount) {
        if (keyPoints == null || keyPoints.Length < 2) return new Vector2[targetCount];

        Vector2[] result = new Vector2[targetCount];
        float perimeter = 0;
        float[] segLens = new float[keyPoints.Length];

        // 计算周长
        for (int i = 0; i < keyPoints.Length; i++) {
            // 如果是最后一个点且不闭合，其长度为0
            if (i == keyPoints.Length - 1) {
                float d = Vector2.Distance(keyPoints[i], keyPoints[0]);
                segLens[i] = d; perimeter += d;
            } else {
                float d = Vector2.Distance(keyPoints[i], keyPoints[i + 1]);
                segLens[i] = d; perimeter += d;
            }
        }

        float step = perimeter / targetCount;
        float traveled = 0;
        int curSeg = 0;

        for (int i = 0; i < targetCount; i++) {
            while (traveled + step > segLens[curSeg] + 0.0001f && curSeg < keyPoints.Length - 1) {
                traveled -= segLens[curSeg];
                curSeg++;
            }
            float t = segLens[curSeg] > 0.0001f ? traveled / segLens[curSeg] : 0;
            int nextNode = (curSeg + 1) % keyPoints.Length;
            result[i] = Vector2.Lerp(keyPoints[curSeg], keyPoints[nextNode], t);
            traveled += step;
        }

        return result;
    }

    void RenderMesh(Vector2[] polyVerts) {
        int count = polyVerts.Length;
        if (count < 3) return;

        int strokeSegments = isClosed ? count : count - 1;

        // 顶点总数：1(中心) + count(内圈) + count(描边内) + count(描边外)
        int totalVerts = enableStroke ? (3 * count + 1) : (count + 1);
        int totalTris = enableStroke ? (count * 3 + strokeSegments * 6) : (count * 3);

        Vector3[] v = new Vector3[totalVerts];
        int[] t = new int[totalTris];
        Color[] c = new Color[totalVerts];

        // 中心点填充
        v[0] = Vector3.zero; c[0] = fillColor;

        // 1. 生成所有顶点
        for (int i = 0; i < count; i++) {
            v[i + 1] = polyVerts[i];
            c[i + 1] = fillColor;

            if (enableStroke) {
                Vector2 pPrev = polyVerts[(i - 1 + count) % count];
                Vector2 pNext = polyVerts[(i + 1) % count];

                // 开放路径的首尾法线处理
                if (!isClosed) {
                    if (i == 0) pPrev = polyVerts[0] - (polyVerts[1] - polyVerts[0]);
                    if (i == count - 1) pNext = polyVerts[count - 1] + (polyVerts[count - 1] - polyVerts[count - 2]);
                }

                Vector2 dir = (pNext - pPrev).normalized;
                if (dir == Vector2.zero) dir = Vector2.right;
                Vector2 normal = new Vector2(dir.y, -dir.x);
                Vector2 outerP = polyVerts[i] + normal * strokeWidth;

                int strokeInnerIdx = count + 1 + i;
                int strokeOuterIdx = 2 * count + 1 + i;

                v[strokeInnerIdx] = polyVerts[i];
                c[strokeInnerIdx] = strokeColor;
                v[strokeOuterIdx] = outerP;
                c[strokeOuterIdx] = strokeColor;
            }
        }

        // 2. 生成三角形拓扑
        for (int i = 0; i < count; i++) {
            int next = (i + 1) % count;

            // 填充三角形 (永远围绕中心点闭合)
            t[i * 3] = 0;
            t[i * 3 + 1] = i + 1;
            t[i * 3 + 2] = next + 1;

            // 描边三角形 (根据 isClosed 决定是否生成最后一段相连的 Quad)
            if (enableStroke && i < strokeSegments) {
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

#if UNITY_EDITOR
    // 当在 Inspector 面板中修改任何数值时，Unity 自动调用此方法
    void OnValidate() {
        if (!Application.isPlaying) {
            // 使用 delayCall 是为了避免 Unity 警告我们在 OnValidate 中修改 Mesh
            UnityEditor.EditorApplication.delayCall += () => {
                if (this != null) {
                    UpdatePreview();
                    // 强制刷新场景视图，让线条也同步更新
                    UnityEditor.SceneView.RepaintAll();
                }
            };
        }
    }
#endif
}