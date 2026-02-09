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

    private Mesh _mesh;
    private MeshFilter _mf;
    private MeshRenderer _mr;

    private Vector3[] _vertices;
    private Color[] _colors;
    private int[] _triangles;

    void OnEnable() {
        _mf = GetComponent<MeshFilter>();
        _mr = GetComponent<MeshRenderer>();

        if (_mesh == null) { _mesh = new Mesh(); _mesh.name = "TimelineMesh"; _mf.mesh = _mesh; }
        if (_mr.sharedMaterial == null) _mr.sharedMaterial = new Material(Shader.Find("Sprites/Default"));

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
                debugTime = _currentTime;
            }
        } else {
            Evaluate(debugTime);
        }
    }

    // =========================================================
    // ✨ 核心逻辑：根据时间计算形状 (包含 Loop 修复)
    // =========================================================
    public void Evaluate(float rawTime) {
        if (timelineData == null || timelineData.keyframes.Count == 0) return;

        float duration = timelineData.GetDuration();
        float tLoop = 0f;

        // 1. 计算循环时间
        switch (timelineData.loopMode) {
            case VectorLoopMode.Once:
                tLoop = Mathf.Clamp(rawTime, 0, duration);
                if (rawTime > duration) _isPlaying = false;
                break;
            case VectorLoopMode.Loop:
                // 使用 Repeat 实现循环
                tLoop = Mathf.Repeat(rawTime, duration);
                break;
            case VectorLoopMode.PingPong:
                tLoop = Mathf.PingPong(rawTime, duration);
                break;
        }

        var keys = timelineData.keyframes;
        int prevIndex = -1;

        // 2. 查找当前时间落在哪个区间
        for (int i = 0; i < keys.Count; i++) {
            if (keys[i].time <= tLoop) prevIndex = i;
            else break;
        }

        // -------------------------------------------------------------
        // ✨ 情况 A: 时间在第一帧之前 (且不是 Loop 模式的回环阶段)
        // -------------------------------------------------------------
        if (prevIndex == -1) {
            var first = keys[0];
            RenderShape(first.shapeAsset, first.shapeAsset, 0, first.scale, first.scale, 0);
            return;
        }

        // -------------------------------------------------------------
        // ✨ 情况 B: 时间超过了最后一帧
        //    这是处理 Loop 回环的关键点！
        // -------------------------------------------------------------
        if (prevIndex >= keys.Count - 1) {
            // 如果是 Loop 模式，且 Duration 比最后一帧时间长
            // 我们需要补间：LastFrame -> FirstFrame
            if (timelineData.loopMode == VectorLoopMode.Loop && duration > keys[prevIndex].time) {
                TimelineKeyframe lastKey = keys[keys.Count - 1]; // 源：最后一帧
                TimelineKeyframe firstKey = keys[0];             // 目标：第一帧

                // 计算回环段的总时长： (总时长 - 最后一帧时间) + (第一帧时间)
                // 想象时间轴是圆的，这是两点间的弧长
                float loopSegmentDuration = (duration - lastKey.time) + firstKey.time;

                // 如果时长太短，视为骤变
                if (loopSegmentDuration < 0.0001f || firstKey.isInstant) {
                    RenderShape(firstKey.shapeAsset, firstKey.shapeAsset, 0, firstKey.scale, firstKey.scale, 0);
                } else {
                    // 计算当前在这个回环段的进度
                    // 当前时间 tLoop 肯定大于 lastKey.time
                    float currentSegmentTime = tLoop - lastKey.time;
                    float tLinear = currentSegmentTime / loopSegmentDuration;

                    // 使用第一帧定义的曲线（进入第一帧的曲线）
                    float tCurved = firstKey.curve.Evaluate(tLinear);

                    RenderShape(
                        lastKey.shapeAsset,
                        firstKey.shapeAsset,
                        tCurved,
                        lastKey.scale,
                        firstKey.scale,
                        firstKey.alignOffset // 使用第一帧的对齐设置
                    );
                }
                return;
            } else {
                // 非 Loop 模式，或者 PingPong 模式，或者时间还没到 Duration
                // 保持最后一帧的状态
                var last = keys[keys.Count - 1];
                RenderShape(last.shapeAsset, last.shapeAsset, 0, last.scale, last.scale, 0);
                return;
            }
        }

        // -------------------------------------------------------------
        // ✨ 情况 C: 正常的中间帧补间 (Prev -> Next)
        // -------------------------------------------------------------
        TimelineKeyframe prevKey = keys[prevIndex];
        TimelineKeyframe nextKey = keys[prevIndex + 1];

        // 骤变检测
        if (nextKey.isInstant || (nextKey.time - prevKey.time) < 0.0001f) {
            RenderShape(nextKey.shapeAsset, nextKey.shapeAsset, 0, nextKey.scale, nextKey.scale, 0);
            return;
        }

        // 进度计算
        float segmentDuration = nextKey.time - prevKey.time;
        float segmentLocalTime = tLoop - prevKey.time;
        float t = segmentLocalTime / segmentDuration;

        // 应用曲线
        float tFinal = nextKey.curve.Evaluate(t);

        // 渲染
        RenderShape(
            prevKey.shapeAsset,
            nextKey.shapeAsset,
            tFinal,
            prevKey.scale,
            nextKey.scale,
            nextKey.alignOffset
        );
    }

    // --- 底层 RenderShape 方法保持不变 ---
    void RenderShape(VectorShapeAsset shapeA, VectorShapeAsset shapeB, float t, float scaleA, float scaleB, int offset) {
        if (shapeA == null && shapeB == null) { _mesh.Clear(); return; }

        Vector2[] vertsA = shapeA != null ? shapeA.vertices : shapeB?.vertices;
        Vector2[] vertsB = shapeB != null ? shapeB.vertices : shapeA?.vertices;

        if (vertsA == null || vertsB == null) return;

        int res = vertsA.Length;
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

        float currentScale = Mathf.Lerp(scaleA, scaleB, t);

        if (currentScale < 0.001f) {
            for (int i = 0; i < totalVerts; i++) _vertices[i] = Vector3.zero;
        } else {
            _vertices[0] = Vector3.zero;
            _colors[0] = Color.white;

            for (int i = 0; i < res; i++) {
                Vector2 pA = (shapeA != null) ? vertsA[i] : vertsB[i];

                int idxB = (i + offset) % res;
                if (idxB < 0) idxB += res;
                Vector2 pB = (shapeB != null) ? vertsB[idxB] : vertsA[idxB];

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