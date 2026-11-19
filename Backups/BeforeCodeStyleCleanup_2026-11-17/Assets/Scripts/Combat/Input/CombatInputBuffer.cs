using System.Collections.Generic;

namespace Endfield.Combat
{
    public enum CombatInputKind { Attack }

    public readonly struct CombatInput
    {
        public readonly CombatInputKind Kind;
        public readonly long Sequence;
        public readonly double ExpiresAt;

        public CombatInput(CombatInputKind kind, long sequence, double expiresAt)
        {
            Kind = kind;
            Sequence = sequence;
            ExpiresAt = expiresAt;
        }
    }

    public sealed class CombatInputBuffer
    {
        private readonly List<CombatInput> inputs = new List<CombatInput>();
        private readonly int[] lastFrames = { -1 };
        private long sequence;

        public int Count => inputs.Count;

        public void Enqueue(CombatInputKind kind, int frame, double now, float duration)
        {
            if (frame == lastFrames[(int)kind])
                return;

            lastFrames[(int)kind] = frame;
            inputs.Add(new CombatInput(kind, ++sequence, now + duration));
        }

        public void Expire(double now)
        {
            inputs.RemoveAll(input => input.ExpiresAt <= now);
        }

        public bool TryPeek(CombatInputKind kind, out CombatInput input)
        {
            foreach (CombatInput candidate in inputs)
            {
                if (candidate.Kind == kind)
                {
                    input = candidate;
                    return true;
                }
            }
            input = default;
            return false;
        }

        public bool Consume(long acceptedSequence)
        {
            int index = inputs.FindIndex(input => input.Sequence == acceptedSequence);
            if (index < 0)
                return false;
            inputs.RemoveAt(index);
            return true;
        }

        public void Clear() => inputs.Clear();
    }
}
