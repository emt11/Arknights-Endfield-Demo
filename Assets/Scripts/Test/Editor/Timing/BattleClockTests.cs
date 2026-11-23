using System;
using Endfield.CombatContracts;
using NUnit.Framework;

namespace Endfield.Timing.Tests
{
    public sealed class BattleClockTests
    {
        [Test]
        public void PauseRateAndLocalHitStopProduceIndependentTimeSteps()
        {
            var clock = new BattleClock();
            var target = new ActorId(1);
            var other = new ActorId(2);
            clock.RegisterActor(target);
            clock.RegisterActor(other);

            void Step(double real, double battle, double victim, double attacker)
            {
                clock.Advance(real);
                clock.TryGetActionTime(target, out var a);
                clock.TryGetActionTime(other, out var b);
                Assert.That(clock.Real.Delta, Is.EqualTo(real));
                Assert.That(clock.Battle.Delta, Is.EqualTo(battle).Within(1e-12));
                Assert.That(clock.Presentation, Is.EqualTo(clock.Battle));
                Assert.That(a.Delta, Is.EqualTo(victim).Within(1e-12));
                Assert.That(b.Delta, Is.EqualTo(attacker).Within(1e-12));
            }

            Step(.016, .016, .016, .016);
            clock.TryRequestHitStop(target, .030);
            clock.TryGetActionTime(target, out var previous);
            Assert.That(previous.Delta, Is.EqualTo(.016));
            Step(.020, .020, 0, .020);
            clock.SetBattleRate(.5);
            clock.SetPaused(true);
            Step(.100, 0, 0, 0);
            clock.SetPaused(false);
            Step(.016, .008, .003, .008);
            Step(.020, .010, .010, .010);
            Assert.That(clock.Real.Elapsed, Is.EqualTo(.172).Within(1e-12));
            Assert.That(clock.Battle.Elapsed, Is.EqualTo(.054).Within(1e-12));
            clock.TryGetActionTime(target, out var end);
            clock.TryGetActionTime(other, out var otherEnd);
            Assert.That(end.Elapsed, Is.EqualTo(.029).Within(1e-12));
            Assert.That(otherEnd.Elapsed, Is.EqualTo(.054).Within(1e-12));
        }

        [Test]
        public void OverlappingRequestsKeepLongestRemainderAndLeavingDiscardsIt()
        {
            var clock = new BattleClock();
            var actor = new ActorId(9);
            Assert.That(clock.RegisterActor(default), Is.False);
            Assert.That(clock.RegisterActor(actor), Is.True);
            clock.TryRequestHitStop(actor, .030);
            clock.Advance(.020);
            Assert.That(clock.RegisterActor(actor), Is.False);
            clock.TryRequestHitStop(actor, .004);
            clock.TryRequestHitStop(actor, .015);
            clock.TryRequestHitStop(actor, 0);
            Assert.Throws<ArgumentOutOfRangeException>(() => clock.SetBattleRate(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => clock.Advance(double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => clock.TryRequestHitStop(actor, -1));
            clock.Advance(0);
            clock.Advance(.020);
            clock.TryGetActionTime(actor, out var step);
            Assert.That(step.Elapsed, Is.EqualTo(.005).Within(1e-12));
            Assert.That(clock.Real.Elapsed, Is.EqualTo(.040));
            clock.TryRequestHitStop(actor, 1);
            Assert.That(clock.UnregisterActor(actor), Is.True);
            Assert.That(clock.TryGetActionTime(actor, out _), Is.False);
            Assert.That(clock.TryRequestHitStop(actor, 1), Is.False);
            clock.RegisterActor(actor);
            clock.Advance(.010);
            clock.TryGetActionTime(actor, out step);
            Assert.That(step.Elapsed, Is.EqualTo(.010));
        }
    }
}
