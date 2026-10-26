using System;

namespace SharpGTA.Runtime
{
    /// <summary>One deterministic 30 Hz driver frame, not a claim about the original variable timer.</summary>
    public struct SimulationTime
    {
        internal SimulationTime(ulong tick, double elapsedSeconds, uint milliseconds)
        {
            Tick = tick;
            DeltaSeconds = SimulationClock.DeltaSeconds;
            TimeStep = SimulationClock.GtaTimeStep;
            ElapsedSeconds = elapsedSeconds;
            Milliseconds = milliseconds;
        }

        public ulong Tick { get; private set; }
        public float DeltaSeconds { get; private set; }
        public float TimeStep { get; private set; }
        public double ElapsedSeconds { get; private set; }
        public uint Milliseconds { get; private set; }
    }

    public sealed class SimulationClock
    {
        public const int TicksPerSecond = 30;
        public const float DeltaSeconds = 1.0f / 30.0f;
        public const float GtaTimeStep = 50.0f / 30.0f;

        public ulong Tick { get; private set; }
        public double ElapsedSeconds { get; private set; }

        public SimulationTime Current
        {
            get { return CreateTime(Tick); }
        }

        public SimulationTime Advance()
        {
            if (Tick == ulong.MaxValue)
                throw new InvalidOperationException("Simulation tick counter overflow.");
            SimulationTime next = CreateTime(Tick + 1);
            Tick = next.Tick;
            ElapsedSeconds = next.ElapsedSeconds;
            return next;
        }

        public static ulong TickToMilliseconds(ulong tick)
        {
            ulong seconds = tick / (ulong)TicksPerSecond;
            ulong fraction = (tick % (ulong)TicksPerSecond) * 1000UL / (ulong)TicksPerSecond;
            if (seconds > (ulong.MaxValue - fraction) / 1000UL)
                throw new OverflowException("Simulation tick is too large to convert to milliseconds.");
            return seconds * 1000UL + fraction;
        }

        private static SimulationTime CreateTime(ulong tick)
        {
            ulong milliseconds = TickToMilliseconds(tick) + 1UL;
            return new SimulationTime(tick, tick / (double)TicksPerSecond,
                milliseconds > uint.MaxValue ? uint.MaxValue : (uint)milliseconds);
        }
    }
}
