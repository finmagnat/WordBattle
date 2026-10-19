using System;
using System.Collections.Generic;
using Core.Config;
using Core.Data;
using Cysharp.Threading.Tasks;
using Core.Services.Inventory;
using UnityEngine;
using Zenject;

namespace Core.Services
{
    /// <summary>
    /// Связка магазинного rewarded-оффера: rewarded earned -> PlayFab CloudScript AddBooster
    /// -> обновить локальный инвентарь
    /// + локальные лимиты/cooldown (после успешного начисления)
    /// + событие для обновления UI
    /// </summary>
    public sealed class RewardedBoosterGrantService : IService
    {
        [Inject] private RewardedAdsService _ads;
        [Inject] private InventorySyncService _inventorySync;
        [Inject] private RewardedLimitsService _limits;
        
        public event Action<RewardType, int> OnRewardGranted;

        public UniTask InitializeAsync() => UniTask.CompletedTask;

        private bool _pending;

        public async UniTask<bool> ShowAndGrantAsync(RewardType rewardType, IReadOnlyList<RewardDto> rewards)
        {
            if (_pending || rewardType == RewardType.None ||
                !TryCreateRewardSnapshot(rewards, out var snapshot, out _)) return false;
            _pending = true;
            var completion = new UniTaskCompletionSource<bool>();
            bool earned = false;
            bool closed = false;
            void OnClosed(RewardType type)
            {
                if (type != rewardType) return;
                closed = true;
                if (!earned) completion.TrySetResult(false);
            }
            async UniTaskVoid OnEarned()
            {
                if (earned || closed) return;
                earned = true;
                try { completion.TrySetResult(await GrantAsync(rewardType, snapshot)); }
                catch (Exception exception) { completion.TrySetException(exception); }
            }
            _ads.OnClosed += OnClosed;
            try
            {
                if (!_ads.ShowFor(rewardType, _ => OnEarned().Forget())) return false;
                return await completion.Task;
            }
            finally
            {
                _ads.OnClosed -= OnClosed;
                _pending = false;
            }
        }

        private static bool TryCreateRewardSnapshot(
            IReadOnlyList<RewardDto> rewards,
            out RewardDto[] rewardSnapshot,
            out string error)
        {
            if (rewards == null || rewards.Count == 0)
            {
                rewardSnapshot = null;
                error = "Rewarded offer has no rewards";
                return false;
            }

            rewardSnapshot = new RewardDto[rewards.Count];

            for (int i = 0; i < rewards.Count; i++)
            {
                RewardDto reward = rewards[i];
                if (reward == null || reward.ItemId == BoosterType.None || reward.Amount <= 0)
                {
                    rewardSnapshot = null;
                    error = $"Invalid reward at index {i}";
                    return false;
                }

                if (!RewardedBoosterCatalog.TryGetPlayFabKey(reward.ItemId, out _))
                {
                    rewardSnapshot = null;
                    error = $"No PlayFab inventory key configured for {reward.ItemId}";
                    return false;
                }

                rewardSnapshot[i] = new RewardDto
                {
                    ItemId = reward.ItemId,
                    Amount = reward.Amount
                };
            }

            error = null;
            return true;
        }

        private async UniTask<bool> GrantAsync(RewardType rewardType, IReadOnlyList<RewardDto> rewards)
        {
            int totalGranted = 0;

            foreach (RewardDto reward in rewards)
            {
                bool granted;

                try
                {
                    granted = await _inventorySync.GrantBoosterAsync(reward.ItemId, reward.Amount);
                }
                catch (Exception exception)
                {
                    Debug.LogError(
                        $"[Reward] Failed to grant +{reward.Amount} {reward.ItemId}: {exception}");
                    continue;
                }

                if (!granted)
                {
                    Debug.LogWarning($"[Reward] Failed to grant +{reward.Amount} {reward.ItemId}.");
                    continue;
                }

                totalGranted += reward.Amount;
                Debug.Log($"[Reward] Granted +{reward.Amount} {reward.ItemId} (server).");
            }

            if (totalGranted <= 0)
                return false;

            _limits.RegisterSuccessfulClaim(rewardType);
            OnRewardGranted?.Invoke(rewardType, totalGranted);
            return true;
        }
    }
}
