using System.Collections;
using Endfield.Controller;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Endfield.Input.Tests
{
    public sealed class CombatInputReaderTests
    {
        private CombatInputReader reader;
        private Mouse mouse;
        private Keyboard keyboard;

        [UnitySetUp]
        public IEnumerator Load()
        {
            yield return new EnterPlayMode();
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/CombatDemo.unity",
                new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            GameObject.Find("Characters/PlayerRoot").GetComponent<ThirdPersonController>().enabled = false;
            mouse = InputSystem.AddDevice<Mouse>();
            keyboard = InputSystem.AddDevice<Keyboard>();
            reader = new CombatInputReader(AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/Input/Combat.inputactions"));
            reader.Enable();
        }

        [UnityTearDown]
        public IEnumerator Leave()
        {
            reader?.Dispose();
            if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            yield return new ExitPlayMode();
        }

        private void SendButtons(MouseState state, params Key[] keys)
        {
            InputSystem.QueueStateEvent(mouse, state);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
            InputSystem.Update();
        }

        private CombatInputCommand Read()
        {
            reader.Sample();
            return reader.ConsumeCommands();
        }

        [UnityTest]
        public IEnumerator TapAndHoldProduceDistinctOneShotRequests()
        {
            var pressed = new MouseState().WithButton(MouseButton.Left);
            SendButtons(pressed);
            SendButtons(default);
            Assert.That(Read(), Is.EqualTo(CombatInputCommand.Attack), "两次采样之间短按也应收到普攻");
            Assert.That(reader.ConsumeCommands(), Is.EqualTo(CombatInputCommand.None));

            SendButtons(pressed);
            Assert.That(Read(), Is.EqualTo(CombatInputCommand.Attack));
            yield return new WaitForSecondsRealtime(.50f);
            Assert.That(Read(), Is.EqualTo(CombatInputCommand.HeavyAttack), "到时仍未松开");
            yield return new WaitForSecondsRealtime(.45f);
            Assert.That(Read(), Is.EqualTo(CombatInputCommand.None));
            SendButtons(default);
            Assert.That(Read(), Is.EqualTo(CombatInputCommand.None));

            // 同一次采样前收到按下、到时和闪避，保留每种请求交给动作层仲裁
            SendButtons(pressed);
            yield return new WaitForSecondsRealtime(.50f);
            SendButtons(pressed.WithButton(MouseButton.Right));
            Assert.That(Read(), Is.EqualTo(CombatInputCommand.Attack | CombatInputCommand.HeavyAttack | CombatInputCommand.Dodge));
            Assert.That(reader.ConsumeCommands(), Is.EqualTo(CombatInputCommand.None));
        }

        [UnityTest]
        public IEnumerator ReleasingCaptureOrDisablingCancelsTheWholePress()
        {
            var left = new MouseState().WithButton(MouseButton.Left);
            SendButtons(left, Key.Escape);
            Assert.That(Read(), Is.EqualTo(CombatInputCommand.None));
            Assert.That(reader.Captured, Is.False);
            SendButtons(default);
            SendButtons(left);
            Assert.That(Read(), Is.EqualTo(CombatInputCommand.None));
            Assert.That(reader.Captured, Is.True);
            yield return new WaitForSecondsRealtime(.50f);
            Assert.That(Read(), Is.EqualTo(CombatInputCommand.None), "捕获用的点击不能变成重击");

            SendButtons(default);
            SendButtons(left, Key.W, Key.LeftShift);
            Assert.That(Read(), Is.EqualTo(CombatInputCommand.Attack));
            Assert.That(reader.Move.y, Is.EqualTo(1));
            Assert.That(reader.SprintHeld, Is.True);
            reader.ReleaseCapture();
            Assert.That(reader.Move, Is.EqualTo(Vector2.zero));
            Assert.That(reader.SprintHeld, Is.False);
            reader.Disable();
            reader.Enable();
            yield return new WaitForSecondsRealtime(.50f);
            Assert.That(Read(), Is.EqualTo(CombatInputCommand.None));
            SendButtons(default);
            SendButtons(left.WithButton(MouseButton.Right));
            Assert.That(Read(), Is.EqualTo(CombatInputCommand.Attack | CombatInputCommand.Dodge));
            reader.CancelAttackHold();
            yield return new WaitForSecondsRealtime(.50f);
            Assert.That(Read(), Is.EqualTo(CombatInputCommand.None));
        }
    }
}
