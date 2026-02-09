using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
[ExecuteAlways] // 确保在编辑模式下也能看到形状变化
public class VectorShapeRenderer : MonoBehaviour {
    public enum ShapeType {
        Circle,
        Polygon,
        Star
    }

    [Header("Base Settings")]
    public ShapeType shapeType = ShapeType.Circle;
    public Color color = Color.white;
    public float radius = 1.0f;

    [Header("Shape Specifics")]
    [Range(3, 360)] public int resolution = 60; // 圆形的精细度
    [Range(3, 12)] public int polygonSides = 6; // 多边形边数
    [Range(3, 12)] public int starPoints = 5;   // 星星角数
    [Range(0.1f, 1.0f)] public float starInnerRadiusRatio = 0.4f; // 星星内径比例

    [Header("Sorting Layer (Like SpriteRenderer)")]
    // 注意：这里需要手动输入Layer名称，或者写Editor扩展做下拉菜单
    public string sortingLayerName = "Default";
    public int orderInLayer = 0;

    // --- 内部缓存 ---
    private MeshFilter _meshFilter;
    private MeshRenderer _meshRenderer;
    private Mesh _mesh;
    private Vector3[] _vertices;
    private int[] _triangles;
    private Color[] _colors;

    private void OnEnable() {
        _meshFilter = GetComponent<MeshFilter>();
        _meshRenderer = GetComponent<MeshRenderer>();

        // 初始化材质：使用 Unity 自带的 Sprite 材质，支持顶点色且不需光照
        if (_meshRenderer.sharedMaterial == null) {
            _meshRenderer.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
        }

        _mesh = new Mesh();
        _mesh.name = "VectorShape";
        _meshFilter.mesh = _mesh;

        UpdateMesh();
    }

    private void Update() {
        // 在编辑器下实时更新形状和层级
#if UNITY_EDITOR
        UpdateMesh();
        UpdateSorting();
#endif
    }

    // 专门用于更新 Sorting Layer
    public void UpdateSorting() {
        if (_meshRenderer == null) _meshRenderer = GetComponent<MeshRenderer>();

        // 只有当值改变时才重新赋值，避免不必要的开销
        if (_meshRenderer.sortingLayerName != sortingLayerName)
            _meshRenderer.sortingLayerName = sortingLayerName;

        if (_meshRenderer.sortingOrder != orderInLayer)
            _meshRenderer.sortingOrder = orderInLayer;

        // 实时更新颜色（如果材质支持）
        // 对于 Sprites/Default，颜色主要通过顶点色控制，但也可以通过材质属性
    }

    public void UpdateMesh() {
        if (_mesh == null) return;

        int totalVertices = 0;
        int totalTriangles = 0;

        // 1. 根据形状计算所需的顶点数和三角形数
        // 均采用 "Center Fan" (扇形) 结构：1个中心点 + N个周边点
        switch (shapeType) {
            case ShapeType.Circle:
                totalVertices = resolution + 1; // +1 是圆心
                totalTriangles = resolution;
                break;
            case ShapeType.Polygon:
                totalVertices = polygonSides + 1;
                totalTriangles = polygonSides;
                break;
            case ShapeType.Star:
                // 星星有凸点和凹点，所以周边点数是 角数*2
                totalVertices = (starPoints * 2) + 1;
                totalTriangles = starPoints * 2;
                break;
        }

        // 2. 分配数组内存 (简单优化：仅在长度不够时重新分配)
        if (_vertices == null || _vertices.Length != totalVertices) {
            _vertices = new Vector3[totalVertices];
            _colors = new Color[totalVertices];
            _triangles = new int[totalTriangles * 3];
        }

        // 3. 设置中心点
        _vertices[0] = Vector3.zero;
        _colors[0] = color;

        // 4. 计算周边顶点
        GenerateVertices();

        // 5. 构建三角形索引
        GenerateTriangles(totalTriangles);

        // 6. 赋值给 Mesh
        _mesh.Clear();
        _mesh.vertices = _vertices;
        _mesh.triangles = _triangles;
        _mesh.colors = _colors; // 应用顶点颜色
    }

    void GenerateVertices() {
        int sideCount = 0;
        float angleStep = 0;
        float currentRadius = radius;

        // 确定循环参数
        switch (shapeType) {
            case ShapeType.Circle:
                sideCount = resolution;
                angleStep = 2 * Mathf.PI / sideCount;
                break;
            case ShapeType.Polygon:
                sideCount = polygonSides;
                angleStep = 2 * Mathf.PI / sideCount;
                break;
            case ShapeType.Star:
                sideCount = starPoints * 2;
                angleStep = 2 * Mathf.PI / sideCount;
                break;
        }

        // 旋转校正：让图形默认朝上
        float angleOffset = Mathf.PI / 2;

        for (int i = 0; i < sideCount; i++) {
            float angle = i * angleStep + angleOffset;

            // 特殊处理：星星的凹凸半径交替
            if (shapeType == ShapeType.Star) {
                currentRadius = (i % 2 == 0) ? radius : radius * starInnerRadiusRatio;
            }

            _vertices[i + 1] = new Vector3(Mathf.Cos(angle) * currentRadius, Mathf.Sin(angle) * currentRadius, 0);
            _colors[i + 1] = color; // 所有顶点同色
        }
    }

    void GenerateTriangles(int triangleCount) {
        // 扇形构建：中心点(0) -> 当前点(i+1) -> 下一点
        for (int i = 0; i < triangleCount; i++) {
            _triangles[i * 3] = 0;           // 中心点
            _triangles[i * 3 + 1] = i + 1;   // 当前周边点

            // 下一个点。如果是最后一个三角形，需要闭环回到 1
            int nextIndex = (i + 1) >= triangleCount ? 1 : i + 2;
            _triangles[i * 3 + 2] = nextIndex;
        }
    }
}