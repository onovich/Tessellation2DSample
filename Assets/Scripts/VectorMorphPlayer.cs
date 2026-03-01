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

    // ✨ 提取法线和Miter信息，而不是直接返回位置
    private void GetStrokeData(Vector2[] pts, int i, bool isClosed, out Vector2 normal, out float miter) {
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

        if (tangent.sqrMagnitude < 0.01f) tangent = new Vector2(-d1.y, d1.x);

        normal = new Vector2(tangent.y, -tangent.x);

        miter = 1f;
        float dot = Vector2.Dot(d1, tangent);
        if (Mathf.Abs(dot) > 0.05f) {
            miter = Mathf.Clamp(1f / dot, 0.1f, 3f);
        }
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
        int activeOffset = (!closedA && !closedB) ? 0 : morphClip.alignOffset;

        int hiddenCount = 0;
        for (int i = 0; i < res; i++) {
            int idxB = (i + activeOffset) % res;
            if (idxB < 0) idxB += res;
            if ((!closedA && i == res - 1) || (!closedB && idxB == res - 1)) hiddenCount++;
        }

        int strokeSegments = res - hiddenCount;
        int vertCount = enableStroke ? (3 * res + 1) : (res + 1);
        int triCount = enableStroke ? (res * 3 + strokeSegments * 6) : (res * 3);

        bool topoChanged = _lastRes != res || _lastOffset != activeOffset ||
                           _lastClosedA != closedA || _lastClosedB != closedB || _lastEnableStroke != enableStroke;

        if (_vertices == null || _vertices.Length != vertCount || topoChanged) {
            _lastRes = res; _lastOffset = activeOffset;
            _lastClosedA = closedA; _lastClosedB = closedB; _lastEnableStroke = enableStroke;

            _vertices = new Vector3[vertCount]; _colors = new Color[vertCount]; _triangles = new int[triCount];

            int currentTriIdx = res * 3;
            for (int i = 0; i < res; i++) {
                int next = (i + 1) % res;
                _triangles[i * 3] = 0; _triangles[i * 3 + 1] = i + 1; _triangles[i * 3 + 2] = next + 1;

                int idxB = (i + activeOffset) % res;
                if (idxB < 0) idxB += res;

                if (enableStroke && !((!closedA && i == res - 1) || (!closedB && idxB == res - 1))) {
                    int inner1 = res + 1 + i; int inner2 = res + 1 + next;
                    int outer1 = 2 * res + 1 + i; int outer2 = 2 * res + 1 + next;
                    _triangles[currentTriIdx++] = inner1; _triangles[currentTriIdx++] = outer1; _triangles[currentTriIdx++] = outer2;
                    _triangles[currentTriIdx++] = inner1; _triangles[currentTriIdx++] = outer2; _triangles[currentTriIdx++] = inner2;
                }
            }
            _mesh.Clear(); _mesh.vertices = _vertices; _mesh.triangles = _triangles;
        }

        _vertices[0] = Vector3.zero; _colors[0] = fillColor;

        for (int i = 0; i < res; i++) {
            int idxB = (i + activeOffset) % res;
            if (idxB < 0) idxB += res;

            Vector2 innerA = src[i];
            Vector2 innerB = dst[idxB];
            Vector2 currentInner = Vector2.Lerp(innerA, innerB, t);

            _vertices[i + 1] = new Vector3(currentInner.x, currentInner.y, 0);
            _colors[i + 1] = fillColor;

            if (enableStroke) {
                // ✨ 核心修复：通过旋转角度（LerpAngle）而不是插值坐标，彻底解决变细问题
                GetStrokeData(src, i, closedA, out Vector2 normA, out float miterA);
                GetStrokeData(dst, idxB, closedB, out Vector2 normB, out float miterB);

                float angleA = Mathf.Atan2(normA.y, normA.x) * Mathf.Rad2Deg;
                float angleB = Mathf.Atan2(normB.y, normB.x) * Mathf.Rad2Deg;
                float currentAngle = Mathf.LerpAngle(angleA, angleB, t) * Mathf.Deg2Rad;

                Vector2 currentNorm = new Vector2(Mathf.Cos(currentAngle), Mathf.Sin(currentAngle));
                float currentMiter = Mathf.Lerp(miterA, miterB, t);

                Vector2 currentOuter = currentInner + currentNorm * (strokeWidth * currentMiter);

                _vertices[res + 1 + i] = new Vector3(currentInner.x, currentInner.y, 0);
                _colors[res + 1 + i] = strokeColor;
                _vertices[2 * res + 1 + i] = new Vector3(currentOuter.x, currentOuter.y, 0);
                _colors[2 * res + 1 + i] = strokeColor;
            }
        }

        _mesh.vertices = _vertices; _mesh.colors = _colors;
        _mesh.RecalculateBounds();
    }
}