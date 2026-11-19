#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Endfield.Character;
using Endfield.AnimationSystem;
using Endfield.Combat;
using Endfield.Pooling;
using Endfield.Controller;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEditor;
using Endfield.Presentation;

namespace Endfield.Validation
{
    [DefaultExecutionOrder(1000)]
    public sealed class CombatActionProbe : MonoBehaviour
    {
        public static bool Completed { get; private set; }
        public static string Failure { get; private set; }
        public static int Assertions { get; private set; }

        private IEnumerator Start()
        {
            Completed = false;
            Failure = null;
            Assertions = 0;
            IEnumerator playbackChecks = null;
            try
            {
                CombatSimulation simulation = FindFirstObjectByType<CombatSimulation>();
                simulation.enabled = false;
                Combatant player = FindFirstObjectByType<PlayerController>().Character;
                ActionController actions = player.Actions;
                var results = new List<ActionResult>();
                actions.ResultPublished += results.Add;
                simulation.QueueAttack(200);
                int moves = player.Motor.MovementSubmissions;
                simulation.AdvanceFrame(1f / 60f, false);
                Require(actions.Current != null, "Attack input did not start action");
                Require(simulation.PendingInputCount == 0, "Accepted attack input not consumed");
                Require(player.Motor.MovementSubmissions == moves + 1, "Motor did not commit exactly once");
                ActionRuntime first = actions.Current;
                Require(Math.Abs(player.Animation.ClipTime - first.LocalTime) < 0.00001d, "Animation is not synchronized");
                Require(results.Count == 1 && results[0].Started && results[0].reason == null, "Start result not published exactly once");
                Require(!actions.TryStart(player.Definition.BasicAttacks[0], null, out _), "Attack restarted outside cancel window");
                actions.End(first.ExecutionId, ActionEndReason.Cancelled);
                Require(actions.Current == null && first.Ended, "Cancelled action retained authority");
                Require(results.Count == 1, "Cancel result published synchronously");
                Require(actions.TryStart(player.Definition.BasicAttacks[0], null, out long secondId), "Repeated clip failed to start");
                actions.ProcessInitialPoints(secondId);
                actions.End(first.ExecutionId, ActionEndReason.Interrupted);
                Require(actions.Current.ExecutionId == secondId, "Old ID cancelled new action");
                simulation.AdvanceFrame(1f / 60f, false);
                Require(results.Count == 3 && !results[1].Started && results[2].Started, "Replacement result order is wrong");
                ActionRuntime second = actions.Current;
                float configuredRate = second.PlaybackRate;
                try
                {
                    second.PlaybackRate = -1f;
                }
                catch (ArgumentOutOfRangeException) { }
                Require(second.PlaybackRate == configuredRate, "Rejected playback rate leaves the current rate intact");
                second.PlaybackRate = 3f;
                int limit = 500;
                while (actions.Current != null && limit-- > 0)
                {
                    moves = player.Motor.MovementSubmissions;
                    simulation.AdvanceFrame(1f / 60f, false);
                    Require(player.Motor.MovementSubmissions == moves + 1, "Movement committed more than once");
                }

                Require(limit > 0 && second.Ended, "Action failed to complete");
                Require(second.Playback.NextPoint == second.Definition.animation.pointEvents.Length, "Action skipped points including endpoint");
                Require(Math.Abs(player.Animation.ClipTime - second.Definition.Duration) < 0.0001d, "Final pose overshot clip end");
                Require(results.Count == 4 && results[3].reason == ActionEndReason.Completed, "Completion result missing or duplicated");
                Vector3 position = player.transform.position;
                simulation.AdvanceFrame(0.5f, true);
                Require(Vector3.Distance(position, player.transform.position) < 0.00001f, "Paused character moved");
                actions.ResultPublished -= results.Add;
                ValidatePublicationFailure(simulation, player);
                ValidateLifecycle(simulation, player);
                ValidateTimelinePolicy(player);
                playbackChecks = ValidatePoints(simulation, player);
            }
            catch (Exception exception)
            {
                Failure = exception.ToString();
            }

            while (playbackChecks != null)
            {
                try
                {
                    if (!playbackChecks.MoveNext())
                        break;
                }
                catch (Exception exception)
                {
                    Failure = exception.ToString();
                    break;
                }
                yield return playbackChecks.Current;
            }
            (playbackChecks as IDisposable)?.Dispose();
            Completed = true;
        }

        private static void ValidatePublicationFailure(CombatSimulation simulation, Combatant player)
        {
            var calls = new List<int>();
            var expected = new InvalidOperationException("Probe notification");
            simulation.Defer(() => calls.Add(1));
            simulation.Defer(() =>
            {
                calls.Add(2);
                simulation.Defer(() => calls.Add(4));
                throw expected;
            });
            simulation.Defer(() => calls.Add(3));
            bool propagated = false;
            try
            {
                simulation.AdvanceFrame(1f / 60f, false);
            }
            catch (InvalidOperationException exception) when (exception == expected)
            {
                propagated = true;
            }

            Require(propagated && !simulation.IsPublishing && calls.SequenceEqual(new[] { 1, 2 }), "Failed callback releases publishing state");
            Require(player.Actions.TryStart(player.Definition.BasicAttacks[0], null, out long id), "Can start after callback failure");
            simulation.AdvanceFrame(1f / 60f, false);
            Require(calls.SequenceEqual(new[] { 1, 2, 3, 4 }), "Unattempted and newly queued notifications run once in order");
            player.Actions.End(id, ActionEndReason.Cancelled);
        }

        private static void ValidateLifecycle(CombatSimulation simulation, Combatant player)
        {
            ActionController actions = player.Actions;
            ActionDefinition definition = Instantiate(player.Definition.BasicAttacks[0]);
            definition.animation = Instantiate(definition.animation);
            definition.animation.windowEvents = new[]
            {
                new EffectWindow
                {
                    start = 0f,
                    end = definition.Duration,
                    effect = AssetDatabase.LoadAssetAtPath<ParticleEffectDefinition>("Assets/ScriptObjects/Presentation/ChargeGlow.asset")
                }
            };
            foreach (Behaviour component in new Behaviour[] { actions, player.Animation, player.Motor, player })
            {
                Require(actions.TryStart(definition, null, out long id), "Lifecycle action starts");
                actions.ProcessInitialPoints(id);
                ActionRuntime runtime = actions.Current;
                var handle = (PoolHandle)runtime.Playback.windows[0].state;
                Require(handle.IsValid, "Lifecycle effect entered");
                component.enabled = false;
                Require(actions.Current == null && runtime.Ended && !handle.IsValid, component.GetType().Name + " interrupts and returns effect");
                Require(!actions.TryStart(definition, null, out _), "Disabled dependency rejects start");
                if (component == actions)
                    player.Motor.SetIntent(Vector3.forward, false);
                int moves = player.Motor.MovementSubmissions;
                simulation.AdvanceFrame(1f / 60f, false);
                Require(runtime.LocalTime == 0f, "Disabled action does not advance");
                Require(player.Motor.MovementSubmissions == moves + (component == actions ? 1 : 0), "Disabled movement dependency is not simulated");
                if (component == actions)
                    Require(player.Motor.LocomotionSpeed > 0f, "Disabled actions still allow ordinary movement");
                component.enabled = true;
                player.Motor.ResetMotion();
            }

            Require(actions.TryStart(definition, null, out long objectId), "Object disable action starts");
            actions.ProcessInitialPoints(objectId);
            ActionRuntime objectRuntime = actions.Current;
            var objectEffect = (PoolHandle)objectRuntime.Playback.windows[0].state;
            player.gameObject.SetActive(false);
            Require(objectRuntime.Ended && !objectEffect.IsValid, "Object disable clears effects");
            simulation.AdvanceFrame(1f / 60f, false);
            player.gameObject.SetActive(true);
            Require(actions.TryStart(definition, null, out long resumeId), "Re-enabled object accepts new execution");
            PlayerController controller = FindFirstObjectByType<PlayerController>();
            simulation.QueueAttack(300);
            controller.enabled = false;
            Require(simulation.PendingInputCount == 0, "Player disable clears queued input");
            simulation.AdvanceFrame(1f / 60f, false);
            Require(actions.Current.ExecutionId == resumeId && actions.Current.LocalTime > 0f, "Player disable preserves running action");
            actions.End(resumeId, ActionEndReason.Cancelled);
            simulation.QueueAttack(301);
            simulation.AdvanceFrame(1f / 60f, false);
            Require(actions.Current == null && simulation.PendingInputCount == 0, "Disabled player does not consume stale attack");
            controller.enabled = true;

            Action<ActionResult> disableDuringPublish = result =>
            {
                if (result.Started)
                    player.Animation.enabled = false;
            };
            actions.ResultPublished += disableDuringPublish;
            Require(actions.TryStart(definition, null, out long deferredId), "Deferred cleanup action starts");
            actions.ProcessInitialPoints(deferredId);
            ActionRuntime deferred = actions.Current;
            simulation.AdvanceFrame(1f / 60f, false);
            Require(actions.Current == deferred && !player.Animation.enabled, "Result callback defers cancellation");
            simulation.AdvanceFrame(1f / 60f, false);
            Require(deferred.Ended && actions.Current == null && !deferred.Playback.windows[0].active, "Queued cleanup succeeds after graph release");
            actions.ResultPublished -= disableDuringPublish;
            player.Animation.enabled = true;
            Destroy(definition.animation);
            Destroy(definition);
        }

        private static void ValidateTimelinePolicy(Combatant player)
        {
            var action = Instantiate(player.Definition.BasicAttacks[0]);
            var animation = Instantiate(action.animation);
            action.animation = animation;
            action.openingTurnDuration = 0f;
            animation.playbackRate = 1f;
            animation.pointEvents = Array.Empty<TimelinePointEvent>();
            animation.windowEvents = new TimelineWindowEvent[]
            {
                new CancelWindow { start = 0f, end = .2f, categories = ActionCategory.Attack },
                new CancelWindow { start = .1f, end = .3f, categories = ActionCategory.Attack | ActionCategory.Move }
            };
            var actions = player.Actions;
            Require(actions.TryStart(action, null, out long id, Vector3.left) && Vector3.Dot(player.transform.forward, Vector3.left) > .999f, "Zero-duration opening applies facing at submission");
            actions.ProcessInitialPoints(id);
            Require(actions.CanCancel(ActionCategory.Attack) && !actions.CanCancel(ActionCategory.Move), "Zero-time window grants only its categories");
            actions.Current.Advance(.2f);
            actions.ProcessEvents();
            Require(actions.CanCancel(ActionCategory.Attack) && actions.CanCancel(ActionCategory.Move), "An overlapping window still grants categories after the first exit");
            actions.Current.Advance(.11f);
            actions.ProcessEvents();
            Require(!actions.CanCancel(ActionCategory.Attack) && !actions.CanCancel(ActionCategory.Move), "All permissions expire at the last exit");
            actions.End(id, ActionEndReason.Cancelled);

            var playerAnimation = player.Animation;
            playerAnimation.EvaluatePose(5f);
            animation.easeInDuration = 1f;
            animation.easeOutDuration = 1f;
            animation.easeInCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
            animation.easeOutCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
            var fast = Instantiate(animation);
            fast.easeInDuration = .2f;
            var mixer = (AnimationMixerPlayable)typeof(AnimationPlayer).GetField("mixer", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(playerAnimation);
            var first = playerAnimation.Play(animation);
            first.Advance(.25f);
            Require(first.Time == .25f && playerAnimation.ClipTime == 0d, "Advancing the clock leaves the pose at its previous sample");
            playerAnimation.EvaluatePose(.25f);
            Require(first.Time == .25f && Math.Abs(playerAnimation.ClipTime - first.Time) < .0001d, "Pose sampling uses the clock without advancing it");
            Require(Mathf.Abs(mixer.GetInputWeight(1) - animation.easeInCurve.Evaluate(.25f)) < .0001f, "Configured entrance curve sets the sampled mixer weight");
            var second = playerAnimation.Play(fast);
            second.Advance(.1f);
            playerAnimation.EvaluatePose(.1f);
            float firstWeight = mixer.GetInputWeight(1);
            float secondWeight = mixer.GetInputWeight(2);
            float oldShare = 1f - fast.easeInCurve.Evaluate(.5f);
            float entryWeight = animation.easeInCurve.Evaluate(.25f);
            Require(Mathf.Abs(firstWeight - entryWeight * oldShare) < .0001f && Mathf.Abs(secondWeight - (1f - oldShare)) < .0001f
                && Mathf.Abs(mixer.GetInputWeight(0) - (1f - entryWeight) * oldShare) < .0001f, "Incoming curve splits the visible base and old clip proportionally");
            var third = playerAnimation.Play(animation);
            Require(first.Ended && second.Ended && !third.Ended && Mathf.Abs(mixer.GetInputWeight(1) - firstWeight) < .0001f && Mathf.Abs(mixer.GetInputWeight(2) - secondWeight) < .0001f, "Rapid replacements preserve both visible weights while transferring event ownership");
            third.Advance(.5f);
            playerAnimation.EvaluatePose(.5f);
            float baseBeforeStop = mixer.GetInputWeight(0);
            float thirdBeforeStop = mixer.GetInputWeight(3);
            playerAnimation.Stop(third);
            playerAnimation.EvaluatePose(.25f);
            float remaining = animation.easeOutCurve.Evaluate(.25f);
            Require(Mathf.Abs(mixer.GetInputWeight(0) - (baseBeforeStop * remaining + 1f - remaining)) < .0001f
                && Mathf.Abs(mixer.GetInputWeight(3) - thirdBeforeStop * remaining) < .0001f
                && Mathf.Abs(mixer.GetInputWeight(1) - firstWeight * .5f * remaining) < .0001f
                && Mathf.Abs(mixer.GetInputWeight(2) - secondWeight * .5f * remaining) < .0001f, "Cancellation uses one exit curve for every visible source");
            playerAnimation.EvaluatePose(5f);
            Require(mixer.GetInputWeight(0) == 1f && !mixer.GetInput(1).IsValid() && !mixer.GetInput(2).IsValid()
                && !mixer.GetInput(3).IsValid(), "Returning to locomotion releases all clip inputs");

            var greatSword2 = playerAnimation.Play(player.Definition.BasicAttacks[1].animation);
            greatSword2.Advance(.3f);
            playerAnimation.EvaluatePose(.3f);
            var greatSword3 = playerAnimation.Play(player.Definition.BasicAttacks[2].animation);
            greatSword3.Advance(.2f);
            playerAnimation.EvaluatePose(.2f);
            Require(Mathf.Abs(mixer.GetInputWeight(1) - .6f) < .0001f && Mathf.Abs(mixer.GetInputWeight(2) - .4f) < .0001f
                && mixer.GetInputWeight(0) == 0f, "GreatSword 2 to 3 at 0.20 combat seconds is 60/40 with no locomotion");
            var greatSword1 = playerAnimation.Play(player.Definition.BasicAttacks[0].animation);
            greatSword1.Advance(.15f);
            playerAnimation.EvaluatePose(.15f);
            Require(Mathf.Abs(mixer.GetInputWeight(1) - .3f) < .0001f && Mathf.Abs(mixer.GetInputWeight(2) - .2f) < .0001f
                && Mathf.Abs(mixer.GetInputWeight(3) - .5f) < .0001f, "Wrapping to GreatSword 1 preserves the old clips' ratio");

            AnimationDefinition firstAttack = greatSword1.Definition;
            float halfExitTime = firstAttack.Duration - firstAttack.easeOutDuration * .5f;
            float stepToExit = (halfExitTime - greatSword1.Time) / firstAttack.playbackRate;
            greatSword1.Advance(stepToExit);
            playerAnimation.EvaluatePose(stepToExit);
            Require(Mathf.Abs(mixer.GetInputWeight(0) - .5f) < .0001f, "A large step reaches the configured natural exit weight");
            playerAnimation.Stop(greatSword1);
            playerAnimation.EvaluatePose(.075f);
            Require(Mathf.Abs(mixer.GetInputWeight(0) - .75f) < .0001f, "Stopping during a natural exit continues the same curve");
            playerAnimation.EvaluatePose(1f);

            fast.easeInDuration = fast.Duration;
            fast.easeOutDuration = fast.Duration;
            var shortPlayback = playerAnimation.Play(fast);
            float halfClip = fast.Duration * .5f;
            shortPlayback.Advance(halfClip);
            playerAnimation.EvaluatePose(halfClip);
            Require(mixer.GetInputWeight(1) == 1f, "Overlapping durations fit both transitions inside a short clip");
            shortPlayback.Advance(halfClip);
            playerAnimation.EvaluatePose(halfClip);
            playerAnimation.DispatchEvents(shortPlayback);
            Require(!mixer.GetInput(1).IsValid() && Math.Abs(playerAnimation.ClipTime - fast.Duration) < .0001d,
                "Natural completion releases the input after sampling the endpoint");

            fast.easeInDuration = 0f;
            fast.easeOutDuration = 0f;
            var immediate = playerAnimation.Play(fast);
            Require(mixer.GetInputWeight(1) == 1f, "Zero duration starts fully weighted");
            playerAnimation.Stop(immediate);
            Require(mixer.GetInputWeight(0) == 1f && !mixer.GetInput(1).IsValid(), "Zero duration returns to locomotion immediately");
            var hit = new HitWindow { start = .125f, end = .1875f };
            var warp = new MotionWarpWindow { start = .140625f, end = .1875f };
            animation.windowEvents = new TimelineWindowEvent[] { hit, warp };
            var intervals = new ActionRuntime(action, 0, null);
            intervals.Advance(.125f);
            Require(intervals.hitIntervals.Count == 0 && intervals.warpIntervals.Count == 0, "Touching a window start does not create an interval");
            intervals.Advance(.0625f);
            Require(intervals.hitIntervals.Single() == (hit, hit.start, hit.end)
                && intervals.warpIntervals.Single() == (warp, warp.start, warp.end), "A step submits exactly the overlap of both movement windows");
            intervals.Advance(.0625f);
            Require(intervals.hitIntervals.Count == 0 && intervals.warpIntervals.Count == 0, "Starting at a window end does not repeat the interval");

            Destroy(fast);
            Destroy(animation);
            Destroy(action);
        }

        private static IEnumerator ValidatePoints(CombatSimulation simulation, Combatant player)
        {
            ActionDefinition definition = Instantiate(player.Definition.BasicAttacks[0]);
            definition.animation = Instantiate(definition.animation);
            AudioDefinition audio = ScriptableObject.CreateInstance<AudioDefinition>();
            AudioClip clip = AudioClip.Create("Point validation", 44100, 1, 44100, false);
            var audioData = new SerializedObject(audio);
            PooledAudioSource audioPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Audio/WorldAudioSource.prefab").GetComponent<PooledAudioSource>();
            audioData.FindProperty("prefab").objectReferenceValue = audioPrefab;
            audioData.FindProperty("clips").arraySize = 1;
            audioData.FindProperty("clips").GetArrayElementAtIndex(0).objectReferenceValue = clip;
            audioData.FindProperty("volume").floatValue = 0f;
            audioData.ApplyModifiedPropertiesWithoutUndo();
            ParticleEffectDefinition effect = AssetDatabase.LoadAssetAtPath<ParticleEffectDefinition>("Assets/ScriptObjects/Presentation/ChargeGlow.asset");
            Transform anchor = player.Animation.Model.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.RightHand);
            definition.useRootMotion = false;
            definition.animation.windowEvents = Array.Empty<EffectWindow>();
            definition.animation.pointEvents = new TimelinePointEvent[]
            {
                new ComboAdvancePoint { time = 0f },
                new AudioPoint
                {
                    time = 0f,
                    audio = audio,
                    anchor = AnimationAnchor.RightHand,
                    offset = Vector3.right * .2f
                },
                new EffectPoint
                {
                    time = 0f,
                    effect = effect,
                    anchor = AnimationAnchor.RightHand,
                    offset = Vector3.up * .3f,
                    rotation = new Vector3(0f, 35f, 0f),
                    follow = true
                },
                new ComboAdvancePoint { time = .002f },
                new ComboAdvancePoint { time = .003f },
                new EffectPoint
                {
                    time = definition.Duration,
                    effect = effect,
                    follow = true
                }
            };
            ActionController actions = player.Actions;
            Require(actions.TryStart(definition, null, out long id), "Typed point action starts");
            ActionRuntime runtime = actions.Current;
            Require(runtime.Playback.ResolveAnchor(AnimationAnchor.ModelRoot) == player.Animation.Model, "Default anchor is the playing model");
            Require(runtime.Playback.ResolveAnchor(AnimationAnchor.Weapon) == player.Weapon.Socket && player.Weapon.Hitboxes[0].transform.IsChildOf(player.Weapon.Socket), "Effects and equipped weapon share the configured socket");
            Require(runtime.Playback.NextPoint == 0, "Start does not process zero points before input consumption");
            actions.ProcessInitialPoints(id);
            Require(runtime.Playback.NextPoint == 3 && runtime.ComboAdvanced && runtime.Playback.ownedHandles.Count == 1, "Three zero-time point types execute");
            PooledAudioSource audioInstance = FindObjectsByType<PooledAudioSource>(FindObjectsSortMode.None).First(item => item.IsRented);
            PooledParticleEffect effectInstance = FindObjectsByType<PooledParticleEffect>(FindObjectsSortMode.None).First(item => item.IsRented);
            Require(Vector3.Distance(audioInstance.transform.position, anchor.TransformPoint(Vector3.right * .2f)) < .00001f, "Audio point uses configured bone and offset");
            Require(Vector3.Distance(effectInstance.transform.position, anchor.TransformPoint(Vector3.up * .3f)) < .00001f, "Effect point uses configured bone and offset");
            Require(Quaternion.Angle(effectInstance.transform.rotation, anchor.rotation * Quaternion.Euler(0f, 35f, 0f)) < .1f, "Effect point applies rotation");
            actions.ProcessInitialPoints(id);
            Require(runtime.Playback.NextPoint == 3 && runtime.Playback.ownedHandles.Count == 1, "Zero points do not repeat");
            player.transform.position += Vector3.right;
            typeof(PooledParticleEffect).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(effectInstance, null);
            float followError = Vector3.Distance(effectInstance.transform.position, anchor.TransformPoint(Vector3.up * .3f));
            // 100 倍骨骼矩阵往返会产生约 1e-5 的世界坐标舍入误差
            Require(followError < .0001f, "Following point retains local offset; world error=" + followError + "; bone scale=" + anchor.lossyScale);
            runtime.Advance(.01f);
            player.Animation.EvaluatePose(.01f);
            actions.ProcessEvents();
            Require(runtime.Playback.NextPoint == 5, "Step crossing multiple points dispatches all");
            runtime.Advance(definition.Duration);
            player.Animation.EvaluatePose(definition.Duration);
            actions.ProcessEvents();
            Require(runtime.Playback.NextPoint == 6 && runtime.Playback.ownedHandles.Count == 2, "Endpoint point dispatches before completion");
            var handles = runtime.Playback.ownedHandles.ToArray();
            actions.FinishAtEnd();
            Require(handles.All(handle => !handle.IsValid), "Completion returns following point effects");
            Require(actions.TryStart(definition, null, out long secondId), "Shared definition starts a fresh execution");
            ActionRuntime next = actions.Current;
            Require(next.Playback.NextPoint == 0 && !next.ComboAdvanced && next.Playback.ownedHandles.Count == 0, "Point runtime state is not shared");
            audioData.FindProperty("loop").boolValue = true;
            audioData.ApplyModifiedPropertiesWithoutUndo();
            actions.ProcessInitialPoints(secondId);
            Require(next.Playback.ownedHandles.Count == 2, "Looping audio and following effect belong to this action");
            var owned = next.Playback.ownedHandles.ToArray();
            actions.End(secondId, ActionEndReason.Cancelled);
            simulation.AdvanceFrame(1f / 60f, false);
            actions.ProcessEvents();
            Require(next.Playback.NextPoint == 3 && owned.All(handle => !handle.IsValid), "Cancellation returns effects and stops pending points");
            GameObject npcModel = Instantiate(player.Animation.Model.gameObject);
            AnimationPlayer npc = npcModel.AddComponent<AnimationPlayer>();
            var settings = new SerializedObject(npc);
            settings.FindProperty("feedback").objectReferenceValue = simulation.Feedback;
            settings.ApplyModifiedPropertiesWithoutUndo();
            try
            {
                Require(npc.GetComponentInParent<Combatant>() == null, "NPC plays without a combat owner");
                definition.animation.windowEvents = new TimelineWindowEvent[]
                {
                    new EffectWindow { start = 0f, end = definition.Duration, effect = effect, anchor = AnimationAnchor.RightHand }
                };
                ActionDefinition basic = player.Definition.BasicAttacks[0];
                definition.animation.windowEvents = definition.animation.windowEvents.Concat(basic.animation.windowEvents.Where(entry => entry is HitWindow || entry is MotionWarpWindow)).OrderBy(entry => entry.start).ToArray();
                definition.useRootMotion = true;
                var target = simulation.Units.First(unit => unit != player);
                player.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                player.Motor.ResetMotion();
                target.transform.position = Vector3.forward * 2f;
                Physics.SyncTransforms();
                float health = target.Health.Current;
                AnimationPlayback ambient = npc.Play(definition.animation);
                Require(ambient.ResolveAnchor(AnimationAnchor.RightHand) != anchor && ambient.ResolveAnchor(AnimationAnchor.RightHand).IsChildOf(npcModel.transform), "Shared event resolves the hand on its own actor");
                Transform npcSocket = ambient.ResolveAnchor(AnimationAnchor.Weapon);
                npcModel.GetComponent<AnimationAnchorBindings>().Bind(AnimationAnchor.LeftHand, npcSocket);
                Require(ambient.ResolveAnchor(AnimationAnchor.LeftHand) == npcSocket && npcSocket != player.Weapon.Socket, "An explicit binding overrides the humanoid bone on this actor only");
                var npcMixer = (AnimationMixerPlayable)typeof(AnimationPlayer).GetField("mixer", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(npc);
                Require(npcMixer.GetInputWeight(1) == 1f, "First clip on a standalone graph immediately owns the full pose");
                Require(actions.TryStart(definition, target, out long sharedId), "Shared animation starts on combat actor");
                actions.ProcessInitialPoints(sharedId);
                ActionRuntime attack = actions.Current;
                Require(player.Animation.Play(definition.animation) == null && !player.Animation.Stop(attack.Playback), "Ordinary commands respect action ownership");
                yield return null;
                yield return null;
                Require(ambient.Time > 0f && attack.LocalTime == 0f && attack.Playback.Time == 0f, "Only standalone graph advances with Update");
                Require(ambient.ownedHandles.Count == 2 && attack.Playback.ownedHandles.Count == 2, "Both actors independently play audio and following VFX");
                var trail = (PoolHandle)ambient.windows[0].state;
                var attackTrail = (PoolHandle)attack.Playback.windows[0].state;
                for (int step = 0; step < 60; step++)
                    simulation.AdvanceFrame(1f / 60f, false);
                float damage = DamageCalculator.Calculate(player.Stats.Attack, basic.animation.windowEvents.OfType<HitWindow>().First().damageMultiplier, target.Stats.Defense, simulation.Rules.defenseConstant);
                Require(Mathf.Abs(health - target.Health.Current - damage) < .001f, "Only the combat execution deals the configured hit");
                Require(actions.PlayAnimation(definition.animation) == null, "Character rejects an ordinary request during the attack");
                var replay = actions.PlayAnimation(definition.animation, true);
                Require(replay != null && attack.EndReason == ActionEndReason.Interrupted && actions.Current == null, "Reaction replaces the attack directly");
                Require(trail.IsValid && !attackTrail.IsValid && npc.Current == ambient, "Interrupting the attack keeps the NPC trail alive");
                simulation.AdvanceFrame(.1f, true);
                simulation.RequestHitStop(.02f);
                simulation.AdvanceFrame(.02f, false);
                Require(replay.Time == 0f, "Ordinary playback on a combat actor respects pause and hit stop");
                simulation.AdvanceFrame(1f / 60f, false);
                player.Animation.DispatchEvents(attack.Playback);
                Require(!player.Animation.Stop(attack.Playback) && replay.windows[0].active, "Stale requests cannot end replacement effects");
                player.Animation.Stop(replay);
                ambient.Advance(definition.Duration);
                npc.EvaluatePose(definition.Duration);
                npc.DispatchEvents(ambient);
                Require(ambient.Ended && !trail.IsValid && ambient.NextPoint == 6 && npc.Current == null, "Standalone endpoint notification precedes cleanup");
                yield return null;
                Require(Math.Abs(npc.ClipTime - definition.Duration) < .0001d && npcMixer.GetInputWeight(1) == 1f, "No base controller holds the final pose");

                var callbacks = new CallbackWindow { start = .001f, end = .003f };
                var silent = Instantiate(basic.animation);
                int replacementPoints = 0;
                silent.pointEvents = new TimelinePointEvent[] { new CallbackPoint { time = 0f, trigger = () => replacementPoints++ } };
                definition.animation.pointEvents = new TimelinePointEvent[] { new CallbackPoint { time = 0f, trigger = () => player.Animation.Play(silent) } };
                var replacedAtStart = player.Animation.Play(definition.animation);
                simulation.AdvanceFrame(1f / 60f, false);
                Require(replacedAtStart.Ended && player.Animation.Current.Definition == silent && player.Animation.Current.Time == 0f &&
                    replacementPoints == 0, "Replacing ordinary playback at zero leaves the new first pose and events for the next step");
                simulation.AdvanceFrame(1f / 60f, false);
                Require(player.Animation.Current.Time > 0f && replacementPoints == 1, "Replacement advances and receives its zero event once");
                player.Animation.Stop(player.Animation.Current);

                definition.animation.pointEvents = Array.Empty<TimelinePointEvent>();
                definition.animation.windowEvents = new TimelineWindowEvent[] { callbacks };
                var interrupted = npc.Play(definition.animation);
                callbacks.onEnter = () => npc.Play(silent);
                interrupted.Advance(.01f);
                npc.EvaluatePose(.01f);
                npc.DispatchEvents(interrupted);
                Require(interrupted.Ended && callbacks.exits == 1 && callbacks.elapsed == 0f && npc.Current.Definition == silent, "Replacement during Enter exits only the old state");
                callbacks.onEnter = null;
                var crossing = npc.Play(definition.animation);
                crossing.Advance(.01f);
                npc.EvaluatePose(.01f);
                npc.DispatchEvents(crossing);
                npc.DispatchEvents(crossing);
                npc.Stop(crossing);
                Require(callbacks.exits == 2 && Mathf.Abs(callbacks.elapsed - .002f) < .00001f, "Short state ticks only its overlap and exits exactly once");
                definition.animation.pointEvents = new TimelinePointEvent[]
                {
                    new CallbackPoint { time = .004f, trigger = () => npc.Play(silent) },
                    new CallbackPoint { time = .005f, trigger = () => throw new InvalidOperationException("Stale point ran") }
                };
                var chronological = npc.Play(definition.animation);
                chronological.Advance(.01f);
                npc.EvaluatePose(.01f);
                npc.DispatchEvents(chronological);
                Require(callbacks.exits == 3 && chronological.NextPoint == 1 && chronological.Ended, "A completed short window precedes a later replacing point in the same step");
                npc.Stop(npc.Current);
                Destroy(silent);
            }
            finally
            {
                npc.enabled = false;
                Destroy(npcModel);
                foreach (PooledAudioSource instance in FindObjectsByType<PooledAudioSource>(FindObjectsSortMode.None))
                    instance.Handle.Return();
                Destroy(definition.animation);
                Destroy(definition);
                Destroy(audio);
                Destroy(clip);
            }
        }

        private sealed class CallbackPoint : TimelinePointEvent
        {
            public Action trigger;
            public override void OnTrigger(AnimationPlayback playback) => trigger();
        }

        private sealed class CallbackWindow : TimelineWindowEvent
        {
            public Action onEnter;
            public int exits;
            public float elapsed;

            public override object OnEnter(AnimationPlayback playback)
            {
                onEnter?.Invoke();
                return this;
            }

            public override void OnInterval(AnimationPlayback playback, float from, float to) => elapsed += to - from;

            public override void OnExit(AnimationPlayback playback, object state) => exits++;
        }

        private static void Require(bool condition, string message)
        {
            Assertions++;
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
#endif
