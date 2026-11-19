using System;
using System.Linq;
using Endfield.AnimationSystem;
using Endfield.Combat;
using UnityEditor;
using UnityEngine;

namespace Endfield.EditorCombat
{
    // 两个 Inspector 和时间轴通过同一序列化属性写回实际拥有轨道的资产
    internal static class TimelineEventEditor
    {
        internal static readonly Type[] pointTypes = EventTypes<TimelinePointEvent>();
        internal static readonly Type[] windowTypes = EventTypes<TimelineWindowEvent>();
        internal static readonly Type[] otherPointTypes = pointTypes.Where(type => type != typeof(ComboAdvancePoint)).ToArray();
        internal static readonly Type[] otherWindowTypes = windowTypes.Where(type => type != typeof(CancelWindow) && type != typeof(HitWindow) && type != typeof(MotionWarpWindow)).ToArray();

        private static Type[] EventTypes<T>() => TypeCache.GetTypesDerivedFrom<T>()
            .Where(type => !type.IsAbstract && type.IsSerializable && type.IsPublic)
            .OrderBy(type => type.Name).ToArray();

        internal static int ChooseType(int selected, Type[] types)
        {
            if (types.Length == 1)
                return 0;

            string[] names = Array.ConvertAll(types, type => ObjectNames.NicifyVariableName(type.Name));
            return EditorGUILayout.Popup(Mathf.Clamp(selected, 0, names.Length - 1), names);
        }

        internal static int Add(SerializedProperty entries, Type type)
        {
            int index = entries.arraySize++;
            entries.GetArrayElementAtIndex(index).managedReferenceValue = Activator.CreateInstance(type);
            entries.serializedObject.ApplyModifiedProperties();
            return index;
        }

        internal static void Duplicate(SerializedProperty entries, int index)
        {
            var source = entries.GetArrayElementAtIndex(index);
            object value = source.managedReferenceValue;
            object copy = JsonUtility.FromJson(JsonUtility.ToJson(value), value.GetType());
            entries.InsertArrayElementAtIndex(index);
            entries.GetArrayElementAtIndex(index + 1).managedReferenceValue = copy;
            entries.serializedObject.ApplyModifiedProperties();
        }

        internal static void Sort(SerializedProperty entries)
        {
            bool points = entries.name == nameof(AnimationDefinition.pointEvents);
            string key = points ? "time" : "start";
            for (int index = 1; index < entries.arraySize; index++)
            {
                SerializedProperty item = entries.GetArrayElementAtIndex(index);
                float time = item.FindPropertyRelative(key).floatValue;
                int priority = points ? PointPriority(item.managedReferenceValue) : 0;
                int destination = index;
                while (destination > 0)
                {
                    SerializedProperty prior = entries.GetArrayElementAtIndex(destination - 1);
                    float priorTime = prior.FindPropertyRelative(key).floatValue;
                    if (priorTime < time || (priorTime == time && (!points || PointPriority(prior.managedReferenceValue) <= priority)))
                        break;
                    destination--;
                }
                if (destination != index)
                    entries.MoveArrayElement(index, destination);
            }
            entries.serializedObject.ApplyModifiedProperties();
        }

        private static int PointPriority(object entry) => entry switch
        {
            ComboAdvancePoint => 0,
            AudioPoint => 1,
            _ => 2
        };

        internal static void DrawList(SerializedProperty entries, Type[] types, ref int type, string title)
        {
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            bool added = false;
            for (int index = 0; index < entries.arraySize; index++)
            {
                var item = entries.GetArrayElementAtIndex(index);
                EditorGUILayout.PropertyField(item, new GUIContent(item.managedReferenceValue.GetType().Name), true);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("复制"))
                    {
                        entries.serializedObject.ApplyModifiedProperties();
                        Duplicate(entries, index);
                        return;
                    }
                    if (GUILayout.Button("删除"))
                    {
                        entries.DeleteArrayElementAtIndex(index);
                        break;
                    }
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                type = ChooseType(type, types);
                if (GUILayout.Button("添加" + title))
                {
                    Add(entries, types[type]);
                    added = true;
                }
            }
            if (added || entries.serializedObject.hasModifiedProperties)
                Sort(entries);
        }
    }
}
