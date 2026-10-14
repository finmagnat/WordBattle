using UI.Components;

namespace Core.Events
{
    public class TimerWarningEvent : IGameEvent
    {
        public TimerProgressBar Timer { get; }
        public bool IsHeartbeat { get; }

        public TimerWarningEvent(TimerProgressBar timer, bool isHeartbeat)
        {
            Timer = timer;
            IsHeartbeat = isHeartbeat;
        }
    }
}
