using UnityEngine;
using System.Collections.Generic;
using System;

// ✨ 新增了 BezierPath
public enum VectorShapeType { Circle, Polygon, Star, BezierPath }

// ✨ 新增：贝塞尔节点的数据结构
[Serializable]
public class BezierNode {
    public Vector2 position;
    public Vector2 controlIn;  // 相对位置：相对于 position 的偏移
    public Vector2 controlOut; // 相对位置：相对于 position 的偏移

    public BezierNode(Vector2 pos) {
        position = pos;
        controlIn = new Vector2(-0.5f, 0);
        controlOut = new Vector2(0.5f, 0);
    }
}

[CreateAssetMenu(fileName = "NewVectorShape", menuName = "VectorMorph/Shape Asset")]
public class VectorShapeAsset : ScriptableObject {
    [Header("Baked Data (Read Only)")]
    public Vector2[] vertices; // 实际用于渲染的烘焙数据
    public int resolution;     // 采样精度

    [Header("Source Parameters (For Editing)")]
    public VectorShapeType shapeType;
    public bool isClosed = true; // ✨ 新增：决定描边是否首尾相连
    public int sides;
    public float radius;
    public int starPoints;
    public float starInnerRatio;

    [Header("Bezier Data")]
    // ✨ 新增：存储贝塞尔路径的节点数据
    public List<BezierNode> bezierNodes = new List<BezierNode>();
}