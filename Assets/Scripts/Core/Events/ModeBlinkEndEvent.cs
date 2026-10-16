using UI.Components;

namespace Core.Events
{
    public class ModeBlinkEndEvent : IGameEvent
    {
        public SelectableLetter letter;
        public int moveGeneration;
    }
}
