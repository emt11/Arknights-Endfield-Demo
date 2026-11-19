using System.Collections.Generic;

namespace Endfield.Combat
{
    public enum CombatInputKind
    {
        Attack
    }

    public readonly struct CombatInput
    {
        public readonly CombatInputKind kind;
        public readonly long sequence;
        public readonly double expiresAt;

        public CombatInput(CombatInputKind kind, long sequence, double expiresAt)
        {
            this.kind = kind;
            this.sequence = sequence;
            this.expiresAt = expiresAt;
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
            for (int index = inputs.Count - 1; index >= 0; index--)
            {
                if (inputs[index].expiresAt <= now)
                    inputs.RemoveAt(index);
            }
        }

        public bool TryPeek(CombatInputKind kind, out CombatInput input)
        {
            foreach (CombatInput candidate in inputs)
            {
                if (candidate.kind == kind)
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
            int index = inputs.FindIndex(input => input.sequence == acceptedSequence);
            if (index < 0)
                return false;
            inputs.RemoveAt(index);
            return true;
        }

        public void Clear() => inputs.Clear();
    }
}
