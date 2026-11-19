using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
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
            const string path = "Assets/Tests/Editor/TimelineFixture.asset";
            var source = AssetDatabase.LoadAssetAtPath<ActionDefinition>("Assets/Game/Combat/Actions/GreatSwordAttack1.asset");
            ActionDefinition action = AssetDatabase.LoadAssetAtPath<ActionDefinition>(path);
            if (action == null)
            {
                action = UnityEngine.Object.Instantiate(source);
                AssetDatabase.CreateAsset(action, path);
            }

            action.points = new[] { new ActionPoint { time = .3f, kind = ActionPointKind.ComboAdvance } };
            action.hitWindows = new[] { new HitWindow { start = .5f, end = 1.5f } };
            var data = new SerializedObject(action);
            var view = new ActionTimelineView();
            var window = ScriptableObject.CreateInstance<EditorWindow>();
            window.titleContent = new GUIContent("Timeline verification");
            Event pending = null;
            Vector2 injectedPosition = Vector2.zero;
            string trace = "";
            float cursor = 0f;
            var container = new IMGUIContainer(() =>
            {
                if (pending != null)
                {
                    Event.current.mousePosition = injectedPosition;
                    Event.current.button = 0;
                }

                data.Update();
                if (pending != null)
                    trace += Event.current.type + " " + Event.current.mousePosition + " hot=" + GUIUtility.hotControl;
                view.Draw(new Rect(0f, 0f, 800f, 140f), data, cursor, value => cursor = value);
                if (pending != null)
                    trace += " -> " + Event.current.type + " hot=" + GUIUtility.hotControl + " start=" + action.hitWindows[0].start + "; ";
            });
            container.style.width = 800f;
            container.style.height = 160f;
            window.rootVisualElement.Add(container);
            window.ShowUtility();
            window.position = new Rect(150f, 100f, 800f, 160f);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            object parent = typeof(EditorWindow).GetField("m_Parent", flags).GetValue(window);
            typeof(EditorWindow).Assembly.GetType("UnityEditor.GUIView").GetMethod("RepaintImmediately", flags).Invoke(parent, null);
            MethodInfo handle = typeof(IMGUIContainer).GetMethod("HandleIMGUIEvent", flags, null, new[] { typeof(Event), typeof(bool) }, null);
            Action<Event> send = value =>
            {
                pending = value;
                injectedPosition = value.mousePosition;
                handle.Invoke(container, new object[] { value, false });
                pending = null;
            };
            Func<float, float> x = time => 100f + time / action.Duration * 690f;
            Action<float, float, float> drag = (from, to, y) =>
            {
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
                drag(.5f, .7f, 68f);
                check(Mathf.Abs(action.hitWindows[0].start - .7f) < .001f, "start handle and snap");
                Undo.PerformUndo();
                check(Mathf.Abs(action.hitWindows[0].start - .5f) < .001f, "one Undo restores drag");
                Undo.PerformRedo();
                check(Mathf.Abs(action.hitWindows[0].start - .7f) < .001f, "Redo restores drag");
                drag(1.5f, 1.8f, 68f);
                check(Mathf.Abs(action.hitWindows[0].end - 1.8f) < .001f, "end handle");
                drag(1.2f, 1.5f, 68f);
                check(Mathf.Abs(action.hitWindows[0].start - 1f) < .001f && Mathf.Abs(action.hitWindows[0].end - 2.1f) < .001f, "whole window preserves duration");
                drag(.3f, .77f, 40f);
                float snapped = Mathf.Round(.77f * action.clip.frameRate) / action.clip.frameRate;
                check(Mathf.Abs(action.points[0].time - snapped) < .001f, "point snap");
                view.snap = false;
                drag(snapped, .76321f, 40f);
                check(Mathf.Abs(action.points[0].time - .76321f) < .00001f, "off-frame precision");
                string before = EditorJsonUtility.ToJson(action);
                drag(0f, .375f, 12f);
                check(Mathf.Abs(cursor - .375f) < .001f && before == EditorJsonUtility.ToJson(action), "scrub never edits data");
                AssetDatabase.SaveAssetIfDirty(action);
                AssetDatabase.ImportAsset(path);
                action = AssetDatabase.LoadAssetAtPath<ActionDefinition>(path);
                check(Mathf.Abs(action.points[0].time - .76321f) < .00001f, "save and reimport");
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
            }
        }
    }
}
