using System;
using System.Linq;
using Endfield.Combat;
using UnityEditor;
using UnityEngine;

namespace Endfield.EditorCombat
{
    // 只负责时间轴绘制、选中项和一次拖拽，不持有另一份动作数据
    public sealed class ActionTimelineView
    {
        private enum DragMode
        {
            None,
            Start,
            End,
            Window,
            Cursor
        }

        private static readonly string[] properties = { "", "points", "hitWindows", "cancelWindows", "effectWindows", "warpWindows" };
        private static readonly string[] labels = { "主动画", "点通知", "命中窗口", "取消窗口", "持续特效", "运动扭曲" };
        private const float LabelWidth = 100f;
        private const float RowHeight = 28f;
        private Rect rect;
        private int selectedRow = 1;
        private int selectedIndex = -1;
        private DragMode dragMode;
        private int control;
        private int undoGroup;
        private float dragTime;
        private float originalStart;
        private float originalEnd;
        private float viewStart;
        private float zoom = 1f;
        private float duration;

        public bool snap = true;
        public Rect Rect => rect;
        public float Height => RowHeight * labels.Length;

        public void Draw(Rect area, SerializedObject data, float cursor, Action<float> seek)
        {
            rect = area;
            var action = (ActionDefinition)data.targetObject;
            duration = action.Duration;
            control = GUIUtility.GetControlID(FocusType.Passive);
            HandleInput(data, action, seek);
            for (int row = 0; row < labels.Length; row++)
            {
                var background = new Rect(rect.x, rect.y + row * RowHeight, rect.width, RowHeight - 1f);
                EditorGUI.DrawRect(background, row % 2 == 0 ? new Color(0.15f, 0.16f, 0.18f) : new Color(0.12f, 0.13f, 0.15f));
                GUI.Label(new Rect(background.x + 5f, background.y + 4f, LabelWidth - 5f, 20f), labels[row]);
            }

            GUI.BeginGroup(new Rect(rect.x + LabelWidth, rect.y, rect.width - LabelWidth, rect.height));
            GUI.Label(new Rect(2f, 2f, rect.width - LabelWidth, 24f), action.clip.name + "  " + action.clipStart.ToString("0.###") + "–" + action.clipEnd.ToString("0.###") + " s");
            for (int row = 1; row < labels.Length; row++)
            {
                SerializedProperty entries = data.FindProperty(properties[row]);
                for (int index = 0; index < entries.arraySize; index++)
                {
                    SerializedProperty item = entries.GetArrayElementAtIndex(index);
                    float start = item.FindPropertyRelative(row == 1 ? "time" : "start").floatValue;
                    float end = row == 1 ? start : item.FindPropertyRelative("end").floatValue;
                    Color color = row switch
                    {
                        1 => Color.yellow,
                        2 => new Color(.75f, .25f, .2f),
                        3 => new Color(.25f, .6f, .35f),
                        _ => new Color(.45f, .3f, .7f)
                    };
                    if (row == selectedRow && index == selectedIndex)
                        color = Color.Lerp(color, Color.white, .4f);

                    float left = TimeToX(start) - rect.x - LabelWidth - (row == 1 ? 3f : 0f);
                    float width = row == 1 ? 6f : Mathf.Max(2f, TimeToX(end) - TimeToX(start));
                    EditorGUI.DrawRect(new Rect(left, row * RowHeight + 5f, width, 18f), color);
                }
            }

            EditorGUI.DrawRect(new Rect(TimeToX(cursor) - rect.x - LabelWidth, 0f, 1f, rect.height), Color.white);
            GUI.EndGroup();
        }

        public void DrawToolbar(SerializedObject data)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                snap = GUILayout.Toggle(snap, "帧吸附", GUILayout.Width(70f));
                GUILayout.Label("缩放", GUILayout.Width(32f));
                zoom = GUILayout.HorizontalSlider(zoom, 1f, 8f, GUILayout.Width(90f));
                viewStart = GUILayout.HorizontalSlider(viewStart, 0f, Mathf.Max(0f, duration - duration / zoom), GUILayout.Width(100f));
                selectedRow = EditorGUILayout.Popup(selectedRow - 1, new[] { "点通知", "命中窗口", "取消窗口", "持续特效", "运动扭曲" }, GUILayout.Width(100f)) + 1;
                if (GUILayout.Button("添加", GUILayout.Width(45f)))
                    Add(data);

                using (new EditorGUI.DisabledScope(selectedIndex < 0 || selectedIndex >= data.FindProperty(properties[selectedRow]).arraySize))
                {
                    if (GUILayout.Button("复制", GUILayout.Width(45f)))
                    {
                        SerializedProperty entries = data.FindProperty(properties[selectedRow]);
                        entries.InsertArrayElementAtIndex(selectedIndex);
                        data.ApplyModifiedProperties();
                    }
                    if (GUILayout.Button("删除", GUILayout.Width(45f)))
                    {
                        data.FindProperty(properties[selectedRow]).DeleteArrayElementAtIndex(selectedIndex);
                        selectedIndex = -1;
                        data.ApplyModifiedProperties();
                    }
                }
            }
        }

        public void DrawSelection(SerializedObject data, Hitbox[] hitboxes)
        {
            SerializedProperty entries = data.FindProperty(properties[selectedRow]);
            if (selectedIndex < 0 || selectedIndex >= entries.arraySize)
                return;

            SerializedProperty item = entries.GetArrayElementAtIndex(selectedIndex);
            if (selectedRow == 2)
            {
                EditorGUILayout.LabelField("命中窗口 " + selectedIndex, EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(item.FindPropertyRelative("start"));
                EditorGUILayout.PropertyField(item.FindPropertyRelative("end"));
                EditorGUILayout.PropertyField(item.FindPropertyRelative("group"));
                EditorGUILayout.PropertyField(item.FindPropertyRelative("damageMultiplier"));
                SerializedProperty ids = item.FindPropertyRelative("hitboxIds");
                if (hitboxes.Length == 0)
                    EditorGUILayout.HelpBox("为预览角色配置 WeaponController 和带 Hitbox 的武器 Prefab", MessageType.Info);
                foreach (Hitbox hitbox in hitboxes)
                {
                    int index = -1;
                    for (int slot = 0; slot < ids.arraySize; slot++)
                    {
                        if (ids.GetArrayElementAtIndex(slot).intValue == hitbox.id)
                            index = slot;
                    }
                    bool selected = index >= 0;
                    bool enabled = EditorGUILayout.ToggleLeft($"{hitbox.name}  (ID {hitbox.id}, {hitbox.shape})", selected);
                    if (enabled && !selected)
                    {
                        ids.arraySize++;
                        ids.GetArrayElementAtIndex(ids.arraySize - 1).intValue = hitbox.id;
                    }
                    else if (!enabled && selected)
                        ids.DeleteArrayElementAtIndex(index);
                }
            }
            else
                EditorGUILayout.PropertyField(item, new GUIContent(labels[selectedRow] + " " + selectedIndex), true);
            if (data.ApplyModifiedProperties())
                SortPoints((ActionDefinition)data.targetObject);
        }

        private void Add(SerializedObject data)
        {
            SerializedProperty entries = data.FindProperty(properties[selectedRow]);
            selectedIndex = entries.arraySize;
            entries.arraySize++;
            SerializedProperty item = entries.GetArrayElementAtIndex(selectedIndex);
            item.FindPropertyRelative(selectedRow == 1 ? "time" : "start").floatValue = 0f;
            if (selectedRow != 1)
                item.FindPropertyRelative("end").floatValue = Mathf.Min(.2f, duration);
            if (selectedRow == 2)
            {
                item.FindPropertyRelative("damageMultiplier").floatValue = 1f;
                item.FindPropertyRelative("hitboxIds").arraySize = 0;
            }
            if (selectedRow == 1)
                item.FindPropertyRelative("kind").enumValueIndex = (int)ActionPointKind.ComboAdvance;
            if (selectedRow == 5)
            {
                item.FindPropertyRelative("warpTranslation").boolValue = true;
                item.FindPropertyRelative("warpRotation").boolValue = true;
                item.FindPropertyRelative("stopGap").floatValue = .15f;
                item.FindPropertyRelative("maxApproach").floatValue = 2f;
                item.FindPropertyRelative("maxExtension").floatValue = 1f;
                item.FindPropertyRelative("maxSpeed").floatValue = 8f;
                item.FindPropertyRelative("maxYawSpeed").floatValue = 720f;
                item.FindPropertyRelative("maxYawCorrection").floatValue = 60f;
                item.FindPropertyRelative("correction").animationCurveValue = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            }

            data.ApplyModifiedProperties();
            SortPoints((ActionDefinition)data.targetObject);
        }

        private float TimeToX(float value) => rect.x + LabelWidth + (value - viewStart) / (duration / zoom) * (rect.width - LabelWidth - 10f);

        private float XToTime(float value) => viewStart + (value - rect.x - LabelWidth) / (rect.width - LabelWidth - 10f) * (duration / zoom);

        private float SnapTime(float value, ActionDefinition action) => snap ? Mathf.Round(value * action.clip.frameRate) / action.clip.frameRate : value;

        private void HandleInput(SerializedObject data, ActionDefinition action, Action<float> seek)
        {
            Event current = Event.current;
            if (current.type == EventType.MouseDown && current.button == 0 && rect.Contains(current.mousePosition) && current.mousePosition.x >= rect.x + LabelWidth)
            {
                int row = Mathf.FloorToInt((current.mousePosition.y - rect.y) / RowHeight);
                dragMode = DragMode.Cursor;
                if (row > 0)
                {
                    selectedRow = row;
                    selectedIndex = -1;
                    SerializedProperty entries = data.FindProperty(properties[row]);
                    for (int index = entries.arraySize - 1; index >= 0; index--)
                    {
                        SerializedProperty item = entries.GetArrayElementAtIndex(index);
                        float start = item.FindPropertyRelative(row == 1 ? "time" : "start").floatValue;
                        float end = row == 1 ? start : item.FindPropertyRelative("end").floatValue;
                        float pointerX = current.mousePosition.x;
                        if (pointerX < TimeToX(start) - 6f || pointerX > TimeToX(end) + 6f)
                            continue;

                        selectedIndex = index;
                        originalStart = start;
                        originalEnd = end;
                        dragTime = XToTime(pointerX);
                        if (row == 1 || Mathf.Abs(pointerX - TimeToX(start)) <= 6f)
                            dragMode = DragMode.Start;
                        else if (Mathf.Abs(pointerX - TimeToX(end)) <= 6f)
                            dragMode = DragMode.End;
                        else
                            dragMode = DragMode.Window;
                        Undo.IncrementCurrentGroup();
                        undoGroup = Undo.GetCurrentGroup();
                        Undo.SetCurrentGroupName("Drag action timing");
                        break;
                    }
                }

                GUIUtility.hotControl = control;
                if (dragMode == DragMode.Cursor)
                    seek(XToTime(current.mousePosition.x));
                current.Use();
            }

            if (GUIUtility.hotControl != control)
                return;

            if (current.type == EventType.MouseDrag)
            {
                float value = XToTime(current.mousePosition.x);
                if (dragMode == DragMode.Cursor)
                    seek(value);
                else
                {
                    SerializedProperty item = data.FindProperty(properties[selectedRow]).GetArrayElementAtIndex(selectedIndex);
                    float minimum = Mathf.Min(1f / action.clip.frameRate, originalEnd - originalStart);
                    if (selectedRow == 1)
                        item.FindPropertyRelative("time").floatValue = Mathf.Clamp(SnapTime(value, action), 0f, duration);
                    else if (dragMode == DragMode.Start)
                        item.FindPropertyRelative("start").floatValue = Mathf.Clamp(SnapTime(value, action), 0f, originalEnd - minimum);
                    else if (dragMode == DragMode.End)
                        item.FindPropertyRelative("end").floatValue = Mathf.Clamp(SnapTime(value, action), originalStart + minimum, duration);
                    else
                    {
                        float length = originalEnd - originalStart;
                        float start = Mathf.Clamp(SnapTime(originalStart + value - dragTime, action), 0f, duration - length);
                        item.FindPropertyRelative("start").floatValue = start;
                        item.FindPropertyRelative("end").floatValue = start + length;
                    }

                    data.ApplyModifiedProperties();
                }

                current.Use();
            }
            else if (current.type == EventType.MouseUp)
            {
                if (dragMode != DragMode.Cursor)
                {
                    SortPoints(action);
                    Undo.CollapseUndoOperations(undoGroup);
                }

                GUIUtility.hotControl = 0;
                dragMode = DragMode.None;
                current.Use();
            }
        }

        private void SortPoints(ActionDefinition action)
        {
            ActionPoint selected = selectedRow == 1 && selectedIndex >= 0 && selectedIndex < action.points.Length ? action.points[selectedIndex] : null;
            // 同时刻先处理连段，再音效、特效；稳定排序保留同类条目的顺序
            Undo.RecordObject(action, "Sort action points");
            action.points = action.points.OrderBy(point => point.time).ThenBy(Priority).ToArray();
            if (selected != null)
                selectedIndex = Array.IndexOf(action.points, selected);
            EditorUtility.SetDirty(action);
        }

        private static int Priority(ActionPoint point) => point.kind switch
        {
            ActionPointKind.ComboAdvance => 0,
            ActionPointKind.Audio => 1,
            _ => 2
        };
    }
}
