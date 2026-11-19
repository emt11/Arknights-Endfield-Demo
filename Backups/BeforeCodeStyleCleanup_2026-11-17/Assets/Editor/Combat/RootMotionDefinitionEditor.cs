using Endfield.Combat;
using UnityEditor;
using UnityEngine;

namespace Endfield.EditorCombat
{
    [CustomEditor(typeof(RootMotionDefinition))]
    public sealed class RootMotionDefinitionEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            var root = (RootMotionDefinition)target;
            serializedObject.Update();
            foreach (string property in new[] { "Clip", "ModelPrefab", "ModelScale", "SampleRate" })
                EditorGUILayout.PropertyField(serializedObject.FindProperty(property));
            serializedObject.ApplyModifiedProperties();
            EditorGUILayout.LabelField("样本数", root.Samples.Length.ToString());
            if (root.Clip == null || root.ModelPrefab == null)
                return;
            if (root.DependencySignature != RootMotionBaker.Signature(root))
                EditorGUILayout.HelpBox("源动画或模型与烘焙数据不一致，需要重新烘焙", MessageType.Warning);
            if (GUILayout.Button("使用实际 Animator / Avatar 重新烘焙"))
                RootMotionBaker.Bake(root);
        }
    }
}
