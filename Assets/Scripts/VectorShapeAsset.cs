using UnityEngine;

public enum VectorShapeType { Circle, Polygon, Star }

[CreateAssetMenu(fileName = "NewVectorShape", menuName = "VectorMorph/Shape Asset")]
public class VectorShapeAsset : ScriptableObject {
    [Header("Baked Data (Read Only)")]
    public Vector2[] vertices; // 实际用于渲染的烘焙数据
    public int resolution;     // 采样精度

    [Header("Source Parameters (For Editing)")]
    // ✨ 新增：存储生成参数，以便 Load 回 Creator 修改
    public VectorShapeType shapeType;
    public int sides;
    public float radius;
    public int starPoints;
    public float starInnerRatio;
}