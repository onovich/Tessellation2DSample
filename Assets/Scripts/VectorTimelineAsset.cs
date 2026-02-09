using UnityEngine;
using System.Collections.Generic;
using System;

// 循环模式枚举
public enum VectorLoopMode {
    Once,       // 播放一次停止
    Loop,       // 循环播放
    PingPong    // 乒乓播放（来回）
}

// 关键帧定义
[Serializable]
public struct TimelineKeyframe {
    [Tooltip("时间点 (秒)")]
    public float time;

    [Tooltip("该时刻显示的形状")]
    public VectorShapeAsset shapeAsset;

    [Tooltip("缩放比例 (设为0即隐藏)")]
    public float scale;

    [Tooltip("是否为突变 (无补间)")]
    public bool isInstant;

    [Tooltip("如果不是突变，到达此帧的曲线")]
    public AnimationCurve curve;

    [Tooltip("对齐偏移 (Flash Hint)")]
    public int alignOffset;
}

[CreateAssetMenu(fileName = "NewVectorTimeline", menuName = "VectorMorph/Timeline Asset")]
public class VectorTimelineAsset : ScriptableObject {
    public VectorLoopMode loopMode = VectorLoopMode.Loop;

    [Tooltip("总时长 (秒)，如果为0则自动使用最后一个关键帧的时间")]
    public float duration = 0f;

    // 关键帧列表
    public List<TimelineKeyframe> keyframes = new List<TimelineKeyframe>();

    // 获取有效时长
    public float GetDuration() {
        if (duration > 0) return duration;
        if (keyframes.Count > 0) return keyframes[keyframes.Count - 1].time;
        return 1f;
    }
}