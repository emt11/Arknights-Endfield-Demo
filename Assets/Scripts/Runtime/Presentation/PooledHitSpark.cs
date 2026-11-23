using System.Collections;
using Endfield.Pooling;
using UnityEngine;

namespace Endfield.Presentation
{
    [RequireComponent(typeof(ParticleSystem))]
    public sealed class PooledHitSpark : MonoBehaviour, IPoolReset
    {
        [SerializeField] private ParticleSystem particles;
        private PoolLease lease;

        public void Bind(PoolLease current)
        {
            lease = current;
        }

        private void OnEnable()
        {
            particles.Play(true);
            StartCoroutine(ReleaseAfterPlayback(lease));
        }

        private IEnumerator ReleaseAfterPlayback(PoolLease playback)
        {
            while (particles.IsAlive(true)) yield return null;
            playback.TryRelease();
        }

        public void ResetForReuse()
        {
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            lease = default;
        }
    }
}
