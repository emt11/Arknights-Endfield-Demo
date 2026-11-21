using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Endfield.Stats.EditModeTests")]

namespace Endfield.Stats
{
    public sealed class StatSet : IReadOnlyStatSet, IDisposable
    {
        private static int mainThread;
        private readonly Guid id = Guid.NewGuid();
        private StatDependencyGraph graph;
        private StatInstance[] states;
        private double[] values;
        private Dictionary<SourceID, Dictionary<ulong, int>> sources = new Dictionary<SourceID, Dictionary<ulong, int>>();
        private readonly List<Subscription> subscriptions = new List<Subscription>();
        private long revision;
        private bool notifying;

#if UNITY_EDITOR
        private readonly int[] evaluationCounts;

        internal int EvaluationCount(string statId) => evaluationCounts[graph.Index(statId)];
#endif

        public Guid Id => id;
        public long Revision
        {
            get
            {
                CheckAccess();
                return revision;
            }
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void CaptureMainThread()
        {
            mainThread = Thread.CurrentThread.ManagedThreadId;
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        private static void RequireMainThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != mainThread)
                throw new InvalidOperationException("Live StatSet access requires the Unity main thread");
        }

        private void CheckAccess()
        {
            RequireMainThread();
            if (graph == null) throw new ObjectDisposedException(nameof(StatSet));
        }

        public static StatSet Create(IEnumerable<StatDefinition> definitions,
            IReadOnlyDictionary<string, double> initialValues = null, AttributeBinding binding = default)
        {
            RequireMainThread();
            return new StatSet(new StatDependencyGraph(definitions, binding), initialValues);
        }

        private StatSet(StatDependencyGraph graph, IReadOnlyDictionary<string, double> initialValues)
        {
            this.graph = graph;
            states = new StatInstance[graph.Nodes.Length];
            values = new double[states.Length];
#if UNITY_EDITOR
            evaluationCounts = new int[states.Length];
#endif
            if (initialValues != null)
            {
                foreach (var pair in initialValues)
                {
                    int index = graph.Index(pair.Key);
                    if (graph.Nodes[index].Definition.Formula != null)
                        throw new ArgumentException($"Initial value for derived stat: {pair.Key}");
                    states[index] = new StatInstance(StatCalculator.Finite(pair.Value, pair.Key));
                }
            }

            foreach (int i in graph.Order)
            {
                var definition = graph.Nodes[i].Definition;
                if (states[i] == null)
                {
                    if (definition.RequiresInitialValue) throw new ArgumentException($"Missing initial value: {definition.Id}");
                    states[i] = new StatInstance(definition.DefaultValue);
                }
                values[i] = EvaluateValue(i, states[i], values);
            }
        }

        public double GetValue(string statId)
        {
            CheckAccess();
            return values[graph.Index(statId)];
        }

        public bool TryGetValue(string statId, out double value)
        {
            CheckAccess();
            bool found = graph.TryIndex(statId, out int index);
            value = found ? values[index] : default;
            return found;
        }

        public StatCommitResult SetBase(string statId, double value) => ApplyBatch(StatCommand.SetBase(statId, value));

        public StatCommitResult AddModifier(StatModifier modifier) => ApplyBatch(StatCommand.AddModifier(modifier));

        public StatCommitResult RemoveModifier(ModifierKey key) => ApplyBatch(StatCommand.RemoveModifier(key));

        public StatCommitResult ReplaceSource(SourceID source, params StatModifier[] entries) =>
            ApplyBatch(StatCommand.ReplaceSource(source, entries));

        public StatCommitResult RemoveSource(SourceID source) => ApplyBatch(StatCommand.RemoveSource(source));

        public StatCommitResult ApplyBatch(params StatCommand[] commands)
        {
            CheckAccess();
            if (notifying) return Failure("Cannot write to this StatSet during notification");
            if (commands == null) return Failure("Stat command list is required");

            var transaction = new StatTransaction(graph, states, sources);
            var results = new StatCommandResult[commands.Length];
            var changes = new List<StatChange>();
            double[] candidate;
            bool[] dirty;
            try
            {
                for (int i = 0; i < commands.Length; i++) results[i] = transaction.Apply(commands[i]);
                dirty = transaction.FindChanges();
                if (!dirty.Contains(true)) return new StatCommitResult(revision, results);
                candidate = (double[])values.Clone();
                foreach (int index in graph.Order)
                {
                    if (!dirty[index]) continue;
                    var node = graph.Nodes[index];
                    candidate[index] = EvaluateValue(index, transaction.States[index], candidate);
                    if (candidate[index] != values[index])
                        changes.Add(new StatChange(node.Definition.Id, values[index], candidate[index]));
                    foreach (int dependent in node.Dependents) dirty[dependent] = true;
                }
            }
            catch (ArgumentException error)
            {
                return Failure(error.Message);
            }

            states = transaction.States;
            sources = transaction.Sources;
            values = candidate;
            revision++;
            if (changes.Count > 0 && subscriptions.Count > 0) Publish(new StatChangeSet(id, revision, changes));
            return new StatCommitResult(revision, results);
        }

        private double EvaluateValue(int index, StatInstance state, double[] inputs)
        {
#if UNITY_EDITOR
            evaluationCounts[index]++;
#endif
            return StatCalculator.Evaluate(graph.Nodes[index], state, inputs, null, out _);
        }

        private StatCommitResult Failure(string diagnostic) =>
            new StatCommitResult(revision, Array.Empty<StatCommandResult>(), diagnostic);

        public StatSnapshot Snapshot(params string[] statIds)
        {
            CheckAccess();
            if (statIds == null) throw new ArgumentNullException(nameof(statIds));
            var copy = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var statId in statIds) copy[statId] = values[graph.Index(statId)];
            return new StatSnapshot(id, revision, copy);
        }

        public IReadOnlyList<StatModifier> GetModifiers(string statId)
        {
            CheckAccess();
            return Array.AsReadOnly(states[graph.Index(statId)].Modifiers.Values.ToArray());
        }

        public StatBreakdown GetBreakdown(string statId)
        {
            CheckAccess();
            return Explain(graph.Index(statId), new Dictionary<int, StatBreakdown>());
        }

        private StatBreakdown Explain(int index, Dictionary<int, StatBreakdown> explained)
        {
            if (explained.TryGetValue(index, out var existing)) return existing;
            var node = graph.Nodes[index];
            var inputs = new Dictionary<string, StatBreakdown>(StringComparer.Ordinal);
            for (int i = 0; i < node.Inputs.Length; i++)
                inputs.Add(node.Slots[i], Explain(node.Inputs[i], explained));
            var steps = new List<StatStep>();
            StatCalculator.Evaluate(node, states[index], values, steps, out var winner);
            var breakdown = new StatBreakdown(id, revision, node.Definition, inputs,
                Array.AsReadOnly(states[index].Modifiers.Values.ToArray()), steps, winner);
            explained.Add(index, breakdown);
            return breakdown;
        }

        public IDisposable Subscribe(Action<StatChangeSet> listener)
        {
            CheckAccess();
            if (listener == null) throw new ArgumentNullException(nameof(listener));
            var subscription = new Subscription(this, listener);
            subscriptions.Add(subscription);
            return subscription;
        }

        private sealed class Subscription : IDisposable
        {
            private StatSet owner;
            private Action<StatChangeSet> listener;

            internal Subscription(StatSet owner, Action<StatChangeSet> listener)
            {
                this.owner = owner;
                this.listener = listener;
            }

            internal void Deliver(StatChangeSet changeSet) => listener?.Invoke(changeSet);

            public void Dispose()
            {
                RequireMainThread();
                if (owner == null) return;
                owner.subscriptions.Remove(this);
                owner = null;
                listener = null;
            }
        }

        private void Publish(StatChangeSet changeSet)
        {
            notifying = true;
            try
            {
                foreach (var subscription in subscriptions.ToArray())
                {
                    try
                    {
                        subscription.Deliver(changeSet);
                    }
                    catch (Exception error)
                    {
                        Debug.LogError($"StatSet {id} revision {revision}: listener failed: {error}");
                    }
                }
            }
            finally
            {
                notifying = false;
            }
        }

        public void Dispose()
        {
            RequireMainThread();
            if (notifying) throw new InvalidOperationException("Cannot dispose this StatSet during notification");
            while (subscriptions.Count > 0)
                subscriptions[subscriptions.Count - 1].Dispose();
            graph = null;
            states = null;
            values = null;
            sources = null;
        }
    }
}
