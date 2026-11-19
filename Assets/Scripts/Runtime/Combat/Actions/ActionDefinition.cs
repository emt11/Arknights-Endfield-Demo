using System;
using Endfield.AnimationSystem;
using UnityEngine;

namespace Endfield.Combat
{
    [Flags]
    public enum ActionCategory
    {
        Attack = 1,
        Move = 2
    }

    [CreateAssetMenu(menuName = "Endfield/Action Definition", fileName = "ActionDefinition")]
    public sealed class ActionDefinition : ScriptableObject
    {
        public AnimationDefinition animation;
        [Min(0f), Tooltip("起手转向时长（片段秒）")] public float openingTurnDuration = .38f;
        public bool useRootMotion = true;
        public ActionCategory category = ActionCategory.Attack;
        public RootMotionDefinition rootMotion;

        public float Duration => animation.Duration;
    }
}
