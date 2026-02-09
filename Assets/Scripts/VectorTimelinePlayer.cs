using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
[ExecuteAlways]
public class VectorTimelinePlayer : MonoBehaviour {
    [Header("Data")]
    public VectorTimelineAsset timelineData;

    [Header("Playback")]
    public bool autoPlay = true;
    public float playbackSpeed = 1.0f;

    [Header("Debug")]
    [Range(0, 10)]
    public float debugTime = 0f;

    // --- 内部状态 ---
    private float _currentTime = 0f;
    private bool _isPlaying = false;

    // ✨ 新增：公开属性供编辑器读取（只读）
    public float CurrentTime => _currentTime;
    public bool IsPlaying => _isPlaying;

    private Mesh _mesh;
    private MeshFilter _mf;
    private MeshRenderer _mr;

    private Vector3[] _vertices;
    private Color[] _colors;
    private int[] _triangles;

    void OnEnable() {
        InitializeMesh();
        if (timelineData != null) Evaluate(0);
    }

    // 封装初始化逻辑，防止 Mesh 丢失
    void InitializeMesh() {
        _mf = GetComponent<MeshFilter>();
        _mr = GetComponent<MeshRenderer>();

        if (_mesh == null) {
            _mesh = new Mesh();
            _mesh.name = "TimelineMesh";
            _mf.mesh = _mesh;
        } else if (_mf.sharedMesh != _mesh) // 处理编辑器下 Mesh 引用丢失的情况
          {
            _mf.mesh = _mesh;
        }

        if (_mr.sharedMaterial == null)
            _mr.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
    }

    void Start() {
        if (Application.isPlaying && autoPlay) Play();
    }

    public void Play() {
        _currentTime = 0;
        _isPlaying = true;
    }

    void Update() {
        // 运行时逻辑
        if (Application.isPlaying) {
            if (_isPlaying && timelineData != null) {
                _currentTime += Time.deltaTime * playbackSpeed;
                Evaluate(_currentTime);
                debugTime = _currentTime;
            }
        }
        // 编辑器预览逻辑
        else {
            if (_mesh == null) InitializeMesh(); // 编辑器下防止 Mesh 丢失
            Evaluate(debugTime);
        }
    }

    // =========================================================
    // ✨ 核心逻辑：根据时间计算形状
    // =========================================================
    public void Evaluate(float rawTime) {
        // 🛡️ 安全检查：防止空数据报错
        if (timelineData == null || timelineData.keyframes == null || timelineData.keyframes.Count == 0) {
            if (_mesh != null) _mesh.Clear();
            return;
        }

        float duration = timelineData.GetDuration();
        float tLoop = 0f;

        // 1. 循环时间计算
        switch (timelineData.loopMode) {
            case VectorLoopMode.Once:
                tLoop = Mathf.Clamp(rawTime, 0, duration);
                if (rawTime > duration) _isPlaying = false;
                break;
            case VectorLoopMode.Loop:
                tLoop = Mathf.Repeat(rawTime, duration);
                break;
            case VectorLoopMode.PingPong:
                tLoop = Mathf.PingPong(rawTime, duration);
                break;
        }

        var allKeys = timelineData.keyframes;
        int prevIndex = -1;

        // 2. 查找 Prev Key (✨ 核心约束：忽略 Duration 之外的关键帧)
        for (int i = 0; i < allKeys.Count; i++) {
            // 如果关键帧已经超出 Duration，直接停止搜索 (前提是 List 已按时间排序，通常 Bake 时已排序)
            if (allKeys[i].time > duration) break;

            if (allKeys[i].time <= tLoop) prevIndex = i;
            else break;
        }

        // --- 情况 A: 时间在第一帧之前 ---
        if (prevIndex == -1) {
            var first = allKeys[0];
            // 极端情况：连第一帧都在 Duration 外
            if (first.time > duration) { _mesh.Clear(); return; }
            RenderShape(first.shapeAsset, first.shapeAsset, 0, first.scale, first.scale, 0);
            return;
        }

        // --- 情况 B: 它是有效范围内的“最后一帧” ---
        // 判定条件：它是 List 的最后一个，或者下一个关键帧已经在 Duration 外了
        // 🛡️ 安全检查：防止数组越界
        bool isNextKeyOutOfBounds = (prevIndex + 1 < allKeys.Count) && (allKeys[prevIndex + 1].time > duration);
        bool isLastKeyInList = (prevIndex >= allKeys.Count - 1);

        if (isLastKeyInList || isNextKeyOutOfBounds) {
            // 处理 Loop 回环逻辑
            if (timelineData.loopMode == VectorLoopMode.Loop) {
                TimelineKeyframe lastKey = allKeys[prevIndex];
                TimelineKeyframe firstKey = allKeys[0];

                // 极端情况检查
                if (firstKey.time > duration) return;

                // 计算回环段: (Duration - lastTime) + firstTime
                float loopSegmentDuration = (duration - lastKey.time) + firstKey.time;

                // 防止除以0
                if (loopSegmentDuration < 0.0001f || firstKey.isInstant) {
                    RenderShape(firstKey.shapeAsset, firstKey.shapeAsset, 0, firstKey.scale, firstKey.scale, 0);
                } else {
                    float currentSegmentTime = tLoop - lastKey.time;
                    float tLinear = currentSegmentTime / loopSegmentDuration;
                    float tCurved = firstKey.curve.Evaluate(tLinear);

                    RenderShape(lastKey.shapeAsset, firstKey.shapeAsset, tCurved, lastKey.scale, firstKey.scale, firstKey.alignOffset);
                }
                return;
            } else {
                // 非 Loop 模式，保持在该帧状态
                var last = allKeys[prevIndex];
                RenderShape(last.shapeAsset, last.shapeAsset, 0, last.scale, last.scale, 0);
                return;
            }
        }

        // --- 情况 C: 正常的中间帧补间 ---
        TimelineKeyframe prevKey = allKeys[prevIndex];
        TimelineKeyframe nextKey = allKeys[prevIndex + 1];

        if (nextKey.isInstant || (nextKey.time - prevKey.time) < 0.0001f) {
            RenderShape(nextKey.shapeAsset, nextKey.shapeAsset, 0, nextKey.scale, nextKey.scale, 0);
            return;
        }

        float segmentDuration = nextKey.time - prevKey.time;
        float segmentLocalTime = tLoop - prevKey.time;
        float t = segmentLocalTime / segmentDuration;
        float tFinal = nextKey.curve.Evaluate(t);

        RenderShape(prevKey.shapeAsset, nextKey.shapeAsset, tFinal, prevKey.scale, nextKey.scale, nextKey.alignOffset);
    }

    void RenderShape(VectorShapeAsset shapeA, VectorShapeAsset shapeB, float t, float scaleA, float scaleB, int offset) {
        // 🛡️ 安全检查：全空则清空 Mesh
        if (shapeA == null && shapeB == null) { _mesh.Clear(); return; }

        // 处理空帧情况（例如某一帧形状为空，视为隐藏）
        Vector2[] vertsA = shapeA != null ? shapeA.vertices : shapeB?.vertices;
        Vector2[] vertsB = shapeB != null ? shapeB.vertices : shapeA?.vertices;

        if (vertsA == null || vertsB == null) return;

        int resA = vertsA.Length;
        int resB = vertsB.Length;

        // 我们以 A 的顶点数作为主基准来构建网格
        int totalVerts = resA + 1;

        if (_vertices == null || _vertices.Length != totalVerts) {
            _vertices = new Vector3[totalVerts];
            _colors = new Color[totalVerts];
            _triangles = new int[resA * 3];
            for (int i = 0; i < resA; i++) {
                _triangles[i * 3] = 0;
                _triangles[i * 3 + 1] = i + 1;
                _triangles[i * 3 + 2] = (i + 1) >= resA ? 1 : i + 2;
            }
            _mesh.Clear();
        }

        float currentScale = Mathf.Lerp(scaleA, scaleB, t);

        if (currentScale < 0.001f) {
            // 优化：缩放极小时折叠顶点
            for (int i = 0; i < totalVerts; i++) _vertices[i] = Vector3.zero;
        } else {
            _vertices[0] = Vector3.zero;
            _colors[0] = Color.white;

            for (int i = 0; i < resA; i++) {
                Vector2 pA = vertsA[i]; // 不会越界，因为循环次数是 resA

                // 🛡️ 核心修复：防止 Target 形状点数不同导致的越界
                // 计算 B 的索引时，必须对 resB 取模，而不是 resA
                int idxB = (i + offset) % resB;
                if (idxB < 0) idxB += resB;

                Vector2 pB = vertsB[idxB]; // 安全访问

                Vector2 finalPos = Vector2.Lerp(pA, pB, t) * currentScale;
                _vertices[i + 1] = new Vector3(finalPos.x, finalPos.y, 0);
                _colors[i + 1] = Color.white;
            }
        }

        _mesh.vertices = _vertices;
        _mesh.colors = _colors;

        // 只有当三角形数量变化时才重新赋值（优化性能）
        if (_mesh.triangles.Length != _triangles.Length)
            _mesh.triangles = _triangles;

        _mesh.RecalculateBounds();
    }
}