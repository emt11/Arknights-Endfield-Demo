using System.Collections.Generic;
using Endfield.Character;
using Endfield.Pooling;
using UnityEngine;

namespace Endfield.Combat
{
    public enum ActionEndReason
    {
        Completed,
        Cancelled,
        Interrupted
    }

    public readonly struct ActionResult
    {
        public readonly long executionId;
        public readonly ActionDefinition definition;
        public readonly bool started;
        public readonly ActionEndReason reason;

        public ActionResult(long executionId, ActionDefinition definition, bool started, ActionEndReason reason)
        {
            this.executionId = executionId;
            this.definition = definition;
            this.started = started;
            this.reason = reason;
        }
    }

    public sealed class ActionRuntime
    {
        internal readonly bool[] enteredEffects;
        internal readonly PoolHandle[] windowEffects;
        internal readonly List<PoolHandle> ownedEffects = new List<PoolHandle>();
        internal readonly HashSet<(int Group, int Target)> hits = new HashSet<(int, int)>();

        public ActionDefinition Definition { get; }
        public long ExecutionId { get; }
        public Combatant Target { get; }
        public float LocalTime { get; internal set; }
        public float PreviousTime { get; internal set; }
        public float PlaybackRate { get; set; }
        public int NextPoint { get; internal set; }
        public bool ComboAdvanced { get; internal set; }
        public bool Ended { get; internal set; }
        public ActionEndReason EndReason { get; internal set; }
        public WarpState Warp { get; } = new WarpState();
        public ActionRuntime(ActionDefinition definition, long executionId, Combatant target)
        {
            Definition = definition;
            ExecutionId = executionId;
            Target = target;
            PlaybackRate = definition.playbackRate;
            enteredEffects = new bool[definition.effectWindows.Length];
            windowEffects = new PoolHandle[definition.effectWindows.Length];
        }
    }
}
