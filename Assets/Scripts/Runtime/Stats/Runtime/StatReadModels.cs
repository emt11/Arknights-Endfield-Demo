using System;
using System.Collections.Generic;

namespace Endfield.Stats
{
    public interface IReadOnlyStatSet
    {
        double GetValue(StatId id);
        IDisposable Subscribe(Action<IReadOnlyList<StatChange>> listener);
    }

    public readonly struct StatChange
    {
        public StatId StatId { get; }
        public double OldValue { get; }
        public double NewValue { get; }

        internal StatChange(StatId statId, double oldValue, double newValue)
        {
            StatId = statId;
            OldValue = oldValue;
            NewValue = newValue;
        }
    }

    public readonly struct StatWriteResult
    {
        public bool Success => Error == null;
        public string Error { get; }

        public int RemovedEntryCount { get; }

        internal StatWriteResult(string error, int removedEntryCount = 0)
        {
            Error = error;
            RemovedEntryCount = removedEntryCount;
        }
    }
}
