using System;
using System.Collections.Generic;
using Endfield.CombatContracts;
using Endfield.Common;

namespace Endfield.Timing
{
    public readonly struct TimeStep
    {
        public double Delta { get; } // 本步推进时间
        public double Elapsed { get; } // 累计推进时间

        internal TimeStep(double delta, double elapsed)
        {
            Delta = delta;
            Elapsed = elapsed;
        }

        internal TimeStep Advance(double delta) => new(delta, Elapsed + delta);
    }

    public sealed class BattleClock
    {
        private sealed class ActorTime
        {
            public TimeStep Action; // 动作时钟
            public double RemainingHitStop; // 剩余顿帧时间
        }

        // 按角色 ID 保存已注册角色的动作时钟和剩余顿帧时间
        private readonly Dictionary<ActorId, ActorTime> actors = new();
        private readonly ChangeNotifier<TimeStep> battleSteps = new();
        private bool paused;
        private double battleRate = 1;

        public TimeStep Real { get; private set; }
        public TimeStep Battle { get; private set; }
        public TimeStep Presentation => Battle;

        public void SetPaused(bool value) => paused = value;

        public void SetBattleRate(double rate)
        {
            NumericValidation.RequireFiniteInRange(rate, double.Epsilon, double.MaxValue, nameof(rate));
            battleRate = rate;
        }

        public bool RegisterActor(ActorId actor) => actor.Value != 0 && actors.TryAdd(actor, new ActorTime());

        public bool UnregisterActor(ActorId actor) => actors.Remove(actor);

        public bool TryGetActionTime(ActorId actor, out TimeStep step)
        {
            bool found = actors.TryGetValue(actor, out var time);
            step = found ? time.Action : default;
            return found;
        }

        // 给指定角色请求一段顿帧
        public bool TryRequestHitStop(ActorId actor, double seconds)
        {
            NumericValidation.RequireFiniteInRange(seconds, 0, double.MaxValue, nameof(seconds));
            if (!actors.TryGetValue(actor, out var time)) return false;
            time.RemainingHitStop = Math.Max(time.RemainingHitStop, seconds);
            return true;
        }

        public IDisposable SubscribeBattleStep(Action<TimeStep> listener) => battleSteps.Subscribe(listener);

        // 推进战斗时钟
        public void Advance(double realDelta)
        {
            NumericValidation.RequireFiniteInRange(realDelta, 0, double.MaxValue, nameof(realDelta));
            double available = paused ? 0 : realDelta;
            double battleDelta = available * battleRate;
            NumericValidation.RequireFiniteInRange(battleDelta, 0, double.MaxValue, nameof(battleDelta));
            Real = Real.Advance(realDelta);
            Battle = Battle.Advance(battleDelta);
            foreach (var time in actors.Values)
            {
                double frozen = Math.Min(time.RemainingHitStop, available);
                time.RemainingHitStop -= frozen;
                time.Action = time.Action.Advance((available - frozen) * battleRate);
            }
            battleSteps.Publish(Battle);
        }
    }
}
