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
    [ShowIf(nameof(IsBezier))]
    [PropertyTooltip("决定贝塞尔曲线是否首尾相连。关闭时描边敞开，但填充仍然存在。")]
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

    // ✨ 核心判断：只有贝塞尔曲线且取消了 isClosed 时，才被视为真正的开放路径
    private bool ActualClosed => shapeType != VectorShapeType.BezierPath || isClosed;

    // --- 内部缓存 ---
    private Mesh _mesh;
    private MeshFilter _mf;
    private MeshRenderer _mr;

    void OnEnable() {
        InitComponents();
        UpdatePreview();
    }

#if UNITY_EDITOR
    // 监听面板数值变化，实现实时预览
    void OnValidate() {
        if (!Application.isPlaying) {
            UnityEditor.EditorApplication.delayCall += () => {
                if (this != null) {
                    UpdatePreview();
                    UnityEditor.SceneView.RepaintAll();
                }
            };
        }
    }
#endif

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

        // ✨ 传入真实的闭合状态
        Vector2[] finalData = ResamplePoints(GenerateKeyPoints(), bakeResolution, ActualClosed);

        targetAsset.vertices = finalData;
        targetAsset.resolution = bakeResolution;

        targetAsset.shapeType = shapeType;
        targetAsset.isClosed = isClosed;
        targetAsset.sides = sides;
        targetAsset.radius = radius;
        targetAsset.starPoints = starPoints;
        targetAsset.starInnerRatio = starInnerRatio;

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
        // ✨ 传入真实的闭合状态
        Vector2[] finalVerts = ResamplePoints(keyPoints, bakeResolution, ActualClosed);

        RenderMesh(finalVerts);
    }

    void OnDrawGizmos() {
        if (!showResolutionGizmos || _mesh == null || _mesh.vertices.Length == 0) return;

        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = gizmoColor;

        float dotSize = 0.02f;
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
        int segments = ActualClosed ? bezierNodes.Count : bezierNodes.Count - 1;
        int samplesPerSegment = 30;

        for (int i = 0; i < segments; i++) {
            BezierNode p1 = bezierNodes[i];
            BezierNode p2 = bezierNodes[(i + 1) % bezierNodes.Count];

            Vector2 p0_pos = p1.position;
            Vector2 p1_pos = p1.position + p1.controlOut;
            Vector2 p2_pos = p2.position + p2.controlIn;
            Vector2 p3_pos = p2.position;

            for (int j = 0; j <= samplesPerSegment; j++) {
                if (j == samplesPerSegment && (i < segments - 1 || ActualClosed)) continue;

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
    // ✨ 底层算法：等距重采样与网格构建 (修复了多余的闭合线)
    // =========================================================
    public static Vector2[] ResamplePoints(Vector2[] keyPoints, int targetCount, bool isClosedLoop) {
        if (keyPoints == null || keyPoints.Length < 2) return new Vector2[targetCount];

        Vector2[] result = new Vector2[targetCount];
        float perimeter = 0;

        // 开放路径的有效线段数量比点数少1
        int segmentCount = isClosedLoop ? keyPoints.Length : keyPoints.Length - 1;
        float[] segLens = new float[segmentCount];

        // 1. 计算正确的周长
        for (int i = 0; i < segmentCount; i++) {
            float d = Vector2.Distance(keyPoints[i], keyPoints[(i + 1) % keyPoints.Length]);
            segLens[i] = d;
            perimeter += d;
        }

        // 2. 开放路径时，targetCount个顶点之间只有 (targetCount - 1) 个步长
        float step = perimeter / (isClosedLoop ? targetCount : targetCount - 1);
        float traveled = 0;
        int curSeg = 0;

        for (int i = 0; i < targetCount; i++) {
            // 兜底优化：如果是开放路径的最后一个顶点，直接精准赋值，消除浮点误差导致的短线
            if (!isClosedLoop && i == targetCount - 1) {
                result[i] = keyPoints[keyPoints.Length - 1];
                continue;
            }

            while (traveled + step > segLens[curSeg] + 0.0001f && curSeg < segmentCount - 1) {
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

    private Vector2 GetOuterPoint(Vector2[] pts, int i, bool isClosed, float width) {
        int res = pts.Length;
        Vector2 p = pts[i];
        Vector2 pPrev = pts[(i - 1 + res) % res];
        Vector2 pNext = pts[(i + 1) % res];

        if (!isClosed) {
            if (i == 0) pPrev = p - (pNext - p);
            if (i == res - 1) pNext = p + (p - pPrev);
        }

        Vector2 d1 = (p - pPrev).normalized;
        Vector2 d2 = (pNext - p).normalized;
        Vector2 tangent = (d1 + d2).normalized;

        if (tangent.sqrMagnitude < 0.01f) {
            tangent = new Vector2(-d1.y, d1.x);
        }

        Vector2 normal = new Vector2(tangent.y, -tangent.x);

        float miter = 1f;
        float dot = Vector2.Dot(d1, tangent);
        if (Mathf.Abs(dot) > 0.05f) {
            miter = 1f / dot;
            miter = Mathf.Clamp(miter, 0.1f, 3f);
        }
        return p + normal * (width * miter);
    }

    void RenderMesh(Vector2[] polyVerts) {
        int count = polyVerts.Length;
        if (count < 3) return;

        int strokeSegments = ActualClosed ? count : count - 1;

        int totalVerts = enableStroke ? (3 * count + 1) : (count + 1);
        int totalTris = enableStroke ? (count * 3 + strokeSegments * 6) : (count * 3);

        Vector3[] v = new Vector3[totalVerts];
        int[] t = new int[totalTris];
        Color[] c = new Color[totalVerts];

        v[0] = Vector3.zero; c[0] = fillColor;

        for (int i = 0; i < count; i++) {
            Vector2 currentInner = polyVerts[i];
            v[i + 1] = currentInner;
            c[i + 1] = fillColor;

            if (enableStroke) {
                Vector2 currentOuter = GetOuterPoint(polyVerts, i, ActualClosed, strokeWidth);

                v[count + 1 + i] = currentInner;
                c[count + 1 + i] = strokeColor;
                v[2 * count + 1 + i] = currentOuter;
                c[2 * count + 1 + i] = strokeColor;
            }
        }

        for (int i = 0; i < count; i++) {
            int next = (i + 1) % count;
            t[i * 3] = 0; t[i * 3 + 1] = i + 1; t[i * 3 + 2] = next + 1;

            if (enableStroke && i < strokeSegments) {
                int inner1 = count + 1 + i;
                int inner2 = count + 1 + next;
                int outer1 = 2 * count + 1 + i;
                int outer2 = 2 * count + 1 + next;

                int tIdx = count * 3 + i * 6;
                t[tIdx] = inner1; t[tIdx + 1] = outer1; t[tIdx + 2] = outer2;
                t[tIdx + 3] = inner1; t[tIdx + 4] = outer2; t[tIdx + 5] = inner2;
            }
        }

        _mesh.Clear();
        _mesh.vertices = v;
        _mesh.triangles = t;
        _mesh.colors = c;
    }
}