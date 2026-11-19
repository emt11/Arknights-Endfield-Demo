using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Linq;
using Endfield.AnimationSystem;
using Endfield.Combat;
using Endfield.EditorCombat;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Endfield.EditorValidation
{
    public static class CombatTimelineValidation
    {
        public static string Run()
        {
            string path = AssetDatabase.GenerateUniqueAssetPath("Assets/ScriptObjects/Tests/Editor/TimelineValidationTemp.asset");
            var source = AssetDatabase.LoadAssetAtPath<ActionDefinition>("Assets/ScriptObjects/Combat/Actions/GreatSwordAttack1.asset");
            ActionDefinition action = AssetDatabase.LoadAssetAtPath<ActionDefinition>(path);
            if (action == null)
            {
                action = UnityEngine.Object.Instantiate(source);
                action.animation = UnityEngine.Object.Instantiate(source.animation);
                AssetDatabase.CreateAsset(action, path);
                AssetDatabase.AddObjectToAsset(action.animation, action);
                EditorUtility.SetDirty(action);
                AssetDatabase.SaveAssetIfDirty(action);
            }

            action.animation.pointEvents = new[] { new ComboAdvancePoint { time = .3f } };
            action.animation.windowEvents = new[] { new HitWindow { start = .5f, end = 1.5f } };
            Hitbox[] hitboxes = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Weapons/GreatSword.prefab").GetComponentsInChildren<Hitbox>();
            var data = new SerializedObject(action);
            var view = new ActionTimelineView();
            var animationData = new SerializedObject(action.animation);
            view.Bind(animationData);
            var window = ScriptableObject.CreateInstance<EditorWindow>();
            window.titleContent = new GUIContent("Timeline verification");
            Event pending = null;
            Vector2 injectedPosition = Vector2.zero;
            string trace = "";
            float cursor = 0f;
            Rect selector = default;
            bool drawEndButton = false;
            FieldInfo selectedItem = typeof(ActionTimelineView).GetField("selectedIndex", BindingFlags.Instance | BindingFlags.NonPublic);
            var container = new IMGUIContainer(() =>
            {
                if (pending != null)
                {
                    Event.current.mousePosition = injectedPosition;
                    Event.current.button = 0;
                }

                data.Update();
                animationData.Update();
                if (drawEndButton)
                {
                    AnimationDefinitionEditor.DrawWindowHints(animationData);
                    if (Event.current.type == EventType.Repaint)
                        selector = GUILayoutUtility.GetLastRect();
                    return;
                }
                if (pending != null)
                    trace += Event.current.type + " " + Event.current.mousePosition + " hot=" + GUIUtility.hotControl;
                view.Draw(new Rect(0f, 0f, 800f, view.Height), cursor, value => cursor = value);
                if (pending != null)
                    trace += " -> " + Event.current.type + " hot=" + GUIUtility.hotControl + " start=" + action.animation.windowEvents.OfType<HitWindow>().FirstOrDefault()?.start + "; ";
                GUILayout.Space(210f);
                if (pending != null)
                    Event.current.mousePosition = injectedPosition;
                view.DrawSelection(hitboxes);
                if ((int)selectedItem.GetValue(view) >= 0 && Event.current.type == EventType.Repaint)
                    selector = GUILayoutUtility.GetLastRect();
            });
            container.style.width = 800f;
            container.style.height = 400f;
            window.rootVisualElement.Add(container);
            window.ShowUtility();
            window.position = new Rect(150f, 100f, 800f, 400f);
            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            object parent = typeof(EditorWindow).GetField("m_Parent", Flags).GetValue(window);
            typeof(EditorWindow).Assembly.GetType("UnityEditor.GUIView").GetMethod("RepaintImmediately", Flags).Invoke(parent, null);
            MethodInfo handle = typeof(IMGUIContainer).GetMethod("HandleIMGUIEvent", Flags, null, new[] { typeof(Event), typeof(bool) }, null);
            Action<Event> send = value =>
            {
                pending = value;
                injectedPosition = value.mousePosition;
                handle.Invoke(container, new object[] { value, false });
                pending = null;
            };
            Func<float, float> x = time => 100f + time / action.Duration * 690f;
            Action<float, float, TimelineTrack> drag = (from, to, track) =>
            {
                float y = ((int)track + .5f) * ActionTimelineView.RowHeight;
                send(new Event { type = EventType.MouseDown, button = 0, mousePosition = new Vector2(x(from), y) });
                send(new Event { type = EventType.MouseDrag, button = 0, mousePosition = new Vector2(x(to), y) });
                send(new Event { type = EventType.MouseUp, button = 0, mousePosition = new Vector2(x(to), y) });
                Undo.FlushUndoRecordObjects();
                data.Update();
            };
            var checks = new List<string>();
            Action<bool, string> check = (passed, label) =>
            {
                if (!passed)
                    throw new InvalidOperationException(label);
                checks.Add(label);
            };
            try
            {
                drag(.5f, .7f, TimelineTrack.Hit);
                check(Mathf.Abs(action.animation.windowEvents.OfType<HitWindow>().First().start - .7f) < .001f, "start handle and snap");
                Undo.PerformUndo();
                check(Mathf.Abs(action.animation.windowEvents.OfType<HitWindow>().First().start - .5f) < .001f, "one Undo restores drag");
                Undo.PerformRedo();
                check(Mathf.Abs(action.animation.windowEvents.OfType<HitWindow>().First().start - .7f) < .001f, "Redo restores drag");
                drag(1.5f, 1.8f, TimelineTrack.Hit);
                check(Mathf.Abs(action.animation.windowEvents.OfType<HitWindow>().First().end - 1.8f) < .001f, "end handle");
                drag(1.2f, 1.5f, TimelineTrack.Hit);
                check(Mathf.Abs(action.animation.windowEvents.OfType<HitWindow>().First().start - 1f) < .001f && Mathf.Abs(action.animation.windowEvents.OfType<HitWindow>().First().end - 2.1f) < .001f, "whole window preserves duration");
                drag(1.4f, 1.7f, TimelineTrack.ComboAdvance);
                check(Mathf.Abs(cursor - 1.7f) < .001f && (int)selectedItem.GetValue(view) == -1, "drag on another track's empty space seeks");
                Undo.PerformUndo();
                check(Mathf.Abs(action.animation.windowEvents[0].start - .7f) < .001f, "empty drag leaves the preceding edit undoable");
                Undo.PerformRedo();
                drag(.3f, .77f, TimelineTrack.ComboAdvance);
                float snapped = Mathf.Round(.77f * action.animation.clip.frameRate) / action.animation.clip.frameRate;
                check(Mathf.Abs(action.animation.pointEvents[0].time - snapped) < .001f, "point snap");
                view.snap = false;
                drag(snapped, .76321f, TimelineTrack.ComboAdvance);
                check(Mathf.Abs(action.animation.pointEvents[0].time - .76321f) < .00001f, "off-frame precision");
                string before = EditorJsonUtility.ToJson(action.animation);
                drag(0f, .375f, TimelineTrack.MainAnimation);
                check(Mathf.Abs(cursor - .375f) < .001f && before == EditorJsonUtility.ToJson(action.animation), "scrub never edits data");
                drag(1.3f, 1.3f, TimelineTrack.Hit);
                window.Repaint();
                typeof(EditorWindow).Assembly.GetType("UnityEditor.GUIView").GetMethod("RepaintImmediately", Flags).Invoke(parent, null);
                Vector2 toggle = new Vector2(8f, selector.center.y);
                send(new Event { type = EventType.MouseDown, button = 0, mousePosition = toggle });
                send(new Event { type = EventType.MouseUp, button = 0, mousePosition = toggle });
                Undo.FlushUndoRecordObjects();
                check(action.animation.windowEvents.OfType<HitWindow>().First().hitboxIds.Length == 1 && action.animation.windowEvents.OfType<HitWindow>().First().hitboxIds[0] == 0, "weapon Hitbox checkbox selects stable ID");
                Undo.PerformUndo();
                check(action.animation.windowEvents.OfType<HitWindow>().First().hitboxIds.Length == 0, "Undo restores Hitbox selection");
                Undo.PerformRedo();
                check(action.animation.windowEvents.OfType<HitWindow>().First().hitboxIds.Length == 1, "Redo restores Hitbox selection");
                AssetDatabase.SaveAssetIfDirty(action);
                AssetDatabase.ImportAsset(path);
                action = AssetDatabase.LoadAssetAtPath<ActionDefinition>(path);
                check(Mathf.Abs(action.animation.pointEvents[0].time - .76321f) < .00001f, "save and reimport");
                check(action.animation.windowEvents.OfType<HitWindow>().First().hitboxIds.Length == 1 && action.animation.windowEvents.OfType<HitWindow>().First().hitboxIds[0] == 0, "save and reimport Hitbox ID");
                action.animation.pointEvents = Array.Empty<TimelinePointEvent>();
                action.animation.windowEvents = Array.Empty<TimelineWindowEvent>();
                animationData.Update();
                view.SelectTrack(TimelineTrack.OtherPoints);
                MethodInfo add = typeof(ActionTimelineView).GetMethod("Add", Flags);
                FieldInfo pointType = typeof(ActionTimelineView).GetField("newEventTypeIndex", Flags);
                for (int type = 0; type < 2; type++)
                {
                    pointType.SetValue(view, type);
                    add.Invoke(view, null);
                }
                var animation = action.animation;
                check(animation.pointEvents.Length == 2 && animation.pointEvents[0] is AudioPoint && animation.pointEvents[1] is EffectPoint, "points stored on the animation");
                var effect = (EffectPoint)animation.pointEvents[1];
                effect.effect = AssetDatabase.LoadAssetAtPath<Endfield.Presentation.ParticleEffectDefinition>("Assets/ScriptObjects/Presentation/HitSpark.asset");
                effect.offset = Vector3.right;
                animationData.Update();
                var entries = animationData.FindProperty("pointEvents");
                check(entries.GetArrayElementAtIndex(0).FindPropertyRelative("rotation") == null &&
                    entries.GetArrayElementAtIndex(1).FindPropertyRelative("audio") == null, "type-specific fields");
                Undo.IncrementCurrentGroup();
                TimelineEventEditor.Duplicate(entries, 1);
                Undo.FlushUndoRecordObjects();
                check(!ReferenceEquals(animation.pointEvents[1], animation.pointEvents[2]) && ((EffectPoint)animation.pointEvents[2]).effect == effect.effect, "duplicate retains asset reference");
                Undo.PerformUndo();
                check(animation.pointEvents.Length == 2, "Undo duplicate");
                Undo.PerformRedo();
                check(animation.pointEvents.Length == 3, "Redo duplicate");
                animationData.Update();
                entries.GetArrayElementAtIndex(2).FindPropertyRelative("offset").vector3Value = Vector3.up;
                animationData.ApplyModifiedProperties();
                TimelineEventEditor.Sort(entries);
                check(((EffectPoint)animation.pointEvents[1]).offset == Vector3.right && ((EffectPoint)animation.pointEvents[2]).offset == Vector3.up, "stable order and independent copies");
                // 普通动画使用同一组轨道编辑函数
                view.Bind(animationData);
                drag(0f, .4f, TimelineTrack.OtherPoints);
                check(Mathf.Abs(animation.pointEvents[(int)selectedItem.GetValue(view)].time - .4f) < .00001f, "standalone drag retains selected point");
                Undo.IncrementCurrentGroup();
                entries.DeleteArrayElementAtIndex(2);
                animationData.ApplyModifiedProperties();
                Undo.FlushUndoRecordObjects();
                check(animation.pointEvents.Length == 2, "delete concrete point");
                Undo.PerformUndo();
                check(animation.pointEvents.Length == 3, "Undo delete");
                view.SelectTrack(TimelineTrack.OtherWindows);
                add.Invoke(view, null);
                var states = animationData.FindProperty("windowEvents");
                states.GetArrayElementAtIndex(0).FindPropertyRelative("effect").objectReferenceValue = ((EffectPoint)animation.pointEvents[1]).effect;
                states.GetArrayElementAtIndex(0).FindPropertyRelative("anchor").intValue = (int)AnimationAnchor.RightHand;
                animationData.ApplyModifiedProperties();
                drag(.2f, .7f, TimelineTrack.OtherWindows);
                check(animation.windowEvents[0] is EffectWindow && Mathf.Abs(animation.windowEvents[0].end - .7f) < .00001f, "standalone window endpoint drag");
                using (var preview = new ActionPreview(animation, source.rootMotion.modelPrefab, 1f / 60f))
                {
                    preview.Sample(.1f);
                    Transform hand = preview.Model.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.RightHand);
                    Vector3 handStart = hand.position;
                    var visuals = preview.Model.gameObject.scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Endfield.Presentation.PooledParticleEffect>(true)).ToArray();
                    check(visuals.Any(visual => visual.gameObject.activeSelf && Vector3.Distance(visual.transform.position, hand.position) < .0001f), "window effect mounts on the preview model's humanoid hand");
                    check(visuals.Any(effectPreview => effectPreview.gameObject.activeSelf && effectPreview.StartSystems.Any(system => system.time > 0f)), "scrub samples configured particles in the preview scene");
                    preview.Sample(.5f);
                    check(Vector3.Distance(handStart, hand.position) > .001f && preview.Hitboxes.Length == 0 && preview.WarpedPath == null, "standalone preview animates a model without combat components");
                }
                AssetDatabase.SaveAssetIfDirty(animation);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                check(action.animation.pointEvents[0] is AudioPoint && ((EffectPoint)action.animation.pointEvents[1]).effect != null, "polymorphism and references survive reimport");
                check(((EffectWindow)action.animation.windowEvents[0]).anchor == AnimationAnchor.RightHand, "typed window anchor survives reimport");
                check(((EffectWindow)action.animation.windowEvents[0]).effect != null && Mathf.Abs(action.animation.windowEvents[0].end - .7f) < .00001f, "state reference and time survive reimport");
                view.Bind(animationData);
                view.SelectTrack(TimelineTrack.MotionWarp);
                check((int)selectedItem.GetValue(view) == -1, "switching tracks clears the old selection");
                action.animation.windowEvents = new[]
                {
                    new MotionWarpWindow { start = .7f, end = .9f, maxSpeed = 4f },
                    new MotionWarpWindow { start = .3f, end = .5f, maxSpeed = 2f },
                    new MotionWarpWindow { start = .3f, end = .6f, maxSpeed = 3f }
                };
                selectedItem.SetValue(view, 0);
                animationData.Update();
                {
                    view.SortTimings();
                    check(action.animation.windowEvents.Cast<MotionWarpWindow>().Select(window => window.maxSpeed).SequenceEqual(new[] { 2f, 3f, 4f }) &&
                        (int)selectedItem.GetValue(view) == 2, "warp windows use stable ordering and retain selection");
                    data.Update();
                    add.Invoke(view, null);
                    var added = (MotionWarpWindow)action.animation.windowEvents[(int)selectedItem.GetValue(view)];
                    var defaults = new MotionWarpWindow();
                    check(added.start == 0f && added.end > 0f && added.maxSpeed == defaults.maxSpeed &&
                        added.correction.Evaluate(1f) == defaults.correction.Evaluate(1f), "new warp uses runtime defaults");
                    view.ClearSelection();
                    check((int)selectedItem.GetValue(view) == -1, "changing assets clears selection");
                }
                action.animation.windowEvents = new[] { new MotionWarpWindow { start = .2f, end = action.Duration + 2f } };
                animationData.Update();
                drag(.8f, 1.1f, TimelineTrack.MotionWarp);
                check(Mathf.Approximately(action.animation.windowEvents[0].start, .2f) && Mathf.Approximately(action.animation.windowEvents[0].end, action.Duration + 2f),
                    $"dragging an overlong window body preserves both endpoints ({action.animation.windowEvents[0].start:R}, {action.animation.windowEvents[0].end:R}, clip {action.Duration:R})");
                drag(.2f, .3f, TimelineTrack.MotionWarp);
                check(Mathf.Abs(action.animation.windowEvents[0].start - .3f) < .001f, "overlong window start remains editable");
                Undo.PerformUndo();
                drawEndButton = true;
                typeof(EditorWindow).Assembly.GetType("UnityEditor.GUIView").GetMethod("RepaintImmediately", Flags).Invoke(parent, null);
                check(action.animation.windowEvents[0].end > action.Duration, "viewing an overlong window preserves its configured end");
                Undo.IncrementCurrentGroup();
                send(new Event { type = EventType.MouseDown, button = 0, mousePosition = selector.center });
                send(new Event { type = EventType.MouseUp, button = 0, mousePosition = selector.center });
                Undo.FlushUndoRecordObjects();
                check(action.animation.windowEvents[0].end == action.Duration && action.animation.windowEvents[0].start == .2f, "align button changes only the window end");
                Undo.PerformUndo();
                check(action.animation.windowEvents[0].end > action.Duration, "undo restores the original end");
                string result = "PASS P1-06: " + checks.Count + " checks: " + string.Join("; ", checks);
                File.WriteAllText("Logs/CombatTimelineValidation.txt", result);
                return result;
            }
            catch (Exception exception)
            {
                File.WriteAllText("Logs/CombatTimelineValidation.txt", "FAIL " + exception);
                throw;
            }
            finally
            {
                window.Close();
                AssetDatabase.DeleteAsset(path);
            }
        }
    }
}
