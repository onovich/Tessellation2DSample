using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
[ExecuteAlways]
public class VectorMorphPlayer : MonoBehaviour {
    [Header("Data")]
    public VectorMorphClip morphClip;

    [Header("Style")]
    public Color fillColor = Color.white;
    public bool enableStroke = true;
    public Color strokeColor = Color.black;
    [Min(0f)] public float strokeWidth = 0.1f;

    [Header("Preview / Debug")]
    [Range(0f, 1f)] public float progress = 0f;
    public bool autoPlayOnStart = true;
    public bool loop = false;

    private Mesh _mesh;
    private MeshFilter _mf;
    private MeshRenderer _mr;

    private Vector3[] _vertices;
    private Color[] _colors;
    private int[] _triangles;

    private float _timer = 0;
    private bool _isPlaying = false;

    // ✨ 状态缓存：仅当拓扑结构发生变化时才重建三角形数组，大幅优化性能
    private int _lastRes = -1;
    private int _lastOffset = -1;
    private bool _lastClosedA = false;
    private bool _lastClosedB = false;
    private bool _lastEnableStroke = false;

    void OnEnable() {
        _mf = GetComponent<MeshFilter>();
        _mr = GetComponent<MeshRenderer>();
        if (_mesh == null) { _mesh = new Mesh(); _mesh.name = "MorphInstance"; _mf.mesh = _mesh; }
        if (_mr.sharedMaterial == null) _mr.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
        if (morphClip != null) UpdateMesh(0);
    }

    void Start() {
        if (Application.isPlaying && autoPlayOnStart) Play();
    }

    void Update() {
        if (Application.isPlaying && _isPlaying && morphClip != null) {
            _timer += Time.deltaTime;
            float tRaw = _timer / morphClip.duration;
            if (loop && tRaw > 1f) { _timer = 0; tRaw = 0; }

            float t = Mathf.Clamp01(tRaw);
            UpdateMesh(morphClip.curve.Evaluate(t));

            if (t >= 1f && !loop) _isPlaying = false;
        } else if (!Application.isPlaying) {
            UpdateMesh(progress);
        }
    }

    public void Play() {
        if (morphClip == null) return;
        _timer = 0;
        _isPlaying = true;
    }

    void UpdateMesh(float t) {
        if (morphClip == null || morphClip.sourceShape == null || morphClip.targetShape == null) return;
        if (morphClip.sourceShape.vertices == null || morphClip.targetShape.vertices == null) return;

        Vector2[] src = morphClip.sourceShape.vertices;
        Vector2[] dst = morphClip.targetShape.vertices;
        if (src.Length != dst.Length) return;

        int res = src.Length;

        bool closedA = morphClip.sourceShape.shapeType != VectorShapeType.BezierPath || morphClip.sourceShape.isClosed;
        bool closedB = morphClip.targetShape.shapeType != VectorShapeType.BezierPath || morphClip.targetShape.isClosed;

        // ✨ 防呆保护：如果两个都是开放路径，强制偏移为 0，防止拓扑撕裂产生孤岛
        int activeOffset = (!closedA && !closedB) ? 0 : morphClip.alignOffset;

        // 动态计算隐藏的线段数量
        int hiddenCount = 0;
        for (int i = 0; i < res; i++) {
            int idxB = (i + activeOffset) % res;
            if (idxB < 0) idxB += res;
            bool isGapA = (!closedA && i == res - 1);
            bool isGapB = (!closedB && idxB == res - 1);
            if (isGapA || isGapB) hiddenCount++;
        }

        int strokeSegments = res - hiddenCount;
        int vertCount = enableStroke ? (3 * res + 1) : (res + 1);
        int triCount = enableStroke ? (res * 3 + strokeSegments * 6) : (res * 3);

        bool topoChanged = _lastRes != res || _lastOffset != activeOffset ||
                           _lastClosedA != closedA || _lastClosedB != closedB || _lastEnableStroke != enableStroke;

        if (_vertices == null || _vertices.Length != vertCount || topoChanged) {
            _lastRes = res; _lastOffset = activeOffset;
            _lastClosedA = closedA; _lastClosedB = closedB; _lastEnableStroke = enableStroke;

            _vertices = new Vector3[vertCount];
            _colors = new Color[vertCount];
            _triangles = new int[triCount];

            int currentTriIdx = res * 3;
            for (int i = 0; i < res; i++) {
                int next = (i + 1) % res;
                _triangles[i * 3] = 0; _triangles[i * 3 + 1] = i + 1; _triangles[i * 3 + 2] = next + 1;

                int idxB = (i + activeOffset) % res;
                if (idxB < 0) idxB += res;
                bool isGapA = (!closedA && i == res - 1);
                bool isGapB = (!closedB && idxB == res - 1);

                // ✨ 核心判断：只有当前线段既不跨越 A 的缺口，也不跨越 B 的缺口，才生成描边
                if (enableStroke && !(isGapA || isGapB)) {
                    int inner1 = res + 1 + i; int inner2 = res + 1 + next;
                    int outer1 = 2 * res + 1 + i; int outer2 = 2 * res + 1 + next;
                    _triangles[currentTriIdx++] = inner1; _triangles[currentTriIdx++] = outer1; _triangles[currentTriIdx++] = outer2;
                    _triangles[currentTriIdx++] = inner1; _triangles[currentTriIdx++] = outer2; _triangles[currentTriIdx++] = inner2;
                }
            }
            _mesh.Clear();
            _mesh.vertices = _vertices; // 先占位，防止越界报错
            _mesh.triangles = _triangles;
        }

        Vector2[] currentPos = new Vector2[res];
        for (int i = 0; i < res; i++) {
            int idxB = (i + activeOffset) % res;
            if (idxB < 0) idxB += res;
            currentPos[i] = Vector2.Lerp(src[i], dst[idxB], t);
        }

        _vertices[0] = Vector3.zero; _colors[0] = fillColor;

        for (int i = 0; i < res; i++) {
            _vertices[i + 1] = new Vector3(currentPos[i].x, currentPos[i].y, 0);
            _colors[i + 1] = fillColor;

            if (enableStroke) {
                Vector2 pPrev = currentPos[(i - 1 + res) % res];
                Vector2 pNext = currentPos[(i + 1) % res];

                int idxB = (i + activeOffset) % res;
                if (idxB < 0) idxB += res;
                int prevI = (i - 1 + res) % res;
                int prevIdxB = (prevI + activeOffset) % res;
                if (prevIdxB < 0) prevIdxB += res;

                bool isBeforeGap = (!closedA && i == res - 1) || (!closedB && idxB == res - 1);
                bool isAfterGap = (!closedA && prevI == res - 1) || (!closedB && prevIdxB == res - 1);

                // ✨ 自动进行缺口处的法线平齐补偿
                if (isBeforeGap && isAfterGap) {
                    pNext = currentPos[i] + Vector2.right; pPrev = currentPos[i] - Vector2.right;
                } else if (isBeforeGap) {
                    pNext = currentPos[i] + (currentPos[i] - pPrev);
                } else if (isAfterGap) {
                    pPrev = currentPos[i] - (pNext - currentPos[i]);
                }

                Vector2 dir = (pNext - pPrev).normalized;
                if (dir == Vector2.zero) dir = Vector2.right;
                Vector2 normal = new Vector2(dir.y, -dir.x);
                Vector2 outerP = currentPos[i] + normal * strokeWidth;

                _vertices[res + 1 + i] = new Vector3(currentPos[i].x, currentPos[i].y, 0);
                _colors[res + 1 + i] = strokeColor;
                _vertices[2 * res + 1 + i] = new Vector3(outerP.x, outerP.y, 0);
                _colors[2 * res + 1 + i] = strokeColor;
            }
        }

        _mesh.vertices = _vertices; _mesh.colors = _colors;
        _mesh.RecalculateBounds();
    }
}