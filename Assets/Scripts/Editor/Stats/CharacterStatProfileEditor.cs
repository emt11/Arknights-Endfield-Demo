using System.Collections.Generic;
using System.Linq;
using Endfield.Stats;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(CharacterStatProfile))]
public sealed class CharacterStatProfileEditor : Editor
{
    private readonly HashSet<StatGroup> collapsed = new();
    private StatDefinition[] primary;
    private string[] primaryNames;

    private void OnEnable()
    {
        primary = StatCatalog.GetByGroup(StatGroup.PrimaryAttributes).ToArray();
        primaryNames = primary.Select(item => item.DisplayName).ToArray();
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        EditorGUILayout.PropertyField(serializedObject.FindProperty("displayName"), new GUIContent("角色显示名", "用于角色属性面板和装备按钮，留空时显示配置资产名"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("catalog"), new GUIContent("属性目录"));
        DrawPrimary("main", "主属性");
        DrawPrimary("secondary", "副属性");
        if (serializedObject.FindProperty("main").intValue == serializedObject.FindProperty("secondary").intValue)
            EditorGUILayout.HelpBox("主属性和副属性须选择不同项", MessageType.Error);

        var entries = serializedObject.FindProperty("initialValues");
        foreach (var group in StatCatalog.Groups)
        {
            var direct = StatCatalog.GetByGroup(group).Where(item => !item.Derived);
            if (!direct.Any()) continue;
            bool expanded = EditorGUILayout.Foldout(!collapsed.Contains(group), StatCatalog.GetGroupName(group), true);
            if (!expanded)
            {
                collapsed.Add(group);
                continue;
            }

            collapsed.Remove(group);
            foreach (var definition in direct)
                DrawInitialValue(entries, definition);
        }
        serializedObject.ApplyModifiedProperties();
    }

    private void DrawPrimary(string propertyName, string label)
    {
        var property = serializedObject.FindProperty(propertyName);
        int index = System.Array.FindIndex(primary, item => (int)item.Id == property.intValue);
        int selected = EditorGUILayout.Popup(label, index, primaryNames);
        if (selected >= 0) property.intValue = (int)primary[selected].Id;
    }

    private static void DrawInitialValue(SerializedProperty entries, StatDefinition definition)
    {
        int index = 0;
        while (index < entries.arraySize &&
               entries.GetArrayElementAtIndex(index).FindPropertyRelative("id").intValue != (int)definition.Id)
            index++;

        bool present = index < entries.arraySize;
        using (new EditorGUILayout.HorizontalScope())
        {
            bool include = EditorGUILayout.Toggle(present, GUILayout.Width(18));
            if (include != present)
            {
                if (include)
                {
                    entries.arraySize++;
                    var added = entries.GetArrayElementAtIndex(index);
                    added.FindPropertyRelative("id").intValue = (int)definition.Id;
                    added.FindPropertyRelative("value").doubleValue = definition.DefaultValue;
                }
                else entries.DeleteArrayElementAtIndex(index);
            }

            bool ratio = definition.Unit == StatUnit.Ratio;
            string label = definition.DisplayName + (ratio ? " (%)" : " ") + (definition.Required ? " · 必填" : "");
            var property = include ? entries.GetArrayElementAtIndex(index).FindPropertyRelative("value") : null;
            double scale = ratio ? 100 : 1;
            using (new EditorGUI.DisabledScope(!include))
            {
                EditorGUI.BeginChangeCheck();
                double value = EditorGUILayout.DoubleField(label, (property?.doubleValue ?? definition.DefaultValue) * scale);
                if (EditorGUI.EndChangeCheck() && include) property.doubleValue = value / scale;
            }
        }
    }
}
