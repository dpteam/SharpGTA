using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace SharpGTA.Runtime
{
    public interface ISimulationModule
    {
        string Name { get; }
        void Process(SimulationContext context);
    }

    public interface IStateSerializable
    {
        void WriteState(StateWriter writer);
    }

    public sealed class SimulationContext
    {
        internal SimulationContext(GameDefinition definition, GtaRandom random)
        {
            Definition = definition;
            Random = random;
        }

        public GameDefinition Definition { get; private set; }
        public GtaRandom Random { get; private set; }
        public SimulationTime Time { get; internal set; }
        public InputFrame Input { get; internal set; }
    }

    /// <summary>
    /// Deterministic headless-friendly module loop. Presentation and audio never own
    /// simulation state, and module registration order is explicit and stable.
    /// </summary>
    public sealed class SimulationRuntime
    {
        private readonly List<ISimulationModule> modules = new List<ISimulationModule>();
        private readonly ReadOnlyCollection<ISimulationModule> readOnlyModules;
        private readonly SimulationContext context;

        public SimulationRuntime(GameDefinition definition)
            : this(definition, null, null, null)
        {
        }

        public SimulationRuntime(GameDefinition definition, IInputSource input,
            IAudioService audio, IPresentationService presentation)
        {
            if (definition == null)
                throw new ArgumentNullException("definition");
            Definition = definition;
            Input = input ?? new NullInputSource();
            Audio = audio ?? new NullAudioService();
            Presentation = presentation ?? new NullPresentationService();
            Clock = new SimulationClock();
            Random = new GtaRandom();
            context = new SimulationContext(definition, Random);
            context.Time = Clock.Current;
            readOnlyModules = modules.AsReadOnly();
        }

        public GameDefinition Definition { get; private set; }
        public SimulationClock Clock { get; private set; }
        public GtaRandom Random { get; private set; }
        public IInputSource Input { get; private set; }
        public IAudioService Audio { get; private set; }
        public IPresentationService Presentation { get; private set; }
        public ReadOnlyCollection<ISimulationModule> Modules { get { return readOnlyModules; } }
        public InputFrame CurrentInput { get { return context.Input; } }

        public void AddModule(ISimulationModule module)
        {
            if (module == null)
                throw new ArgumentNullException("module");
            if (module.Name == null)
                throw new ArgumentException("A simulation module must have a stable name.", "module");
            if (Clock.Tick != 0)
                throw new InvalidOperationException("Modules cannot be added after simulation has started.");
            for (int i = 0; i < modules.Count; i++)
            {
                if (string.Equals(modules[i].Name, module.Name, StringComparison.Ordinal))
                    throw new ArgumentException("Duplicate simulation module name: " + module.Name, "module");
            }
            modules.Add(module);
        }

        public void RunTicks(ulong count)
        {
            if (count > ulong.MaxValue - Clock.Tick)
                throw new ArgumentOutOfRangeException("count");
            for (ulong i = 0; i < count; i++)
                Step();
        }

        public void RunUntil(ulong targetTick)
        {
            if (targetTick < Clock.Tick)
                throw new ArgumentOutOfRangeException("targetTick");
            RunTicks(targetTick - Clock.Tick);
        }

        public void Step()
        {
            ulong inputTick = Clock.Tick == ulong.MaxValue ? Clock.Tick : Clock.Tick + 1;
            context.Input = Input.ReadInput(inputTick);
            context.Time = Clock.Advance();
            for (int i = 0; i < modules.Count; i++)
                modules[i].Process(context);
            Audio.Service(context.Time);
            Presentation.Present(context.Time);
        }

        public string ComputeStateHash()
        {
            StateWriter writer = new StateWriter();
            writer.WriteString("managed-simulation-state-v2");
            writer.WriteInt32((int)Definition.Id);
            writer.WriteUInt64(Clock.Tick);
            writer.WriteUInt32(Random.State);
            for (int i = 0; i < modules.Count; i++)
            {
                writer.WriteString(modules[i].Name);
                IStateSerializable serializable = modules[i] as IStateSerializable;
                writer.WriteByte(serializable == null ? (byte)0 : (byte)1);
                if (serializable != null)
                    serializable.WriteState(writer);
            }
            return writer.ToHex();
        }
    }
}
