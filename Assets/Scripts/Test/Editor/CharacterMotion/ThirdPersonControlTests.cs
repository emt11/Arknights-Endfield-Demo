using System.Collections;
using Endfield.CombatSession;
using Endfield.Controller;
using Endfield.Timing;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Endfield.CharacterMotion.Tests
{
    public sealed class ThirdPersonControlTests
    {
        private GameObject player;
        private ThirdPersonController control;
        private ThirdPersonCamera view;
        private BattleClock clock;
        private Keyboard keyboard;
        private Mouse mouse;

        [UnitySetUp]
        public IEnumerator Load()
        {
            yield return new EnterPlayMode();
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/CombatDemo.unity",
                new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            var time = GameObject.Find("Combat Session").GetComponent<BattleTimeDriver>();
            time.enabled = false;
            clock = time.Clock;
            player = GameObject.Find("Characters/PlayerRoot");
            control = player.GetComponent<ThirdPersonController>();
            view = Camera.main.GetComponent<ThirdPersonCamera>();
            keyboard = InputSystem.AddDevice<Keyboard>();
            mouse = InputSystem.AddDevice<Mouse>();
            PositionPlayer(new Vector3(80, 0, 80));
            view.Initialize(player.transform);
        }

        [UnityTearDown]
        public IEnumerator Leave()
        {
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
            yield return new ExitPlayMode();
        }

        private void PositionPlayer(Vector3 point)
        {
            var capsule = player.GetComponent<CharacterController>();
            capsule.enabled = false;
            player.transform.SetPositionAndRotation(point, Quaternion.identity);
            capsule.enabled = true;
            Physics.SyncTransforms();
        }

        private void InputStep(double seconds, MouseState buttons, params Key[] keys)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
            InputSystem.QueueStateEvent(mouse, buttons);
            InputSystem.Update();
            control.SendMessage("Update");
            clock.Advance(seconds);
        }

        [UnityTest]
        public IEnumerator MouseRelativeWalkRunAndDodgeUseActualCollisionLimitedDisplacement()
        {
            var pos = player.transform.position;
            var movement = new MouseState { delta = new Vector2(900, 0) };
            InputStep(.2, movement, Key.W);
            Assert.That(player.transform.position.x - pos.x, Is.EqualTo(.287872f).Within(.01));
            Assert.That(player.transform.position.z, Is.EqualTo(pos.z).Within(.01));
            pos = player.transform.position;
            InputStep(.2, default, Key.W, Key.D, Key.LeftShift);
            Assert.That(Vector3.Distance(player.transform.position, pos), Is.EqualTo(.98222f).Within(.01));

            var driver = player.GetComponent<ActorAnimationBinding>().Driver;
            var click = new MouseState().WithButton(MouseButton.Left);
            InputStep(.12, click);
            Assert.That(driver.CurrentClip.name, Is.EqualTo("GS_Attack1"));
            InputStep(.02, default);
            pos = player.transform.position;
            InputStep(.1, click.WithButton(MouseButton.Right), Key.W);
            Assert.That(driver.IsDodging, Is.True);
            Assert.That(driver.CurrentClip, Is.Null);
            Assert.That(player.transform.position.x - pos.x, Is.EqualTo(1f).Within(.01));
            InputStep(.1, new MouseState { delta = new Vector2(900, 0) });
            Assert.That(player.transform.position.x - pos.x, Is.EqualTo(2f).Within(.01));
            InputStep(1.6, default);
            InputStep(.02, default);
            Assert.That(driver.IsDodging, Is.False);

            var wall = GameObject.Find("Environment/Wall").GetComponent<Collider>().bounds;
            PositionPlayer(new Vector3(wall.center.x, 0, wall.min.z - 1));
            view.Initialize(player.transform);
            InputStep(.2, new MouseState().WithButton(MouseButton.Right));
            Assert.That(player.transform.position.z, Is.InRange(wall.min.z - .7f, wall.min.z - .15f));
            InputStep(2, default);
            InputStep(.02, default);
            pos = player.transform.position;
            InputStep(.2, default, Key.W);
            Assert.That(Vector3.Distance(player.transform.position, pos), Is.LessThan(.015));
            yield break;
        }

        [UnityTest]
        public IEnumerator HoldingAttackUpgradesTheLightClipOnceAndRespectsPause()
        {
            var animation = player.GetComponent<ActorAnimationBinding>().Driver;
            var left = new MouseState().WithButton(MouseButton.Left);
            InputStep(.02, left);
            Assert.That(animation.CurrentClip.name, Is.EqualTo("GS_Attack1"));

            // 放慢动作时间，输入系统仍按真实时间识别长按
            clock.SetBattleRate(.25f);
            GameObject.Find("Combat Session").GetComponent<BattleTimeDriver>().enabled = true;
            yield return new WaitForSecondsRealtime(.55f);
            yield return null;
            Assert.That(animation.CurrentClip.name, Is.EqualTo("GS_Attack2"));
            double startTime = animation.CurrentClipTime;
            yield return new WaitForSecondsRealtime(.45f);
            Assert.That(animation.CurrentClipTime, Is.GreaterThan(startTime + .05));

            InputStep(.02, default);
            animation.Stop(0);
            clock.SetPaused(true);
            InputStep(.02, left);
            yield return new WaitForSecondsRealtime(.55f);
            yield return null;
            clock.SetPaused(false);
            yield return null;
            Assert.That(animation.CurrentClip, Is.Null, "冻结步收到的普通与重击请求应消费并丢弃");
            InputStep(.02, default);
            InputStep(.02, left);
            Assert.That(animation.CurrentClip.name, Is.EqualTo("GS_Attack1"));

            // 同一轮输入更新处理跨度 0.45 秒的按下和松开，右键也在本步到达
            InputStep(.02, default);
            yield return new WaitForSecondsRealtime(.55f);
            double releaseTime = InputState.currentTime;
            InputSystem.QueueStateEvent(mouse, left, releaseTime - .45);
            InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(MouseButton.Right), releaseTime);
            InputSystem.Update();
            control.SendMessage("Update");
            clock.Advance(.02);
            Assert.That(animation.IsDodging, Is.True);
            Assert.That(animation.CurrentClip, Is.Null);
        }

        [UnityTest]
        public IEnumerator CameraRetractsImmediatelyAndRecoversAtTheConfiguredRate()
        {
            var wall = GameObject.Find("Environment/Wall").GetComponent<Collider>().bounds;
            PositionPlayer(new Vector3(wall.center.x, 0, wall.max.z + 2));
            view.Initialize(player.transform);
            view.ApplyLook(new Vector2(0, 150));
            view.SendMessage("LateUpdate");
            Assert.That(view.PivotOverlapping, Is.False);
            Assert.That(view.CurrentDistance, Is.EqualTo(view.SafeDistance).And.InRange(.1f, 2f));
            float near = view.CurrentDistance;
            PositionPlayer(new Vector3(wall.max.x + 8, 0, wall.max.z + 2));
            view.SendMessage("LateUpdate");
            Assert.That(view.SafeDistance, Is.EqualTo(4f));
            Assert.That(view.CurrentDistance, Is.EqualTo(Mathf.Min(4f, near + 4f * Time.unscaledDeltaTime)).Within(.001f));

            PositionPlayer(new Vector3(wall.center.x, 0, wall.center.z));
            view.SendMessage("LateUpdate");
            Assert.That(view.PivotOverlapping, Is.True);
            Assert.That(view.CurrentDistance, Is.Zero);
            view.ApplyLook(new Vector2(0, -4000));
            view.SendMessage("LateUpdate");
            Assert.That(view.transform.eulerAngles.x, Is.EqualTo(70f).Within(.01));
            view.ApplyLook(new Vector2(0, 4000));
            view.SendMessage("LateUpdate");
            Assert.That(Mathf.DeltaAngle(0, view.transform.eulerAngles.x), Is.EqualTo(-50f).Within(.01));
            yield break;
        }
    }
}
