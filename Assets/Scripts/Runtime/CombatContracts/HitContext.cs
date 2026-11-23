using UnityEngine;

namespace Endfield.CombatContracts
{
    // 命中上下文
    public readonly struct HitContext
    {
        public AttackId Attack { get; }
        public HitWindowId Window { get; }
        public ActorId Target { get; }
        public Vector3 WorldPoint { get; }
        public double BattleTime { get; }

        public HitContext(AttackId attack, HitWindowId window, ActorId target, Vector3 worldPoint, double battleTime)
        {
            Attack = attack;
            Window = window;
            Target = target;
            WorldPoint = worldPoint;
            BattleTime = battleTime;
        }
    }
}
