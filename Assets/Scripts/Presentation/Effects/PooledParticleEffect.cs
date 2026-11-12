using Endfield.Pooling;
using UnityEngine;

namespace Endfield.Presentation
{
    public sealed class PooledParticleEffect : PooledObject
    {
        [SerializeField] private ParticleSystem[] startSystems;
        private ParticleSystem[] allSystems;
        private TrailRenderer[] trails;
        private Transform followTarget;
        private bool followsTarget;
        private bool started;

        protected override void OnCreated()
        {
            allSystems = GetComponentsInChildren<ParticleSystem>(true);
            trails = GetComponentsInChildren<TrailRenderer>(true);
            foreach (ParticleSystem system in allSystems)
            {
                var main = system.main;
                main.playOnAwake = false;
                main.stopAction = ParticleSystemStopAction.None;
                main.useUnscaledTime = false;
                main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            }

            foreach (TrailRenderer trail in trails)
                trail.autodestruct = false;
        }

        protected override void OnRent()
        {
            started = false;
            followsTarget = false;
            followTarget = null;
            ClearPlayback();
        }

        public PoolHandle Play(Transform follow = null)
        {
            followTarget = follow;
            followsTarget = follow != null;
            if (followsTarget)
                transform.SetPositionAndRotation(follow.position, follow.rotation);

            PoolHandle handle = Activate();
            foreach (ParticleSystem system in startSystems)
                system.Play(false);
            started = true;
            return handle;
        }

        private void Update()
        {
            if (!started)
                return;

            foreach (ParticleSystem system in allSystems)
            {
                if (system.IsAlive(false))
                    return;
            }

            ReturnToPool();
        }

        private void LateUpdate()
        {
            if (!followsTarget)
                return;

            if (followTarget == null || !followTarget.gameObject.activeInHierarchy)
            {
                ReturnToPool();
                return;
            }

            transform.SetPositionAndRotation(followTarget.position, followTarget.rotation);
        }

        protected override void OnReturn()
        {
            started = false;
            followsTarget = false;
            followTarget = null;
            ClearPlayback();
        }

        private void ClearPlayback()
        {
            foreach (ParticleSystem system in allSystems)
                system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            foreach (TrailRenderer trail in trails)
                trail.Clear();
        }
    }
}
