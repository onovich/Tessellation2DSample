using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
[ExecuteAlways]
public class VectorMorphPlayer : MonoBehaviour
{
    [Header("Data")]
    public VectorMorphClip morphClip;

    [Header("Preview / Debug")]
    [Range(0f, 1f)] public float progress = 0f;
    public bool autoPlayOnStart = false;

    private Mesh _mesh;
    private Vector3[] _vertices;
    private Color[] _colors;
    private int[] _triangles;
    
    private float _timer = 0;
    private bool _isPlaying = false;

    void OnEnable()
    {
        _mesh = new Mesh();
        GetComponent<MeshFilter>().mesh = _mesh;
        
        if(morphClip != null) UpdateMesh(0); // 初始化显示
    }

    void Start()
    {
        if (Application.isPlaying && autoPlayOnStart)
        {
            Play();
        }
    }

    void Update()
    {
        // 运行时动画逻辑
        if (Application.isPlaying && _isPlaying && morphClip != null)
        {
            _timer += Time.deltaTime;
            float t = Mathf.Clamp01(_timer / morphClip.duration);
            
            // 应用曲线
            float curvedT = morphClip.curve.Evaluate(t);
            UpdateMesh(curvedT);

            if (t >= 1f) _isPlaying = false; // 结束
        }
        // 编辑器预览逻辑
        else if (!Application.isPlaying)
        {
            UpdateMesh(progress);
        }
    }

    public void Play()
    {
        if (morphClip == null) return;
        _timer = 0;
        _isPlaying = true;
    }

    // --- 核心渲染与插值逻辑 ---
    void UpdateMesh(float t)
    {
        if (morphClip == null || morphClip.sourceShape == null || morphClip.targetShape == null) return;

        Vector2[] src = morphClip.sourceShape.vertices;
        Vector2[] dst = morphClip.targetShape.vertices;
        int res = src.Length; // 假设两者分辨率一致（必须一致）

        // 1. 初始化数组 (Lazy Init)
        int vertCount = res + 1;
        if (_vertices == null || _vertices.Length != vertCount)
        {
            _vertices = new Vector3[vertCount];
            _colors = new Color[vertCount];
            _triangles = new int[res * 3];
            
            // 初始化三角形索引 (只需做一次)
            for(int i=0; i<res; i++)
            {
                _triangles[i*3] = 0;
                _triangles[i*3+1] = i+1;
                _triangles[i*3+2] = (i+1)>=res ? 1 : i+2;
            }
            _mesh.Clear(); // 结构变了要清理
        }

        // 2. 插值计算
        _vertices[0] = Vector3.zero; // 中心点
        _colors[0] = Color.Lerp(Color.white, Color.white, t); // 可以在Clip里加颜色配置

        for (int i = 0; i < res; i++)
        {
            // 获取源点
            Vector2 p1 = src[i];
            
            // 获取目标点 (应用 Offset 对齐 !)
            // 使用 morphClip 中存储的 offset
            int offsetIndex = (i + morphClip.alignOffset) % res;
            if (offsetIndex < 0) offsetIndex += res; // 防止负数
            Vector2 p2 = dst[offsetIndex];

            // Lerp
            Vector2 finalP = Vector2.Lerp(p1, p2, t);
            _vertices[i+1] = new Vector3(finalP.x, finalP.y, 0);
            _colors[i+1] = Color.white;
        }

        // 3. 应用 Mesh
        _mesh.vertices = _vertices;
        _mesh.colors = _colors;
        
        // 只有三角形数组为空或者重建时才赋值，优化性能
        if (_mesh.triangles.Length != _triangles.Length) 
            _mesh.triangles = _triangles;
            
        _mesh.RecalculateBounds();
    }

    // --- 在 Scene 视图绘制 Gizmo 辅助对齐 ---
    void OnDrawGizmos()
    {
        if (morphClip == null || morphClip.sourceShape == null || morphClip.targetShape == null) return;
        
        // 仅在非运行时显示辅助点，方便调节 Offset
        if (!Application.isPlaying)
        {
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