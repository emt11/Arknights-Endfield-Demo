using System.Linq;
using System;
using System.IO;
using System.Reflection;
using Endfield.AnimationSystem;
using Endfield.Combat;
using Endfield.EditorCombat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace Endfield.EditorValidation
{
    public static class CombatWarpEditorValidation
    {
        public static string Run()
        {
            var source = AssetDatabase.LoadAssetAtPath<ActionDefinition>("Assets/ScriptObjects/Combat/Actions/GreatSwordAttack1.asset");
            string path = AssetDatabase.GenerateUniqueAssetPath("Assets/ScriptObjects/Tests/Editor/WarpValidationTemp.asset");
            var action = UnityEngine.Object.Instantiate(source);
            action.animation = UnityEngine.Object.Instantiate(source.animation);
            AssetDatabase.CreateAsset(action, path);
            AssetDatabase.AddObjectToAsset(action.animation, action);
            try
            {
                return Validate(action);
            }
            finally
            {
                AssetDatabase.DeleteAsset(path);
            }
        }

        private static string Validate(ActionDefinition action)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Characters/Player.prefab");
            RootMotionDefinition root = action.rootMotion;
            ValidatePreviewParameters(action, prefab);
            int scenes = EditorSceneManager.previewSceneCount;
            if (root.Sample(root.clip.length).position.magnitude < .1f)
                throw new InvalidOperationException("Humanoid bake is empty");

            using (var preview = new ActionPreview(action, prefab))
            {
                Transform socket = AnimationAnchorBindings.Resolve(preview.Model, AnimationAnchor.Weapon);
                if (preview.Hitboxes.Length != 1 || preview.Hitboxes[0].transform.parent.parent != socket || !socket.IsChildOf(preview.Model))
                    throw new InvalidOperationException("Preview weapon was not mounted on the configured socket");
                Hitbox blade = preview.Hitboxes[0];
                blade.GetGeometry(blade.ReadPose(), out _, out _, out float radius);
                if (Mathf.Abs(radius - .14f) > .001f)
                    throw new InvalidOperationException("Preview and runtime weapon scale differ");
                preview.SetTarget(new Vector3(.5f, 0f, 1.5f));
                preview.Sample(.22f);
                Vector3 sampled = preview.Model.position;
                HitboxPose sampledBlade = blade.ReadPose();
                preview.Sample(.01f);
                if (Quaternion.Angle(sampledBlade.rotation, blade.ReadPose().rotation) < 1f)
                    throw new InvalidOperationException("Preview weapon does not follow the animated bone");
                preview.Sample(.22f);
                if (Vector3.Distance(sampled, preview.Model.position) > .00001f)
                    throw new InvalidOperationException("Scrubbing accumulated warp error");
                if (Vector3.Distance(sampledBlade.position, blade.ReadPose().position) > .00001f)
                    throw new InvalidOperationException("Scrubbing accumulated weapon pose error");
                Texture2D image = preview.Snapshot(600, 450);
                File.WriteAllBytes("Logs/WarpPreview.png", image.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(image);
            }
            if (EditorSceneManager.previewSceneCount != scenes)
                throw new InvalidOperationException("Warp preview leaked a scene");

            ActionTimelineWindow window = EditorWindow.GetWindow<ActionTimelineWindow>();
            window.position = new Rect(100f, 100f, 1000f, 900f);
            window.Select(action);
            window.Show();
            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            object parent = typeof(EditorWindow).GetField("m_Parent", Flags).GetValue(window);
            typeof(EditorWindow).Assembly.GetType("UnityEditor.GUIView").GetMethod("RepaintImmediately", Flags).Invoke(parent, null);
            VisualElement element = window.rootVisualElement;
            while (element.parent != null)
                element = element.parent;
            IMGUIContainer container = null;
            foreach (VisualElement child in element.Children())
            {
                if (child is IMGUIContainer found)
                    container = found;
            }
            FieldInfo targetField = typeof(ActionTimelineWindow).GetField("simulatedTarget", Flags);
            Vector3 before = (Vector3)targetField.GetValue(window);
            MethodInfo project = typeof(ActionTimelineWindow).GetMethod("Project", Flags);
            Vector2 point = (Vector3)project.Invoke(window, new object[] { window.PreviewRect, before });
            var draw = (Action)Delegate.CreateDelegate(typeof(Action), window, "OnGUI");
            MethodInfo handle = typeof(IMGUIContainer).GetMethod("HandleIMGUIEvent", Flags, null, new[] { typeof(Event), typeof(Action), typeof(bool) }, null);
            Vector2 injected = point;
            Action callback = () =>
            {
                Event.current.mousePosition = injected;
                Event.current.button = 0;
                draw();
            };
            try
            {
                FieldInfo previewField = typeof(ActionTimelineWindow).GetField("preview", Flags);
                var preview = (ActionPreview)previewField.GetValue(window);
                RootMotionSample[] path = preview.WarpedPath;
                using var animation = new SerializedObject(action.animation);
                SerializedProperty entries = animation.FindProperty("windowEvents");
                var hitIndex = Array.FindIndex(action.animation.windowEvents, entry => entry is HitWindow);
                var warpIndex = Array.FindIndex(action.animation.windowEvents, entry => entry is MotionWarpWindow);
                entries.GetArrayElementAtIndex(hitIndex).FindPropertyRelative("damageMultiplier").floatValue += .1f;
                animation.ApplyModifiedProperties();
                MethodInfo refresh = typeof(ActionTimelineWindow).GetMethod("RefreshPreview", Flags);
                refresh.Invoke(window, null);
                if (preview != previewField.GetValue(window) || preview.WarpedPath != path)
                    throw new InvalidOperationException("Hit edit rebuilt the preview scene or its trajectory");
                Undo.IncrementCurrentGroup();
                entries.GetArrayElementAtIndex(warpIndex).FindPropertyRelative("maxSpeed").floatValue += 1f;
                animation.ApplyModifiedProperties();
                Undo.FlushUndoRecordObjects();
                refresh.Invoke(window, null);
                if (preview != previewField.GetValue(window) || preview.WarpedPath == path)
                    throw new InvalidOperationException("Warp edit did not refresh only the trajectory");
                path = preview.WarpedPath;
                Undo.PerformUndo();
                if (preview != previewField.GetValue(window) || preview.WarpedPath == path)
                    throw new InvalidOperationException("Undo did not refresh the warp trajectory");
                path = preview.WarpedPath;
                var effect = AssetDatabase.LoadAssetAtPath<Endfield.Presentation.ParticleEffectDefinition>("Assets/ScriptObjects/Presentation/HitSpark.asset");
                action.animation.pointEvents = new[] { new Endfield.AnimationSystem.EffectPoint { effect = effect } };
                EditorUtility.SetDirty(action.animation);
                refresh.Invoke(window, null);
                var visual = preview.Model.gameObject.scene.GetRootGameObjects().SelectMany(obj => obj.GetComponentsInChildren<Endfield.Presentation.PooledParticleEffect>(true)).Single();
                if (preview != previewField.GetValue(window) || preview.WarpedPath != path || !visual.gameObject.activeSelf)
                    throw new InvalidOperationException("Effect edit did not refresh only the particle preview");

                handle.Invoke(container, new object[] { new Event { type = EventType.MouseDown, button = 0 }, callback, false });
                injected += new Vector2(35f, 0f);
                handle.Invoke(container, new object[] { new Event { type = EventType.MouseDrag, button = 0 }, callback, false });
                handle.Invoke(container, new object[] { new Event { type = EventType.MouseUp, button = 0 }, callback, false });
                Vector3 after = (Vector3)targetField.GetValue(window);
                if (Vector3.Distance(before, after) < .01f)
                    throw new InvalidOperationException("Timeline target drag did not change the simulated target");
            }
            finally
            {
                window.Close();
            }

            AssetDatabase.SaveAssetIfDirty(action);
            AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(action));
            const string Result = "PASS P1-08 editor: Humanoid bake and signature, 360-degree range boundaries, preview/runtime trajectories and blade samples at 30/60/120 FPS and 1/1.4x, clip endpoint, deterministic scrub, weapon socket and scale, preview cleanup, IMGUI target drag and save/reimport. Image: Logs/WarpPreview.png";
            File.WriteAllText("Logs/CombatWarpEditorValidation.txt", Result);
            return Result;
        }

        private static void ValidatePreviewParameters(ActionDefinition source, GameObject prefab)
        {
            ActionDefinition action = UnityEngine.Object.Instantiate(source);
            action.animation = UnityEngine.Object.Instantiate(source.animation);
            RootMotionDefinition root = UnityEngine.Object.Instantiate(source.rootMotion);
            try
            {
                string signature = RootMotionBaker.Signature(root);
                RootMotionSample before = root.Sample(1f);
                root.sampleRate = 30f;
                if (root.Sample(1f).position != before.position || root.Sample(1f).rotation != before.rotation)
                    throw new InvalidOperationException("Editing bake inputs changed the stored trajectory");
                if (signature == RootMotionBaker.Signature(root))
                    throw new InvalidOperationException("Sampling rate missing from signature");
                RootMotionBaker.Bake(root);
                if (root.DependencySignature != RootMotionBaker.Signature(root) || root.Sample(-1f).time != 0f || root.Sample(float.MaxValue).time != root.clip.length)
                    throw new InvalidOperationException("Sampling rate rebake did not cover endpoints");
                root.Sample(root.clip.length * .37f);
                root.Sample(root.clip.length);

                foreach (Vector3 direction in new[] { Vector3.forward, Vector3.right, Vector3.back })
                {
                    if (!TargetingController.IsWithinAssistance(direction * 3.5f + Vector3.up * .5f) ||
                        !TargetingController.IsWithinAssistance(direction * 3.5f + Vector3.down * .5f) ||
                        TargetingController.IsWithinAssistance(direction * 3.501f) ||
                        TargetingController.IsWithinAssistance(direction + Vector3.down * .501f))
                        throw new InvalidOperationException("Assistance cylinder boundary at " + direction);
                }
                foreach (MotionWarpWindow window in action.animation.windowEvents.OfType<MotionWarpWindow>())
                {
                    window.maxSpeed = .15f;
                    window.maxYawSpeed = 5f;
                }
                foreach (float rate in new[] { 1f, 1.4f })
                {
                    action.animation.playbackRate = rate;
                    foreach (int fps in new[] { 30, 60, 120 })
                    {
                        foreach (Vector3 target in new[] { Vector3.right * 2f, Vector3.back * 2f, new Vector3(0f, .4f, 3.48f), new Vector3(0f, .51f, 2f) })
                        {
                            using var preview = new ActionPreview(action, prefab, .5f, 1f / fps, target);
                            var runtime = new ActionRuntime(action, 0, null);
                            var warpTarget = new WarpTarget(true, target, .5f, TargetingController.IsWithinAssistance(target));
                            Vector3 position = Vector3.zero;
                            Quaternion rotation = runtime.InitializeOpeningFacing(Quaternion.identity, warpTarget.eligible ? target : Vector3.zero);
                            for (int index = 1; index < preview.WarpedPath.Length; index++)
                            {
                                runtime.Advance(1f / fps);
                                ActionMovement movement = MotionWarping.Evaluate(runtime, position, rotation, prefab.GetComponent<Endfield.Character.Combatant>().BodyRadius, warpTarget, _ => true);
                                position += movement.delta;
                                rotation = movement.rotation;
                                RootMotionSample actual = preview.WarpedPath[index];
                                if (Mathf.Abs(actual.time - runtime.LocalTime) > .0001f || Vector3.Distance(actual.position, position) > .0001f || Quaternion.Angle(actual.rotation, rotation) > .1f)
                                    throw new InvalidOperationException("Preview differs from runtime at rate " + rate + " sample " + index);
                            }
                            if (preview.WarpedPath[^1].time != action.Duration)
                                throw new InvalidOperationException("Preview missed clip end at " + fps + " FPS");
                            preview.Sample(.227f);
                            Hitbox blade = preview.Hitboxes[0];
                            HitboxPose start = blade.StartPose;
                            HitboxPose end = blade.EndPose;
                            if (Mathf.Abs(preview.PreviousTime - (.227f - rate / fps)) > .00001f)
                                throw new InvalidOperationException("Preview sweep interval differs from the configured FPS");
                            Vector3 first = preview.Model.position;
                            preview.Sample(preview.PreviousTime);
                            if (Vector3.Distance(start.position, blade.EndPose.position) > .00001f)
                                throw new InvalidOperationException("Preview sweep starts at the wrong pose");
                            preview.Sample(.227f);
                            if (Vector3.Distance(first, preview.Model.position) > .00001f || Vector3.Distance(end.position, blade.EndPose.position) > .00001f)
                                throw new InvalidOperationException("Rate-aware scrub is not deterministic");
                        }
                    }
                }
                action.openingTurnDuration = 0f;
                using (var preview = new ActionPreview(action, prefab, initialTarget: Vector3.left * 2f))
                {
                    if (Vector3.Angle(preview.WarpedPath[0].rotation * Vector3.forward, Vector3.left) > .01f)
                        throw new InvalidOperationException("Zero-duration preview must start at the attack facing");
                }
                action.useRootMotion = false;
                using (var preview = new ActionPreview(action, prefab))
                {
                    preview.SetTarget(Vector3.forward * 5f);
                    foreach (RootMotionSample sample in preview.WarpedPath)
                        if (sample.position != Vector3.zero || Quaternion.Angle(sample.rotation, Quaternion.identity) > .001f)
                            throw new InvalidOperationException("Root-motion switch did not stop preview movement");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(action.animation);
                UnityEngine.Object.DestroyImmediate(action);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
    }
}
