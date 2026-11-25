using System.Collections;
using Endfield.CharacterControl;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace Endfield.Input.Tests
{
    public sealed class GameInputManagerTests
    {
        private GameInputManager inputManager;
        private Mouse mouse;
        private Keyboard keyboard;
        private GameViewInputScope inputScope;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return new EnterPlayMode();
            inputScope = new GameViewInputScope();
            var local = Object.FindFirstObjectByType<GameInputManager>();
            if (local) local.enabled = false;
            mouse = InputSystem.AddDevice<Mouse>();
            keyboard = InputSystem.AddDevice<Keyboard>();
            inputManager = new GameObject(nameof(GameInputManagerTests)).AddComponent<GameInputManager>();
            Send(default);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Object.Destroy(inputManager.gameObject);
            if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            inputScope?.Dispose();
            yield return new ExitPlayMode();
        }

        private void Send(MouseState state, params Key[] keys)
        {
            InputSystem.QueueStateEvent(mouse, state);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
            InputSystem.Update();
        }

        private InputDataFrame ReadFrame()
        {
            inputManager.SendMessage("Update");
            return inputManager.Frame;
        }

        private CharacterCommand Sample() => ReadFrame().Pressed;

        [UnityTest]
        public IEnumerator ShortAndLongPressEachProduceOnlyOneAttackOnPress()
        {
            var left = new MouseState().WithButton(MouseButton.Left);
            foreach (float duration in new[] { .12f, .80f })
            {
                Send(left);
                Assert.That(Sample(), Is.EqualTo(CharacterCommand.Attack));
                Assert.That(Sample(), Is.EqualTo(CharacterCommand.None));
                yield return new WaitForSecondsRealtime(duration);
                Assert.That(Sample(), Is.EqualTo(CharacterCommand.None));
                Send(default);
                Assert.That(Sample(), Is.EqualTo(CharacterCommand.None));
            }

            Send(new MouseState().WithButton(MouseButton.Left).WithButton(MouseButton.Right));
            Send(default);
            Assert.That(Sample(), Is.EqualTo(CharacterCommand.Attack | CharacterCommand.Dodge));
            Send(left);
            var first = ReadFrame();
            Assert.That(Sample(), Is.EqualTo(CharacterCommand.None));
            Assert.That(first.Pressed, Is.EqualTo(CharacterCommand.Attack), "已返回的输入帧独立于下一次采样");
        }

        [UnityTest]
        public IEnumerator RecaptureConsumesTheClickAndResetDropsPendingPresses()
        {
            var left = new MouseState().WithButton(MouseButton.Left);
            var both = new MouseState().WithButton(MouseButton.Left).WithButton(MouseButton.Right);
            Send(left, Key.Escape);
            Assert.That(Sample(), Is.EqualTo(CharacterCommand.None));
            Assert.That(inputManager.Captured, Is.False);
            Send(default);
            Send(left);
            Assert.That(Sample(), Is.EqualTo(CharacterCommand.None));
            Assert.That(inputManager.Captured, Is.True);
            Send(default);
            Send(left, Key.W, Key.D, Key.LeftShift);
            var frame = ReadFrame();
            Assert.That(frame.Move.magnitude, Is.EqualTo(1f).Within(.001f));
            Assert.That(frame.SprintHeld, Is.True);
            Send(both);
            inputManager.ClearPendingPresses();
            Assert.That(Sample(), Is.EqualTo(CharacterCommand.None));

            inputManager.enabled = false;
            Assert.That(inputManager.Frame, Is.EqualTo(default(InputDataFrame)));
            Assert.That(inputManager.Captured, Is.False);
            inputManager.enabled = true;
            Send(both);
            Assert.That(Sample(), Is.EqualTo(CharacterCommand.None));
            Send(default);
            Send(both);
            Assert.That(Sample(), Is.EqualTo(CharacterCommand.Attack | CharacterCommand.Dodge));
            inputManager.ReleaseCapture();
            Send(default);
            Send(left);
            Send(default);
            Send(left);
            Assert.That(Sample(), Is.EqualTo(CharacterCommand.Attack), "重新捕获后的下一次独立按下可在同次采样前到达");
            yield break;
        }
    }
}
