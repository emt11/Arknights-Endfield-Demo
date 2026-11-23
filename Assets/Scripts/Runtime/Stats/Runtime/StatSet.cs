using System;
using System.Collections.Generic;
using System.Linq;
using Endfield.Common;

namespace Endfield.Stats
{
    public sealed class StatSet : IReadOnlyStatSet
    {
        private readonly StatId main;
        private readonly StatId secondary;
        private readonly StatCoefficients coefficients;
        private double[] bases; // 计算输入
        private double[] panel; // 最终面板展示
        private SortedDictionary<SourceID, StatModifier[]> sources = new SortedDictionary<SourceID, StatModifier[]>();
        private readonly ChangeNotifier<IReadOnlyList<StatChange>> changes = new();
        private bool notifying;

        public static StatSet Create(IReadOnlyDictionary<StatId, double> initialValues, StatId main, StatId secondary,
            StatCoefficients? coefficients = null)
        {
            if (initialValues == null) throw new ArgumentNullException(nameof(initialValues));
            if (!IsPrimary(main) || !IsPrimary(secondary) || main == secondary)
                throw new ArgumentException($"Invalid main and secondary attributes: {main}, {secondary}");

            // 初始化属性
            var bases = new double[StatCatalog.Definitions.Length];
            foreach (var pair in initialValues)
            {
                if (StatCatalog.Get(pair.Key).Derived || !double.IsFinite(pair.Value))
                    throw new ArgumentException($"Invalid initial value for {pair.Key}: {pair.Value}");
                bases[(int)pair.Key] = pair.Value;
            }

            // 可选属性赋默认值
            foreach (var definition in StatCatalog.Definitions)
            {
                if (initialValues.ContainsKey(definition.Id)) continue;
                if (definition.Required) throw new ArgumentException($"Missing initial value for {definition.Id}");
                bases[(int)definition.Id] = definition.DefaultValue;
            }
            return new StatSet(bases, main, secondary, coefficients ?? StatCoefficients.Default);
        }

        private static bool IsPrimary(StatId id) => id is StatId.STR or StatId.AGI or StatId.INT or StatId.WIL;

        private StatSet(double[] bases, StatId main, StatId secondary, StatCoefficients coefficients)
        {
            this.bases = bases;
            this.main = main;
            this.secondary = secondary;
            this.coefficients = coefficients;
            panel = Calculate(bases, sources);
        }

        public StatId MainAttribute => main;

        public double GetValue(StatId id) => panel[(int)id];

        public StatWriteResult SetBase(StatId id, double value)
        {
            if ((uint)id >= bases.Length || StatCatalog.Get(id).Derived || !double.IsFinite(value))
                return new StatWriteResult($"Invalid base value for {id}: {value}");
            if (bases[(int)id] == value) return default;

            var nextBases = (double[])bases.Clone();
            nextBases[(int)id] = value;
            return Publish(nextBases, sources);
        }

        public StatWriteResult ReplaceSource(SourceID source, params StatModifier[] entries)
        {
            if (source.Domain == null || entries == null)
                return new StatWriteResult("A source and its entries are required");

            sources.TryGetValue(source, out var previous);
            if (previous == null ? entries.Length == 0 : previous.SequenceEqual(entries))
                return default;

            try
            {
                foreach (var entry in entries) entry.Validate();
            }
            catch (ArgumentException error)
            {
                return new StatWriteResult(error.Message);
            }

            var nextSources = new SortedDictionary<SourceID, StatModifier[]>(sources);
            if (entries.Length == 0)
                nextSources.Remove(source);
            else
                nextSources[source] = (StatModifier[])entries.Clone();
            var result = Publish(bases, nextSources);
            return result.Success && entries.Length == 0 ? new StatWriteResult(null, previous.Length) : result;
        }

        public StatWriteResult RemoveSource(SourceID source) => ReplaceSource(source, Array.Empty<StatModifier>());

        private double[] Calculate(double[] nextBases, SortedDictionary<SourceID, StatModifier[]> nextSources,
            bool ignoreZeroMultipliers = false)
        {
            return StatCalculator.Calculate(nextBases, nextSources, main, secondary, coefficients, ignoreZeroMultipliers);
        }

        private StatWriteResult Publish(double[] nextBases, SortedDictionary<SourceID, StatModifier[]> nextSources)
        {
            if (notifying) return new StatWriteResult("Cannot modify a StatSet during its notification");

            double[] nextPanel;
            try
            {
                nextPanel = Calculate(nextBases, nextSources);
                if (nextSources.Values.Any(entries => entries.Any(m => m.Operation == ModifierOperation.Multiplicative && m.Value == 0)))
                    Calculate(nextBases, nextSources, ignoreZeroMultipliers: true);
            }
            catch (Exception error) when (error is ArgumentException || error is ArithmeticException)
            {
                return new StatWriteResult(error.Message);
            }

            var previousPanel = panel;
            bases = nextBases;
            sources = nextSources;
            panel = nextPanel;
            if (changes.HasSubscribers) NotifyChanges(previousPanel);
            return default;
        }

        private void NotifyChanges(double[] previousPanel)
        {
            var batch = new List<StatChange>();
            foreach (var definition in StatCatalog.Definitions)
            {
                var id = definition.Id;
                int index = (int)id;
                if (previousPanel[index] != panel[index])
                    batch.Add(new StatChange(id, previousPanel[index], panel[index]));
            }
            if (batch.Count == 0) return;

            notifying = true;
            try
            {
                changes.Publish(batch.AsReadOnly());
            }
            finally
            {
                notifying = false;
            }
        }

        public IDisposable Subscribe(Action<IReadOnlyList<StatChange>> listener) => changes.Subscribe(listener);
    }
}
