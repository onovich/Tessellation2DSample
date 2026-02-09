using UnityEngine;

[CreateAssetMenu(fileName = "NewMorphClip", menuName = "VectorMorph/Morph Clip")]
public class VectorMorphClip : ScriptableObject {
    public VectorShapeAsset sourceShape;
    public VectorShapeAsset targetShape;

    [Tooltip("对齐偏移量：解决形状旋转或翻转问题")]
    public int alignOffset = 0;

    [Tooltip("动画默认时长")]
    public float duration = 1.0f;

    [Tooltip("动画曲线")]
    public AnimationCurve curve = AnimationCurve.EaseInOut(0, 0, 1, 1);
}