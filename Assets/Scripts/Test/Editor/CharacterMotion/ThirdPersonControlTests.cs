using System.Collections;
using System.Reflection;
using Endfield.Actors;
using Endfield.CharacterControl;
using Endfield.Controller;
using Endfield.Input;
using Endfield.Input.Tests;
using Endfield.Timing;
using Endfield.UI;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Endfield.CharacterMotion.Tests
{
    public sealed class ThirdPersonControlTests
    {
        private const string ScenePath = "Assets/Scenes/CombatDemo.unity";
        private static readonly MethodInfo SampleInput = typeof(GameInputManager)
            .GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo ApplyInput = typeof(PlayerController)
            .GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
        private PartyMemberControl phoebe, si;
        private PlayerController local;
        private GameInputManager inputManager;
        private ThirdPersonCamera view;
        private BattleClock clock;
        private Mouse mouse;
        private Keyboard keyboard;
        private GameViewInputScope inputScope;

        [UnitySetUp]
        public IEnumerator Load()
        {
            yield return new EnterPlayMode();
            inputScope = new GameViewInputScope();
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            var driver = GameObject.Find("Combat Session").GetComponent<BattleTimeDriver>();
            driver.enabled = false;
            clock = driver.Clock;
            local = driver.GetComponent<PlayerController>();
            inputManager = driver.GetComponent<GameInputManager>();
            phoebe = GameObject.Find("Characters/PlayerRoot").GetComponent<PartyMemberControl>();
            si = GameObject.Find("Characters/SiRoot").GetComponent<PartyMemberControl>();
            view = Camera.main.GetComponent<ThirdPersonCamera>();
            keyboard = InputSystem.AddDevice<Keyboard>();
            mouse = InputSystem.AddDevice<Mouse>();
            yield return null;
            yield return null;
            Place(phoebe, new Vector3(12, 0, 12));
            Place(si, new Vector3(-12, 0, 12));
            view.Initialize(phoebe.transform);
            Sample(default);
        }

        [UnityTearDown]
        public IEnumerator Leave()
        {
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
            inputScope?.Dispose();
            yield return new ExitPlayMode();
        }

        private static void Place(PartyMemberControl member, Vector3 position)
        {
            var capsule = member.GetComponent<CharacterController>();
            capsule.enabled = false;
            member.transform.SetPositionAndRotation(position, Quaternion.identity);
            capsule.enabled = true;
            Physics.SyncTransforms();
        }

        private void Sample(MouseState buttons, params Key[] keys)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
            InputSystem.QueueStateEvent(mouse, buttons);
            InputSystem.Update();
            // SendMessage 会同时调用同一节点上 BattleTimeDriver 的 Update
            SampleInput.Invoke(inputManager, null);
            ApplyInput.Invoke(local, null);
        }

        private void Step(double seconds, MouseState buttons, params Key[] keys)
        {
            Sample(buttons, keys);
            clock.Advance(seconds);
        }

        private static void At(PartyMemberControl member, Vector3 expected)
        {
            Vector3 planar = Vector3.ProjectOnPlane(member.transform.position - expected, Vector3.up);
            Assert.That(planar.magnitude, Is.LessThan(.01f),
                $"{member.name} 实际 {member.transform.position}，预期 {expected}");
        }

        [UnityTest]
        public IEnumerator CharacterPanelOwnsInputUntilClosedByCOrEscape()
        {
            var panel = GameObject.Find("GameUI/CharacterPanelRoot").GetComponent<CharacterStatsPanel>();
            Assert.That(panel.IsOpen, Is.False);
            Assert.That(Cursor.lockState, Is.EqualTo(CursorLockMode.Locked));
            Step(.1, default, Key.W);
            Assert.That(phoebe.CurrentState, Is.SameAs(phoebe.Walk));
            Vector3 start = phoebe.transform.position;
            Quaternion yaw = view.PlanarRotation;
            var both = new MouseState { delta = new Vector2(600, 0) }
                .WithButton(MouseButton.Left).WithButton(MouseButton.Right);

            Step(.1, both, Key.C, Key.W, Key.LeftShift);
            Assert.That(panel.IsOpen && Cursor.visible, Is.True);
            Assert.That(Cursor.lockState, Is.EqualTo(CursorLockMode.None));
            Step(.1, default);
            Step(.1, both, Key.W, Key.LeftShift);
            Assert.That(phoebe.CurrentState, Is.SameAs(phoebe.Idle));
            Assert.That(phoebe.StepCommands, Is.EqualTo(CharacterCommand.None));
            At(phoebe, start);
            Assert.That(Quaternion.Angle(yaw, view.PlanarRotation), Is.LessThan(.01f));

            local.enabled = false;
            local.enabled = true;
            Step(.1, both, Key.W);
            Assert.That(phoebe.CurrentState, Is.SameAs(phoebe.Idle));
            Step(.1, both, Key.Escape, Key.W);
            Assert.That(panel.IsOpen || Cursor.visible, Is.False);
            Assert.That(phoebe.StepCommands, Is.EqualTo(CharacterCommand.None));
            Step(.1, both, Key.W, Key.LeftShift);
            Assert.That(phoebe.CurrentState, Is.SameAs(phoebe.Run));
            Assert.That(phoebe.StepCommands, Is.EqualTo(CharacterCommand.None));
            Step(.1, default);
            Step(.1, default, Key.C);
            Step(.1, default);
            Step(.1, new MouseState().WithButton(MouseButton.Left), Key.C);
            Assert.That(panel.IsOpen, Is.False);
            Assert.That(phoebe.StepCommands, Is.EqualTo(CharacterCommand.None));
            Step(.1, default);
            Step(.1, new MouseState().WithButton(MouseButton.Left));
            Assert.That(phoebe.StepCommands, Is.EqualTo(CharacterCommand.Attack));
            Sample(default);
            Sample(both, Key.W);
            panel.Open();
            clock.Advance(.1);
            Assert.That(phoebe.StepCommands, Is.EqualTo(CharacterCommand.None));
            Assert.That(phoebe.CurrentState, Is.SameAs(phoebe.Idle), "采样后从 UI 打开面板应立即撤回已提交的角色输入");
            yield break;
        }

        [UnityTest]
        public IEnumerator CapturingTheCursorKeepsTheCameraAtItsInitialPitchAndMovementRespondsImmediately()
        {
            inputManager.enabled = false;
            inputManager.enabled = true;
            var warp = new MouseState { delta = new Vector2(274, -725) };
            // 模拟锁定光标后，在输入系统更新前控制器已执行一次 Update
            SampleInput.Invoke(inputManager, null);
            ApplyInput.Invoke(local, null);
            Vector3 start = phoebe.transform.position;
            Step(.02, warp, Key.W);
            view.SendMessage("LateUpdate");
            Assert.That(view.transform.eulerAngles.x, Is.EqualTo(15f).Within(.01f));
            Assert.That(Quaternion.Angle(view.PlanarRotation, Quaternion.identity), Is.LessThan(.01f));
            Assert.That(phoebe.transform.position.z, Is.GreaterThan(start.z));

            yield return null;
            Step(.02, warp);
            view.SendMessage("LateUpdate");
            Assert.That(view.transform.eulerAngles.x, Is.EqualTo(15f).Within(.01f));
            yield return null;
            // Play Mode 中光标归中的增量在锁定后的第 2 帧才到达
            Step(.02, warp);
            view.SendMessage("LateUpdate");
            Assert.That(view.transform.eulerAngles.x, Is.EqualTo(15f).Within(.01f));
            Assert.That(Quaternion.Angle(view.PlanarRotation, Quaternion.identity), Is.LessThan(.01f));
            yield return null;
            Step(.02, new MouseState { delta = new Vector2(40, -20) }, Key.W);
            view.SendMessage("LateUpdate");
            Assert.That(view.transform.eulerAngles.x, Is.EqualTo(17f).Within(.01f));
            Assert.That(view.transform.eulerAngles.y, Is.EqualTo(4f).Within(.01f));
            yield break;
        }

        [UnityTest]
        public IEnumerator ClickingCloseAfterOpeningWithCRestoresControlWithoutAttacking()
        {
            var panel = GameObject.Find("GameUI/CharacterPanelRoot").GetComponent<CharacterStatsPanel>();
            Step(.1, default, Key.Escape);
            Step(.1, default, Key.C);
            Assert.That(panel.IsOpen, Is.True);
            Canvas.ForceUpdateCanvases();
            var button = (RectTransform)panel.transform.Find("PanelView/Panel/CloseBTN");
            var position = RectTransformUtility.WorldToScreenPoint(null, button.TransformPoint(button.rect.center));
            var pointer = new MouseState { position = position };
            InputSystem.QueueStateEvent(mouse, pointer);
            yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = position }.WithButton(MouseButton.Left));
            yield return null;
            Assert.That(Cursor.visible, Is.True);
            InputSystem.QueueStateEvent(mouse, pointer);
            yield return null;
            Assert.That(panel.IsOpen, Is.False);
            Assert.That(Cursor.visible, Is.False);
            clock.Advance(.1);
            Assert.That(phoebe.StepCommands, Is.EqualTo(CharacterCommand.None));
        }

        private static AnimatorControllerPlayable LocomotionFor(Animator model)
        {
            foreach (var graph in UnityEditor.Playables.Utility.GetAllGraphs())
            {
                for (int i = 0; i < graph.GetOutputCount(); i++)
                {
                    var output = graph.GetOutput(i);
                    if (!output.IsPlayableOutputOfType<AnimationPlayableOutput>() ||
                        ((AnimationPlayableOutput)output).GetTarget() != model) continue;
                    var source = output.GetSourcePlayable();
                    if (!source.IsPlayableOfType<AnimatorControllerPlayable>()) continue;
                    return (AnimatorControllerPlayable)source;
                }
            }
            throw new AssertionException($"{model.name} 没有步态控制器输出");
        }

        private static void AssertGait(Animator model, string expectedClip)
        {
            foreach (var info in LocomotionFor(model).GetCurrentAnimatorClipInfo(0))
                if (info.clip.name == expectedClip && info.weight > .9f) return;
            Assert.Fail($"{model.name} 的模型输出中没有正在播放的 {expectedClip}");
        }

        [UnityTest]
        public IEnumerator WalkingAndRunningAnimateBothCharactersLegsAndStoppingReturnsToIdle()
        {
            foreach (var (member, prefix) in new[] { (phoebe, "GS"), (si, "DK") })
            {
                void Move(double seconds, Vector3 direction, bool run = false)
                {
                    member.SetControlInput(direction, run, CharacterCommand.None);
                    clock.Advance(seconds);
                }

                var model = member.GetComponent<Combatant>().ModelAnimator;
                var leg = model.GetBoneTransform(HumanBodyBones.RightUpperLeg);
                Move(.2, Vector3.forward);
                AssertGait(model, prefix + "_Walk");
                var walkingPose = leg.localRotation;
                Move(.16, Vector3.forward);
                Assert.That(Quaternion.Angle(walkingPose, leg.localRotation), Is.GreaterThan(1f), member.name);
                Move(.5, Vector3.forward, true);
                AssertGait(model, prefix + "_Run");
                Vector3 releasePosition = member.transform.position;
                float releaseSpeed = member.GroundSpeed;
                Move(.02, Vector3.zero);
                Assert.That(member.CurrentState, Is.SameAs(member.Idle));
                Assert.That(member.GroundSpeed, Is.EqualTo(releaseSpeed - .36f).Within(.005f));
                Assert.That(member.transform.position.z, Is.GreaterThan(releasePosition.z));
                float brakingAmount = LocomotionFor(model).GetFloat("MoveAmount");
                Assert.That(brakingAmount, Is.GreaterThan(1f).And.LessThan(2f));
                Move(.04, Vector3.zero);
                Assert.That(LocomotionFor(model).GetFloat("MoveAmount"), Is.LessThan(brakingAmount).And.GreaterThan(0f));
                Move(.4, Vector3.zero);
                Assert.That(member.GroundSpeed, Is.Zero);
                AssertGait(model, prefix + "_Idle");
            }
            yield break;
        }

        [UnityTest]
        public IEnumerator WalkingPoseHoldsThroughHitStopAndResumesOnTheNextPositiveActorStep()
        {
            var actor = phoebe.GetComponent<Combatant>();
            var leg = actor.ModelAnimator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            Step(.1, default, Key.W);
            var pose = leg.localRotation;
            clock.TryRequestHitStop(actor.Runtime.Id, .2);
            Step(.2, default, Key.W);
            yield return null;
            yield return null;
            Assert.That(Quaternion.Angle(pose, leg.localRotation), Is.LessThan(.05f));
            Step(.16, default, Key.W);
            Assert.That(Quaternion.Angle(pose, leg.localRotation), Is.GreaterThan(1f));
        }

        [UnityTest]
        public IEnumerator MovementUsesThisFramesCameraYawAndWorksWithAnimatorsDisabled()
        {
            foreach (var member in new[] { phoebe, si })
            {
                var animator = member.GetComponent<Combatant>().ModelAnimator;
                Assert.That(animator.applyRootMotion, Is.False);
                animator.enabled = false;
                Assert.That(member.CurrentState, Is.SameAs(member.Idle));
            }
            Assert.That(phoebe.Idle, Is.Not.SameAs(si.Idle));
            Assert.That(phoebe.Walk, Is.Not.SameAs(phoebe.Run));
            Vector3 start = phoebe.transform.position;
            Step(.2, new MouseState { delta = new Vector2(450, 0) });
            At(phoebe, start);
            Assert.That(Quaternion.Angle(phoebe.transform.rotation, Quaternion.identity), Is.LessThan(.01f));

            Step(.02, new MouseState { delta = new Vector2(450, 0) }, Key.W);
            Assert.That(phoebe.CurrentState, Is.SameAs(phoebe.Walk));
            Assert.That(Vector3.Angle(phoebe.MoveWorld, Vector3.right), Is.LessThan(.05f));
            Vector3 firstStep = Vector3.ProjectOnPlane(phoebe.transform.position - start, Vector3.up);
            Assert.That(phoebe.transform.eulerAngles.y, Is.InRange(.1f, 15f));
            Assert.That(Vector3.Angle(firstStep, phoebe.transform.forward), Is.LessThan(.1f));
            for (int i = 0; i < 30; i++) Step(.02, default, Key.W);
            Assert.That(Vector3.Angle(phoebe.transform.forward, Vector3.right), Is.LessThan(.3f));
            Step(.4, default, Key.W, Key.LeftShift);
            start = phoebe.transform.position;
            Step(.2, default, Key.W, Key.LeftShift);
            Assert.That(phoebe.CurrentState, Is.SameAs(phoebe.Run));
            At(phoebe, start + Vector3.right * .982222f);
            Assert.That(phoebe.GroundSpeed, Is.EqualTo(4.91111).Within(.01));
            start = phoebe.transform.position;
            Step(.2, default, Key.W, Key.D);
            Assert.That(phoebe.CurrentState, Is.SameAs(phoebe.Walk));
            Assert.That(Vector3.Distance(start, phoebe.transform.position), Is.EqualTo(.287872).Within(.01));
            Step(.2, default);
            Assert.That(phoebe.CurrentState, Is.SameAs(phoebe.Idle));
            Assert.That(phoebe.GroundSpeed, Is.Zero);
            Assert.That(si.CurrentState, Is.SameAs(si.Idle));
            yield break;
        }

        [UnityTest]
        public IEnumerator DodgeInputIsReportedOnceWithoutMovingOrInterruptingWalking()
        {
            var right = new MouseState().WithButton(MouseButton.Right);
            Vector3 start = phoebe.transform.position;
            Step(.1, right);
            Assert.That(phoebe.StepCommands, Is.EqualTo(CharacterCommand.Dodge));
            Assert.That(phoebe.CurrentState, Is.SameAs(phoebe.Idle));
            At(phoebe, start);
            Step(.1, right);
            Assert.That(phoebe.StepCommands, Is.EqualTo(CharacterCommand.None));
            Step(.1, default, Key.W);
            Step(.1, right, Key.W);
            Assert.That(phoebe.StepCommands, Is.EqualTo(CharacterCommand.Dodge));
            Assert.That(phoebe.CurrentState, Is.SameAs(phoebe.Walk));
            At(phoebe, start + Vector3.forward * (.12f + .1f * 1.43936f));
            clock.Advance(.1);
            Assert.That(phoebe.StepCommands, Is.EqualTo(CharacterCommand.None));
            Assert.That(phoebe.CurrentState, Is.SameAs(phoebe.Walk));
            yield break;
        }

        [UnityTest]
        public IEnumerator WallLimitsActualGroundSpeedAndReturnsTheModelToIdle()
        {
            var wall = GameObject.Find("Environment/Wall");
            Bounds bounds = wall.GetComponent<Collider>().bounds;
            Place(phoebe, new Vector3(bounds.center.x, 0, bounds.min.z - 1));
            Step(.8, default, Key.W);
            Step(.2, default, Key.W);
            Vector3 start = phoebe.transform.position;
            Step(.2, default, Key.W);
            Assert.That(phoebe.CurrentState, Is.SameAs(phoebe.Walk));
            Assert.That(Vector3.Distance(start, phoebe.transform.position), Is.LessThan(.015f));
            Assert.That(phoebe.GroundSpeed, Is.LessThan(.075f));
            AssertGait(phoebe.GetComponent<Combatant>().ModelAnimator, "GS_Idle");
            yield break;
        }

        [UnityTest]
        public IEnumerator IndependentActionClocksAndFrozenCommandsStayWithTheirOwner()
        {
            si.transform.rotation = Quaternion.LookRotation(Vector3.left);
            Sample(default, Key.W);
            si.SetControlInput(new Vector3(-2, 1, 0), false, CharacterCommand.None);
            Assert.That(si.MoveWorld, Is.EqualTo(Vector3.left));
            Vector3 startA = phoebe.transform.position, startB = si.transform.position;
            clock.Advance(.10);
            Vector3 beforeHitStop = phoebe.transform.position;
            var actor = phoebe.GetComponent<Combatant>().Runtime;
            clock.TryRequestHitStop(actor.Id, .030);
            Sample(new MouseState().WithButton(MouseButton.Right), Key.W);
            si.SetControlInput(Vector3.left, false, CharacterCommand.Attack);
            clock.Advance(.020);
            Assert.That(phoebe.StepCommands, Is.EqualTo(CharacterCommand.None));
            Assert.That(si.StepCommands, Is.EqualTo(CharacterCommand.Attack));
            Assert.That(phoebe.transform.position, Is.EqualTo(beforeHitStop));
            At(phoebe, startA + Vector3.forward * .12f);
            clock.Advance(.020);
            Assert.That(phoebe.StepCommands | si.StepCommands, Is.EqualTo(CharacterCommand.None));
            At(phoebe, startA + Vector3.forward * (.12f + .01f * 1.32f));
            At(si, startB + Vector3.left * (.12f + .02f * 1.44f + .02f * 1.54665f));

            Sample(default);
            si.SetControlInput(Vector3.zero, false, CharacterCommand.None);
            clock.Advance(2);
            si.SetControlInput(Vector3.zero, false, CharacterCommand.Dodge | CharacterCommand.Attack);
            startA = phoebe.transform.position;
            startB = si.transform.position;
            clock.SetPaused(true);
            Step(.2, new MouseState { delta = new Vector2(900, 0) }
                .WithButton(MouseButton.Left).WithButton(MouseButton.Right));
            Assert.That(Quaternion.Angle(view.PlanarRotation, Quaternion.Euler(0, 90, 0)), Is.LessThan(.01f));
            At(phoebe, startA);
            At(si, startB);
            clock.SetPaused(false);
            clock.Advance(.1);
            Assert.That(phoebe.StepCommands | si.StepCommands, Is.EqualTo(CharacterCommand.None));
            Assert.That(phoebe.CurrentState, Is.SameAs(phoebe.Idle));
            Assert.That(si.CurrentState, Is.SameAs(si.Idle));
            At(phoebe, startA);
            At(si, startB);
            yield break;
        }

        [UnityTest]
        public IEnumerator OnlyTheStartingCharacterReceivesPlayerCommands()
        {
            var left = new MouseState().WithButton(MouseButton.Left);
            Step(.1, left, Key.W);
            Assert.That(phoebe.StepCommands, Is.EqualTo(CharacterCommand.Attack));
            Assert.That(phoebe.CurrentState, Is.SameAs(phoebe.Walk));
            Assert.That(si.StepCommands, Is.EqualTo(CharacterCommand.None));
            Assert.That(si.CurrentState, Is.SameAs(si.Idle));
            Sample(default);
            Sample(left);
            Sample(default);
            clock.Advance(.1);
            Assert.That(phoebe.StepCommands, Is.EqualTo(CharacterCommand.None), "下一次采样覆盖尚未执行的按下");
            yield break;
        }

        [UnityTest]
        public IEnumerator DisablingAndReopeningPreservesOnlyTheCurrentAdmission()
        {
            var right = new MouseState().WithButton(MouseButton.Right);
            Step(.1, default, Key.W);
            Vector3 start = phoebe.transform.position;
            phoebe.gameObject.SetActive(false);
            Step(2, default);
            Sample(new MouseState().WithButton(MouseButton.Left).WithButton(MouseButton.Right));
            phoebe.gameObject.SetActive(true);
            clock.Advance(.1);
            Assert.That(phoebe.StepCommands, Is.EqualTo(CharacterCommand.None));
            Assert.That(phoebe.CurrentState, Is.SameAs(phoebe.Idle));
            At(phoebe, start);
            local.enabled = false;
            clock.Advance(2);
            Assert.That(phoebe.CurrentState, Is.SameAs(phoebe.Idle));
            local.enabled = true;
            Step(.1, default, Key.W);
            Assert.That(phoebe.CurrentState, Is.SameAs(phoebe.Walk));

            Step(.1, default);
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            local = GameObject.Find("Combat Session").GetComponent<PlayerController>();
            inputManager = local.GetComponent<GameInputManager>();
            phoebe = GameObject.Find("Characters/PlayerRoot").GetComponent<PartyMemberControl>();
            si = GameObject.Find("Characters/SiRoot").GetComponent<PartyMemberControl>();
            Assert.That(si.CurrentState, Is.SameAs(si.Idle));
            Assert.That(phoebe.CurrentState, Is.SameAs(phoebe.Idle));
            foreach (var member in new[] { phoebe, si })
            {
                var capsule = member.GetComponent<CharacterController>();
                Assert.That(Physics.Raycast(capsule.bounds.center, Vector3.down, out var hit, 2f,
                    LayerMask.GetMask("Environment"), QueryTriggerInteraction.Ignore), Is.True);
                Assert.That(capsule.bounds.min.y, Is.EqualTo(hit.point.y).Within(capsule.skinWidth + .001f));
            }
            var driver = local.GetComponent<BattleTimeDriver>();
            driver.enabled = false;
            clock = driver.Clock;
            start = phoebe.transform.position;
            Step(.1, right);
            Assert.That(phoebe.StepCommands, Is.EqualTo(CharacterCommand.Dodge));
            At(phoebe, start);
            yield break;
        }

        [UnityTest]
        public IEnumerator RenderFrameSamplesBeforeTheClockAndFollowsAfterMovement()
        {
            local.GetComponent<BattleTimeDriver>().enabled = true;
            var id = phoebe.GetComponent<Combatant>().Runtime.Id;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
            yield return null;
            Vector3 start = phoebe.transform.position;
            clock.TryGetActionTime(id, out var before);
            yield return null;
            clock.TryGetActionTime(id, out var after);
            Assert.That(phoebe.CurrentState, Is.SameAs(phoebe.Walk));
            Assert.That(phoebe.GroundSpeed, Is.GreaterThan(0f));
            Assert.That(phoebe.transform.position.z - start.z,
                Is.EqualTo((after.Elapsed - before.Elapsed) * phoebe.GroundSpeed).Within(.001));
            clock.SetPaused(true);
            yield return null;
            Assert.That(Vector3.Distance(view.transform.position + view.transform.forward * view.CurrentDistance,
                phoebe.transform.position + Vector3.up * 1.5f), Is.LessThan(.001f));
        }

        [UnityTest]
        public IEnumerator CameraRetractsOnContactAndRecoversUsingRealFrameTime()
        {
            var wall = GameObject.Find("Environment/Wall").GetComponent<Collider>();
            Bounds bounds = wall.bounds;
            Place(phoebe, new Vector3(bounds.center.x, 0, bounds.max.z + 2));
            view.Initialize(phoebe.transform);
            view.ApplyLook(new Vector2(0, 150));
            view.SendMessage("LateUpdate");
            Assert.That(view.PivotOverlapping, Is.False);
            Assert.That(view.CurrentDistance, Is.EqualTo(view.SafeDistance).And.InRange(.1f, 2f));
            float near = view.CurrentDistance;
            Place(phoebe, new Vector3(bounds.max.x + 8, 0, bounds.max.z + 2));
            clock.SetPaused(true);
            view.SendMessage("LateUpdate");
            Assert.That(view.SafeDistance, Is.EqualTo(4f));
            Assert.That(view.CurrentDistance, Is.EqualTo(Mathf.Min(4f, near + 4f * Time.unscaledDeltaTime)).Within(.001f));
            yield return new WaitForSecondsRealtime(1.15f);
            Assert.That(view.CurrentDistance, Is.EqualTo(4f).Within(.01f));
            Place(phoebe, new Vector3(bounds.center.x, 0, bounds.center.z));
            view.SendMessage("LateUpdate");
            Assert.That(view.PivotOverlapping, Is.True);
            Assert.That(view.CurrentDistance, Is.Zero);
            wall.isTrigger = true;
            Physics.SyncTransforms();
            view.SendMessage("LateUpdate");
            Assert.That(view.PivotOverlapping, Is.False);
            Assert.That(view.SafeDistance, Is.EqualTo(4f));
            view.ApplyLook(new Vector2(0, -4000));
            view.SendMessage("LateUpdate");
            Assert.That(view.transform.eulerAngles.x, Is.EqualTo(70f).Within(.01f));
            view.ApplyLook(new Vector2(0, 4000));
            view.SendMessage("LateUpdate");
            Assert.That(Mathf.DeltaAngle(0, view.transform.eulerAngles.x), Is.EqualTo(-50f).Within(.01f));
        }
    }
}
