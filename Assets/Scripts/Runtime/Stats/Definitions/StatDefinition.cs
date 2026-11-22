namespace Endfield.Stats
{
    public enum StatId
    {
        STR, // 力量：增加最大生命
        AGI, // 敏捷，提高物理抗性
        INT, // 智识，提高法术抗性
        WIL, // 意志，提高受治疗效率
        BaseATK, // 基础攻击力
        BaseDEF, // 基础防御力
        BaseHP, // 基础生命
        WeaponATK, // 武器攻击力
        FixedATK, // 固定攻击力
        ATKPercentBonus, // 攻击力百分比加成
        CritRate, // 暴击率
        CritDamage, // 暴击伤害
        AttributeATKBonus, // 主副属性攻击力加成
        ATK, // 最终攻击力
        DEF, // 最终防御力
        MaxHP, // 最大生命
        PhysicalResistance, // 物理抗性点数
        ArtsResistance, // 法术抗性点数
        HealingReceivedBonus // 受治疗者的治疗加成比例
    }

    public enum StatGroup
    {
        PrimaryAttributes, // 基础属性：力量、敏捷、智识、意志，也是主副属性的候选项
        BaseCombat, // 基础战斗：基础攻防和生命，以及武器攻击等战斗计算输入
        DerivedCombat, // 战斗面板：最终攻防、最大生命、暴击率等常用读数
        DamageBonus, // 伤害加成：用于按伤害类型或条件归类加成
        Resistance, // 抗性：物理抗性和法术抗性
        Special // 特殊属性：例如受治疗效率
    }

    public enum StatUnit
    {
        Point, // 点数，按原数值显示
        Ratio // 比例，面板显示为百分比，例如 0.2 显示为 20%
    }

    public sealed class StatDefinition
    {
        public StatId Id { get; }
        public StatGroup Group { get; }
        public string DisplayName { get; }
        public StatUnit Unit { get; }
        public double DefaultValue { get; } // 可选属性的默认值
        public double Minimum { get; }
        public double Maximum { get; }
        public bool Required { get; } // 是否必填属性
        public bool Derived { get; } // 是否为派生属性，派生属性的起始值由公式计算

        internal StatDefinition(StatId id, StatGroup group, string displayName, StatUnit unit = StatUnit.Point,
            bool required = false, bool derived = false, double defaultValue = 0,
            double minimum = double.NegativeInfinity, double maximum = double.PositiveInfinity)
        {
            Id = id;
            Group = group;
            DisplayName = displayName;
            Unit = unit;
            Required = required;
            Derived = derived;
            DefaultValue = defaultValue;
            Minimum = minimum;
            Maximum = maximum;
        }
    }
}
