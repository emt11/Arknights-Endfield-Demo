using System;
using Endfield.AnimationSystem;
using Endfield.Character;
using UnityEngine;

namespace Endfield.Combat
{
    [DisallowMultipleComponent]
    public sealed class ActionController : MonoBehaviour
    {
        private Combatant owner;
        private CombatSimulation simulation;

        public ActionRuntime Current { get; private set; }
        public event Action<ActionResult> ResultPublished;

        internal void Initialize(Combatant character, CombatSimulation combatSimulation)
        {
            owner = character;
            simulation = combatSimulation;
            if (owner.Animation == null || owner.Motor == null)
                throw new InvalidOperationException(name + " requires AnimationPlayer and CharacterMotor");
        }

        public bool CanStart(ActionDefinition definition)
        {
            if (!owner.CanExecuteAction || definition == null || simulation.IsPublishing)
                return false;

            return CanCancel(definition.category);
        }

        public bool CanCancel(ActionCategory category)
        {
            if (Current == null)
                return true;

            foreach (CancelWindow window in Current.cancelWindows)
            {
                if ((window.categories & category) != 0)
                    return true;
            }

            return false;
        }

        public bool TryStart(ActionDefinition definition, Combatant target, out long executionId, Vector3 facingDirection = default)
        {
            executionId = 0;
            if (!CanStart(definition))
                return false;

            var next = new ActionRuntime(definition, simulation.NextExecutionId(), target, owner.Animation.Model, simulation.Feedback);
            if (Current != null)
                EndCurrent(ActionEndReason.Cancelled, false);

            Current = next;
            Quaternion initialRotation = next.InitializeOpeningFacing(owner.transform.rotation, facingDirection);
            owner.Motor.ApplyRotation(initialRotation);
            AnimationPlayback outgoing = owner.Animation.StartPlayback(next.Playback);
            simulation.Record(this, new ActionResult(next.ExecutionId, next.Definition));
            executionId = next.ExecutionId;
            outgoing?.End();
            return true;
        }

        public AnimationPlayback PlayAnimation(AnimationDefinition animation, bool interruptAction = false)
        {
            if (!isActiveAndEnabled || owner == null || !owner.isActiveAndEnabled || !owner.Animation.isActiveAndEnabled ||
                simulation.IsPublishing || animation == null)
                return null;
            if (Current == null)
                return owner.Animation.Play(animation);
            if (!interruptAction)
                return null;

            EndCurrent(ActionEndReason.Interrupted, false);
            return owner.Animation.ReplaceWith(animation);
        }

        // 输入已消费后处理零时刻表现，避免起手过程重入
        internal void ProcessInitialPoints(long executionId)
        {
            if (Current != null && Current.ExecutionId == executionId)
                ProcessEvents();
        }

        internal void ProcessEvents()
        {
            if (Current != null)
                owner.Animation.DispatchEvents(Current.Playback);
        }

        internal void FinishAtEnd()
        {
            if (Current != null && Current.LocalTime >= Current.Definition.Duration)
                End(Current.ExecutionId, ActionEndReason.Completed);
        }

        public void End(long executionId, ActionEndReason reason)
        {
            if (simulation != null && simulation.IsPublishing)
            {
                simulation.QueueActionEnd(this, executionId, reason);
                return;
            }

            if (Current == null || Current.ExecutionId != executionId)
                return;

            EndCurrent(reason, true);
        }

        private void EndCurrent(ActionEndReason reason, bool stopPlayback)
        {
            ActionRuntime ended = Current;
            Current = null;
            ended.EndReason = reason;
            simulation.Record(this, new ActionResult(ended.ExecutionId, ended.Definition, reason));
            if (stopPlayback)
                owner.Animation.EndPlayback(ended.Playback);
        }

        internal void Publish(ActionResult result) => ResultPublished?.Invoke(result);

        private void OnDisable()
        {
            if (Current != null)
                End(Current.ExecutionId, ActionEndReason.Cancelled);
        }
    }
}
