using Endfield.Pooling;
using Endfield.Presentation;
using UnityEngine;

public sealed class PoolTest : MonoBehaviour
{
    [SerializeField] private GameObjectPool pool;
    [SerializeField] private CombatFeedbackPlayer feedback;
    [SerializeField] private AudioAsset audio;
    [SerializeField] private ParticleEffectAsset effect;
    [SerializeField, Min(1)] private int playCount = 1;

    [ContextMenu("测试/播放")]
    private void Play()
    {
        if (!Application.isPlaying) return;

        for (int i = 0; i < playCount; i++)
        {
            feedback.PlayAudio(audio, transform.position);
            feedback.PlayEffect(effect, transform.position, transform.rotation);
        }

        PrintCounts();
    }

    [ContextMenu("测试/归还全部")]
    private void ReturnAll()
    {
        if (!Application.isPlaying) return;

        pool.ReturnAll();
        PrintCounts();
    }

    [ContextMenu("测试/打印数量")]
    private void PrintCounts()
    {
        if (!Application.isPlaying) return;

        if (audio != null) PrintCount(audio.Prefab);
        if (effect != null) PrintCount(effect.Prefab);
    }

    private void PrintCount(PooledObject prefab)
    {
        pool.GetCounts(prefab, out int rented, out int inactive);
        Debug.Log($"{prefab.name}：借出 {rented}，空闲 {inactive}", this);
    }
}