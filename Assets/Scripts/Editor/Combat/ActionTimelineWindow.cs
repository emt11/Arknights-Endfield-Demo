using System.Linq;
using Endfield.AnimationSystem;
using Endfield.Combat;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace Endfield.EditorCombat
{
    public sealed class ActionTimelineWindow : EditorWindow
    {
        private const string PreviewStepKey = "Endfield.ActionTimeline.PreviewStep";
        private const float DefaultPreviewStep = 1f / 60f;
        private const int MaxPreviewFps = 1000;
        [SerializeField] private ActionDefinition action;
        [SerializeField] private AnimationDefinition standalone;
        private AnimationDefinition Animation => action != null ? action.animation : standalone;
        [SerializeField] private GameObject previewPrefab;
        [SerializeField] private float time;
        [SerializeField] private Vector3 simulatedTarget = new Vector3(0f, 0f, 2f);
        [SerializeField] private float simulatedTargetRadius = .5f;
        [SerializeField] private float previewStep = DefaultPreviewStep;
        private ActionPreview preview;
        private bool playing;
        private double lastUpdate;
        private string error;
        private readonly ActionTimelineView timeline = new ActionTimelineView();
        private Vector2 detailsScroll;
        private bool showConfiguration;
        private bool draggingTarget;
        private int notifyType;
        private int windowType;
        private int actionRevision;
        private int animationRevision;
        private int rootRevision;
        private AnimationDefinition previewAnimation;
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
            Object asset = EditorUtility.EntityIdToObject(instanceId);
            if (asset is ActionDefinition definition)
                GetWindow<ActionTimelineWindow>("Action Timeline").Select(definition);
            else if (asset is AnimationDefinition animation)
                GetWindow<ActionTimelineWindow>("Action Timeline").Select(animation);
            else
                return false;
            return true;
        }

        private void OnEnable()
        {
            if (previewStep == DefaultPreviewStep)
                previewStep = SessionState.GetFloat(PreviewStepKey, DefaultPreviewStep);
            minSize = new Vector2(650f, 500f);
            if (previewPrefab == null)
                previewPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Characters/Player.prefab");

            EditorApplication.update += TickPreview;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload += ReleasePreview;
            Undo.undoRedoPerformed += RefreshPreview;
            lastUpdate = EditorApplication.timeSinceStartup;
            RefreshPreview();
        }

        private void OnDisable()
        {
            SessionState.SetFloat(PreviewStepKey, previewStep);
            EditorApplication.update -= TickPreview;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= ReleasePreview;
            Undo.undoRedoPerformed -= RefreshPreview;
            ReleasePreview();
        }

        public void Select(ActionDefinition definition)
        {
            timeline.ClearSelection();
            action = definition;
            standalone = null;
            time = 0f;
            playing = false;
            RefreshPreview();
            Repaint();
        }

        public void Select(AnimationDefinition definition)
        {
            timeline.ClearSelection();
            action = null;
            standalone = definition;
            time = 0f;
            playing = false;
            RefreshPreview();
            Repaint();
        }

        public void Seek(float value)
        {
            time = Animation == null || Animation.clip == null ? 0f : Mathf.Clamp(value, 0f, Animation.Duration);
            preview?.Sample(time);
            Repaint();
        }

        private void OnProjectChange() => RefreshPreview();

        private void RefreshPreview()
        {
            error = null;
            previewAnimation = Animation;
            actionRevision = action == null ? 0 : EditorUtility.GetDirtyCount(action);
            animationRevision = Animation == null ? 0 : EditorUtility.GetDirtyCount(Animation);
            rootRevision = action != null && action.rootMotion != null ? EditorUtility.GetDirtyCount(action.rootMotion) : 0;
            if (Animation == null || Animation.clip == null || previewPrefab == null || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                ReleasePreview();
                return;
            }

            try
            {
                if (preview != null && preview.Matches(action, Animation, previewPrefab))
                    preview.Refresh(previewStep, simulatedTargetRadius, simulatedTarget);
                else
                {
                    ReleasePreview();
                    preview = new ActionPreview(Animation, previewPrefab, previewStep, action, simulatedTargetRadius, simulatedTarget);
                }
                Seek(time);
            }
            catch (System.Exception exception)
            {
                // 预览失败时释放资源并显示原因，编辑界面仍可继续使用
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
                RefreshPreview();
        }

        private void TickPreview()
        {
            double now = EditorApplication.timeSinceStartup;
            if (playing && Animation != null)
            {
                Seek(time + (float)(now - lastUpdate) * Animation.playbackRate);
                if (time >= Animation.Duration)
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
            Object selected = action != null ? (Object)action : standalone;
            Object next = EditorGUILayout.ObjectField("动作或动画", selected, typeof(ScriptableObject), false);
            previewPrefab = (GameObject)EditorGUILayout.ObjectField("预览角色", previewPrefab, typeof(GameObject), false);
            if (EditorGUI.EndChangeCheck())
            {
                if (next is ActionDefinition nextAction)
                    Select(nextAction);
                else
                    Select(next as AnimationDefinition);
            }

            if (action == null && standalone == null)
            {
                EditorGUILayout.HelpBox("选择动作或动画资源", MessageType.Info);
                return;
            }
            if (Animation != previewAnimation || (action != null && actionRevision != EditorUtility.GetDirtyCount(action)) ||
                (Animation != null && animationRevision != EditorUtility.GetDirtyCount(Animation)) ||
                (action != null && action.rootMotion != null && rootRevision != EditorUtility.GetDirtyCount(action.rootMotion)))
                RefreshPreview();

            if (error != null)
                EditorGUILayout.HelpBox(error, MessageType.Error);

            if (Animation == null || Animation.clip == null || Animation.Duration <= 0f)
            {
                DrawConfiguration();
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(playing ? "暂停" : "播放", GUILayout.Width(60f)))
                {
                    if (time >= Animation.Duration)
                        Seek(0f);
                    playing = !playing;
                    lastUpdate = EditorApplication.timeSinceStartup;
                }

                if (GUILayout.Button("上一帧", GUILayout.Width(65f)))
                    Seek(time - 1f / Animation.clip.frameRate);
                if (GUILayout.Button("下一帧", GUILayout.Width(65f)))
                    Seek(time + 1f / Animation.clip.frameRate);

                float value = EditorGUILayout.Slider(time, 0f, Animation.Duration);
                if (value != time)
                    Seek(value);
            }

            Rect previewRect = GUILayoutUtility.GetRect(100f, Mathf.Max(200f, position.height * 0.43f), GUILayout.ExpandWidth(true));
            previewArea = previewRect;
            if (preview != null && action != null && Animation.windowEvents.OfType<MotionWarpWindow>().Any())
                HandleTargetDrag(previewRect);
            if (Event.current.type == EventType.Repaint && preview != null)
            {
                preview.Draw(previewRect);
                if (action != null)
                {
                    DrawRootPaths(previewRect);
                    DrawHitShapes(previewRect);
                }
            }

            var animationData = new SerializedObject(Animation);
            timeline.Bind(animationData);
            timeline.DrawToolbar();
            Rect timelineRect = GUILayoutUtility.GetRect(100f, timeline.Height, GUILayout.ExpandWidth(true));
            timeline.Draw(timelineRect, time, Seek);
            EditorGUILayout.LabelField($"{time:0.###} s   /   {time * Animation.clip.frameRate:0.##} 帧");
            detailsScroll = EditorGUILayout.BeginScrollView(detailsScroll);
            timeline.DrawSelection(preview != null ? preview.Hitboxes : System.Array.Empty<Hitbox>());
            DrawConfiguration();
            AnimationDefinitionEditor.DrawWindowHints(animationData);
            EditorGUILayout.EndScrollView();
            if (GUI.changed)
                Repaint();
        }

        private void DrawConfiguration()
        {
            EditorGUI.BeginChangeCheck();
            if (Animation != null && Animation.clip != null)
            {
                int fps = Mathf.Clamp(Mathf.RoundToInt(1f / previewStep), 1, MaxPreviewFps);
                EditorGUI.BeginChangeCheck();
                GUI.SetNextControlName(nameof(previewStep));
                fps = EditorGUILayout.DelayedIntField("预览战斗帧率（FPS）", fps);
                if (EditorGUI.EndChangeCheck())
                    previewStep = 1f / Mathf.Clamp(fps, 1, MaxPreviewFps);
            }
            if (action != null && action.rootMotion != null)
            {
                simulatedTarget = EditorGUILayout.Vector3Field("模拟目标（拖动预览圆点）", simulatedTarget);
                simulatedTargetRadius = Mathf.Max(0f, EditorGUILayout.FloatField("模拟目标半径", simulatedTargetRadius));
            }
            if (EditorGUI.EndChangeCheck())
                RefreshPreview();
            if (action != null && action.rootMotion != null && GUILayout.Button("更新根轨迹烘焙"))
            {
                RootMotionBaker.Bake(action.rootMotion);
                RefreshPreview();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                showConfiguration = EditorGUILayout.Foldout(showConfiguration || Animation == null || Animation.clip == null, "资源配置", true);
                if (GUILayout.Button("保存", GUILayout.Width(100f)))
                {
                    if (action != null)
                        AssetDatabase.SaveAssetIfDirty(action);
                    if (Animation != null)
                        AssetDatabase.SaveAssetIfDirty(Animation);
                }
                if (GUILayout.Button("重载", GUILayout.Width(60f)))
                {
                    if (action != null)
                        AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(action));
                    if (Animation != null)
                        AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(Animation));
                    ReleasePreview();
                    RefreshPreview();
                }
            }

            if (!showConfiguration)
                return;

            if (action != null)
            {
                var data = new SerializedObject(action);
                SerializedProperty property = data.GetIterator();
                property.NextVisible(true);
                while (property.NextVisible(false))
                    EditorGUILayout.PropertyField(property, true);
                data.ApplyModifiedProperties();
            }
            if (Animation != null)
            {
                EditorGUILayout.LabelField("共享动画配置 · " + Animation.name, EditorStyles.boldLabel);
                AnimationDefinitionEditor.DrawConfiguration(new SerializedObject(Animation), ref notifyType, ref windowType);
            }
        }

        private void DrawHitShapes(Rect rect)
        {
            Handles.BeginGUI();
            Handles.color = new Color(.5f, .6f, .65f, .7f);
            foreach (Hitbox hitbox in preview.Hitboxes)
                DrawHitbox(rect, hitbox, hitbox.EndPose);
            foreach (HitWindow window in Animation.windowEvents.OfType<HitWindow>())
            {
                float start = Mathf.Max(preview.PreviousTime, window.start);
                float end = Mathf.Min(time, window.end);
                if (start >= end)
                    continue;
                foreach (Hitbox hitbox in preview.Hitboxes)
                {
                    if (System.Array.IndexOf(window.hitboxIds, hitbox.Id) < 0)
                        continue;
                    float span = time - preview.PreviousTime;
                    HitboxPose from = HitboxPose.Lerp(hitbox.StartPose, hitbox.EndPose, (start - preview.PreviousTime) / span);
                    HitboxPose to = HitboxPose.Lerp(hitbox.StartPose, hitbox.EndPose, (end - preview.PreviousTime) / span);
                    int segments = hitbox.GetSweepSegments(from, to);
                    for (int sample = 0; sample <= segments; sample++)
                    {
                        Handles.color = new Color(1f, .4f, .2f, sample == segments ? 1f : .3f);
                        DrawHitbox(rect, hitbox, HitboxPose.Lerp(from, to, sample / (float)segments));
                    }
                }
            }
            GUI.Label(new Rect(rect.x + 5f, rect.yMax - 25f, rect.width, 25f), "灰：武器形状  橙：有效窗口内的近似扫掠（单动画预览）");
            Handles.EndGUI();
        }

        private void DrawHitbox(Rect rect, Hitbox hitbox, HitboxPose pose)
        {
            hitbox.GetGeometry(pose, out Vector3 pointA, out Vector3 pointB, out float radius);
            Vector3 direction = (pointB - pointA).normalized;
            Vector3 side = Vector3.Cross(direction, Mathf.Abs(direction.y) < .9f ? Vector3.up : Vector3.right).normalized * radius;
            Vector3 up = Vector3.Cross(direction, side);
            if (hitbox.shape == HitboxShape.Capsule)
            {
                Handles.DrawLine(Project(rect, pointA + side), Project(rect, pointB + side));
                Handles.DrawLine(Project(rect, pointA - side), Project(rect, pointB - side));
                Handles.DrawLine(Project(rect, pointA + up), Project(rect, pointB + up));
                Handles.DrawLine(Project(rect, pointA - up), Project(rect, pointB - up));
            }
            int ends = hitbox.shape == HitboxShape.Capsule ? 2 : 1;
            for (int end = 0; end < ends; end++)
            {
                Vector3 center = end == 0 ? pointA : pointB;
                for (int axis = 0; axis < 3; axis++)
                {
                    var points = new Vector3[25];
                    for (int index = 0; index < points.Length; index++)
                    {
                        float angle = index * Mathf.PI * 2f / (points.Length - 1);
                        Vector3 offset = axis switch
                        {
                            0 => new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f),
                            1 => new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)),
                            _ => new Vector3(0f, Mathf.Cos(angle), Mathf.Sin(angle))
                        };
                        points[index] = Project(rect, center + offset * radius);
                    }
                    Handles.DrawAAPolyLine(1.5f, points);
                }
            }
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
                    points[point] = Project(rect, paths[index][point].position);
                Handles.color = index == 0 ? Color.gray : Color.cyan;
                Handles.DrawAAPolyLine(2f, points);
            }

            var circle = new Vector3[49];
            float gap = Animation.windowEvents.OfType<MotionWarpWindow>().Any() ? Animation.windowEvents.OfType<MotionWarpWindow>().First().stopGap : 0f;
            for (int index = 0; index < circle.Length; index++)
            {
                float angle = index * Mathf.PI * 2f / (circle.Length - 1);
                circle[index] = Project(rect, simulatedTarget + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * (preview.BodyRadius + preview.TargetRadius + gap));
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
