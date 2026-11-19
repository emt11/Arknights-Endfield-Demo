using System;
using Endfield.Character;
using Endfield.Pooling;
using UnityEngine;

namespace Endfield.Combat
{
    [DisallowMultipleComponent]
    public sealed class ActionController : MonoBehaviour
    {
        private Combatant owner;
        private CombatSimulation simulation;

        public ActionRuntime Current { get; private set; }
        public ActionDefinition BasicAttack => owner.Definition.BasicAttacks[0];
        public event Action<ActionResult> ResultPublished;

        public void Initialize(Combatant character, CombatSimulation combatSimulation)
        {
            owner = character;
            simulation = combatSimulation;
            if (owner.Animation == null || owner.Motor == null)
                throw new InvalidOperationException(name + " requires AnimationPlayer and CharacterMotor");
        }

        public bool CanStart(ActionDefinition definition)
        {
            if (owner.Health.IsDead || definition == null || simulation.IsPublishing)
                return false;

            return CanCancel(definition.category);
        }

        public bool CanCancel(ActionCategory category)
        {
            if (Current == null)
                return true;

            foreach (CancelWindow window in Current.Definition.cancelWindows)
            {
                if (window.Allows(Current.LocalTime, category))
                    return true;
            }

            return false;
        }

        public bool TryStart(ActionDefinition definition, Combatant target, out long executionId)
        {
            executionId = 0;
            if (!CanStart(definition))
                return false;

            var next = new ActionRuntime(definition, simulation.NextExecutionId(), target);
            if (Current != null)
                Cancel(Current.ExecutionId, ActionEndReason.Cancelled);

            Current = next;
            owner.Animation.Play(next);
            simulation.Record(this, new ActionResult(next.ExecutionId, next.Definition, true, default));
            executionId = next.ExecutionId;
            return true;
        }

        // 输入已消费后处理零时刻表现，避免起手过程重入
        public void ProcessInitialPoints(long executionId)
        {
            if (Current != null && Current.ExecutionId == executionId)
            {
                ProcessPoints(Current);
                ProcessEffectWindows(Current);
            }
        }

        public void Advance(float stepDelta)
        {
            if (Current == null)
                return;

            Current.PreviousTime = Current.LocalTime;
            Current.LocalTime = Mathf.Min(Current.Definition.Duration, Current.LocalTime + stepDelta * Current.PlaybackRate);
        }

        public void ProcessEvents()
        {
            if (Current != null)
            {
                ProcessPoints(Current);
                if (Current != null)
                    ProcessEffectWindows(Current);
            }
        }

        private Transform ResolveAnchor(string path) => string.IsNullOrEmpty(path) ? owner.Animation.Model : owner.Animation.Model.Find(path);

        private void ProcessEffectWindows(ActionRuntime runtime)
        {
            EffectWindow[] windows = runtime.Definition.effectWindows;
            for (int index = 0; index < windows.Length; index++)
            {
                EffectWindow window = windows[index];
                if (!runtime.enteredEffects[index] && runtime.LocalTime >= window.start)
                {
                    runtime.enteredEffects[index] = true;
                    Transform anchor = ResolveAnchor(window.bonePath);
                    runtime.windowEffects[index] = simulation.Feedback.PlayEffect(window.effect, anchor.TransformPoint(window.offset), anchor.rotation * Quaternion.Euler(window.rotation), window.follow ? anchor : null);
                }

                // 完整跨过短窗口也必须先进入再退出，不能遗留租借句柄
                if (runtime.enteredEffects[index] && runtime.LocalTime >= window.end)
                    runtime.windowEffects[index].Return();
            }
        }

        private void ProcessPoints(ActionRuntime runtime)
        {
            ActionPoint[] points = runtime.Definition.points;
            while (Current == runtime && runtime.NextPoint < points.Length && points[runtime.NextPoint].time <= runtime.LocalTime)
            {
                ActionPoint point = points[runtime.NextPoint++];
                if (point.kind == ActionPointKind.ComboAdvance)
                {
                    runtime.ComboAdvanced = true;
                    continue;
                }
                Transform anchor = ResolveAnchor(point.bonePath);
                Vector3 position = anchor.TransformPoint(point.offset);
                Quaternion rotation = anchor.rotation * Quaternion.Euler(point.rotation);
                if (point.kind == ActionPointKind.Audio)
                    simulation.Feedback.PlayAudio(point.audio, position);
                else
                {
                    PoolHandle handle = simulation.Feedback.PlayEffect(point.effect, position, rotation, point.follow ? anchor : null);
                    if (point.follow)
                        runtime.ownedEffects.Add(handle);
                }
            }
        }

        public void FinishAtEnd()
        {
            if (Current != null && Current.LocalTime >= Current.Definition.Duration)
                Cancel(Current.ExecutionId, ActionEndReason.Completed);
        }

        public void Cancel(long executionId, ActionEndReason reason)
        {
            if (simulation != null && simulation.IsPublishing)
            {
                simulation.QueueCancellation(this, executionId, reason);
                return;
            }

            if (Current == null || Current.ExecutionId != executionId)
                return;

            ActionRuntime ended = Current;
            Current = null;
            ended.Ended = true;
            ended.EndReason = reason;
            foreach (PoolHandle effect in ended.ownedEffects)
                effect.Return();

            ended.ownedEffects.Clear();
            foreach (PoolHandle effect in ended.windowEffects)
                effect.Return();
            owner.Animation.FadeOut(ended.Definition.blendDuration);
            simulation.Record(this, new ActionResult(ended.ExecutionId, ended.Definition, false, reason));
        }

        internal void Publish(ActionResult result) => ResultPublished?.Invoke(result);

        private void OnDisable()
        {
            if (Current != null)
                Cancel(Current.ExecutionId, ActionEndReason.Cancelled);
        }
    }
}
