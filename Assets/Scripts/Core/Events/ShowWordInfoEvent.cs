using System;

namespace Core.Events
{
    public class ShowWordInfoEvent : IGameEvent
    {
        public string word;
        public Action onClosed;
    }
}
