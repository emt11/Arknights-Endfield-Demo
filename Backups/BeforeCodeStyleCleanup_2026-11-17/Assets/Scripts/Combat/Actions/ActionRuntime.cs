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
        public readonly long ExecutionId;
        public readonly ActionDefinition Definition;
        public readonly bool Started;
        public readonly ActionEndReason Reason;

        public ActionResult(long executionId, ActionDefinition definition, bool started, ActionEndReason reason)
        {
            ExecutionId = executionId;
            Definition = definition;
            Started = started;
            Reason = reason;
        }
    }

    public sealed class ActionRuntime
    {
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
        internal readonly bool[] EnteredEffects;
        internal readonly PoolHandle[] WindowEffects;
        internal readonly List<PoolHandle> OwnedEffects = new List<PoolHandle>();
        internal readonly HashSet<(int Group, int Target)> Hits = new HashSet<(int, int)>();

        public ActionRuntime(ActionDefinition definition, long executionId, Combatant target)
        {
            Definition = definition;
            ExecutionId = executionId;
            Target = target;
            PlaybackRate = definition.PlaybackRate;
            EnteredEffects = new bool[definition.EffectWindows.Length];
            WindowEffects = new PoolHandle[definition.EffectWindows.Length];
        }
    }
}
