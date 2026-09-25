using Core.Events;
using TMPro;
using UnityEngine;

namespace Core.UI.Components
{
    public class WordListItem : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI _wordText;
        
        private string _word;

        public void Initialize(string word)
        {
            _word = word;
            _wordText.text = $"<line-height=100%>\n<size=100%><voffset=18>{_word.Length} {_word}</size></voffset>   <size=120%><voffset=32><sprite name=\"question\"></voffset></size>\n</line-height>";
        }
        
        public void OnPressed() => EventBus.Raise(new ShowWordInfoEvent{word = _word});
    }
}