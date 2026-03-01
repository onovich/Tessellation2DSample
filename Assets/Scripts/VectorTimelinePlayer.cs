using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
[ExecuteAlways]
public class VectorTimelinePlayer : MonoBehaviour {
    [Header("Data")]
    public VectorTimelineAsset timelineData;

    [Header("Style")]
    public Color fillColor = Color.white;
    public bool enableStroke = true;
    public Color strokeColor = Color.black;
    [Min(0f)] public float strokeWidth = 0.1f;

    [Header("Playback")]
    public bool autoPlay = true;
    public float playbackSpeed = 1.0f;

    [Header("Debug")]
    [Range(0, 10)]
    public float debugTime = 0f;

    private float _currentTime = 0f;
    private bool _isPlaying = false;

    public float CurrentTime => _currentTime;
    public bool IsPlaying => _isPlaying;

    private Mesh _mesh;
    private MeshFilter _mf;
    private MeshRenderer _mr;
    private Vector3[] _vertices;
    private Color[] _colors;
    private int[] _triangles;

    // ✨ 状态缓存
    private int _lastResA = -1;
    private int _lastResB = -1;
    private int _lastOffset = -1;
    private bool _lastClosedA = false;
    private bool _lastClosedB = false;
    private bool _lastEnableStroke = false;

    void OnEnable() {
        InitializeMesh();
        if (timelineData != null) Evaluate(0);
    }

    void InitializeMesh() {
        _mf = GetComponent<MeshFilter>();
        _mr = GetComponent<MeshRenderer>();
        if (_mesh == null) { _mesh = new Mesh(); _mesh.name = "TimelineMesh"; _mf.mesh = _mesh; } else if (_mf.sharedMesh != _mesh) { _mf.mesh = _mesh; }
        if (_mr.sharedMaterial == null) _mr.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
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
            if (_mesh == null) InitializeMesh();
            Evaluate(debugTime);
        }
    }

    public void Evaluate(float rawTime) {
        if (timelineData == null || timelineData.keyframes == null || timelineData.keyframes.Count == 0) {
            if (_mesh != null) _mesh.Clear(); return;
        }

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

        var allKeys = timelineData.keyframes;
        int prevIndex = -1;

        for (int i = 0; i < allKeys.Count; i++) {
            if (allKeys[i].time > duration) break;
            if (allKeys[i].time <= tLoop) prevIndex = i; else break;
        }

        if (prevIndex == -1) {
            var first = allKeys[0];
            if (first.time > duration) { _mesh.Clear(); return; }
            RenderShape(first.shapeAsset, first.shapeAsset, 0, first.scale, first.scale, 0);
            return;
        }

        bool isNextKeyOutOfBounds = (prevIndex + 1 < allKeys.Count) && (allKeys[prevIndex + 1].time > duration);
        bool isLastKeyInList = (prevIndex >= allKeys.Count - 1);

        if (isLastKeyInList || isNextKeyOutOfBounds) {
            if (timelineData.loopMode == VectorLoopMode.Loop) {
                TimelineKeyframe lastKey = allKeys[prevIndex];
                TimelineKeyframe firstKey = allKeys[0];
                if (firstKey.time > duration) return;

                float loopSegmentDuration = (duration - lastKey.time) + firstKey.time;
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
                var last = allKeys[prevIndex];
                RenderShape(last.shapeAsset, last.shapeAsset, 0, last.scale, last.scale, 0);
                return;
            }
        }

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
        if (shapeA == null && shapeB == null) { _mesh.Clear(); return; }

        Vector2[] vertsA = shapeA != null ? shapeA.vertices : shapeB?.vertices;
        Vector2[] vertsB = shapeB != null ? shapeB.vertices : shapeA?.vertices;
        if (vertsA == null || vertsB == null) return;

        bool closedA = shapeA == null || shapeA.shapeType != VectorShapeType.BezierPath || shapeA.isClosed;
        bool closedB = shapeB == null || shapeB.shapeType != VectorShapeType.BezierPath || shapeB.isClosed;

        // ✨ 防止孤岛
        int activeOffset = (!closedA && !closedB) ? 0 : offset;

        int resA = vertsA.Length;
        int resB = vertsB.Length;

        int hiddenCount = 0;
        for (int i = 0; i < resA; i++) {
            int idxB = (i + activeOffset) % resB;
            if (idxB < 0) idxB += resB;
            bool isGapA = (!closedA && i == resA - 1);
            bool isGapB = (!closedB && idxB == resB - 1);
            if (isGapA || isGapB) hiddenCount++;
        }

        int strokeSegments = resA - hiddenCount;
        int totalVerts = enableStroke ? (3 * resA + 1) : (resA + 1);
        int totalTris = enableStroke ? (resA * 3 + strokeSegments * 6) : (resA * 3);

        bool topoChanged = _lastResA != resA || _lastResB != resB || _lastOffset != activeOffset ||
                           _lastClosedA != closedA || _lastClosedB != closedB || _lastEnableStroke != enableStroke;

        if (_vertices == null || _vertices.Length != totalVerts || topoChanged) {
            _lastResA = resA; _lastResB = resB; _lastOffset = activeOffset;
            _lastClosedA = closedA; _lastClosedB = closedB; _lastEnableStroke = enableStroke;

            _vertices = new Vector3[totalVerts];
            _colors = new Color[totalVerts];
            _triangles = new int[totalTris];

            int currentTriIdx = resA * 3;
            for (int i = 0; i < resA; i++) {
                int next = (i + 1) % resA;
                _triangles[i * 3] = 0; _triangles[i * 3 + 1] = i + 1; _triangles[i * 3 + 2] = next + 1;

                int idxB = (i + activeOffset) % resB;
                if (idxB < 0) idxB += resB;
                bool isGapA = (!closedA && i == resA - 1);
                bool isGapB = (!closedB && idxB == resB - 1);

                if (enableStroke && !(isGapA || isGapB)) {
                    int inner1 = resA + 1 + i; int inner2 = resA + 1 + next;
                    int outer1 = 2 * resA + 1 + i; int outer2 = 2 * resA + 1 + next;
                    _triangles[currentTriIdx++] = inner1; _triangles[currentTriIdx++] = outer1; _triangles[currentTriIdx++] = outer2;
                    _triangles[currentTriIdx++] = inner1; _triangles[currentTriIdx++] = outer2; _triangles[currentTriIdx++] = inner2;
                }
            }
            _mesh.Clear();
            _mesh.vertices = _vertices;
            _mesh.triangles = _triangles;
        }

        float currentScale = Mathf.Lerp(scaleA, scaleB, t);

        if (currentScale < 0.001f) {
            for (int i = 0; i < totalVerts; i++) _vertices[i] = Vector3.zero;
        } else {
            _vertices[0] = Vector3.zero;
            _colors[0] = fillColor;

            Vector2[] currentPos = new Vector2[resA];
            for (int i = 0; i < resA; i++) {
                int idxB = (i + activeOffset) % resB;
                if (idxB < 0) idxB += resB;
                currentPos[i] = Vector2.Lerp(vertsA[i], vertsB[idxB], t) * currentScale;
            }

            for (int i = 0; i < resA; i++) {
                _vertices[i + 1] = new Vector3(currentPos[i].x, currentPos[i].y, 0);
                _colors[i + 1] = fillColor;

                if (enableStroke) {
                    Vector2 pPrev = currentPos[(i - 1 + resA) % resA];
                    Vector2 pNext = currentPos[(i + 1) % resA];

                    int idxB = (i + activeOffset) % resB;
                    if (idxB < 0) idxB += resB;
                    int prevI = (i - 1 + resA) % resA;
                    int prevIdxB = (prevI + activeOffset) % resB;
                    if (prevIdxB < 0) prevIdxB += resB;

                    bool isBeforeGap = (!closedA && i == resA - 1) || (!closedB && idxB == resB - 1);
                    bool isAfterGap = (!closedA && prevI == resA - 1) || (!closedB && prevIdxB == resB - 1);

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

                    Vector2 outerP = currentPos[i] + normal * (strokeWidth * currentScale);

                    _vertices[resA + 1 + i] = new Vector3(currentPos[i].x, currentPos[i].y, 0);
                    _colors[resA + 1 + i] = strokeColor;
                    _vertices[2 * resA + 1 + i] = new Vector3(outerP.x, outerP.y, 0);
                    _colors[2 * resA + 1 + i] = strokeColor;
                }
            }
        }

        _mesh.vertices = _vertices; _mesh.colors = _colors;
        _mesh.RecalculateBounds();
    }
}