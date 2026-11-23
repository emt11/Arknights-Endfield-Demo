namespace Endfield.CombatContracts
{
    // 伤害结算上下文
    public readonly struct DamageApplied
    {
        public ActorId Source { get; }
        public ActorId Target { get; }
        public double BattleTime { get; }
        public double HPLost { get; }
        public double Overkill { get; }

        public DamageApplied(ActorId source, ActorId target, double battleTime, double hpLost, double overkill)
        {
            Source = source;
            Target = target;
            BattleTime = battleTime;
            HPLost = hpLost;
            Overkill = overkill;
        }
    }
}
