using Core.Events;
using Cysharp.Threading.Tasks;
using UI.Popups;

namespace UI.Components
{
    public class KeyboardPanel : UIPopup
    {
        private void Start()
        {
            EventBus.Subscribe<KeyboardLetterSelectEvent>(OnKeyBoardLetterSelect);
        }

        private void OnDestroy()
        {
            EventBus.Unsubscribe<KeyboardLetterSelectEvent>(OnKeyBoardLetterSelect);
        }
        
        private void OnKeyBoardLetterSelect(KeyboardLetterSelectEvent obj)
        {
            HideAsync().Forget();
        }
    }
}