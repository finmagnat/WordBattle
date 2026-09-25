using Core.Data;
using Core.Events;
using Core.Services;
using Cysharp.Threading.Tasks;
using TMPro;
using UI.Popups;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

namespace UI.Components
{
    public class StatisticsPanel : UIPopup
    {
        [SerializeField] protected Button _closeButton;
        [SerializeField] protected TextMeshProUGUI _startWordText;
        [SerializeField] protected StatisticPlayerPanel _statisticPlayerPlayerPanelOwner;
        [SerializeField] protected StatisticPlayerPanel _statisticPlayerPlayerPanelOpponent;
     
        internal StatisticPlayerPanel StatisticPlayerPlayerPanelOwner => _statisticPlayerPlayerPanelOwner;
        internal StatisticPlayerPanel StatisticPlayerPlayerPanelOpponent => _statisticPlayerPlayerPanelOpponent;
        
        [Inject] private AnalyticsService _analytics;
        
        private UniTaskCompletionSource<PopupExitData> _completionSource;
        private string _startWord;
        
        private void Start()
        {
            _closeButton.onClick.AddListener(async () =>
            {
                _analytics.TrackEvent(AnalyticsEvents.GameFlow.CloseHistoryGameClicked);
                await HideAsync();
                _completionSource?.TrySetResult(new PopupExitData { Result = PopupResult.Exit });
            });
        }

        internal void SetStartWord(string value)
        {
            _startWord = value;
            _startWordText.text = $"{value}   <size=100%><voffset=20><sprite name=\"question\"></voffset></size>";
        }
        
        public override async UniTask ShowAsync()
        {
            _completionSource = new UniTaskCompletionSource<PopupExitData>();
            
            await base.ShowAsync();
        }
        
        public UniTask<PopupExitData> WaitForResultAsync() => _completionSource.Task;

        public void OnStartWordPressed() => EventBus.Raise(new ShowWordInfoEvent{word = _startWord});

        internal void Reset()
        {
            _startWordText.text = "";
            _statisticPlayerPlayerPanelOwner.Reset();
            _statisticPlayerPlayerPanelOpponent.Reset();
        }
    }
}
