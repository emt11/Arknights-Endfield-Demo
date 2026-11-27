using Endfield.CharacterControl.FSM;
using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Endfield.CharacterControl.Tests
{
    public sealed class StateMachineTests
    {
        private sealed class Probe : CharacterState
        {
            public Action Entered, Exited;
            public Action<double> Ticked;

            public override void Enter() => Entered?.Invoke();

            public override void Tick(double delta) => Ticked?.Invoke(delta);

            public override void Exit() => Exited?.Invoke();
        }

        [Test]
        public void SwitchingKeepsLifecycleOrderAndTicksOnlyTheActiveState()
        {
            var machine = new StateMachine();
            var events = new List<string>();
            var first = new Probe();
            var second = new Probe();
            first.Entered = () => events.Add("enter A");
            first.Exited = () =>
            {
                Assert.That(machine.CurrentState, Is.SameAs(first));
                events.Add("exit A");
            };
            first.Ticked = delta =>
            {
                Assert.That(delta, Is.EqualTo(.125));
                events.Add("tick A");
                machine.ChangeState(second);
            };
            second.Entered = () =>
            {
                Assert.That(machine.CurrentState, Is.SameAs(second));
                events.Add("enter B");
            };
            second.Ticked = delta => events.Add("tick B");

            machine.Initialize(first);
            machine.ChangeState(first);
            machine.Tick(.125);
            Assert.That(events, Is.EqualTo(new[] { "enter A", "tick A", "exit A", "enter B" }));
            machine.Tick(.5);
            Assert.That(events[4], Is.EqualTo("tick B"));
        }

        [Test]
        public void NestedTransitionDuringEnterOrExitThrowsAndReleasesTheGuard()
        {
            var machine = new StateMachine();
            var first = new Probe();
            var next = new Probe();
            first.Entered = () => machine.ChangeState(next);
            Assert.Throws<InvalidOperationException>(() => machine.Initialize(first));
            Assert.That(machine.CurrentState, Is.SameAs(first));
            first.Exited = () => machine.ChangeState(next);
            Assert.Throws<InvalidOperationException>(() => machine.ChangeState(next));
            Assert.That(machine.CurrentState, Is.SameAs(first));

            first.Exited = null;
            machine.ChangeState(next);
            Assert.That(machine.CurrentState, Is.SameAs(next));
            Assert.Throws<InvalidOperationException>(() => machine.Initialize(first));
        }

        private sealed class EnemyIdle : CharacterState
        {
            private readonly StateMachine machine;
            private readonly CharacterState patrol;
            private readonly double wait;
            private double elapsed;

            public EnemyIdle(StateMachine machine, CharacterState patrol, double wait)
            {
                this.machine = machine;
                this.patrol = patrol;
                this.wait = wait;
            }

            public override void Enter() => elapsed = 0;

            public override void Tick(double delta)
            {
                elapsed += delta;
                if (elapsed >= wait) machine.ChangeState(patrol);
            }
        }

        [Test]
        public void TwoEnemiesOfTheSameTypeOwnTheirTimersAndTheirTransitions()
        {
            var a = new StateMachine();
            var b = new StateMachine();
            var patrolA = new Probe();
            var patrolB = new Probe();
            var idleB = new EnemyIdle(b, patrolB, .30);
            a.Initialize(new EnemyIdle(a, patrolA, .10));
            b.Initialize(idleB);

            a.Tick(.10);
            b.Tick(.10);
            Assert.That(a.CurrentState, Is.SameAs(patrolA));
            Assert.That(b.CurrentState, Is.SameAs(idleB));
            b.Tick(.20);
            Assert.That(b.CurrentState, Is.SameAs(patrolB));
        }
    }
}
