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

    void OnEnable() {
        InitializeMesh();
        if (timelineData != null) Evaluate(0);
    }

    void InitializeMesh() {
        _mf = GetComponent<MeshFilter>();
        _mr = GetComponent<MeshRenderer>();
        if (_mesh == null) {
            _mesh = new Mesh();
            _mesh.name = "TimelineMesh";
            _mf.mesh = _mesh;
        } else if (_mf.sharedMesh != _mesh) {
            _mf.mesh = _mesh;
        }
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
            if (_mesh != null) _mesh.Clear();
            return;
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
            if (allKeys[i].time <= tLoop) prevIndex = i;
            else break;
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

        // ✨ 获取真实的闭合状态 
        bool closedA = shapeA == null || shapeA.shapeType != VectorShapeType.BezierPath || shapeA.isClosed;
        bool closedB = shapeB == null || shapeB.shapeType != VectorShapeType.BezierPath || shapeB.isClosed;
        bool actualClosed = closedA && closedB;

        int resA = vertsA.Length;
        int resB = vertsB.Length;

        int strokeSegments = actualClosed ? resA : resA - 1;
        int totalVerts = enableStroke ? (3 * resA + 1) : (resA + 1);
        int totalTris = enableStroke ? (resA * 3 + strokeSegments * 6) : (resA * 3);

        if (_vertices == null || _vertices.Length != totalVerts || _triangles == null || _triangles.Length != totalTris) {
            _vertices = new Vector3[totalVerts];
            _colors = new Color[totalVerts];
            _triangles = new int[totalTris];

            for (int i = 0; i < resA; i++) {
                int next = (i + 1) % resA;
                _triangles[i * 3] = 0;
                _triangles[i * 3 + 1] = i + 1;
                _triangles[i * 3 + 2] = next + 1;

                if (enableStroke && i < strokeSegments) {
                    int inner1 = resA + 1 + i;
                    int inner2 = resA + 1 + next;
                    int outer1 = 2 * resA + 1 + i;
                    int outer2 = 2 * resA + 1 + next;
                    int tIdx = resA * 3 + i * 6;

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

        float currentScale = Mathf.Lerp(scaleA, scaleB, t);

        if (currentScale < 0.001f) {
            for (int i = 0; i < totalVerts; i++) _vertices[i] = Vector3.zero;
        } else {
            _vertices[0] = Vector3.zero;
            _colors[0] = fillColor;

            Vector2[] currentPos = new Vector2[resA];
            for (int i = 0; i < resA; i++) {
                int idxB = (i + offset) % resB;
                if (idxB < 0) idxB += resB;
                currentPos[i] = Vector2.Lerp(vertsA[i], vertsB[idxB], t) * currentScale;
            }

            for (int i = 0; i < resA; i++) {
                Vector2 p = currentPos[i];
                _vertices[i + 1] = new Vector3(p.x, p.y, 0);
                _colors[i + 1] = fillColor;

                if (enableStroke) {
                    Vector2 pPrev = currentPos[(i - 1 + resA) % resA];
                    Vector2 pNext = currentPos[(i + 1) % resA];

                    // ✨ 修复开放路径两端的法线扭曲
                    if (!actualClosed) {
                        if (i == 0) pPrev = currentPos[0] - (currentPos[1] - currentPos[0]);
                        if (i == resA - 1) pNext = currentPos[resA - 1] + (currentPos[resA - 1] - currentPos[resA - 2]);
                    }

                    Vector2 dir = (pNext - pPrev).normalized;
                    if (dir == Vector2.zero) dir = Vector2.right;
                    Vector2 normal = new Vector2(dir.y, -dir.x);

                    Vector2 outerP = p + normal * (strokeWidth * currentScale);

                    _vertices[resA + 1 + i] = new Vector3(p.x, p.y, 0);
                    _colors[resA + 1 + i] = strokeColor;
                    _vertices[2 * resA + 1 + i] = new Vector3(outerP.x, outerP.y, 0);
                    _colors[2 * resA + 1 + i] = strokeColor;
                }
            }
        }

        _mesh.vertices = _vertices;
        _mesh.colors = _colors;
        if (_mesh.triangles.Length != _triangles.Length) _mesh.triangles = _triangles;
        _mesh.RecalculateBounds();
    }
}