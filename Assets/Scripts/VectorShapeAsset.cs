using UnityEngine;

// 1. 存储单一形状的数据（已经过重采样，点数统一）
[CreateAssetMenu(fileName = "NewShape", menuName = "VectorMorph/Shape Asset")]
public class VectorShapeAsset : ScriptableObject
{
    public Vector2[] vertices; // 归一化后的顶点数据（例如固定360个点）
    public int resolution;     // 记录采样时的分辨率
}

// 2. 存储补间动画配置（源形状 + 目标形状 + 对齐参数）
[CreateAssetMenu(fileName = "NewMorphClip", menuName = "VectorMorph/Morph Clip")]
public class VectorMorphClip : ScriptableObject
{
    public VectorShapeAsset sourceShape;
    public VectorShapeAsset targetShape;

    [Tooltip("对齐偏移量：解决形状旋转或翻转问题")]
    public int alignOffset = 0;

    [Tooltip("动画默认时长")]
    public float duration = 1.0f;

    [Tooltip("动画曲线")]
    public AnimationCurve curve = AnimationCurve.EaseInOut(0, 0, 1, 1);
}