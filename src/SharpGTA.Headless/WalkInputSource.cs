using SharpGTA.Runtime;

namespace SharpGTA.Headless
{
    /// <summary>
    /// Scripted "hold W" input for `--walk`: the player probe only needs a constant forward
    /// axis, and keeping it here means the headless run stays window-free and deterministic.
    /// </summary>
    internal sealed class WalkInputSource : IInputSource
    {
        internal static readonly WalkInputSource Instance = new WalkInputSource();

        private WalkInputSource()
        {
        }

        public InputFrame ReadInput(ulong tick)
        {
            InputFrame frame = new InputFrame();
            frame.MoveY = 1.0f;
            return frame;
        }
    }
}