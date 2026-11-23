using System;
using System.Collections.Generic;
using Endfield.Common;
using Endfield.Stats;

namespace Endfield.Effects
{
    public sealed class EffectController : IDisposable
    {
        private sealed class EffectInstance
        {
            public EffectHandle Handle;
            public EffectProfile Profile;
            public IReadOnlyStatSet Instigator; // 施加者属性集
            public double ExpiresAt;
            public double NextPulseAt;
        }

        private readonly StatSet stats;
        private readonly Dictionary<ulong, EffectInstance> active = new();
        private readonly ChangeNotifier<EffectPulse> pulses = new();
        private readonly ChangeNotifier<EffectHandle> ended = new();
        private double elapsed;
        private ulong nextSequence = 1;
        private bool disposed;

        public EffectController(StatSet stats) => this.stats = stats ?? throw new ArgumentNullException(nameof(stats));

        public double OutgoingDamageBonus
        {
            get
            {
                double total = 0;
                foreach (var instance in active.Values) total += instance.Profile.OutgoingDamageBonus;
                return total;
            }
        }

        public IDisposable SubscribePulse(Action<EffectPulse> listener) => pulses.Subscribe(listener);

        public IDisposable SubscribeEnded(Action<EffectHandle> listener) => ended.Subscribe(listener);

        public bool IsActive(EffectHandle handle) => handle.Owner == this && active.ContainsKey(handle.Sequence);

        public bool TryApply(EffectProfile profile, IReadOnlyStatSet instigator, out EffectHandle handle, out string error)
        {
            handle = default;
            if (disposed || !profile || instigator == null)
            {
                error = "施加效果需要有效目标、模板和施加者";
                return false;
            }
            error = profile.ConfigurationError();
            if (error != null) return false;

            ulong sequence = nextSequence++;
            var result = stats.ReplaceSource(new SourceID("Effect", sequence), profile.Modifiers);
            error = result.Error;
            if (!result.Success) return false;

            handle = new EffectHandle(this, sequence);
            active.Add(sequence, new EffectInstance
            {
                Handle = handle,
                Profile = profile,
                Instigator = instigator,
                ExpiresAt = elapsed + profile.DurationSeconds,
                NextPulseAt = elapsed + profile.IntervalSeconds
            });
            return true;
        }

        public bool TryRefresh(EffectHandle handle)
        {
            if (handle.Owner != this || !active.TryGetValue(handle.Sequence, out var instance)) return false;
            instance.ExpiresAt = elapsed + instance.Profile.DurationSeconds;
            return true;
        }

        public bool TryRemove(EffectHandle handle, out string error)
        {
            if (!IsActive(handle))
            {
                error = "效果不属于该目标或已经结束";
                return false;
            }
            var result = stats.RemoveSource(new SourceID("Effect", handle.Sequence));
            error = result.Error;
            if (!result.Success) return false;
            active.Remove(handle.Sequence);
            ended.Publish(handle);
            return true;
        }

        public void Advance(double battleDeltaSeconds)
        {
            NumericValidation.RequireFiniteInRange(battleDeltaSeconds, 0, double.MaxValue, nameof(battleDeltaSeconds));
            if (battleDeltaSeconds == 0) return;
            double until = elapsed + battleDeltaSeconds;
            HashSet<ulong> failed = null;
            string firstError = null;
            while (TryGetNextEvent(until, failed, out var next, out double time, out bool isPulse))
            {
                elapsed = Math.Max(elapsed, Math.Min(until, time));
                if (isPulse)
                {
                    double following = next.NextPulseAt + next.Profile.IntervalSeconds;
                    if (following <= next.NextPulseAt)
                        throw new InvalidOperationException("周期间隔小于当前战斗时刻的计时精度");
                    next.NextPulseAt = following;
                    pulses.Publish(new EffectPulse(next.Handle, next.Instigator, next.Profile.Pulse, next.Profile.Amount));
                }
                else if (!TryRemove(next.Handle, out var error))
                {
                    // 本步继续处理其他效果，下次推进重试失败的撤销，已发出的周期保持已消费
                    (failed ??= new HashSet<ulong>()).Add(next.Handle.Sequence);
                    firstError ??= error;
                }
            }
            elapsed = until;
            if (firstError != null) throw new InvalidOperationException(firstError);
        }

        // TryGetNextEvent() 每次从所有生效中的效果里，找出截至 until 最先该处理的一件事
        private bool TryGetNextEvent(double until, HashSet<ulong> failed, out EffectInstance next,
            out double time, out bool isPulse)
        {
            next = null;
            time = until;
            isPulse = false;
            foreach (var instance in active.Values)
            {
                // 如果这个效果在本次 Advance() 中已经尝试移除过，而且移除失败，就先跳过它，继续检查其他效果
                if (failed != null && failed.Contains(instance.Handle.Sequence)) continue;

                bool pulse = instance.Profile.Pulse != PulseKind.None &&
                    CompareTimes(instance.NextPulseAt, instance.ExpiresAt) <= 0;
                double due = pulse ? instance.NextPulseAt : instance.ExpiresAt;
                int order = CompareTimes(due, time);
                if (order > 0) continue;
                if (order == 0 && next != null)
                {
                    // 同时到点时先结算所有周期，同类事件按施加顺序处理
                    if (isPulse && !pulse) continue;
                    if (isPulse == pulse && next.Handle.Sequence < instance.Handle.Sequence) continue;
                }
                next = instance;
                time = due;
                isPulse = pulse;
            }
            return next != null;
        }

        private static int CompareTimes(double a, double b)
        {
            // 抵消浮点舍入，使 0.1 秒的第三次周期与 0.3 秒的到期点相遇
            if (!double.IsFinite(a) || !double.IsFinite(b)) return a.CompareTo(b);
            double tolerance = 4.44e-16 * Math.Max(Math.Abs(a), Math.Abs(b));
            return Math.Abs(a - b) <= tolerance ? 0 : a.CompareTo(b);
        }

        public void Dispose()
        {
            disposed = true;
            string firstError = null;
            foreach (var instance in new List<EffectInstance>(active.Values))
            {
                if (IsActive(instance.Handle) && !TryRemove(instance.Handle, out var error)) firstError ??= error;
            }
            if (firstError != null) throw new InvalidOperationException(firstError);
        }
    }
}
