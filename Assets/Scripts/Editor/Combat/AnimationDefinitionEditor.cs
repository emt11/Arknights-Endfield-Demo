using Endfield.AnimationSystem;
using Endfield.Combat;
using UnityEditor;
using UnityEngine;

namespace Endfield.EditorCombat
{
    [CustomEditor(typeof(AnimationDefinition))]
    public sealed class AnimationDefinitionEditor : Editor
    {
        private int pointType;
        private int windowType;

        public override void OnInspectorGUI()
        {
            if (GUILayout.Button("打开动画 Timeline"))
                EditorWindow.GetWindow<ActionTimelineWindow>("Action Timeline").Select((AnimationDefinition)target);
            DrawConfiguration(serializedObject, ref pointType, ref windowType);
            DrawWindowHints(serializedObject);
        }

        internal static void DrawWindowHints(SerializedObject data)
        {
            var animation = (AnimationDefinition)data.targetObject;
            if (animation.clip == null)
                return;

            data.Update();
            MotionWarpWindow previous = null;
            foreach (var entry in animation.windowEvents)
            {
                if (entry is MotionWarpWindow warp)
                {
                    if (previous != null && warp.start < previous.end)
                        EditorGUILayout.HelpBox("运动扭曲窗口重叠，同一时段由先开始的窗口控制", MessageType.Warning);
                    previous = warp;
                }
            }
            DrawWindowEnds(data.FindProperty("windowEvents"), "窗口事件", animation.Duration);
            data.ApplyModifiedProperties();
        }

        internal static void DrawWindowEnds(SerializedProperty windows, string label, float duration)
        {
            for (int index = 0; index < windows.arraySize; index++)
            {
                SerializedProperty item = windows.GetArrayElementAtIndex(index);
                SerializedProperty end = item.FindPropertyRelative("end");
                if (end.floatValue <= duration)
                    continue;

                using (new EditorGUI.DisabledScope(item.FindPropertyRelative("start").floatValue >= duration))
                {
                    if (GUILayout.Button($"{label} {index} 的结束时间 {end.floatValue:0.###} s 对齐片段末尾 {duration:0.###} s"))
                        end.floatValue = duration;
                }
            }
        }

        internal static void DrawConfiguration(SerializedObject data, ref int pointType, ref int windowType)
        {
            data.Update();
            DrawPropertiesExcluding(data, "m_Script", "pointEvents", "windowEvents");
            data.ApplyModifiedProperties();
            TimelineEventEditor.DrawList(data.FindProperty("pointEvents"), TimelineEventEditor.pointTypes, ref pointType, "点事件");
            TimelineEventEditor.DrawList(data.FindProperty("windowEvents"), TimelineEventEditor.windowTypes, ref windowType, "窗口事件");
        }
    }
}
