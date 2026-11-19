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
            foreach (string property in new[] { nameof(RootMotionDefinition.clip), nameof(RootMotionDefinition.modelPrefab), nameof(RootMotionDefinition.modelScale), nameof(RootMotionDefinition.sampleRate) })
                EditorGUILayout.PropertyField(serializedObject.FindProperty(property));
            serializedObject.ApplyModifiedProperties();
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.PropertyField(serializedObject.FindProperty("avatar"), new GUIContent("烘焙 Avatar"));
            EditorGUILayout.LabelField("样本数", root.SampleCount.ToString());
            if (root.clip == null || root.modelPrefab == null)
                return;
            if (root.DependencySignature != RootMotionBaker.Signature(root))
                EditorGUILayout.HelpBox("源动画、模型、尺度或采样率与烘焙数据不一致，需要重新烘焙", MessageType.Warning);
            if (GUILayout.Button("使用实际 Animator / Avatar 重新烘焙"))
                RootMotionBaker.Bake(root);
        }
    }
}
