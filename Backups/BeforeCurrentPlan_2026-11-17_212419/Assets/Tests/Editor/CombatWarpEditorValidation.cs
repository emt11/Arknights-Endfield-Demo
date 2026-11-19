using System;
using System.IO;
using System.Reflection;
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
            var action = AssetDatabase.LoadAssetAtPath<ActionDefinition>("Assets/ScriptObjects/Combat/Actions/GreatSwordAttack1.asset");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/Player.prefab");
            RootMotionDefinition root = action.rootMotion;
            int scenes = EditorSceneManager.previewSceneCount;
            if (root.Sample(root.clip.length).position.magnitude < .1f)
                throw new InvalidOperationException("Humanoid bake is empty");
            string signature = root.dependencySignature;
            try
            {
                root.dependencySignature = "stale";
                if (root.dependencySignature == RootMotionBaker.Signature(root))
                    throw new InvalidOperationException("Stale root data was not detected for the editor warning");
            }
            finally
            {
                root.dependencySignature = signature;
            }

            using (var preview = new ActionPreview(action, prefab))
            {
                if (preview.Hitboxes.Length != 1 || preview.Hitboxes[0].transform.parent.parent.name != "WeaponSocket_R")
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
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            object parent = typeof(EditorWindow).GetField("m_Parent", flags).GetValue(window);
            typeof(EditorWindow).Assembly.GetType("UnityEditor.GUIView").GetMethod("RepaintImmediately", flags).Invoke(parent, null);
            VisualElement element = window.rootVisualElement;
            while (element.parent != null)
                element = element.parent;
            IMGUIContainer container = null;
            foreach (VisualElement child in element.Children())
            {
                if (child is IMGUIContainer found)
                    container = found;
            }
            FieldInfo targetField = typeof(ActionTimelineWindow).GetField("simulatedTarget", flags);
            Vector3 before = (Vector3)targetField.GetValue(window);
            MethodInfo project = typeof(ActionTimelineWindow).GetMethod("Project", flags);
            Vector2 point = (Vector3)project.Invoke(window, new object[] { window.PreviewRect, before });
            var draw = (Action)Delegate.CreateDelegate(typeof(Action), window, "OnGUI");
            MethodInfo handle = typeof(IMGUIContainer).GetMethod("HandleIMGUIEvent", flags, null, new[] { typeof(Event), typeof(Action), typeof(bool) }, null);
            Vector2 injected = point;
            Action callback = () =>
            {
                Event.current.mousePosition = injected;
                Event.current.button = 0;
                draw();
            };
            try
            {
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
            const string result = "PASS P1-08 editor: actual Humanoid samples; stale bake detection; deterministic scrub; weapon socket, scale and animated pose; Preview Scene cleanup; actual IMGUI target drag; save/reimport warp settings. Image: Logs/WarpPreview.png";
            File.WriteAllText("Logs/CombatWarpEditorValidation.txt", result);
            return result;
        }
    }
}
