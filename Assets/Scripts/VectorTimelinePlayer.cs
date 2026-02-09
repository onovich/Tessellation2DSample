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
    [Range(0, 10)] // 仅用于手动拖动预览
    public float debugTime = 0f;

    // --- 内部状态 ---
    private float _currentTime = 0f;
    private bool _isPlaying = false;

    private Mesh _mesh;
    private MeshFilter _mf;
    private MeshRenderer _mr;

    // 缓存数据以避免 GC
    private Vector3[] _vertices;
    private Color[] _colors;
    private int[] _triangles;

    void OnEnable() {
        _mf = GetComponent<MeshFilter>();
        _mr = GetComponent<MeshRenderer>();

        if (_mesh == null) {
            _mesh = new Mesh();
            _mesh.name = "TimelineMesh";
            _mf.mesh = _mesh;
        }

        if (_mr.sharedMaterial == null)
            _mr.sharedMaterial = new Material(Shader.Find("Sprites/Default"));

        if (timelineData != null) Evaluate(0);
    }

    void Start() {
        if (Application.isPlaying && autoPlay) Play();
    }

    public void Play() {
        _currentTime = 0;
        _isPlaying = true;
    }

    void Update() {
        if (Application.isPlaying) {
            if (_isPlaying && timelineData != null) {
                _currentTime += Time.deltaTime * playbackSpeed;
                Evaluate(_currentTime);
                debugTime = _currentTime; // 同步给 Inspector 看
            }
        } else {
            // 编辑器模式：允许拖动 debugTime 预览
            Evaluate(debugTime);
        }
    }

    // =========================================================
    // ✨ 核心逻辑：根据时间计算形状
    // =========================================================
    public void Evaluate(float rawTime) {
        if (timelineData == null || timelineData.keyframes.Count == 0) return;

        // 1. 处理循环模式，获取 Time In Loop
        float duration = timelineData.GetDuration();
        float tLoop = 0f;

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

        // 2. 查找关键帧 (Timeline Logic)
        // 我们需要找到 index，使得 keyframes[i].time <= tLoop < keyframes[i+1].time
        var keys = timelineData.keyframes;
        int prevIndex = -1;

        // 简单的线性搜索 (关键帧少时够快，如果成千上万帧建议用二分查找)
        for (int i = 0; i < keys.Count; i++) {
            if (keys[i].time <= tLoop) prevIndex = i;
            else break; // 找到了，当前时间已经在 keys[i] 后面了
        }

        // 3. 处理边界情况
        if (prevIndex == -1) // 时间在第一个关键帧之前
        {
            // 显示第一帧
            RenderShape(keys[0].shapeAsset, keys[0].shapeAsset, 0, keys[0].scale, keys[0].scale, 0);
            return;
        }

        if (prevIndex >= keys.Count - 1) // 时间在最后一个关键帧之后
        {
            // 显示最后一帧
            var last = keys[keys.Count - 1];
            RenderShape(last.shapeAsset, last.shapeAsset, 0, last.scale, last.scale, 0);
            return;
        }

        // 4. 计算补间 (Tween Logic)
        TimelineKeyframe prevKey = keys[prevIndex];
        TimelineKeyframe nextKey = keys[prevIndex + 1];

        // 4.1 骤变 (Instant) 检测
        // 规则：如果 NEXT 帧被标记为 Instant，或者两者时间极其接近
        if (nextKey.isInstant || (nextKey.time - prevKey.time) < 0.0001f) {
            // 直接跳到下一帧（或者保持上一帧，看具体需求，通常是跳变）
            RenderShape(nextKey.shapeAsset, nextKey.shapeAsset, 0, nextKey.scale, nextKey.scale, 0);
            return;
        }

        // 4.2 计算进度 t (0~1)
        float segmentDuration = nextKey.time - prevKey.time;
        float segmentLocalTime = tLoop - prevKey.time;
        float tLinear = segmentLocalTime / segmentDuration;

        // 应用曲线 (使用 Next Key 定义的曲线来进入它)
        float tCurved = nextKey.curve.Evaluate(tLinear);

        // 5. 执行渲染
        RenderShape(
            prevKey.shapeAsset,
            nextKey.shapeAsset,
            tCurved,
            prevKey.scale,
            nextKey.scale,
            nextKey.alignOffset // 使用 Target 的 Offset
        );
    }

    // =========================================================
    // 底层渲染 (Mesh Generation)
    // =========================================================
    void RenderShape(VectorShapeAsset shapeA, VectorShapeAsset shapeB, float t, float scaleA, float scaleB, int offset) {
        // 容错：如果 Asset 为空（可能是隐藏帧），我们需要一个 fallback
        // 这里的逻辑是：如果 shapeAsset 为 null，我们假设它是一个缩放为0的点
        if (shapeA == null && shapeB == null) { _mesh.Clear(); return; }

        // 获取顶点数据，如果为空则找另一个借用一下分辨率（反正缩放可能是0）
        Vector2[] vertsA = shapeA != null ? shapeA.vertices : shapeB?.vertices;
        Vector2[] vertsB = shapeB != null ? shapeB.vertices : shapeA?.vertices;

        if (vertsA == null || vertsB == null) return;

        int res = vertsA.Length;
        // 确保 vertsB 也是 res 长度 (在 Bake 时应该保证了一致性，这里做个安全截断/循环)

        // 内存分配
        int totalVerts = res + 1;
        if (_vertices == null || _vertices.Length != totalVerts) {
            _vertices = new Vector3[totalVerts];
            _colors = new Color[totalVerts];
            _triangles = new int[res * 3];
            for (int i = 0; i < res; i++) {
                _triangles[i * 3] = 0; _triangles[i * 3 + 1] = i + 1;
                _triangles[i * 3 + 2] = (i + 1) >= res ? 1 : i + 2;
            }
            _mesh.Clear();
        }

        // 插值 Scale
        float currentScale = Mathf.Lerp(scaleA, scaleB, t);

        // 如果 Scale 极小，直接不渲染以节省性能（或者隐藏）
        if (currentScale < 0.001f) {
            // 把所有点折叠到 0
            for (int i = 0; i < totalVerts; i++) _vertices[i] = Vector3.zero;
        } else {
            _vertices[0] = Vector3.zero;
            _colors[0] = Color.white;

            for (int i = 0; i < res; i++) {
                // 获取 A 的点
                Vector2 pA = (shapeA != null) ? vertsA[i] : vertsB[i]; // 如果A是空，就从B开始变

                // 获取 B 的点 (应用 Offset)
                int idxB = (i + offset) % res;
                if (idxB < 0) idxB += res;
                Vector2 pB = (shapeB != null) ? vertsB[idxB] : vertsA[idxB]; // 如果B是空，就变回A

                // 核心插值
                Vector2 finalPos = Vector2.Lerp(pA, pB, t) * currentScale;
                _vertices[i + 1] = new Vector3(finalPos.x, finalPos.y, 0);
                _colors[i + 1] = Color.white;
            }
        }

        _mesh.vertices = _vertices;
        _mesh.colors = _colors;
        if (_mesh.triangles.Length != _triangles.Length) _mesh.triangles = _triangles;
        _mesh.RecalculateBounds();
    }
}