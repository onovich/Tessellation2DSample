using UnityEngine;
using System.Collections.Generic;
using System.Linq; // 用于排序
using TriInspector;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class VectorTimelineCreator : MonoBehaviour {
    [Title("Target Asset")]
    [Required]
    public VectorTimelineAsset targetAsset;

    [Title("Configuration")]
    public VectorLoopMode loopMode = VectorLoopMode.Loop;
    public float customDuration = 0f; // 0 = Auto

    [Title("Keyframes")]
    [ListDrawerSettings(Draggable = true, AlwaysExpanded = true)]
    [PropertyTooltip("无需手动排序，Bake 时会自动按时间排序")]
    public List<TimelineKeyframe> keyframes = new List<TimelineKeyframe>();

    // --- 辅助功能：添加关键帧 ---

    [Button(ButtonSizes.Medium, "Add Keyframe (Show Shape)")]
    [GUIColor(0.8f, 1f, 0.8f)]
    public void AddShowKeyframe() {
        keyframes.Add(new TimelineKeyframe {
            time = GetLastTime() + 1.0f,
            scale = 1.0f,
            curve = AnimationCurve.Linear(0, 0, 1, 1),
            isInstant = false
        });
    }

    [Button(ButtonSizes.Medium, "Add Keyframe (Hide/Zero)")]
    [GUIColor(1f, 0.8f, 0.8f)]
    public void AddHideKeyframe() {
        keyframes.Add(new TimelineKeyframe {
            time = GetLastTime() + 0.5f,
            scale = 0.0f, // 隐藏即 Scale = 0
            curve = AnimationCurve.EaseInOut(0, 0, 1, 1),
            isInstant = false
        });
    }

    float GetLastTime() {
        if (keyframes.Count == 0) return 0f;
        return keyframes.Max(k => k.time);
    }

    // =========================================================
    // ✨ 核心功能：Bake (排序并写入 Asset)
    // =========================================================
    [Button(ButtonSizes.Large, "Bake Timeline Data")]
    [GUIColor(0.2f, 0.8f, 1.0f)]
    public void BakeTimeline() {
        if (targetAsset == null) {
            Debug.LogError("请先分配 Target Asset！");
            return;
        }

        // 1. 自动排序：按时间从小到大
        var sortedList = keyframes.OrderBy(k => k.time).ToList();

        // 2. 写入数据
        targetAsset.loopMode = loopMode;
        targetAsset.duration = customDuration;
        targetAsset.keyframes = new List<TimelineKeyframe>(sortedList); // 深拷贝列表

        // 3. 回写到 Inspector (可选，让列表变整齐)
        this.keyframes = sortedList;

        // 4. 保存
#if UNITY_EDITOR
        EditorUtility.SetDirty(targetAsset);
        AssetDatabase.SaveAssets();
#endif
        Debug.Log($"<color=cyan>Timeline Baked! Contains {sortedList.Count} keyframes.</color>");
    }

    // =========================================================
    // ✨ 核心功能：Load (从 Asset 读取配置)
    // =========================================================
    [Button("Load from Asset")]
    public void LoadFromAsset() {
        if (targetAsset == null) return;
        loopMode = targetAsset.loopMode;
        customDuration = targetAsset.duration;
        keyframes = new List<TimelineKeyframe>(targetAsset.keyframes);
        Debug.Log("Loaded configuration from Asset.");
    }
}