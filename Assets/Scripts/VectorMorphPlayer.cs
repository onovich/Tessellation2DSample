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

    void OnEnable() {
        _mf = GetComponent<MeshFilter>();
        _mr = GetComponent<MeshRenderer>();

        if (_mesh == null) {
            _mesh = new Mesh();
            _mesh.name = "MorphInstance";
            _mf.mesh = _mesh;
        }

        if (_mr.sharedMaterial == null) {
            _mr.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
        }

        if (morphClip != null) UpdateMesh(0);
    }

    void Start() {
        if (Application.isPlaying && autoPlayOnStart) Play();
    }

    void Update() {
        if (Application.isPlaying && _isPlaying && morphClip != null) {
            _timer += Time.deltaTime;
            float tRaw = _timer / morphClip.duration;

            if (loop && tRaw > 1f) {
                _timer = 0; tRaw = 0;
            }

            float t = Mathf.Clamp01(tRaw);
            float curvedT = morphClip.curve.Evaluate(t);
            UpdateMesh(curvedT);

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

        // ✨ 获取真实的闭合状态 (如果任一是开放的贝塞尔，则视为开放)
        bool closedSrc = morphClip.sourceShape.shapeType != VectorShapeType.BezierPath || morphClip.sourceShape.isClosed;
        bool closedDst = morphClip.targetShape.shapeType != VectorShapeType.BezierPath || morphClip.targetShape.isClosed;
        bool actualClosed = closedSrc && closedDst;

        int res = src.Length;
        int strokeSegments = actualClosed ? res : res - 1;

        int vertCount = enableStroke ? (3 * res + 1) : (res + 1);
        int triCount = enableStroke ? (res * 3 + strokeSegments * 6) : (res * 3);

        // 如果拓扑结构发生变化（比如从闭合变为了开放），则重新生成索引
        if (_vertices == null || _vertices.Length != vertCount || _triangles == null || _triangles.Length != triCount) {
            _vertices = new Vector3[vertCount];
            _colors = new Color[vertCount];
            _triangles = new int[triCount];

            for (int i = 0; i < res; i++) {
                int next = (i + 1) % res;
                _triangles[i * 3] = 0;
                _triangles[i * 3 + 1] = i + 1;
                _triangles[i * 3 + 2] = next + 1;

                if (enableStroke && i < strokeSegments) {
                    int inner1 = res + 1 + i;
                    int inner2 = res + 1 + next;
                    int outer1 = 2 * res + 1 + i;
                    int outer2 = 2 * res + 1 + next;

                    int tIdx = res * 3 + i * 6;
                    _triangles[tIdx] = inner1;
                    _triangles[tIdx + 1] = outer1;
                    _triangles[tIdx + 2] = outer2;
                    _triangles[tIdx + 3] = inner1;
                    _triangles[tIdx + 4] = outer2;
                    _triangles[tIdx + 5] = inner2;
                }
            }
            _mesh.Clear();
        }

        Vector2[] currentPos = new Vector2[res];
        for (int i = 0; i < res; i++) {
            int offsetIndex = (i + morphClip.alignOffset) % res;
            if (offsetIndex < 0) offsetIndex += res;
            currentPos[i] = Vector2.Lerp(src[i], dst[offsetIndex], t);
        }

        _vertices[0] = Vector3.zero;
        _colors[0] = fillColor;

        for (int i = 0; i < res; i++) {
            Vector2 p = currentPos[i];
            _vertices[i + 1] = new Vector3(p.x, p.y, 0);
            _colors[i + 1] = fillColor;

            if (enableStroke) {
                Vector2 pPrev = currentPos[(i - 1 + res) % res];
                Vector2 pNext = currentPos[(i + 1) % res];

                // ✨ 修复开放路径两端的法线扭曲
                if (!actualClosed) {
                    if (i == 0) pPrev = currentPos[0] - (currentPos[1] - currentPos[0]);
                    if (i == res - 1) pNext = currentPos[res - 1] + (currentPos[res - 1] - currentPos[res - 2]);
                }

                Vector2 dir = (pNext - pPrev).normalized;
                if (dir == Vector2.zero) dir = Vector2.right;
                Vector2 normal = new Vector2(dir.y, -dir.x);
                Vector2 outerP = p + normal * strokeWidth;

                _vertices[res + 1 + i] = new Vector3(p.x, p.y, 0);
                _colors[res + 1 + i] = strokeColor;
                _vertices[2 * res + 1 + i] = new Vector3(outerP.x, outerP.y, 0);
                _colors[2 * res + 1 + i] = strokeColor;
            }
        }

        _mesh.vertices = _vertices;
        _mesh.colors = _colors;
        if (_mesh.triangles.Length != _triangles.Length) _mesh.triangles = _triangles;
        _mesh.RecalculateBounds();
    }
}