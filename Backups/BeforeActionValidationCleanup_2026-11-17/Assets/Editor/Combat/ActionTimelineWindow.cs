using Endfield.Combat;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace Endfield.EditorCombat
{
    public sealed class ActionTimelineWindow : EditorWindow
    {
        [SerializeField] private ActionDefinition action;
        [SerializeField] private GameObject previewPrefab;
        [SerializeField] private float time;
        private ActionPreview preview;
        private bool playing;
        private double lastUpdate;
        private string error;
        private readonly ActionTimelineView timeline = new ActionTimelineView();
        private Vector2 detailsScroll;
        private bool showConfiguration;
        [SerializeField] private Vector3 simulatedTarget = new Vector3(0f, 0f, 2f);
        private bool draggingTarget;
        private Rect previewArea;

        public ActionDefinition SelectedAction => action;
        public float PreviewTime => time;
        public Rect TimelineRect => timeline.Rect;
        public Rect PreviewRect => previewArea;

        [MenuItem("Window/Endfield/Action Timeline")]
        public static void OpenWindow() => GetWindow<ActionTimelineWindow>("Action Timeline");

        [OnOpenAsset]
        private static bool OpenAsset(int instanceId, int line)
        {
            ActionDefinition definition = EditorUtility.EntityIdToObject(instanceId) as ActionDefinition;
            if (definition == null)
                return false;

            GetWindow<ActionTimelineWindow>("Action Timeline").Select(definition);
            return true;
        }

        private void OnEnable()
        {
            minSize = new Vector2(650f, 500f);
            if (previewPrefab == null)
                previewPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/Player.prefab");

            EditorApplication.update += TickPreview;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload += ReleasePreview;
            Undo.undoRedoPerformed += RebuildPreview;
            lastUpdate = EditorApplication.timeSinceStartup;
            RebuildPreview();
        }

        private void OnDisable()
        {
            EditorApplication.update -= TickPreview;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= ReleasePreview;
            Undo.undoRedoPerformed -= RebuildPreview;
            ReleasePreview();
        }

        public void Select(ActionDefinition definition)
        {
            action = definition;
            time = 0f;
            RebuildPreview();
            Repaint();
        }

        public void Seek(float value)
        {
            time = action == null ? 0f : Mathf.Clamp(value, 0f, action.Duration);
            preview?.Sample(time);
            Repaint();
        }

        private void RebuildPreview()
        {
            ReleasePreview();
            error = null;
            if (action == null || action.Clip == null || previewPrefab == null || EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            try
            {
                action.ValidateConfiguration();
                preview = new ActionPreview(action, previewPrefab);
                preview.SetTarget(simulatedTarget);
                Seek(time);
            }
            catch (System.Exception exception)
            {
                // 编辑器允许修复错误资产，显示原因并释放预览资源
                ReleasePreview();
                error = exception.Message;
            }
        }

        private void ReleasePreview()
        {
            playing = false;
            preview?.Dispose();
            preview = null;
        }

        private void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode)
                ReleasePreview();
            else if (state == PlayModeStateChange.EnteredEditMode)
                RebuildPreview();
        }

        private void TickPreview()
        {
            double now = EditorApplication.timeSinceStartup;
            if (playing && action != null)
            {
                Seek(time + (float)(now - lastUpdate) * action.PlaybackRate);
                if (time >= action.Duration)
                    playing = false;
            }

            lastUpdate = now;
        }

        private void OnGUI()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorGUILayout.HelpBox("退出 Play Mode 后编辑和预览动作", MessageType.Info);
                return;
            }
            EditorGUI.BeginChangeCheck();
            ActionDefinition next = (ActionDefinition)EditorGUILayout.ObjectField("动作", action, typeof(ActionDefinition), false);
            previewPrefab = (GameObject)EditorGUILayout.ObjectField("预览角色", previewPrefab, typeof(GameObject), false);
            if (EditorGUI.EndChangeCheck())
            {
                action = next;
                RebuildPreview();
            }

            if (action == null)
            {
                EditorGUILayout.HelpBox("选择 ActionDefinition，时间以动作局部秒数显示", MessageType.Info);
                return;
            }

            if (error != null)
            {
                EditorGUILayout.HelpBox(error, MessageType.Error);
            }

            if (action.Clip == null || action.Duration <= 0f)
            {
                DrawConfiguration();
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(playing ? "暂停" : "播放", GUILayout.Width(60f)))
                {
                    if (time >= action.Duration)
                        Seek(0f);
                    playing = !playing;
                    lastUpdate = EditorApplication.timeSinceStartup;
                }

                if (GUILayout.Button("上一帧", GUILayout.Width(65f)))
                    Seek(time - 1f / action.Clip.frameRate);
                if (GUILayout.Button("下一帧", GUILayout.Width(65f)))
                    Seek(time + 1f / action.Clip.frameRate);

                float value = EditorGUILayout.Slider(time, 0f, action.Duration);
                if (value != time)
                    Seek(value);
            }

            Rect previewRect = GUILayoutUtility.GetRect(100f, Mathf.Max(200f, position.height * 0.43f), GUILayout.ExpandWidth(true));
            previewArea = previewRect;
            if (preview != null && action.WarpWindows.Length > 0)
                HandleTargetDrag(previewRect);
            if (Event.current.type == EventType.Repaint && preview != null)
            {
                preview.Draw(previewRect);
                DrawRootPaths(previewRect);
                DrawHitShapes(previewRect);
            }

            int dirtyCount = EditorUtility.GetDirtyCount(action);
            var data = new SerializedObject(action);
            data.Update();
            timeline.DrawToolbar(data);
            Rect timelineRect = GUILayoutUtility.GetRect(100f, timeline.Height, GUILayout.ExpandWidth(true));
            timeline.Draw(timelineRect, data, time, Seek);
            EditorGUILayout.LabelField($"{time:0.###} s   /   {time * action.Clip.frameRate:0.##} 帧");
            detailsScroll = EditorGUILayout.BeginScrollView(detailsScroll);
            timeline.DrawSelection(data);
            DrawConfiguration();
            EditorGUILayout.EndScrollView();
            if (EditorUtility.GetDirtyCount(action) != dirtyCount)
                RebuildPreview();
            if (GUI.changed)
                Repaint();
        }

        private void DrawConfiguration()
        {
            if (action.RootMotion != null)
            {
                EditorGUI.BeginChangeCheck();
                simulatedTarget = EditorGUILayout.Vector3Field("模拟目标（拖动预览圆点）", simulatedTarget);
                if (EditorGUI.EndChangeCheck() && preview != null)
                {
                    preview.SetTarget(simulatedTarget);
                    Seek(time);
                }
                if (GUILayout.Button("更新根轨迹烘焙"))
                {
                    RootMotionBaker.Bake(action.RootMotion);
                    RebuildPreview();
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                showConfiguration = EditorGUILayout.Foldout(showConfiguration, "动作完整配置", true);
                if (GUILayout.Button("校验并保存", GUILayout.Width(100f)))
                {
                    RebuildPreview();
                    if (error == null)
                        AssetDatabase.SaveAssetIfDirty(action);
                }
                if (GUILayout.Button("重载", GUILayout.Width(60f)))
                {
                    AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(action));
                    RebuildPreview();
                }
            }

            if (!showConfiguration)
                return;

            var data = new SerializedObject(action);
            data.Update();
            SerializedProperty property = data.GetIterator();
            property.NextVisible(true);
            while (property.NextVisible(false))
                EditorGUILayout.PropertyField(property, true);
            if (data.ApplyModifiedProperties())
                RebuildPreview();
        }
        private void DrawHitShapes(Rect rect)
        {
            Handles.BeginGUI();
            Handles.color = new Color(1f, 0.4f, 0.2f);
            foreach (HitWindow window in action.HitWindows)
            {
                if (time < window.Start || time >= window.End)
                    continue;

                for (int axis = 0; axis < 3; axis++)
                {
                    var points = new Vector3[33];
                    for (int index = 0; index < points.Length; index++)
                    {
                        float angle = index * Mathf.PI * 2f / (points.Length - 1);
                        Vector3 offset = axis == 0 ? new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f)
                            : axis == 1 ? new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) : new Vector3(0f, Mathf.Cos(angle), Mathf.Sin(angle));
                        Vector3 viewport = preview.Camera.WorldToViewportPoint(preview.Model.TransformPoint(window.Offset + offset * window.Radius));
                        points[index] = new Vector3(rect.x + viewport.x * rect.width, rect.y + (1f - viewport.y) * rect.height, 0f);
                    }

                    Handles.DrawAAPolyLine(2f, points);
                }
            }

            Handles.EndGUI();
        }

        private Vector3 Project(Rect rect, Vector3 position)
        {
            Vector3 viewport = preview.Camera.WorldToViewportPoint(position);
            return new Vector3(rect.x + viewport.x * rect.width, rect.y + (1f - viewport.y) * rect.height, 0f);
        }

        private void DrawRootPaths(Rect rect)
        {
            if (preview.WarpedPath == null)
                return;
            Handles.BeginGUI();
            RootMotionSample[][] paths = { preview.OriginalPath, preview.WarpedPath };
            for (int index = 0; index < paths.Length; index++)
            {
                var points = new Vector3[paths[index].Length];
                for (int point = 0; point < points.Length; point++)
                    points[point] = Project(rect, paths[index][point].Position);
                Handles.color = index == 0 ? Color.gray : Color.cyan;
                Handles.DrawAAPolyLine(2f, points);
            }

            var circle = new Vector3[49];
            float gap = action.WarpWindows.Length > 0 ? action.WarpWindows[0].StopGap : 0f;
            for (int index = 0; index < circle.Length; index++)
            {
                float angle = index * Mathf.PI * 2f / (circle.Length - 1);
                circle[index] = Project(rect, simulatedTarget + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * (.8f + gap));
            }
            Handles.color = Color.yellow;
            Handles.DrawAAPolyLine(2f, circle);
            Vector3 target = Project(rect, simulatedTarget);
            Handles.DrawSolidDisc(target, Vector3.forward, 6f);
            GUI.Label(new Rect(rect.x + 5f, rect.y + 5f, 340f, 25f), "灰：源轨迹  青：扭曲轨迹  黄：目标与停止间距");
            Handles.EndGUI();
        }

        private void HandleTargetDrag(Rect rect)
        {
            Event current = Event.current;
            int control = GUIUtility.GetControlID(FocusType.Passive);
            Vector2 target = Project(rect, simulatedTarget);
            if (current.type == EventType.MouseDown && current.button == 0 && Vector2.Distance(current.mousePosition, target) < 12f)
            {
                draggingTarget = true;
                GUIUtility.hotControl = control;
                current.Use();
            }
            if (!draggingTarget || GUIUtility.hotControl != control)
                return;
            if (current.type == EventType.MouseDrag)
            {
                Vector2 position = current.mousePosition;
                Ray ray = preview.Camera.ViewportPointToRay(new Vector3((position.x - rect.x) / rect.width, 1f - (position.y - rect.y) / rect.height, 0f));
                var plane = new Plane(Vector3.up, Vector3.zero);
                if (plane.Raycast(ray, out float distance))
                {
                    simulatedTarget = ray.GetPoint(distance);
                    preview.SetTarget(simulatedTarget);
                    Seek(time);
                }
                current.Use();
            }
            else if (current.type == EventType.MouseUp)
            {
                draggingTarget = false;
                GUIUtility.hotControl = 0;
                current.Use();
            }
        }
    }
}
