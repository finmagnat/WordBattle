using Core.Config;
using Core.Services;
using UnityEngine;
using UnityEngine.EventSystems;
using Zenject;

namespace Core.UI.Components
{
    public class SoundClickHandler : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField] private string _uniqueSfxKey;
        private string _defaultSfxKey = SoundsConfig.ButtonClick;
        
        [Inject] private AudioService _audioService;
        
        public void OnPointerClick(PointerEventData eventData)
        {
            _audioService?.PlaySfxAsync(string.IsNullOrEmpty(_uniqueSfxKey) ? _defaultSfxKey : _uniqueSfxKey);
        }
        
        public void OnPointerClick()
        {
            _audioService?.PlaySfxAsync(string.IsNullOrEmpty(_uniqueSfxKey) ? _defaultSfxKey : _uniqueSfxKey);
        }
    }
}