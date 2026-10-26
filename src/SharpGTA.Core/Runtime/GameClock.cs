using System;

namespace SharpGTA.Runtime
{
    /// <summary>GTA-style game clock driven by exact deterministic tick indices.</summary>
    public sealed class GameClock : ISimulationModule, IStateSerializable
    {
        private readonly uint millisecondsPerGameMinute;
        private ulong consumedSimulationMilliseconds;
        private ulong simulationMilliseconds;

        public GameClock()
            : this(1000)
        {
        }

        public GameClock(uint millisecondsPerGameMinute)
        {
            if (millisecondsPerGameMinute == 0)
                throw new ArgumentOutOfRangeException("millisecondsPerGameMinute");
            this.millisecondsPerGameMinute = millisecondsPerGameMinute;
            Hours = 12;
        }

        public string Name { get { return "GameClock"; } }
        public int Hours { get; private set; }
        public int Minutes { get; private set; }
        public int Seconds { get; private set; }
        public long DaysPassed { get; private set; }
        public long TotalGameMinutes { get; private set; }

        public void SetTime(int hours, int minutes)
        {
            if (hours < 0 || hours >= 24)
                throw new ArgumentOutOfRangeException("hours");
            if (minutes < 0 || minutes >= 60)
                throw new ArgumentOutOfRangeException("minutes");
            Hours = hours;
            Minutes = minutes;
            Seconds = 0;
            consumedSimulationMilliseconds = simulationMilliseconds;
        }

        public void Process(SimulationContext context)
        {
            if (context == null)
                throw new ArgumentNullException("context");
            simulationMilliseconds = SimulationClock.TickToMilliseconds(context.Time.Tick);
            while (consumedSimulationMilliseconds <= simulationMilliseconds &&
                millisecondsPerGameMinute <= simulationMilliseconds - consumedSimulationMilliseconds)
            {
                consumedSimulationMilliseconds += millisecondsPerGameMinute;
                TotalGameMinutes++;
                Minutes++;
                if (Minutes == 60)
                {
                    Minutes = 0;
                    Hours++;
                    if (Hours == 24)
                    {
                        Hours = 0;
                        DaysPassed++;
                    }
                }
            }
            ulong withinMinute = simulationMilliseconds - consumedSimulationMilliseconds;
            Seconds = (int)(withinMinute * 60UL / millisecondsPerGameMinute);
        }

        public int MinutesUntil(int hours, int minutes)
        {
            int now = Hours * 60 + Minutes;
            int then = hours * 60 + minutes;
            if (then < now)
                then += 24 * 60;
            return then - now;
        }

        public bool IsTimeInRange(int startHour, int endHour)
        {
            if (startHour > endHour)
                return Hours >= startHour || Hours < endHour;
            return Hours >= startHour && Hours < endHour;
        }

        public void WriteState(StateWriter writer)
        {
            if (writer == null)
                throw new ArgumentNullException("writer");
            writer.WriteUInt32(millisecondsPerGameMinute);
            writer.WriteInt32(Hours);
            writer.WriteInt32(Minutes);
            writer.WriteInt32(Seconds);
            writer.WriteInt64(DaysPassed);
            writer.WriteInt64(TotalGameMinutes);
            writer.WriteUInt64(consumedSimulationMilliseconds);
            writer.WriteUInt64(simulationMilliseconds);
        }
    }
}
