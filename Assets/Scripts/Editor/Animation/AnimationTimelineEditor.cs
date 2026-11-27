using System;
using Endfield.Actors;
using Endfield.Animation;
using Endfield.MotionWarping;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Endfield.Editor.Animation
{
    public sealed class AnimationTimelineEditor : EditorWindow
    {
        [SerializeField] private AnimationDefination asset;
        [SerializeField] private AnimationPreviewSettings previewSettings = new();
        [SerializeField] private bool dodgePreview;
        private SerializedObject data;
        private SerializedObject windowData;
        private AnimationTimelinePreview preview;
        private Transform sceneRoot;
        private Animator sceneAnimator;
        private string sceneBindingError;
        private MotionWarpingAsset warpAsset;
        private SerializedObject warpData;
        private RuntimeAnimatorController controller, shownController;
        private Vector2 scroll;
        private double time, previousUpdate;
        private bool playing, loop, moveTarget;
        private int selectedWindow, dragIndex = -1, undoGroup;
        private (int Animation, int Warp) observedVersions;
        private string dragTrack, dragField, previewError, selectedTrack;
        private int selectedIndex = -1, grabFrame, originalStart, originalEnd, dragControl;
        private bool showSettings, showTargets;
        private float zoom = 1, viewStart;
        private const float LabelWidth = 110, RowHeight = 24;
        private bool refreshRequested = true;
        private bool checkDependencies;

        [MenuItem("Window/Endfield/Animation Timeline")]
        private static void ShowWindow()
        {
            var window = GetWindow<AnimationTimelineEditor>("动画时间线");
            if (window.dodgePreview)
            {
                window.dodgePreview = false;
                window.DisposePreview();
                window.SetWarpAsset(window.PreviewWarpAsset);
            }
        }

        public static void Open(AnimationDefination selected, GameObject actor = null, bool dodgePreview = false)
        {
            var window = GetWindow<AnimationTimelineEditor>("动画时间线");
            window.Select(selected, dodgePreview);
            if (actor && !EditorUtility.IsPersistent(actor)) window.SetScenePreview(actor);
        }

        private void OnEnable()
        {
            minSize = new Vector2(560, 440);
            previewSettings ??= new AnimationPreviewSettings();
            windowData = new SerializedObject(this);
            EditorApplication.update += UpdatePlayback;
            EditorApplication.projectChanged += OnProjectChanged;
            Undo.undoRedoPerformed += Invalidate;
            EditorApplication.playModeStateChanged += OnPlayMode;
            AssemblyReloadEvents.beforeAssemblyReload += Release;
            EditorSceneManager.sceneSaving += OnSceneSaving;
            EditorSceneManager.sceneClosing += OnSceneClosing;
            SceneView.duringSceneGui += OnSceneGUI;
            Select(asset, dodgePreview);
        }

        private void OnDisable()
        {
            EditorApplication.update -= UpdatePlayback;
            EditorApplication.projectChanged -= OnProjectChanged;
            Undo.undoRedoPerformed -= Invalidate;
            EditorApplication.playModeStateChanged -= OnPlayMode;
            AssemblyReloadEvents.beforeAssemblyReload -= Release;
            EditorSceneManager.sceneSaving -= OnSceneSaving;
            EditorSceneManager.sceneClosing -= OnSceneClosing;
            SceneView.duringSceneGui -= OnSceneGUI;
            EndDrag();
            Release();
            data?.Dispose();
            data = null;
            warpData?.Dispose();
            warpData = null;
            windowData?.Dispose();
            windowData = null;
        }

        private void Release()
        {
            sceneRoot = null;
            sceneAnimator = null;
            sceneBindingError = null;
            DisposePreview();
        }

        private void DisposePreview()
        {
            playing = false;
            bool redrawScene = preview != null;
            preview?.Dispose();
            preview = null;
            if (redrawScene) SceneView.RepaintAll();
        }

        private void OnPlayMode(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode) Release();
            if (state == PlayModeStateChange.EnteredEditMode) Invalidate();
        }

        private void Invalidate()
        {
            refreshRequested = true;
            Repaint();
        }

        private void OnProjectChanged()
        {
            checkDependencies = true;
            Invalidate();
        }

        private void OnSceneSaving(Scene scene, string path) => ReleaseForScene(scene);

        private void OnSceneClosing(Scene scene, bool removingScene) => ReleaseForScene(scene);

        private void ReleaseForScene(Scene scene)
        {
            if (!sceneRoot || sceneRoot.gameObject.scene != scene) return;
            Release();
            Invalidate();
        }

        private void OnSceneGUI(SceneView view)
        {
            if (preview?.ScenePreviewActive != true) return;
            if (preview.DrawScene(dodgePreview ? -1 : selectedWindow, !dodgePreview && moveTarget))
            {
                preview.RebuildTrajectory();
                SampleScenePreview();
                Repaint();
            }
        }

        private void SetWarpAsset(MotionWarpingAsset value)
        {
            EndDrag();
            selectedTrack = null;
            warpData?.Dispose();
            warpAsset = value;
            warpData = value ? new SerializedObject(value) : null;
            Invalidate();
        }

        private MotionWarpingAsset PreviewWarpAsset => !dodgePreview && asset ? asset.WarpAsset : null;

        private SerializedProperty Track(string path) =>
            path == "warpWindows" ? warpData?.FindProperty(path) : data.FindProperty(path);

        private void Select(AnimationDefination selected, bool asDodge = false)
        {
            EndDrag();
            Release();
            data?.Dispose();
            asset = selected;
            dodgePreview = asDodge;
            SetWarpAsset(PreviewWarpAsset);
            data = asset ? new SerializedObject(asset) : null;
            time = 0;
            zoom = 1;
            viewStart = 0;
            selectedIndex = -1;
            selectedTrack = null;
            selectedWindow = 0;
            controller = null;
            Invalidate();
        }

        private void UpdatePlayback()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (preview != null && (!asset || !preview.ScenePreviewActive))
            {
                Release();
                Invalidate();
            }
            if (!asset) return;
            RefreshPreview();
            if (playing && asset.Clip)
            {
                double now = EditorApplication.timeSinceStartup;
                if (previewSettings.ValidSpeed) time += (now - previousUpdate) * previewSettings.speed;
                previousUpdate = now;
                if (time >= asset.Duration)
                {
                    if (loop && asset.Duration > 0) time %= asset.Duration;
                    else { time = asset.Duration; playing = false; }
                }
                Repaint();
            }
            SampleScenePreview();
        }

        private void SampleScenePreview()
        {
            if (preview?.ScenePreviewActive != true) return;
            try
            {
                if (preview.Seek(time)) SceneView.RepaintAll();
            }
            catch (Exception error)
            {
                Release();
                sceneBindingError = error.Message;
                Repaint();
            }
        }

        private void RefreshPreview()
        {
            if (warpAsset != PreviewWarpAsset) SetWarpAsset(PreviewWarpAsset);
            var current = (EditorUtility.GetDirtyCount(asset), warpAsset ? EditorUtility.GetDirtyCount(warpAsset) : 0);
            if (!refreshRequested && current == observedVersions && shownController == controller) return;
            try
            {
                if (sceneRoot && asset.Clip)
                {
                    if (preview == null)
                        preview = new AnimationTimelinePreview(asset, controller, sceneAnimator, sceneRoot, previewSettings, dodgePreview);
                    else
                        preview.Refresh(controller, checkDependencies);
                }
                else if (preview != null) DisposePreview();
                time = Math.Min(time, asset.Duration);
                previewError = null;
            }
            catch (Exception error)
            {
                DisposePreview();
                previewError = error.Message;
            }
            observedVersions = current;
            shownController = controller;
            refreshRequested = false;
            checkDependencies = false;
            Repaint();
        }

        private void OnGUI()
        {
            var selected = (AnimationDefination)EditorGUILayout.ObjectField("动画资产", asset, typeof(AnimationDefination), false);
            if (selected != asset) Select(selected);
            if (!asset || data == null || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (warpAsset != PreviewWarpAsset) SetWarpAsset(PreviewWarpAsset);
            data.Update();
            warpData?.Update();
            if (asset.Clip) DrawSceneControls();
            RefreshPreview();
            if (sceneBindingError != null) EditorGUILayout.HelpBox(sceneBindingError, MessageType.Warning);
            if (preview != null && asset.Clip)
            {
                int count = preview.Targets.Length;
                selectedWindow = Mathf.Clamp(selectedWindow, 0, Mathf.Max(0, count - 1));
                EditorGUILayout.LabelField($"Scene 预览中    根 XZ  {preview.Position.x:F3}，{preview.Position.z:F3} m", EditorStyles.miniLabel);
            }
            if (asset.Clip) DrawTransport();
            scroll = EditorGUILayout.BeginScrollView(scroll);
            if (asset.Clip) DrawTimeline();
            if (asset.Clip)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("在播放头添加", GUILayout.Width(94));
                    if (GUILayout.Button("点通知", GUILayout.Width(72)))
                        AddAtCursor("pointNotifies");
                    using (new EditorGUI.DisabledScope(CurrentFrame >= asset.LastFrame))
                        if (GUILayout.Button("窗口通知", GUILayout.Width(82)))
                            AddAtCursor("windowNotifies");
                }
            }
            DrawSelection();
            showSettings = EditorGUILayout.Foldout(showSettings || !asset.Clip, "片段与通知配置", true);
            if (showSettings)
            {
                EditorGUILayout.PropertyField(data.FindProperty("clip"));
                AnimationDefinationEditor.DrawMotionSettings(data, showWarp: !dodgePreview);
                windowData.Update();
                EditorGUILayout.PropertyField(windowData.FindProperty("previewSettings"), new GUIContent("预览设置"), true);
                windowData.ApplyModifiedProperties();
                controller = (RuntimeAnimatorController)EditorGUILayout.ObjectField("预览步态控制器", controller, typeof(RuntimeAnimatorController), false);
                if (preview != null && !preview.UsesController) EditorGUILayout.LabelField("基础姿态：模型参考姿态");
                AnimationDefinationEditor.DrawNotifies(data, asset.Clip ? CurrentFrame : 0, asset.LastFrame);
                if (warpAsset) EditorGUILayout.PropertyField(Track("warpWindows"), new GUIContent("扭曲窗口配置"), true);
            }
            if (!dodgePreview && preview != null && preview.Targets.Length > 0 && preview.HasPreviewTrajectory)
            {
                showTargets = EditorGUILayout.Foldout(showTargets, "预览扭曲目标", true);
                if (showTargets)
                {
                    selectedWindow = EditorGUILayout.IntSlider("目标序号", selectedWindow, 0, preview.Targets.Length - 1);
                    moveTarget = EditorGUILayout.Toggle("在 Scene 拖动目标", moveTarget);
                    var target = preview.Targets[selectedWindow];
                    EditorGUI.BeginChangeCheck();
                    Vector2 point = EditorGUILayout.Vector2Field("测试目标 XZ（米）", new Vector2(target.position.x, target.position.z));
                    float yaw = EditorGUILayout.FloatField("测试目标偏航（度）", target.rotation.eulerAngles.y);
                    if (EditorGUI.EndChangeCheck() && (!point.Equals(new Vector2(target.position.x, target.position.z)) ||
                        yaw != target.rotation.eulerAngles.y))
                    {
                        preview.Targets[selectedWindow] = new Pose(new Vector3(point.x, 0, point.y), Quaternion.Euler(0, yaw, 0));
                        preview.RebuildTrajectory();
                        Repaint();
                    }
                }
            }
            bool edited = data.ApplyModifiedProperties();
            if (warpData?.ApplyModifiedProperties() == true || edited) Invalidate();
            RefreshPreview();
            string error = previewError ?? asset.ConfigurationError() ?? previewSettings.ConfigurationError();
            if (error != null) EditorGUILayout.HelpBox(error, MessageType.Warning);
            else if (preview != null && (asset.UseRootMotion || PreviewWarpAsset) && !preview.HasPreviewTrajectory)
                EditorGUILayout.HelpBox("当前模型无法使用片段根轨迹，只显示姿态", MessageType.Info);
            EditorGUILayout.EndScrollView();
            if (preview != null && GUI.changed) SceneView.RepaintAll();
        }

        private void DrawSceneControls()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                var current = sceneRoot ? sceneRoot.gameObject : null;
                var target = (GameObject)EditorGUILayout.ObjectField("Scene 预览对象", current, typeof(GameObject), true);
                if (target != current) SetScenePreview(target);
                using (new EditorGUI.DisabledScope(!Selection.activeGameObject || EditorUtility.IsPersistent(Selection.activeGameObject)))
                    if (GUILayout.Button("预览选中角色", GUILayout.ExpandWidth(false))) SetScenePreview(Selection.activeGameObject);
                if (current && GUILayout.Button("退出预览", GUILayout.ExpandWidth(false))) SetScenePreview(null);
            }
        }

        private void SetScenePreview(GameObject target)
        {
            if (!target)
            {
                Release();
                Invalidate();
                return;
            }

            var actor = target.GetComponentInParent<Combatant>();
            var animator = actor ? actor.ModelAnimator :
                target.GetComponentInParent<Animator>() ?? target.GetComponentInChildren<Animator>();
            if (animator == sceneAnimator && preview?.ScenePreviewActive == true)
            {
                sceneBindingError = null;
                Repaint();
                return;
            }
            var scene = target.scene;
            if (!scene.IsValid() || !scene.isLoaded || EditorSceneManager.IsPreviewScene(scene) ||
                !animator || !animator.isHuman || !animator.isActiveAndEnabled)
            {
                sceneBindingError = "请选择场景中带有启用的 Humanoid Animator 的角色";
                Repaint();
                return;
            }
            if (AnimationMode.InAnimationMode() && preview?.ScenePreviewActive != true)
            {
                sceneBindingError = "请先结束 Unity 其他窗口的动画预览";
                Repaint();
                return;
            }

            Release();
            sceneRoot = actor ? actor.transform : animator.transform;
            sceneAnimator = animator;
            Invalidate();
        }

        private int CurrentFrame => time >= asset.Duration ? asset.LastFrame : (int)Math.Floor(time * asset.Clip.frameRate + 1e-9);

        private void SeekFrame(int frame)
        {
            playing = false;
            time = asset.FrameTime(Mathf.Clamp(frame, 0, asset.LastFrame));
            Repaint();
        }

        private void DrawTransport()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("上一帧", EditorStyles.toolbarButton)) SeekFrame(CurrentFrame - 1);
                if (GUILayout.Button(playing ? "暂停" : "播放", EditorStyles.toolbarButton))
                {
                    playing = !playing;
                    if (playing && time >= asset.Duration) time = 0;
                    previousUpdate = EditorApplication.timeSinceStartup;
                }
                if (GUILayout.Button("下一帧", EditorStyles.toolbarButton)) SeekFrame(CurrentFrame + 1);
                loop = GUILayout.Toggle(loop, "循环", EditorStyles.toolbarButton);
                GUILayout.FlexibleSpace();
                GUILayout.Label($"{CurrentFrame} / {asset.LastFrame} 帧    {time:F3} s", EditorStyles.miniLabel);
                GUILayout.Space(12);
                GUILayout.Label("缩放", EditorStyles.miniLabel);
                float newZoom = GUILayout.HorizontalSlider(zoom, 1, 8, GUILayout.Width(64));
                if (newZoom != zoom)
                {
                    zoom = newZoom;
                    viewStart = Mathf.Clamp(CurrentFrame - VisibleFrames * .5f, 0, asset.LastFrame - VisibleFrames);
                }
            }
        }

        private float VisibleFrames => Mathf.Max(1, asset.LastFrame / zoom);

        private void DrawTimeline()
        {
            var ruler = GUILayoutUtility.GetRect(100, RowHeight, GUILayout.ExpandWidth(true));
            float left = ruler.x + LabelWidth + 6;
            float width = Mathf.Max(1, ruler.xMax - left - 8);
            float X(float frame) => left + (frame - viewStart) * width / VisibleFrames;
            int At(float x) => Mathf.Clamp(Mathf.RoundToInt(viewStart + (x - left) * VisibleFrames / width), 0, asset.LastFrame);
            EditorGUI.DrawRect(ruler, new Color(.14f, .15f, .17f));
            GUI.Label(new Rect(ruler.x + 5, ruler.y + 3, LabelWidth, 18), "帧", EditorStyles.miniBoldLabel);
            int step = 1;
            while (step * width / VisibleFrames < 38) step = step == 1 ? 5 : step * 2;
            for (int frame = Mathf.CeilToInt(viewStart / step) * step; frame <= viewStart + VisibleFrames; frame += step)
            {
                float x = X(frame);
                EditorGUI.DrawRect(new Rect(x, ruler.yMax - 6, 1, 6), Color.gray);
                GUI.Label(new Rect(x - 4, ruler.y, 32, 18), frame.ToString(), EditorStyles.miniLabel);
            }
            var e = Event.current;
            int control = GUIUtility.GetControlID(FocusType.Passive);
            if (e.type == EventType.MouseDown && e.button == 0 && ruler.Contains(e.mousePosition) && e.mousePosition.x >= left)
            {
                dragControl = GUIUtility.hotControl = control;
                SeekFrame(At(e.mousePosition.x));
                e.Use();
            }

            int rowNumber = 0;
            Rect Row(string label)
            {
                var row = GUILayoutUtility.GetRect(100, RowHeight, GUILayout.ExpandWidth(true));
                EditorGUI.DrawRect(row, (++rowNumber % 2 == 0) ? new Color(.14f, .15f, .17f) : new Color(.18f, .19f, .21f));
                GUI.Label(new Rect(row.x + 5, row.y + 2, LabelWidth - 4, 20), label, EditorStyles.miniLabel);
                return row;
            }
            var bottom = Row("主动画");
            EditorGUI.DrawRect(new Rect(left, bottom.y + 4, width, RowHeight - 8), new Color(.21f, .38f, .53f));
            GUI.Label(new Rect(left + 4, bottom.y + 2, width - 4, 20), asset.Clip.name, EditorStyles.whiteMiniLabel);
            void Lane(string path, string label, bool window, Color color)
            {
                var list = Track(path);
                bool warp = path == "warpWindows";
                bool first = true;
                for (int i = 0; i < list.arraySize; i++)
                {
                    var entry = list.GetArrayElementAtIndex(i);
                    if (!warp && entry.managedReferenceValue == null) continue;
                    bottom = Row(first ? label : "");
                    first = false;
                    string field = window ? "startFrame" : "pointFrame";
                    int start = entry.FindPropertyRelative(field).intValue;
                    int end = window ? entry.FindPropertyRelative("endFrame").intValue : start;
                    float x = X(start), right = X(end);
                    if (right < left || x > left + width) continue;
                    var bar = Rect.MinMaxRect(Mathf.Max(left - 3, x - 3), bottom.y + 3,
                        Mathf.Min(left + width + 3, right + 3), bottom.yMax - 3);
                    bool active = window ? start <= CurrentFrame && CurrentFrame < end : start == CurrentFrame;
                    EditorGUI.DrawRect(bar, active ? Color.Lerp(color, Color.white, .35f) : color);
                    if (selectedTrack == path && selectedIndex == i)
                        EditorGUI.DrawRect(new Rect(bar.x, bar.yMax - 2, bar.width, 2), Color.white);
                    string name = warp ? label : AnimationDefinationEditor.NotifyLabel(entry, label);
                    float textX = window ? bar.x + 4 : bar.xMax + 4;
                    GUI.Label(new Rect(textX, bottom.y + 2, Mathf.Max(0, left + width - textX), 20),
                        window ? $"{name}  {start}–{end}" : $"{name}  {start}", EditorStyles.whiteMiniLabel);
                    if (e.type != EventType.MouseDown || e.button != 0 || !bar.Contains(e.mousePosition)) continue;
                    dragControl = GUIUtility.hotControl = control;
                    dragTrack = selectedTrack = path;
                    dragIndex = selectedIndex = i;
                    if (path == "warpWindows") selectedWindow = i;
                    grabFrame = At(e.mousePosition.x);
                    originalStart = start;
                    originalEnd = end;
                    float edge = Mathf.Min(6, bar.width / 3);
                    dragField = !window ? field : e.mousePosition.x <= x + edge ? "startFrame" :
                        e.mousePosition.x >= right - edge ? "endFrame" : null;
                    Undo.IncrementCurrentGroup();
                    undoGroup = Undo.GetCurrentGroup();
                    Undo.SetCurrentGroupName("移动动画时间线标记");
                    SeekFrame(dragField == "endFrame" ? end : start);
                    e.Use();
                }
                if (first) bottom = Row(label);
            }
            Lane("pointNotifies", "点通知", false, new Color(.92f, .64f, .24f));
            Lane("windowNotifies", "窗口通知", true, new Color(.23f, .59f, .49f));
            if (warpAsset) Lane("warpWindows", "运动扭曲", true, new Color(.58f, .43f, .72f));
            float cursor = X(CurrentFrame);
            if (cursor >= left && cursor <= left + width)
                EditorGUI.DrawRect(new Rect(cursor, ruler.y, 2, bottom.yMax - ruler.y), new Color(1, .8f, .38f));
            if (dragControl == control && GUIUtility.hotControl == control)
            {
                if (e.type == EventType.MouseDrag)
                {
                    int frame = At(e.mousePosition.x);
                    if (dragIndex >= 0)
                    {
                        var list = Track(dragTrack);
                        if (dragIndex >= list.arraySize) { EndDrag(); return; }
                        var item = list.GetArrayElementAtIndex(dragIndex);
                        if (dragField != null)
                        {
                            MoveBoundary(list, dragIndex, dragField, frame, asset.LastFrame);
                            frame = item.FindPropertyRelative(dragField).intValue;
                        }
                        else
                        {
                            int min = 0, max = asset.LastFrame;
                            if (dragTrack == "warpWindows" && dragIndex > 0)
                                min = list.GetArrayElementAtIndex(dragIndex - 1).FindPropertyRelative("endFrame").intValue;
                            if (dragTrack == "warpWindows" && dragIndex + 1 < list.arraySize)
                                max = list.GetArrayElementAtIndex(dragIndex + 1).FindPropertyRelative("startFrame").intValue;
                            int shift = Mathf.Clamp(frame - grabFrame, min - originalStart, max - originalEnd);
                            frame = originalStart + shift;
                            item.FindPropertyRelative("startFrame").intValue = frame;
                            item.FindPropertyRelative("endFrame").intValue = originalEnd + shift;
                        }
                    }
                    SeekFrame(frame);
                    e.Use();
                }
                else if (e.rawType == EventType.MouseUp) { EndDrag(); e.Use(); }
            }
            if (zoom > 1) viewStart = GUILayout.HorizontalScrollbar(viewStart, VisibleFrames, 0, Mathf.Max(1, asset.LastFrame));
        }

        private void AddAtCursor(string track)
        {
            var selectedAsset = asset;
            AnimationDefinationEditor.AddNotify(data.FindProperty(track), CurrentFrame, asset.LastFrame, index =>
            {
                if (asset != selectedAsset) return;
                selectedTrack = track;
                selectedIndex = index;
                playing = false;
                Repaint();
            });
        }

        private void DrawSelection()
        {
            if (selectedTrack == null || selectedIndex < 0) return;
            var list = Track(selectedTrack);
            if (selectedIndex >= list.arraySize) return;
            var entry = list.GetArrayElementAtIndex(selectedIndex);
            bool warp = selectedTrack == "warpWindows";
            if (!warp && entry.managedReferenceValue == null) return;
            EditorGUILayout.LabelField("所选条目", EditorStyles.boldLabel);
            if (!warp) AnimationDefinationEditor.DrawNotifyFields(entry);
            else
            {
                foreach (var field in new[] { "startFrame", "endFrame", "targetOffsetXZ", "alignYaw" })
                    EditorGUILayout.PropertyField(entry.FindPropertyRelative(field));
            }
        }

        internal static void MoveBoundary(SerializedProperty list, int index, string field, int frame, int lastFrame)
        {
            var entry = list.GetArrayElementAtIndex(index);
            bool warp = list.name == "warpWindows";
            bool window = warp || list.name == "windowNotifies";
            int min = 0, max = lastFrame;
            if (window && field == "startFrame")
            {
                max = entry.FindPropertyRelative("endFrame").intValue - 1;
                if (warp && index > 0) min = list.GetArrayElementAtIndex(index - 1).FindPropertyRelative("endFrame").intValue;
            }
            if (window && field == "endFrame")
            {
                min = entry.FindPropertyRelative("startFrame").intValue + 1;
                if (warp && index + 1 < list.arraySize) max = list.GetArrayElementAtIndex(index + 1).FindPropertyRelative("startFrame").intValue;
            }
            if (min <= max) entry.FindPropertyRelative(field).intValue = Mathf.Clamp(frame, min, max);
        }

        private void EndDrag()
        {
            if (dragControl == 0) return;
            if (dragIndex >= 0) Undo.CollapseUndoOperations(undoGroup);
            dragIndex = -1;
            dragTrack = null;
            if (GUIUtility.hotControl == dragControl) GUIUtility.hotControl = 0;
            dragControl = 0;
        }
    }
}
