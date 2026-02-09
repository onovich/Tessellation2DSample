using UnityEngine;

public class Sample : MonoBehaviour {

    public VectorMorphPlayer player;
    public VectorMorphClip anotherClip;

    void TriggerSkill() {
        player.morphClip = anotherClip; // 切换技能图标形状
        player.Play(); // 播放变形
    }

    void Start() { 
        TriggerSkill();
    }

}