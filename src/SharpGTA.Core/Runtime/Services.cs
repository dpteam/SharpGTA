namespace SharpGTA.Runtime
{
    public struct InputFrame
    {
        public float MoveX;
        public float MoveY;
        public float LookX;
        public float LookY;
        public uint Buttons;
    }

    public interface IInputSource
    {
        InputFrame ReadInput(ulong tick);
    }

    public interface IAudioService
    {
        void Service(SimulationTime time);
    }

    public interface IPresentationService
    {
        void Present(SimulationTime time);
    }

    public sealed class NullInputSource : IInputSource
    {
        public InputFrame ReadInput(ulong tick)
        {
            return new InputFrame();
        }
    }

    public sealed class NullAudioService : IAudioService
    {
        public void Service(SimulationTime time)
        {
        }
    }

    public sealed class NullPresentationService : IPresentationService
    {
        public void Present(SimulationTime time)
        {
        }
    }
}
