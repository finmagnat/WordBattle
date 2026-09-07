using System.Collections.Generic;
using Core.Config;
using Core.Data;
using Core.Services;
using Core.Services.Shop;
using Cysharp.Threading.Tasks;
using Core.Services.Inventory;
using UnityEngine;
using Zenject;

namespace UI.Popups
{
    public class RewardPopup : UIPopup<RewardPopupData>
    {
        [Header("UI Elements")]
        [SerializeField] private ShopItemView _itemPrefab;
        [SerializeField] private Transform _contentRoot;

        [Inject] private DiContainer _container;
        [Inject] private AudioService _audioService;
        [Inject] private AnalyticsService _analytics;
        [Inject] private IInventoryService _inventory;

        protected UniTaskCompletionSource<PopupExitData> _completionSource;

        private RewardPopupData _currentData;

        public UniTask<PopupExitData> WaitForResultAsync() => _completionSource.Task;

        public override UniTask PrepareAsync(RewardPopupData data)
        {
            _completionSource = new UniTaskCompletionSource<PopupExitData>();
            Bind(data);
            return UniTask.CompletedTask;
        }

        public override async UniTask ShowAsync()
        {
            await base.ShowAsync();
            _audioService?.PlaySfxAsync(SoundsConfig.PopupReward);
            _analytics.TrackEvent(AnalyticsEvents.Navigation.RewardPopupShown, GetAnalyticsParams());
        }

        public void OnCloseButtonClicked()
        {
            _analytics.TrackEvent(AnalyticsEvents.Navigation.CloseRewardClicked);
            Close().Forget();
        }

        public void OnOkButtonClicked()
        {
            _analytics.TrackEvent(AnalyticsEvents.Navigation.OkRewardClicked);
            Close().Forget();
        }

        private void Bind(RewardPopupData data)
        {
            _currentData = data ?? new RewardPopupData();
            Clear();

            foreach (var item in _currentData.Rewards)
            {
                var view = _container.InstantiatePrefabForComponent<ShopItemView>(_itemPrefab, _contentRoot);
                view.Bind(item);
            }
        }

        private async UniTask Close()
        {
            await HideAsync();
            Clear();
            _completionSource?.TrySetResult(new PopupExitData { Result = PopupResult.Exit });
        }

        private void Clear()
        {
            for (int i = _contentRoot.childCount - 1; i >= 0; i--)
                Destroy(_contentRoot.GetChild(i).gameObject);
        }

        private Dictionary<string, object> GetAnalyticsParams()
        {
            var parameters = new Dictionary<string, object>
            {
                [AnalyticsEvents.Parameter.Source] = _currentData?.Source,
                [AnalyticsEvents.Parameter.ProductId] = _currentData?.ProductId,
                [AnalyticsEvents.Parameter.Reward] = AnalyticsPayloadHelper.GetRewardsPayload(_currentData?.Rewards),
                [AnalyticsEvents.Parameter.Boosters] = AnalyticsPayloadHelper.GetBoostersPayload(_inventory.Boosters)
            };

            if (_currentData?.Source == RewardPopupData.SourceDailyBonus)
            {
                parameters[AnalyticsEvents.Parameter.Day] = _currentData.DailyBonusDay;
                parameters[AnalyticsEvents.Parameter.Jackpot] = _currentData.DailyBonusJackpot;
            }

            return parameters;
        }
    }
}
