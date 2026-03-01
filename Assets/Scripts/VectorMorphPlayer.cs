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

        int res = src.Length;
        // 顶点数：1个中心点 + 内圈填充点 + (可选: 描边内圈点 + 描边外圈点)
        int vertCount = enableStroke ? (3 * res + 1) : (res + 1);
        int triCount = enableStroke ? (res * 9) : (res * 3); // 填充3个 + 描边Quad(6个)

        if (_vertices == null || _vertices.Length != vertCount) {
            _vertices = new Vector3[vertCount];
            _colors = new Color[vertCount];
            _triangles = new int[triCount];

            // 预生成三角形索引
            for (int i = 0; i < res; i++) {
                int next = (i + 1) % res;
                // 中心填充三角形
                _triangles[i * 3] = 0;
                _triangles[i * 3 + 1] = i + 1;
                _triangles[i * 3 + 2] = next + 1;

                if (enableStroke) {
                    // 描边四边形 (由两个三角形组成)
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

        // 预计算当前的形状顶点
        Vector2[] currentPos = new Vector2[res];
        for (int i = 0; i < res; i++) {
            int offsetIndex = (i + morphClip.alignOffset) % res;
            if (offsetIndex < 0) offsetIndex += res;
            currentPos[i] = Vector2.Lerp(src[i], dst[offsetIndex], t);
        }

        // 填充中心点
        _vertices[0] = Vector3.zero;
        _colors[0] = fillColor;

        for (int i = 0; i < res; i++) {
            Vector2 p = currentPos[i];

            // 填充顶点
            _vertices[i + 1] = new Vector3(p.x, p.y, 0);
            _colors[i + 1] = fillColor;

            if (enableStroke) {
                // 计算该顶点的外扩法线 (利用前后相邻点求平分法线)
                Vector2 pPrev = currentPos[(i - 1 + res) % res];
                Vector2 pNext = currentPos[(i + 1) % res];
                Vector2 dir = (pNext - pPrev).normalized;
                if (dir == Vector2.zero) dir = Vector2.right; // 容错处理
                Vector2 normal = new Vector2(dir.y, -dir.x); // 逆时针向外的法向量

                Vector2 outerP = p + normal * strokeWidth;

                // 描边内圈顶点 (位置与填充边缘重合，但颜色不同)
                _vertices[res + 1 + i] = new Vector3(p.x, p.y, 0);
                _colors[res + 1 + i] = strokeColor;

                // 描边外圈顶点
                _vertices[2 * res + 1 + i] = new Vector3(outerP.x, outerP.y, 0);
                _colors[2 * res + 1 + i] = strokeColor;
            }
        }

        _mesh.vertices = _vertices;
        _mesh.colors = _colors;

        if (_mesh.triangles.Length != _triangles.Length)
            _mesh.triangles = _triangles;

        _mesh.RecalculateBounds();
    }
}