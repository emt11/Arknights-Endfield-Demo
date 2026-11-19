using System;
using Endfield.AnimationSystem;
using Endfield.Combat;
using UnityEditor;
using UnityEngine;

namespace Endfield.EditorCombat
{
    internal enum TimelineTrack
    {
        MainAnimation,
        ComboAdvance,
        Hit,
        Cancel,
        OtherWindows,
        MotionWarp,
        OtherPoints
    }

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

        private static readonly TimelineTrack[] tracks = (TimelineTrack[])Enum.GetValues(typeof(TimelineTrack));
        private static readonly string[] trackLabels = Array.ConvertAll(tracks, track => track switch
        {
            TimelineTrack.MainAnimation => "主动画",
            TimelineTrack.ComboAdvance => "连段推进",
            TimelineTrack.Hit => "命中窗口",
            TimelineTrack.Cancel => "取消窗口",
            TimelineTrack.OtherWindows => "其他窗口事件",
            TimelineTrack.MotionWarp => "运动扭曲",
            TimelineTrack.OtherPoints => "其他点事件",
            _ => throw new ArgumentOutOfRangeException(nameof(track))
        });
        private static readonly string[] editableTrackLabels = trackLabels[1..];
        private SerializedObject animationData;
        private AnimationDefinition Animation => (AnimationDefinition)animationData.targetObject;
        private SerializedProperty Entries(TimelineTrack track) => animationData.FindProperty(IsPoint(track) ? "pointEvents" : "windowEvents");
        private static bool IsPoint(TimelineTrack track) => track == TimelineTrack.ComboAdvance || track == TimelineTrack.OtherPoints;
        private static TimelineTrack TrackOf(object entry) => entry switch
        {
            ComboAdvancePoint => TimelineTrack.ComboAdvance,
            HitWindow => TimelineTrack.Hit,
            CancelWindow => TimelineTrack.Cancel,
            MotionWarpWindow => TimelineTrack.MotionWarp,
            TimelineWindowEvent => TimelineTrack.OtherWindows,
            _ => TimelineTrack.OtherPoints
        };

        internal void Bind(SerializedObject animation) => animationData = animation;

        private const float LabelWidth = 100f;
        internal const float RowHeight = 28f;
        private Rect rect;
        private TimelineTrack selectedTrack = TimelineTrack.ComboAdvance;
        private int selectedIndex = -1;
        private int newEventTypeIndex;
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
        public float Height => RowHeight * tracks.Length;

        public void ClearSelection()
        {
            selectedIndex = -1;
            if (dragMode != DragMode.None && GUIUtility.hotControl == control)
                GUIUtility.hotControl = 0;
            dragMode = DragMode.None;
        }

        internal void SelectTrack(TimelineTrack track)
        {
            if (track == selectedTrack)
                return;

            ClearSelection();
            newEventTypeIndex = 0;
            selectedTrack = track;
        }

        public void Draw(Rect area, float cursor, Action<float> seek)
        {
            rect = area;
            duration = Animation.Duration;
            control = GUIUtility.GetControlID(FocusType.Passive);
            HandleInput(seek);
            for (int line = 0; line < tracks.Length; line++)
            {
                var background = new Rect(rect.x, rect.y + line * RowHeight, rect.width, RowHeight - 1f);
                EditorGUI.DrawRect(background, line % 2 == 0 ? new Color(0.15f, 0.16f, 0.18f) : new Color(0.12f, 0.13f, 0.15f));
                GUI.Label(new Rect(background.x + 5f, background.y + 4f, LabelWidth - 5f, 20f), trackLabels[line]);
            }

            GUI.BeginGroup(new Rect(rect.x + LabelWidth, rect.y, rect.width - LabelWidth, rect.height));
            GUI.Label(new Rect(2f, 2f, rect.width - LabelWidth, 24f), Animation.clip.name + "  0–" + Animation.Duration.ToString("0.###") + " s");
            for (int line = 1; line < tracks.Length; line++)
            {
                TimelineTrack track = tracks[line];
                SerializedProperty entries = Entries(track);
                for (int index = 0; index < entries.arraySize; index++)
                {
                    SerializedProperty item = entries.GetArrayElementAtIndex(index);
                    if (TrackOf(item.managedReferenceValue) != track)
                        continue;
                    float start = item.FindPropertyRelative(IsPoint(track) ? "time" : "start").floatValue;
                    float end = IsPoint(track) ? start : item.FindPropertyRelative("end").floatValue;
                    Color color = track switch
                    {
                        TimelineTrack.ComboAdvance => Color.yellow,
                        TimelineTrack.Hit => new Color(.75f, .25f, .2f),
                        TimelineTrack.Cancel => new Color(.25f, .6f, .35f),
                        _ => new Color(.45f, .3f, .7f)
                    };
                    if (track == selectedTrack && index == selectedIndex)
                        color = Color.Lerp(color, Color.white, .4f);

                    float left = TimeToX(start) - rect.x - LabelWidth - (IsPoint(track) ? 3f : 0f);
                    float width = IsPoint(track) ? 6f : Mathf.Max(2f, TimeToX(end) - TimeToX(start));
                    EditorGUI.DrawRect(new Rect(left, line * RowHeight + 5f, width, 18f), color);
                }
            }

            EditorGUI.DrawRect(new Rect(TimeToX(cursor) - rect.x - LabelWidth, 0f, 1f, rect.height), Color.white);
            GUI.EndGroup();
        }

        public void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                snap = GUILayout.Toggle(snap, "帧吸附", GUILayout.Width(70f));
                GUILayout.Label("缩放", GUILayout.Width(32f));
                zoom = GUILayout.HorizontalSlider(zoom, 1f, 8f, GUILayout.Width(90f));
                viewStart = GUILayout.HorizontalSlider(viewStart, 0f, Mathf.Max(0f, duration - duration / zoom), GUILayout.Width(100f));
                int selected = EditorGUILayout.Popup((int)selectedTrack - 1, editableTrackLabels, GUILayout.Width(100f));
                SelectTrack(tracks[selected + 1]);
                if (selectedTrack == TimelineTrack.OtherPoints || selectedTrack == TimelineTrack.OtherWindows)
                    newEventTypeIndex = TimelineEventEditor.ChooseType(newEventTypeIndex, IsPoint(selectedTrack) ? TimelineEventEditor.otherPointTypes : TimelineEventEditor.otherWindowTypes);
                if (GUILayout.Button("添加", GUILayout.Width(45f)))
                    Add();

                SerializedProperty entries = Entries(selectedTrack);
                using (new EditorGUI.DisabledScope(selectedIndex < 0 || selectedIndex >= entries.arraySize))
                {
                    if (GUILayout.Button("复制", GUILayout.Width(45f)))
                    {
                        TimelineEventEditor.Duplicate(entries, selectedIndex);
                        selectedIndex++;
                        SortTimings();
                    }
                    if (GUILayout.Button("删除", GUILayout.Width(45f)))
                    {
                        entries.DeleteArrayElementAtIndex(selectedIndex);
                        selectedIndex = -1;
                        entries.serializedObject.ApplyModifiedProperties();
                    }
                }
            }
        }

        public void DrawSelection(Hitbox[] hitboxes)
        {
            SerializedProperty entries = Entries(selectedTrack);
            if (selectedIndex < 0 || selectedIndex >= entries.arraySize)
                return;

            SerializedProperty item = entries.GetArrayElementAtIndex(selectedIndex);
            if (item.managedReferenceValue is HitWindow)
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
                        if (ids.GetArrayElementAtIndex(slot).intValue == hitbox.Id)
                            index = slot;
                    }
                    bool selected = index >= 0;
                    bool enabled = EditorGUILayout.ToggleLeft($"{hitbox.name}  (ID {hitbox.Id}, {hitbox.shape})", selected);
                    if (enabled && !selected)
                    {
                        ids.arraySize++;
                        ids.GetArrayElementAtIndex(ids.arraySize - 1).intValue = hitbox.Id;
                    }
                    else if (!enabled && selected)
                        ids.DeleteArrayElementAtIndex(index);
                }
            }
            else
                EditorGUILayout.PropertyField(item, new GUIContent(trackLabels[(int)selectedTrack] + " " + selectedIndex), true);
            if (entries.serializedObject.ApplyModifiedProperties())
                SortTimings();
        }

        private void Add()
        {
            SerializedProperty entries = Entries(selectedTrack);
            Type type = selectedTrack switch
            {
                TimelineTrack.ComboAdvance => typeof(ComboAdvancePoint),
                TimelineTrack.Hit => typeof(HitWindow),
                TimelineTrack.Cancel => typeof(CancelWindow),
                TimelineTrack.OtherWindows => TimelineEventEditor.otherWindowTypes[newEventTypeIndex],
                TimelineTrack.MotionWarp => typeof(MotionWarpWindow),
                TimelineTrack.OtherPoints => TimelineEventEditor.otherPointTypes[newEventTypeIndex],
                _ => throw new InvalidOperationException("The main animation track has no events")
            };
            selectedIndex = TimelineEventEditor.Add(entries, type);
            if (!IsPoint(selectedTrack))
            {
                entries.GetArrayElementAtIndex(selectedIndex).FindPropertyRelative("end").floatValue = Mathf.Min(.2f, duration);
                animationData.ApplyModifiedProperties();
            }
            SortTimings();
        }

        private float TimeToX(float value) => rect.x + LabelWidth + (value - viewStart) / (duration / zoom) * (rect.width - LabelWidth - 10f);

        private float XToTime(float value) => viewStart + (value - rect.x - LabelWidth) / (rect.width - LabelWidth - 10f) * (duration / zoom);

        private float SnapTime(float value) => snap ? Mathf.Round(value * Animation.clip.frameRate) / Animation.clip.frameRate : value;

        private void HandleInput(Action<float> seek)
        {
            Event current = Event.current;
            if (current.type == EventType.MouseDown && current.button == 0 && rect.Contains(current.mousePosition) && current.mousePosition.x >= rect.x + LabelWidth)
            {
                TimelineTrack track = tracks[Mathf.Clamp(Mathf.FloorToInt((current.mousePosition.y - rect.y) / RowHeight), 0, tracks.Length - 1)];
                if (track != TimelineTrack.MainAnimation)
                    SelectTrack(track);
                dragMode = DragMode.Cursor;
                if (track != TimelineTrack.MainAnimation)
                {
                    selectedIndex = -1;
                    SerializedProperty entries = Entries(track);
                    for (int index = entries.arraySize - 1; index >= 0; index--)
                    {
                        SerializedProperty item = entries.GetArrayElementAtIndex(index);
                        if (TrackOf(item.managedReferenceValue) != track)
                            continue;
                        float start = item.FindPropertyRelative(IsPoint(track) ? "time" : "start").floatValue;
                        float end = IsPoint(track) ? start : item.FindPropertyRelative("end").floatValue;
                        float pointerX = current.mousePosition.x;
                        if (pointerX < TimeToX(start) - 6f || pointerX > TimeToX(end) + 6f)
                            continue;

                        selectedIndex = index;
                        originalStart = start;
                        originalEnd = end;
                        dragTime = XToTime(pointerX);
                        if (IsPoint(track) || Mathf.Abs(pointerX - TimeToX(start)) <= 6f)
                            dragMode = DragMode.Start;
                        else if (Mathf.Abs(pointerX - TimeToX(end)) <= 6f)
                            dragMode = DragMode.End;
                        else if (end - start > duration)
                        {
                            // 超长窗口可选中、改边界或显式对齐，整段平移没有合法起点
                            dragMode = DragMode.None;
                            current.Use();
                            return;
                        }
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
                    SerializedProperty item = Entries(selectedTrack).GetArrayElementAtIndex(selectedIndex);
                    float minimum = Mathf.Min(1f / Animation.clip.frameRate, originalEnd - originalStart);
                    if (IsPoint(selectedTrack))
                        item.FindPropertyRelative("time").floatValue = Mathf.Clamp(SnapTime(value), 0f, duration);
                    else if (dragMode == DragMode.Start)
                        item.FindPropertyRelative("start").floatValue = Mathf.Clamp(SnapTime(value), 0f, originalEnd - minimum);
                    else if (dragMode == DragMode.End)
                        item.FindPropertyRelative("end").floatValue = Mathf.Clamp(SnapTime(value), originalStart + minimum, duration);
                    else
                    {
                        float length = originalEnd - originalStart;
                        float start = Mathf.Clamp(SnapTime(originalStart + value - dragTime), 0f, duration - length);
                        item.FindPropertyRelative("start").floatValue = start;
                        item.FindPropertyRelative("end").floatValue = start + length;
                    }

                    item.serializedObject.ApplyModifiedProperties();
                }

                current.Use();
            }
            else if (current.type == EventType.MouseUp)
            {
                if (dragMode != DragMode.Cursor)
                {
                    SortTimings();
                    Undo.CollapseUndoOperations(undoGroup);
                }

                GUIUtility.hotControl = 0;
                dragMode = DragMode.None;
                current.Use();
            }
        }

        internal void SortTimings()
        {
            var entries = Entries(selectedTrack);
            long id = selectedIndex >= 0 && selectedIndex < entries.arraySize ? entries.GetArrayElementAtIndex(selectedIndex).managedReferenceId : -1;
            TimelineEventEditor.Sort(entries);
            for (int index = 0; id >= 0 && index < entries.arraySize; index++)
            {
                if (entries.GetArrayElementAtIndex(index).managedReferenceId == id)
                    selectedIndex = index;
            }
        }
    }
}
