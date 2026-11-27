using System;
using System.Linq;
using System.Reflection;
using Endfield.Animation;
using Endfield.MotionWarping;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Endfield.Editor.Animation
{
    [CustomEditor(typeof(AnimationDefination))]
    public sealed class AnimationDefinationEditor : UnityEditor.Editor
    {
        private Pose[] inspectorPath;
        private AnimationClip inspectedClip;
        private GameObject inspectedModel;

        private GameObject previewModel;

        public override void OnInspectorGUI()
        {
            var trajectory = (AnimationDefination)target;
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script", "pointNotifies", "windowNotifies", "useRootMotion", "useMotionWarping", "warpAsset");
            DrawMotionSettings(serializedObject);
            DrawNotifies(serializedObject, 0, trajectory.LastFrame);
            serializedObject.ApplyModifiedProperties();
            string issue = trajectory.ConfigurationError();
            if (issue != null) EditorGUILayout.HelpBox(issue, MessageType.Error);
            previewModel = (GameObject)EditorGUILayout.ObjectField("轨迹预览模型", previewModel, typeof(GameObject), false);
            if (trajectory.Clip && trajectory.Clip.humanMotion && !trajectory.Clip.isLooping &&
                previewModel && previewModel.GetComponentInChildren<Animator>() is Animator animator && animator.isHuman)
            {
                if (inspectedModel != previewModel || inspectedClip != trajectory.Clip)
                {
                    inspectorPath = SampleClip(trajectory.Clip, previewModel);
                    inspectedClip = trajectory.Clip;
                    inspectedModel = previewModel;
                }
                DrawTrajectory(inspectorPath);
            }
            if (GUILayout.Button("编辑时间线")) AnimationTimelineEditor.Open(trajectory);
        }

        internal static void DrawMotionSettings(SerializedObject data, bool showWarp = true)
        {
            EditorGUILayout.PropertyField(data.FindProperty("useRootMotion"));
            if (!showWarp) return;
            var useWarp = data.FindProperty("useMotionWarping");
            EditorGUILayout.PropertyField(useWarp);
            if (useWarp.boolValue) EditorGUILayout.PropertyField(data.FindProperty("warpAsset"));
        }

        internal static void DrawNotifies(SerializedObject data, int frame, int lastFrame)
        {
            var points = data.FindProperty("pointNotifies");
            var windows = data.FindProperty("windowNotifies");
            if (GUILayout.Button("添加点通知")) AddNotify(points, frame, lastFrame);
            DrawNotifyList(points, "点通知");
            using (new EditorGUI.DisabledScope(lastFrame < 1))
                if (GUILayout.Button("添加窗口通知")) AddNotify(windows, frame, lastFrame);
            DrawNotifyList(windows, "窗口通知");
        }

        private static void DrawNotifyList(SerializedProperty list, string label)
        {
            for (int i = 0; i < list.arraySize; i++)
            {
                var item = list.GetArrayElementAtIndex(i);
                bool hasNotify = item.managedReferenceValue != null;
                using (new EditorGUILayout.HorizontalScope())
                {
                    item.isExpanded = EditorGUILayout.Foldout(item.isExpanded,
                        hasNotify ? NotifyLabel(item, label) : "空条目", true);
                    using (new EditorGUI.DisabledScope(i == 0))
                        if (GUILayout.Button("上移", GUILayout.Width(42))) { list.MoveArrayElement(i, i - 1); break; }
                    using (new EditorGUI.DisabledScope(i + 1 == list.arraySize))
                        if (GUILayout.Button("下移", GUILayout.Width(42))) { list.MoveArrayElement(i, i + 1); break; }
                    if (GUILayout.Button("删除", GUILayout.Width(42))) { list.DeleteArrayElementAtIndex(i); break; }
                }
                if (item.isExpanded && hasNotify)
                    using (new EditorGUI.IndentLevelScope()) DrawNotifyFields(item);
            }
        }

        internal static string NotifyLabel(SerializedProperty item, string label)
        {
            var type = item.managedReferenceValue.GetType();
            string signal = item.FindPropertyRelative("signal").stringValue;
            if (!string.IsNullOrEmpty(signal)) return signal;
            return type == typeof(AnimationPointNotify) || type == typeof(AnimationWindowNotify) ? label : TypeLabel(type);
        }

        internal static void DrawNotifyFields(SerializedProperty item)
        {
            var field = item.Copy();
            var end = field.GetEndProperty();
            for (bool visible = field.NextVisible(true); visible && !SerializedProperty.EqualContents(field, end); visible = field.NextVisible(false))
            {
                string label = field.name switch
                {
                    "signal" => "信号",
                    "pointFrame" => "触发帧",
                    "startFrame" => "起始帧",
                    "endFrame" => "结束帧",
                    _ => field.displayName
                };
                EditorGUILayout.PropertyField(field, new GUIContent(label), true);
            }
        }

        internal static void AddNotify(SerializedProperty list, int frame, int lastFrame, Action<int> added = null)
        {
            bool window = list.name == "windowNotifies";
            int start = Mathf.Clamp(frame, 0, window ? lastFrame - 1 : lastFrame);
            Type baseType = window ? typeof(AnimationWindowNotify) : typeof(AnimationPointNotify);
            var types = TypeCache.GetTypesDerivedFrom(baseType)
                .Where(type => !type.IsAbstract && !type.ContainsGenericParameters &&
                    Attribute.IsDefined(type, typeof(SerializableAttribute), false) &&
                    type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null, Type.EmptyTypes, null) != null)
                .OrderBy(TypeLabel).ToArray();

            object defaultNotify = window ? new AnimationWindowNotify("", start, start + 1) : new AnimationPointNotify("", start);
            if (types.Length == 0)
            {
                int index = AppendNotify(list, defaultNotify, window, start);
                added?.Invoke(index);
                return;
            }

            var asset = list.serializedObject.targetObject;
            string path = list.propertyPath;
            var menu = new GenericMenu();
            void Add(Type type)
            {
                if (!asset) return;
                using var data = new SerializedObject(asset);
                var destination = data.FindProperty(path);
                int index = AppendNotify(destination, type == baseType ? defaultNotify : Activator.CreateInstance(type, nonPublic: true), window, start);
                data.ApplyModifiedProperties();
                added?.Invoke(index);
            }
            menu.AddItem(new GUIContent("信号通知"), false, () => Add(baseType));
            foreach (var type in types) menu.AddItem(new GUIContent(TypeLabel(type)), false, () => Add(type));
            menu.ShowAsContext();
        }

        private static string TypeLabel(Type type) => ObjectNames.NicifyVariableName(type.Name);

        private static int AppendNotify(SerializedProperty list, object entry, bool window, int start)
        {
            int index = list.arraySize;
            list.arraySize = index + 1;
            var item = list.GetArrayElementAtIndex(index);
            item.managedReferenceValue = entry;
            item.FindPropertyRelative(window ? "startFrame" : "pointFrame").intValue = start;
            if (window) item.FindPropertyRelative("endFrame").intValue = start + 1;
            item.isExpanded = true;
            return index;
        }

        private static void DrawTrajectory(Pose[] samples)
        {
            EditorGUILayout.LabelField("水平根轨迹（上方为局部 +Z）", EditorStyles.wordWrappedMiniLabel);
            Rect rect = GUILayoutUtility.GetRect(1, 120, GUILayout.ExpandWidth(true));
            if (Event.current.type != EventType.Repaint) return;
            var points = new Vector3[samples.Length];
            float extent = .001f;
            for (int i = 0; i < points.Length; i++)
            {
                var point = samples[i].position;
                points[i] = new Vector3(point.x, -point.z);
                extent = Mathf.Max(extent, point.magnitude);
            }
            Vector3 origin = rect.center;
            float radius = .45f * Mathf.Min(rect.width, rect.height);
            for (int i = 0; i < points.Length; i++) points[i] = origin + points[i] * (radius / extent);
            EditorGUI.DrawRect(rect, new Color(.16f, .16f, .16f));
            using (new Handles.DrawingScope(Color.cyan)) Handles.DrawAAPolyLine(2, points);
        }

        internal static void Bake(MotionWarpingAsset asset)
        {
            if (!asset.ValidWindows()) throw new InvalidOperationException("请设置片段，以及有序且位于片段内的扭曲窗口");
            var samples = SampleClip(asset.Clip, asset.SampleModel);
            var summary = SummarizeWindows(asset, samples);
            Undo.RecordObject(asset, "烘焙扭曲窗口");
            asset.StoreSummary(summary);
            EditorUtility.SetDirty(asset);
        }

        internal static WarpWindowMotion[] SummarizeWindows(MotionWarpingAsset asset, Pose[] samples)
        {
            var summaries = new WarpWindowMotion[asset.Windows.Count];
            for (int i = 0; i < summaries.Length; i++)
            {
                var window = asset.Windows[i];
                if (window.startFrame < 0 || window.endFrame >= samples.Length || window.endFrame <= window.startFrame)
                    throw new InvalidOperationException("扭曲窗口必须位于片段内且结束帧大于起始帧");
                var end = samples[window.endFrame];
                summaries[i] = new WarpWindowMotion { startFrame = window.startFrame, endFrame = window.endFrame,
                    displacement = end.position - samples[window.startFrame].position, endYaw = end.rotation.eulerAngles.y };
            }
            return summaries;
        }

        public static Pose[] SampleClip(AnimationClip clip, GameObject prefab)
        {
            if (!prefab || !clip || !clip.humanMotion || clip.isLooping)
                throw new InvalidOperationException("需要角色模型和单次 Humanoid 片段");

            var scene = EditorSceneManager.NewPreviewScene();
            PlayableGraph graph = default;
            try
            {
                var root = EditorUtility.IsPersistent(prefab)
                    ? (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene)
                    : UnityEngine.Object.Instantiate(prefab, new InstantiateParameters { scene = scene });
                var animator = root.GetComponentInChildren<Animator>();
                if (!animator || !animator.isHuman)
                    throw new InvalidOperationException($"{prefab.name} 缺少 Humanoid Animator");
                // 预览副本由采样图应用根运动，场景角色继续使用自身的 AnimationSource
                if (animator.TryGetComponent<AnimationSource>(out var playback))
                    UnityEngine.Object.DestroyImmediate(playback);
                animator.runtimeAnimatorController = null;
                animator.applyRootMotion = true;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                Quaternion toLocal = Quaternion.Inverse(root.transform.rotation);
                graph = PlayableGraph.Create("动画根运动采样");
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var node = AnimationClipPlayable.Create(graph, clip);
                AnimationPlayableOutput.Create(graph, clip.name, animator).SetSourcePlayable(node);
                graph.Play();
                graph.Evaluate(0);

                int intervals = Mathf.CeilToInt(clip.length * clip.frameRate - 0.00001f);
                var samples = new Pose[intervals + 1];
                samples[0] = Pose.identity;
                Quaternion startRotation = animator.transform.rotation;
                double previous = 0;
                for (int i = 1; i <= intervals; i++)
                {
                    double time = i == intervals ? clip.length : i / (double)clip.frameRate;
                    graph.Evaluate((float)(time - previous));
                    Vector3 movement = toLocal * animator.deltaPosition;
                    movement.y = 0;
                    var yaw = (Quaternion.Inverse(startRotation) * animator.transform.rotation).eulerAngles.y;
                    samples[i] = new Pose(samples[i - 1].position + movement, Quaternion.Euler(0, yaw, 0));
                    previous = time;
                }
                return samples;
            }
            finally
            {
                if (graph.IsValid()) graph.Destroy();
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
