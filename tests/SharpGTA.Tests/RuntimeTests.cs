using System;
using System.Collections.Generic;
using SharpGTA.Games.III;
using SharpGTA.Games.VC;
using SharpGTA.Runtime;

namespace SharpGTA.Tests
{
    public sealed class RuntimeTests
    {
        [Test]
        public void FixedDriverUsesGtaUnitsAndExactGameClockTicks()
        {
            SimulationRuntime runtime = new SimulationRuntime(IIIDefinition.Instance);
            GameClock clock = new GameClock();
            runtime.AddModule(clock);
            Assert.Near(1.0f / 30.0f, SimulationClock.DeltaSeconds);
            Assert.Near(50.0f / 30.0f, SimulationClock.GtaTimeStep);
            runtime.RunTicks(29);
            Assert.Equal(12, clock.Hours);
            Assert.Equal(0, clock.Minutes);
            Assert.Equal(57, clock.Seconds);
            runtime.Step();
            Assert.Equal(12, clock.Hours);
            Assert.Equal(1, clock.Minutes);
            Assert.Equal(0, clock.Seconds);
            runtime.RunTicks(30 * 60 * 24 - 30);
            Assert.Equal(12, clock.Hours);
            Assert.Equal(0, clock.Minutes);
            Assert.Equal(1L, clock.DaysPassed);
        }

        [Test]
        public void ModuleInputAudioAndPresentationOrderIsStable()
        {
            List<string> calls = new List<string>();
            SimulationRuntime runtime = new SimulationRuntime(VCDefinition.Instance,
                new TestInput(calls), new TestAudio(calls), new TestPresentation(calls));
            runtime.AddModule(new TestModule("first", calls));
            runtime.AddModule(new TestModule("second", calls));
            runtime.Step();
            string expected = "input:1,first:1:17,second:1:17,audio:1,present:1";
            Assert.Equal(expected, string.Join(",", calls.ToArray()));
            Assert.Equal(17, (int)runtime.CurrentInput.Buttons);
            Assert.Throws<InvalidOperationException>(delegate
            {
                runtime.AddModule(new TestModule("late", new List<string>()));
            });
        }

        [Test]
        public void StateHashesRepeatAndSeparateGamesAndModuleState()
        {
            string first = HashAfterTicks(IIIDefinition.Instance, 90, false);
            string second = HashAfterTicks(IIIDefinition.Instance, 90, false);
            string changed = HashAfterTicks(IIIDefinition.Instance, 90, true);
            string viceCity = HashAfterTicks(VCDefinition.Instance, 90, false);
            Assert.Equal(first, second);
            Assert.False(first == changed);
            Assert.False(first == viceCity);
            Assert.Equal(16, first.Length);
        }

        [Test]
        public void SetTimeResetsMinutePhaseAndClockConfigurationChangesHash()
        {
            SimulationRuntime runtime = new SimulationRuntime(IIIDefinition.Instance);
            GameClock clock = new GameClock();
            runtime.AddModule(clock);
            runtime.RunTicks(15);
            clock.SetTime(6, 30);
            runtime.Step();
            Assert.Equal(6, clock.Hours);
            Assert.Equal(30, clock.Minutes);
            Assert.Equal(1, clock.Seconds);

            SimulationRuntime fast = new SimulationRuntime(IIIDefinition.Instance);
            SimulationRuntime slow = new SimulationRuntime(IIIDefinition.Instance);
            fast.AddModule(new GameClock(2000));
            slow.AddModule(new GameClock(4000));
            fast.Step();
            slow.Step();
            Assert.False(fast.ComputeStateHash() == slow.ComputeStateHash());
            Assert.Throws<OverflowException>(delegate
            {
                SimulationClock.TickToMilliseconds(ulong.MaxValue);
            });
        }

        [Test]
        public void GameProfilesKeepIndependentLimitsAndSchemas()
        {
            Assert.Equal(5500, IIIDefinition.Instance.Limits.ModelInfo);
            Assert.Equal(6500, VCDefinition.Instance.Limits.ModelInfo);
            Assert.Equal("DATA/GTA3.DAT", IIIDefinition.Instance.MainDatPath);
            Assert.Equal("DATA/GTA_VC.DAT", VCDefinition.Instance.MainDatPath);
            Assert.Equal("MODELS/GTA3.IMG", VCDefinition.Instance.InitialImagePath);
            Assert.Equal(CollisionStorageKind.LevelBased, IIIDefinition.Instance.CollisionStorage);
            Assert.Equal(CollisionStorageKind.StreamedSlots, VCDefinition.Instance.CollisionStorage);
            Assert.False(ReferenceEquals(IIIDefinition.Instance.DataSchema, VCDefinition.Instance.DataSchema));
        }

        private static string HashAfterTicks(GameDefinition definition, int ticks, bool changed)
        {
            SimulationRuntime runtime = new SimulationRuntime(definition);
            runtime.AddModule(new GameClock());
            if (changed)
                runtime.AddModule(new StatefulProbe());
            runtime.RunTicks((ulong)ticks);
            return runtime.ComputeStateHash();
        }

        private sealed class TestInput : IInputSource
        {
            private readonly List<string> calls;
            internal TestInput(List<string> calls) { this.calls = calls; }
            public InputFrame ReadInput(ulong tick)
            {
                calls.Add("input:" + tick);
                InputFrame frame = new InputFrame();
                frame.Buttons = 17;
                return frame;
            }
        }

        private sealed class TestAudio : IAudioService
        {
            private readonly List<string> calls;
            internal TestAudio(List<string> calls) { this.calls = calls; }
            public void Service(SimulationTime time) { calls.Add("audio:" + time.Tick); }
        }

        private sealed class TestPresentation : IPresentationService
        {
            private readonly List<string> calls;
            internal TestPresentation(List<string> calls) { this.calls = calls; }
            public void Present(SimulationTime time) { calls.Add("present:" + time.Tick); }
        }

        private sealed class TestModule : ISimulationModule
        {
            private readonly List<string> calls;
            internal TestModule(string name, List<string> calls) { Name = name; this.calls = calls; }
            public string Name { get; private set; }
            public void Process(SimulationContext context)
            {
                calls.Add(Name + ":" + context.Time.Tick + ":" + context.Input.Buttons);
            }
        }

        private sealed class StatefulProbe : ISimulationModule, IStateSerializable
        {
            private int value;
            public string Name { get { return "StatefulProbe"; } }
            public void Process(SimulationContext context) { value++; }
            public void WriteState(StateWriter writer) { writer.WriteInt32(value); }
        }
    }
}
