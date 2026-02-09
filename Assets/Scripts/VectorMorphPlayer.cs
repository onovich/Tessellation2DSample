using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
[ExecuteAlways]
public class VectorMorphPlayer : MonoBehaviour {
    [Header("Data")]
    public VectorMorphClip morphClip;

    [Header("Preview / Debug")]
    [Range(0f, 1f)] public float progress = 0f;
    public bool autoPlayOnStart = true;
    public bool loop = false; // ✨ 新增：方便测试循环播放

    private Mesh _mesh;
    private MeshFilter _mf;
    private MeshRenderer _mr;

    private Vector3[] _vertices;
    private Color[] _colors;
    private int[] _triangles;

    private float _timer = 0;
    private bool _isPlaying = false;

    void OnEnable() {
        // 1. 获取组件
        _mf = GetComponent<MeshFilter>();
        _mr = GetComponent<MeshRenderer>();

        // 2. 初始化 Mesh
        if (_mesh == null) {
            _mesh = new Mesh();
            _mesh.name = "MorphInstance";
            _mf.mesh = _mesh;
        }

        // 3. ✨✨ 关键修复：如果没有材质，自动给一个 Sprite 材质
        if (_mr.sharedMaterial == null) {
            _mr.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
        }

        // 4. 立即刷新一次 Mesh，防止启动时是空的
        if (morphClip != null) UpdateMesh(0);
    }

    void Start() {
        if (Application.isPlaying && autoPlayOnStart) {
            Play();
        }
    }

    void Update() {
        if (Application.isPlaying && _isPlaying && morphClip != null) {
            _timer += Time.deltaTime;
            float tRaw = _timer / morphClip.duration;

            // 循环逻辑 (可选)
            if (loop && tRaw > 1f) {
                _timer = 0; tRaw = 0;
            }

            float t = Mathf.Clamp01(tRaw);

            // 应用曲线
            float curvedT = morphClip.curve.Evaluate(t);
            UpdateMesh(curvedT);

            if (t >= 1f && !loop) _isPlaying = false;
        } else if (!Application.isPlaying) {
            // 编辑器预览
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

        // 安全检查：防止空数据报错
        if (morphClip.sourceShape.vertices == null || morphClip.targetShape.vertices == null) return;

        Vector2[] src = morphClip.sourceShape.vertices;
        Vector2[] dst = morphClip.targetShape.vertices;

        // 安全检查：分辨率不匹配
        if (src.Length != dst.Length) {
            Debug.LogError($"分辨率不匹配! Src:{src.Length} Dst:{dst.Length}");
            return;
        }

        int res = src.Length;
        int vertCount = res + 1;

        if (_vertices == null || _vertices.Length != vertCount) {
            _vertices = new Vector3[vertCount];
            _colors = new Color[vertCount];
            _triangles = new int[res * 3];

            for (int i = 0; i < res; i++) {
                _triangles[i * 3] = 0;
                _triangles[i * 3 + 1] = i + 1;
                _triangles[i * 3 + 2] = (i + 1) >= res ? 1 : i + 2;
            }
            _mesh.Clear();
        }

        _vertices[0] = Vector3.zero;
        _colors[0] = Color.white;

        for (int i = 0; i < res; i++) {
            Vector2 p1 = src[i];

            // Offset 计算
            int offsetIndex = (i + morphClip.alignOffset) % res;
            if (offsetIndex < 0) offsetIndex += res;

            // 防止索引越界 (双重保险)
            if (offsetIndex >= dst.Length) offsetIndex = 0;

            Vector2 p2 = dst[offsetIndex];

            Vector2 finalP = Vector2.Lerp(p1, p2, t);
            _vertices[i + 1] = new Vector3(finalP.x, finalP.y, 0);
            _colors[i + 1] = Color.white;
        }

        _mesh.vertices = _vertices;
        _mesh.colors = _colors;

        if (_mesh.triangles.Length != _triangles.Length)
            _mesh.triangles = _triangles;

        _mesh.RecalculateBounds(); // ✨ 防止被视锥体剔除
    }

    // --- 在 Scene 视图绘制 Gizmo 辅助对齐 ---
    void OnDrawGizmos() {
        if (morphClip == null || morphClip.sourceShape == null || morphClip.targetShape == null) return;

        // 仅在非运行时显示辅助点，方便调节 Offset
        if (!Application.isPlaying) {
            Vector2[] src = morphClip.sourceShape.vertices;
            Vector2[] dst = morphClip.targetShape.vertices;

            // 绿色球：源形状起点
            Gizmos.color = Color.green;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireSphere(src[0], 0.1f);

            // 红色球：目标形状的起点 (应用 Offset 后)
            Gizmos.color = Color.red;
            int offsetIndex = morphClip.alignOffset % src.Length;
            if (offsetIndex < 0) offsetIndex += src.Length;
            Gizmos.DrawWireSphere(dst[offsetIndex], 0.15f);
        }
    }
}