using Endfield.Actors;
using Endfield.Animation;
using Endfield.CharacterControl;
using Endfield.MotionWarping;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Endfield.Editor.Animation
{
    [CustomEditor(typeof(PartyMemberControl), true)]
    public sealed class PartyMemberAnimationEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var member = (PartyMemberControl)target;
            var animator = member.GetComponent<Combatant>()?.ModelAnimator;
            if (!animator || !animator.TryGetComponent<AnimationSource>(out var source)) return;
            var animation = member.DodgeAnimation;
            if (!animation) return;
            if (animation.Clip != ResolveDodgeClip(source.LocomotionController))
                EditorGUILayout.HelpBox("闪避配置的片段须与 Controller 的 Dodge 片段一致", MessageType.Warning);
        }

        private static AnimationClip ResolveDodgeClip(RuntimeAnimatorController asset)
        {
            var overrides = asset as AnimatorOverrideController;
            var controller = (overrides ? overrides.runtimeAnimatorController : asset) as AnimatorController;
            if (!controller || controller.layers.Length == 0) return null;
            foreach (var child in controller.layers[0].stateMachine.states)
            {
                if (child.state.name != "Dodge" || !(child.state.motion is AnimationClip clip)) continue;
                return overrides ? overrides[clip] ?? clip : clip;
            }
            return null;
        }

    }

    [CustomEditor(typeof(MotionWarpingAsset))]
    public sealed class MotionWarpingAssetEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var asset = (MotionWarpingAsset)target;
            if (!asset.HasCurrentSummary) EditorGUILayout.HelpBox("窗口摘要缺失或已过期", MessageType.Info);
            if (GUILayout.Button("烘焙扭曲窗口"))
            {
                try { AnimationDefinationEditor.Bake(asset); }
                catch (System.InvalidOperationException e) { Debug.LogError(e.Message, asset); }
            }
        }
    }

}
