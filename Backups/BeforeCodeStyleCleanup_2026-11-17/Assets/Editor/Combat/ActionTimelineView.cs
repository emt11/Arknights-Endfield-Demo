using System;
using Endfield.Combat;
using UnityEditor;
using UnityEngine;

namespace Endfield.EditorCombat
{
    // 只负责时间轴绘制、选中项和一次拖拽，不持有另一份动作数据
    public sealed class ActionTimelineView
    {
        private static readonly string[] Properties = { "", "Points", "HitWindows", "CancelWindows", "EffectWindows", "WarpWindows" };
        private static readonly string[] Labels = { "主动画", "点通知", "命中窗口", "取消窗口", "持续特效", "运动扭曲" };
        private const float LabelWidth = 100f;
        private const float RowHeight = 28f;
        private Rect rect;
        private int selectedRow = 1;
        private int selectedIndex = -1;
        private int dragMode;
        private int control;
        private int undoGroup;
        private float dragTime;
        private float originalStart;
        private float originalEnd;
        private float viewStart;
        private float zoom = 1f;
        private float duration;

        public bool Snap = true;
        public Rect Rect => rect;
        public float Height => RowHeight * Labels.Length;

        public void Draw(Rect area, SerializedObject data, float cursor, Action<float> seek)
        {
            rect = area;
            var action = (ActionDefinition)data.targetObject;
            duration = action.Duration;
            control = GUIUtility.GetControlID(FocusType.Passive);
            HandleInput(data, action, seek);
            for (int row = 0; row < Labels.Length; row++)
            {
                var background = new Rect(rect.x, rect.y + row * RowHeight, rect.width, RowHeight - 1f);
                EditorGUI.DrawRect(background, row % 2 == 0 ? new Color(0.15f, 0.16f, 0.18f) : new Color(0.12f, 0.13f, 0.15f));
                GUI.Label(new Rect(background.x + 5f, background.y + 4f, LabelWidth - 5f, 20f), Labels[row]);
            }

            GUI.BeginGroup(new Rect(rect.x + LabelWidth, rect.y, rect.width - LabelWidth, rect.height));
            GUI.Label(new Rect(2f, 2f, rect.width - LabelWidth, 24f), action.Clip.name + "  " + action.ClipStart.ToString("0.###") + "–" + action.ClipEnd.ToString("0.###") + " s");
            for (int row = 1; row < Labels.Length; row++)
            {
                SerializedProperty entries = data.FindProperty(Properties[row]);
                for (int index = 0; index < entries.arraySize; index++)
                {
                    SerializedProperty item = entries.GetArrayElementAtIndex(index);
                    float start = item.FindPropertyRelative(row == 1 ? "Time" : "Start").floatValue;
                    float end = row == 1 ? start : item.FindPropertyRelative("End").floatValue;
                    Color color = row == 1 ? Color.yellow : row == 2 ? new Color(.75f, .25f, .2f) : row == 3 ? new Color(.25f, .6f, .35f) : new Color(.45f, .3f, .7f);
                    if (row == selectedRow && index == selectedIndex)
                        color = Color.Lerp(color, Color.white, .4f);

                    EditorGUI.DrawRect(new Rect(TimeToX(start) - rect.x - LabelWidth - (row == 1 ? 3f : 0f), row * RowHeight + 5f, row == 1 ? 6f : Mathf.Max(2f, TimeToX(end) - TimeToX(start)), 18f), color);

                }
            }

            EditorGUI.DrawRect(new Rect(TimeToX(cursor) - rect.x - LabelWidth, 0f, 1f, rect.height), Color.white);
            GUI.EndGroup();
        }

        public void DrawToolbar(SerializedObject data)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                Snap = GUILayout.Toggle(Snap, "帧吸附", GUILayout.Width(70f));
                GUILayout.Label("缩放", GUILayout.Width(32f));
                zoom = GUILayout.HorizontalSlider(zoom, 1f, 8f, GUILayout.Width(90f));
                viewStart = GUILayout.HorizontalSlider(viewStart, 0f, Mathf.Max(0f, duration - duration / zoom), GUILayout.Width(100f));
                selectedRow = EditorGUILayout.Popup(selectedRow - 1, new[] { "点通知", "命中窗口", "取消窗口", "持续特效", "运动扭曲" }, GUILayout.Width(100f)) + 1;
                if (GUILayout.Button("添加", GUILayout.Width(45f)))
                    Add(data);

                using (new EditorGUI.DisabledScope(selectedIndex < 0 || selectedIndex >= data.FindProperty(Properties[selectedRow]).arraySize))
                {
                    if (GUILayout.Button("复制", GUILayout.Width(45f)))
                    {
                        SerializedProperty entries = data.FindProperty(Properties[selectedRow]);
                        entries.InsertArrayElementAtIndex(selectedIndex);
                        data.ApplyModifiedProperties();
                    }
                    if (GUILayout.Button("删除", GUILayout.Width(45f)))
                    {
                        data.FindProperty(Properties[selectedRow]).DeleteArrayElementAtIndex(selectedIndex);
                        selectedIndex = -1;
                        data.ApplyModifiedProperties();
                    }
                }
            }
        }

        public void DrawSelection(SerializedObject data)
        {
            SerializedProperty entries = data.FindProperty(Properties[selectedRow]);
            if (selectedIndex < 0 || selectedIndex >= entries.arraySize)
                return;

            EditorGUILayout.PropertyField(entries.GetArrayElementAtIndex(selectedIndex), new GUIContent(Labels[selectedRow] + " " + selectedIndex), true);
            if (data.ApplyModifiedProperties())
                SortPoints((ActionDefinition)data.targetObject);
        }

        private void Add(SerializedObject data)
        {
            SerializedProperty entries = data.FindProperty(Properties[selectedRow]);
            selectedIndex = entries.arraySize;
            entries.arraySize++;
            SerializedProperty item = entries.GetArrayElementAtIndex(selectedIndex);
            item.FindPropertyRelative(selectedRow == 1 ? "Time" : "Start").floatValue = 0f;
            if (selectedRow != 1)
                item.FindPropertyRelative("End").floatValue = Mathf.Min(.2f, duration);
            if (selectedRow == 2)
            {
                item.FindPropertyRelative("Radius").floatValue = .8f;
                item.FindPropertyRelative("DamageMultiplier").floatValue = 1f;
                item.FindPropertyRelative("Offset").vector3Value = new Vector3(0f, 1f, 1f);
            }
            if (selectedRow == 1)
                item.FindPropertyRelative("Kind").enumValueIndex = (int)ActionPointKind.ComboAdvance;
            if (selectedRow == 5)
            {
                item.FindPropertyRelative("WarpTranslation").boolValue = true;
                item.FindPropertyRelative("WarpRotation").boolValue = true;
                item.FindPropertyRelative("StopGap").floatValue = .15f;
                item.FindPropertyRelative("MaxApproach").floatValue = 2f;
                item.FindPropertyRelative("MaxExtension").floatValue = 1f;
                item.FindPropertyRelative("MaxSpeed").floatValue = 8f;
                item.FindPropertyRelative("MaxYawSpeed").floatValue = 720f;
                item.FindPropertyRelative("MaxYawCorrection").floatValue = 60f;
                item.FindPropertyRelative("Correction").animationCurveValue = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            }

            data.ApplyModifiedProperties();
            SortPoints((ActionDefinition)data.targetObject);
        }

        private float TimeToX(float value) => rect.x + LabelWidth + (value - viewStart) / (duration / zoom) * (rect.width - LabelWidth - 10f);

        private float XToTime(float value) => viewStart + (value - rect.x - LabelWidth) / (rect.width - LabelWidth - 10f) * (duration / zoom);

        private float SnapTime(float value, ActionDefinition action) => Snap ? Mathf.Round(value * action.Clip.frameRate) / action.Clip.frameRate : value;

        private void HandleInput(SerializedObject data, ActionDefinition action, Action<float> seek)
        {
            Event current = Event.current;
            if (current.type == EventType.MouseDown && current.button == 0 && rect.Contains(current.mousePosition) && current.mousePosition.x >= rect.x + LabelWidth)
            {
                int row = Mathf.FloorToInt((current.mousePosition.y - rect.y) / RowHeight);
                dragMode = 4;
                if (row > 0)
                {
                    selectedRow = row;
                    selectedIndex = -1;
                    SerializedProperty entries = data.FindProperty(Properties[row]);
                    for (int index = entries.arraySize - 1; index >= 0; index--)
                    {
                        SerializedProperty item = entries.GetArrayElementAtIndex(index);
                        float start = item.FindPropertyRelative(row == 1 ? "Time" : "Start").floatValue;
                        float end = row == 1 ? start : item.FindPropertyRelative("End").floatValue;
                        float x = current.mousePosition.x;
                        if (x < TimeToX(start) - 6f || x > TimeToX(end) + 6f)
                            continue;

                        selectedIndex = index;
                        originalStart = start;
                        originalEnd = end;
                        dragTime = XToTime(x);
                        dragMode = row == 1 || Mathf.Abs(x - TimeToX(start)) <= 6f ? 1 : Mathf.Abs(x - TimeToX(end)) <= 6f ? 2 : 3;
                        Undo.IncrementCurrentGroup();
                        undoGroup = Undo.GetCurrentGroup();
                        Undo.SetCurrentGroupName("Drag action timing");
                        break;
                    }
                }

                GUIUtility.hotControl = control;
                if (dragMode == 4)
                    seek(XToTime(current.mousePosition.x));
                current.Use();
            }

            if (GUIUtility.hotControl != control)
                return;

            if (current.type == EventType.MouseDrag)
            {
                float value = XToTime(current.mousePosition.x);
                if (dragMode == 4)
                    seek(value);
                else
                {
                    SerializedProperty item = data.FindProperty(Properties[selectedRow]).GetArrayElementAtIndex(selectedIndex);
                    float minimum = Mathf.Min(1f / action.Clip.frameRate, originalEnd - originalStart);
                    if (selectedRow == 1)
                        item.FindPropertyRelative("Time").floatValue = Mathf.Clamp(SnapTime(value, action), 0f, duration);
                    else if (dragMode == 1)
                        item.FindPropertyRelative("Start").floatValue = Mathf.Clamp(SnapTime(value, action), 0f, originalEnd - minimum);
                    else if (dragMode == 2)
                        item.FindPropertyRelative("End").floatValue = Mathf.Clamp(SnapTime(value, action), originalStart + minimum, duration);
                    else
                    {
                        float length = originalEnd - originalStart;
                        float start = Mathf.Clamp(SnapTime(originalStart + value - dragTime, action), 0f, duration - length);
                        item.FindPropertyRelative("Start").floatValue = start;
                        item.FindPropertyRelative("End").floatValue = start + length;
                    }

                    data.ApplyModifiedProperties();
                }

                current.Use();
            }
            else if (current.type == EventType.MouseUp)
            {
                if (dragMode != 4)
                {
                    SortPoints(action);
                    Undo.CollapseUndoOperations(undoGroup);
                }

                GUIUtility.hotControl = 0;
                dragMode = 0;
                current.Use();
            }
        }

        private void SortPoints(ActionDefinition action)
        {
            ActionPoint selected = selectedRow == 1 && selectedIndex >= 0 && selectedIndex < action.Points.Length ? action.Points[selectedIndex] : null;
            // 同时刻先处理连段，再音效、特效；稳定排序保留同类条目的顺序
            var ordered = new System.Collections.Generic.List<ActionPoint>(action.Points);
            for (int index = 1; index < ordered.Count; index++)
            {
                ActionPoint item = ordered[index];
                int previous = index - 1;
                while (previous >= 0 && (ordered[previous].Time > item.Time || ordered[previous].Time == item.Time && Priority(ordered[previous]) > Priority(item)))
                {
                    ordered[previous + 1] = ordered[previous];
                    previous--;
                }

                ordered[previous + 1] = item;
            }

            Undo.RecordObject(action, "Sort action points");
            action.Points = ordered.ToArray();
            if (selected != null)
                selectedIndex = Array.IndexOf(action.Points, selected);
            EditorUtility.SetDirty(action);
        }

        private static int Priority(ActionPoint point) => point.Kind == ActionPointKind.ComboAdvance ? 0 : point.Kind == ActionPointKind.Audio ? 1 : 2;
    }
}
