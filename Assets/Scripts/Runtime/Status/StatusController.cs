using System;
using System.Collections.Generic;
using Endfield.Common;
using Endfield.Effects;
using Endfield.Resources;
using Endfield.Stats;

namespace Endfield.Status
{
    public enum StatusType { Vulnerable, Heat, Cryo, Frozen, Staggered }
    public enum AttachmentElement { None, Heat, Cryo }

    public readonly struct StatusChange
    {
        public StatusType Type { get; }
        public int PreviousStacks { get; }
        public int CurrentStacks { get; }

        internal StatusChange(StatusType type, int previous, int current)
        {
            Type = type;
            PreviousStacks = previous;
            CurrentStacks = current;
        }
    }

    public sealed class StatusController : IDisposable
    {
        private readonly StatusRuleProfile rules;
        private readonly CharacterResourceContainer resources;
        private readonly EffectController effects;
        private readonly IReadOnlyStatSet target;
        private readonly Dictionary<StatusType, (int Stacks, EffectHandle Timer)> active = new();
        private readonly ChangeNotifier<IReadOnlyList<StatusChange>> changes = new();
        private readonly IDisposable endedSubscription;

        public StatusController(StatusRuleProfile rules, CharacterResourceContainer resources,
            EffectController effects, IReadOnlyStatSet target)
        {
            if (!rules) throw new ArgumentNullException(nameof(rules));
            rules.Validate();
            this.rules = rules;
            this.resources = resources;
            this.effects = effects;
            this.target = target;
            endedSubscription = effects.SubscribeEnded(OnTimerEnded);
        }

        public int GetStacks(StatusType type) => active.TryGetValue(type, out var state) ? state.Stacks : 0;

        public bool IsActive(StatusType type) => active.ContainsKey(type);

        public double PhysicalDamageTakenMultiplier => 1 + GetStacks(StatusType.Vulnerable) * rules.PhysicalBonusPerStack;

        public IDisposable Subscribe(Action<IReadOnlyList<StatusChange>> listener) => changes.Subscribe(listener);

        public IReadOnlyList<StatusChange> ApplyVulnerable(int stacks)
        {
            return Publish(AddStacks(StatusType.Vulnerable, stacks, rules.MaxVulnerableStacks));
        }

        public IReadOnlyList<StatusChange> ApplyAttachment(AttachmentElement element, int stacks)
        {
            if (stacks <= 0) throw new ArgumentOutOfRangeException(nameof(stacks));
            StatusType incoming = element switch
            {
                AttachmentElement.Heat => StatusType.Heat,
                AttachmentElement.Cryo => StatusType.Cryo,
                _ => throw new ArgumentOutOfRangeException(nameof(element))
            };
            var previous = incoming == StatusType.Heat ? StatusType.Cryo : StatusType.Heat;
            if (!IsActive(previous)) return Publish(AddStacks(incoming, stacks, rules.MaxAttachmentStacks));

            // 旧热与新冰生成冻结；旧冰与新热直接置换，同一次操作只发布最终状态
            var added = incoming == StatusType.Cryo
                ? SetStacks(StatusType.Frozen, 1)
                : AddStacks(incoming, stacks, rules.MaxAttachmentStacks);
            return Publish(RemoveState(previous), added);
        }

        public bool TryConsume(StatusType type, int count)
        {
            if (count <= 0 || type is not (StatusType.Vulnerable or StatusType.Heat or StatusType.Cryo)) return false;
            if (!active.TryGetValue(type, out var state) || state.Stacks < count) return false;
            if (state.Stacks == count) Publish(RemoveState(type));
            else
            {
                active[type] = (state.Stacks - count, state.Timer);
                Publish(new StatusChange(type, state.Stacks, state.Stacks - count));
            }
            return true;
        }

        public bool ApplyStagger(double amount)
        {
            if (!resources.AddStagger(amount)) return false;
            Publish(SetStacks(StatusType.Staggered, 1));
            return true;
        }

        private StatusChange AddStacks(StatusType type, int stacks, int maximum)
        {
            if (stacks <= 0) throw new ArgumentOutOfRangeException(nameof(stacks));
            int next = (int)Math.Min(maximum, (long)GetStacks(type) + stacks);
            return SetStacks(type, next);
        }

        private StatusChange SetStacks(StatusType type, int count)
        {
            bool existing = active.TryGetValue(type, out var state);
            var timer = state.Timer;
            if (!existing || !effects.TryRefresh(timer))
            {
                if (!effects.TryApply(rules.TimerFor(type), target, out timer, out var error))
                    throw new InvalidOperationException(error);
            }

            active[type] = (count, timer);
            return new StatusChange(type, state.Stacks, count);
        }

        private StatusChange RemoveState(StatusType type)
        {
            var state = active[type];
            active.Remove(type);
            // 先解绑旧句柄，显式移除引起的同步结束通知不会重复清除此状态
            effects.TryRemove(state.Timer, out _);
            return new StatusChange(type, state.Stacks, 0);
        }

        private void OnTimerEnded(EffectHandle timer)
        {
            foreach (var pair in active)
            {
                if (!pair.Value.Timer.Equals(timer)) continue;
                active.Remove(pair.Key);
                if (pair.Key == StatusType.Staggered) resources.ResetStagger();
                Publish(new StatusChange(pair.Key, pair.Value.Stacks, 0));
                return;
            }
        }

        private IReadOnlyList<StatusChange> Publish(params StatusChange[] batch)
        {
            var result = Array.AsReadOnly(batch);
            changes.Publish(result);
            return result;
        }

        public void Dispose()
        {
            endedSubscription.Dispose();
            bool resetStagger = IsActive(StatusType.Staggered);
            foreach (var state in active.Values) effects.TryRemove(state.Timer, out _);
            active.Clear();
            if (resetStagger) resources.ResetStagger();
        }
    }
}
