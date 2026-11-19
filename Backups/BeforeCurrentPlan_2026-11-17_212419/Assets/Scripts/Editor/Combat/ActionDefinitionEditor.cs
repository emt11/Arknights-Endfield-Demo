using Endfield.Combat;
using UnityEditor;
using UnityEngine;

namespace Endfield.EditorCombat
{
    [CustomEditor(typeof(ActionDefinition))]
    public sealed class ActionDefinitionEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            if (GUILayout.Button("打开动作 Timeline"))
                EditorWindow.GetWindow<ActionTimelineWindow>("Action Timeline").Select((ActionDefinition)target);

            DrawDefaultInspector();
        }
    }
}
