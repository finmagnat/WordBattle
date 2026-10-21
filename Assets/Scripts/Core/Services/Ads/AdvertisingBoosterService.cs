using System;
using Core.Config;
using Core.Data;
using Core.Generated;
using Core.Services.Inventory;
using Core.UI;
using Cysharp.Threading.Tasks;
using UI.Popups;
using UnityEngine;
using Zenject;

namespace Core.Services
{
    public sealed class AdvertisingBoosterService : IService
    {
        [Inject] private RewardedAdsService _ads;
        [Inject] private IConfigService _configs;
        [Inject] private IUIManager _ui;
        [Inject] private InventorySyncService _inventorySync;
        
        public UniTask InitializeAsync()
        {

            return UniTask.CompletedTask;
        }

        public AdsRewardItem GetData()
        {
            int totalWeight = 0;

            foreach (AdsRewardItem item in _configs.Ads.AdsRewardItems)
                totalWeight += Mathf.Max(0, item.Weight);

            if (totalWeight == 0)
                throw new InvalidOperationException(
                    "At least one reward must have a positive weight.");

            int roll = UnityEngine.Random.Range(0, totalWeight);

            foreach (AdsRewardItem item in _configs.Ads.AdsRewardItems)
            {
                int weight = Mathf.Max(0, item.Weight);

                if (roll < weight)
                    return item;

                roll -= weight;
            }

            throw new InvalidOperationException("Failed to select a reward.");
        }

        public bool Execute(AdsRewardItem data)
        {
            if (data == null)
            {
                Debug.LogWarning("[AdvertisingBooster] Cannot show an ad for a null reward.");
                return false;
            }

            if (data.RewardType == RewardType.None || data.BoosterType == BoosterType.None || data.Count <= 0)
            {
                Debug.LogWarning(
                    $"[AdvertisingBooster] Invalid reward config: rewardType={data.RewardType}, " +
                    $"boosterType={data.BoosterType}, count={data.Count}.");
                return false;
            }

            if (!RewardedBoosterCatalog.TryGetPlayFabKey(data.BoosterType, out _))
            {
                Debug.LogError(
                    $"[AdvertisingBooster] No PlayFab inventory key configured for {data.BoosterType}. " +
                    "The ad will not be shown because its reward cannot be granted.");
                return false;
            }

            bool granted = false;
            var snapshot = new AdsRewardItem
            {
                RewardType = data.RewardType, BoosterType = data.BoosterType,
                Count = data.Count, Weight = data.Weight, LabelLocaleKeys = data.LabelLocaleKeys
            };
            return _ads.ShowFor(snapshot.RewardType, _ =>
            {
                if (granted) return;
                granted = true;
                GrantRewardAsync(snapshot).Forget();
            });
        }

        private async UniTaskVoid GrantRewardAsync(AdsRewardItem data)
        {
            try
            {
                bool granted = await _inventorySync.GrantBoosterAsync(data.BoosterType, data.Count);

                if (granted)
                {
                    Debug.Log($"[AdvertisingBooster] Granted +{data.Count} {data.BoosterType} (server).");
                    await _ui.ShowPopupAsync<RewardPopup, RewardPopupData>(AssetKey.RewardPopup, RewardPopupData.FromAdsReward(data));
                    return;
                }

                Debug.LogWarning($"[AdvertisingBooster] Failed to grant +{data.Count} {data.BoosterType}.");
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"[AdvertisingBooster] Failed to grant +{data.Count} {data.BoosterType}: {exception}");
            }
        }
    }
}
