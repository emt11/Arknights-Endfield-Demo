using System;
using System.Collections.Generic;
using System.Linq;

namespace Endfield.Stats
{
    internal sealed class StatNode
    {
        internal readonly StatDefinition Definition;
        internal readonly List<int> Dependents = new List<int>();
        internal int[] Inputs = Array.Empty<int>();
        internal double[] Weights = Array.Empty<double>();
        internal string[] Slots = Array.Empty<string>();

        internal StatNode(StatDefinition definition)
        {
            Definition = definition;
        }
    }

    internal sealed class StatDependencyGraph
    {
        private static readonly string[] AttackSlots = { "BaseATK", "WeaponATK", "ATKPercentBonus", "FixedATK", "AttributeATKBonus" };
        private static readonly UnitType[] AttackUnits = { UnitType.Point, UnitType.Point, UnitType.Ratio, UnitType.Point, UnitType.Ratio };
        private readonly Dictionary<string, int> indices = new Dictionary<string, int>(StringComparer.Ordinal);
        internal readonly StatNode[] Nodes;
        internal readonly int[] Order;

        internal StatDependencyGraph(IEnumerable<StatDefinition> definitions, AttributeBinding binding)
        {
            if (definitions == null) throw new ArgumentNullException(nameof(definitions));
            var nodes = new List<StatNode>();
            foreach (var definition in definitions)
            {
                if (definition == null || string.IsNullOrWhiteSpace(definition.Id))
                    throw new ArgumentException("StatID is required");
                ValidateDefinition(definition);
                nodes.Add(new StatNode(definition.Copy()));
            }

            // Index order doubles as the stable tie-breaker in the topological sort.
            Nodes = nodes.OrderBy(n => n.Definition.Id, StringComparer.Ordinal).ToArray();
            for (int i = 0; i < Nodes.Length; i++)
                if (!indices.TryAdd(Nodes[i].Definition.Id, i))
                    throw new ArgumentException($"Duplicate StatID: {Nodes[i].Definition.Id}");

            bool needsBinding = Nodes.Any(n => n.Definition.Formula != null &&
                (n.Definition.Formula.Kind == FormulaKind.Attack || n.Definition.Formula.Inputs.Any(
                    input => input.StatId == AttributeBinding.MainSlot || input.StatId == AttributeBinding.SecondarySlot)));
            if (needsBinding || binding.Main != null || binding.Secondary != null)
            {
                if (binding.Main == binding.Secondary || !IsPrimary(binding.Main) || !IsPrimary(binding.Secondary))
                    throw new ArgumentException($"Invalid primary attribute binding: {binding.Main}, {binding.Secondary}");
            }

            for (int i = 0; i < Nodes.Length; i++)
                if (Nodes[i].Definition.Formula != null) BindFormula(i, binding);
            Order = Sort();
        }

        internal bool TryIndex(string id, out int index)
        {
            index = -1;
            return id != null && indices.TryGetValue(id, out index);
        }

        internal int Index(string id)
        {
            if (!TryIndex(id, out int index)) throw new ArgumentException($"Unknown StatID: {id}");
            return index;
        }

        private bool IsPrimary(string id)
        {
            return (id == "STR" || id == "AGI" || id == "INT" || id == "WIL") &&
                TryIndex(id, out int i) && Nodes[i].Definition.Category == StatCategory.Primary &&
                Nodes[i].Definition.Unit == UnitType.Point;
        }

        private static void ValidateDefinition(StatDefinition d)
        {
            if (!Enum.IsDefined(typeof(UnitType), d.Unit) || !Enum.IsDefined(typeof(StatCategory), d.Category) ||
                !Enum.IsDefined(typeof(StatValueKind), d.ValueKind) ||
                (d.ValueKind == StatValueKind.Derived && d.Formula == null) ||
                (d.Allowed & ~ModifierOperation.All) != 0 || d.Minimum > d.Maximum ||
                (d.RequiresInitialValue && d.Formula != null))
                throw new ArgumentException($"Invalid definition: {d.Id}");
            StatCalculator.Finite(d.DefaultValue, d.Id);
            if (d.Minimum.HasValue) StatCalculator.Finite(d.Minimum.Value, d.Id);
            if (d.Maximum.HasValue) StatCalculator.Finite(d.Maximum.Value, d.Id);
        }

        private void BindFormula(int index, AttributeBinding binding)
        {
            var node = Nodes[index];
            var definition = node.Definition;
            var formula = definition.Formula;
            var inputs = formula.Inputs.ToArray();
            var weights = formula.Coefficients.ToArray();
            string context = $"{definition.Id} / {formula.Id}";

            if (string.IsNullOrWhiteSpace(formula.Id) || formula.Output != definition.Id || inputs.Length == 0 ||
                inputs.Any(input => string.IsNullOrWhiteSpace(input.Slot)) ||
                inputs.Select(input => input.Slot).Distinct(StringComparer.Ordinal).Count() != inputs.Length)
                throw new ArgumentException($"Invalid formula output or slots: {context}");

            switch (formula.Kind)
            {
                case FormulaKind.WeightedSum:
                    if (weights.Length != inputs.Length) throw new ArgumentException($"Coefficient count: {context}");
                    foreach (var weight in weights) StatCalculator.Finite(weight, context);
                    node.Weights = weights;
                    break;
                case FormulaKind.Attack:
                    if (definition.Unit != UnitType.Point || inputs.Length != AttackSlots.Length || weights.Length != 0)
                        throw new ArgumentException($"Invalid attack formula: {context}");
                    var bySlot = inputs.ToDictionary(input => input.Slot, StringComparer.Ordinal);
                    for (int i = 0; i < AttackSlots.Length; i++)
                        if (!bySlot.TryGetValue(AttackSlots[i], out inputs[i]) || inputs[i].Unit != AttackUnits[i])
                            throw new ArgumentException($"Invalid slot {AttackSlots[i]} in {context}");
                    break;
                default:
                    throw new ArgumentException($"Unknown evaluator {formula.Kind} in {context}");
            }

            node.Inputs = new int[inputs.Length];
            node.Slots = new string[inputs.Length];
            for (int i = 0; i < inputs.Length; i++)
            {
                var input = inputs[i];
                string id = binding.Resolve(input.StatId);
                if (!TryIndex(id, out int dependency) || Nodes[dependency].Definition.Unit != input.Unit)
                    throw new ArgumentException($"Unknown input or unit mismatch for {input.Slot} ({id}) in {context}");
                node.Inputs[i] = dependency;
                node.Slots[i] = input.Slot;
                Nodes[dependency].Dependents.Add(index);
            }
        }

        private int[] Sort()
        {
            var pending = Nodes.Select(n => n.Inputs.Length).ToArray();
            var ready = new SortedSet<int>(Enumerable.Range(0, Nodes.Length).Where(i => pending[i] == 0));
            var order = new List<int>(Nodes.Length);
            while (ready.Count > 0)
            {
                int index = ready.Min;
                ready.Remove(index);
                order.Add(index);
                foreach (int dependent in Nodes[index].Dependents)
                    if (--pending[dependent] == 0) ready.Add(dependent);
            }

            if (order.Count != Nodes.Length)
            {
                int index = Array.FindIndex(pending, n => n > 0);
                var path = new List<int>();
                while (!path.Contains(index))
                {
                    path.Add(index);
                    index = Nodes[index].Inputs.First(input => pending[input] > 0);
                }
                var cycle = path.Skip(path.IndexOf(index)).Append(index).Select(i => Nodes[i].Definition.Id);
                throw new ArgumentException($"Formula dependency cycle: {string.Join(" -> ", cycle)}");
            }
            return order.ToArray();
        }
    }
}
