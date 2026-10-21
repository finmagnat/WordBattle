using System;
using Core.Config;
using Core.Events;
using Cysharp.Threading.Tasks;
using GoogleMobileAds.Api;
using UnityEngine;
using Zenject;

namespace Core.Services
{
    public class RewardedAdsService : IService
    {
        private RewardedAd _ad;
        private bool _loading;
        private bool _showing;
        private float _loadStartedAt;
        private RewardType _activeRewardType;
        private bool _rewardEarnedInCurrentShow;

        [Inject] private IConfigService _configs;
        [Inject] private AnalyticsService _analytics;
        private AdsConfig Ads => _configs.Ads;

        public event Action<RewardType> OnRewardEarned;
        public event Action<RewardType, bool> OnAvailabilityChanged;
        public event Action<RewardType, bool> OnShowingChanged;
        public event Action<RewardType> OnClosed;

        public UniTask InitializeAsync()
        {
#if UNITY_ANDROID || UNITY_IOS
            EnsureSharedLoaded(RewardType.None, AnalyticsEvents.Option.Initial);
#else
            Debug.Log("[Ads] Skipping rewarded init on this platform.");
#endif
            return UniTask.CompletedTask;
        }

        public bool IsLoading(RewardType type)
            => IsSupported(type) && _loading;

        public bool IsReady(RewardType type)
            => IsSupported(type) && !_showing && _ad != null && _ad.CanShowAd();

        public bool IsShowing(RewardType type)
            => IsSupported(type) && _showing;

        public void EnsureLoaded(RewardType type, string loadReason = AnalyticsEvents.Option.Initial)
        {
            if (!IsSupported(type))
                return;

            EnsureSharedLoaded(type, loadReason);
        }

        public bool ShowFor(RewardType rewardType)
            => ShowFor(rewardType, null);

        public virtual bool ShowFor(RewardType rewardType, Action<RewardType> onRewardEarned)
        {
            if (!IsSupported(rewardType))
                return false;

            bool isReady = IsReady(rewardType);
            _analytics.TrackEvent(
                AnalyticsEvents.Ads.RewardedShowAttempt,
                AdsAnalyticsHelper.RewardedAttemptParams(rewardType, isReady));

            if (!isReady)
            {
                Debug.Log($"[Ads] Shared rewarded not ready for {rewardType}.");
                _analytics.TrackEvent(
                    AnalyticsEvents.Ads.RewardedShowFailed,
                    AdsAnalyticsHelper.RewardedErrorParams(rewardType, AnalyticsEvents.Option.NotReady));

                if (!_showing)
                    EnsureSharedLoaded(rewardType, AnalyticsEvents.Option.ReloadOnDemand);

                NotifyAvailabilityChanged(false);
                return false;
            }

            var ad = _ad;
            bool rewardGranted = false;
            var expectedRewardType = rewardType;

            _activeRewardType = expectedRewardType;
            _rewardEarnedInCurrentShow = false;
            _showing = true;
            NotifyShowingChanged(true);
            NotifyAvailabilityChanged(false);

            Debug.Log($"[Ads] Showing shared rewarded for: {expectedRewardType}");
            EventBus.Raise(new AdsOverlayAcquireEvent());

            try
            {
                ad.Show(reward =>
                {
                    if (rewardGranted || !_showing || _activeRewardType != expectedRewardType)
                        return;

                    rewardGranted = true;
                    _rewardEarnedInCurrentShow = true;

                    Debug.Log($"[Ads] Reward earned. expected={expectedRewardType}, adRewardType={reward?.Type}, adRewardAmount={reward?.Amount}");
                    _analytics.TrackEvent(
                        AnalyticsEvents.Ads.RewardedEarned,
                        AdsAnalyticsHelper.RewardedTypeParams(expectedRewardType));

                    if (onRewardEarned != null)
                        onRewardEarned(expectedRewardType);
                    else
                        OnRewardEarned?.Invoke(expectedRewardType);
                });

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Ads] Failed to show shared rewarded for {expectedRewardType}: {ex}");
                _analytics.TrackEvent(
                    AnalyticsEvents.Ads.RewardedShowFailed,
                    AdsAnalyticsHelper.RewardedErrorParams(expectedRewardType, AdsAnalyticsHelper.NormalizeError(ex.ToString())));

                FinishShow(expectedRewardType, AnalyticsEvents.Option.ReloadAfterFail);
                return false;
            }
        }

        private void EnsureSharedLoaded(RewardType requestedType, string loadReason)
        {
            if (_loading || _showing)
                return;

            if (_ad != null && _ad.CanShowAd())
            {
                NotifyAvailabilityChanged(true);
                return;
            }

            LoadRewarded(requestedType, loadReason);
        }

        private void LoadRewarded(RewardType requestedType, string loadReason)
        {
            var adUnitId = Ads.GetRewardedId();
            if (string.IsNullOrWhiteSpace(adUnitId))
            {
                Debug.LogError("[Ads] Shared rewarded ad unit id is empty in AdsConfig.");
                _analytics.TrackEvent(
                    AnalyticsEvents.Ads.RewardedLoadFailed,
                    AdsAnalyticsHelper.RewardedLoadFailedParams(requestedType, AnalyticsEvents.Option.InvalidRequest, loadReason));
                NotifyAvailabilityChanged(false);
                return;
            }

            _loadStartedAt = Time.realtimeSinceStartup;
            _loading = true;
            _analytics.TrackEvent(
                AnalyticsEvents.Ads.RewardedLoadStart,
                AdsAnalyticsHelper.RewardedLoadParams(requestedType, loadReason));
            NotifyAvailabilityChanged(false);

            _ad?.Destroy();
            _ad = null;

            Debug.Log("[Ads] Loading shared rewarded...");
            var request = new AdRequest();

            RewardedAd.Load(adUnitId, request, (ad, error) =>
            {
                _loading = false;

                if (error != null || ad == null)
                {
                    Debug.LogWarning($"[Ads] Failed to load shared rewarded: {error}");
                    _analytics.TrackEvent(
                        AnalyticsEvents.Ads.RewardedLoadFailed,
                        AdsAnalyticsHelper.RewardedLoadFailedParams(
                            requestedType,
                            AdsAnalyticsHelper.NormalizeError(error?.ToString()),
                            loadReason));
                    NotifyAvailabilityChanged(false);
                    return;
                }

                _ad = ad;
                HookFullScreenEvents(ad);

                Debug.Log("[Ads] Shared rewarded loaded.");
                _analytics.TrackEvent(
                    AnalyticsEvents.Ads.RewardedLoadSuccess,
                    AdsAnalyticsHelper.RewardedLoadSuccessParams(
                        requestedType,
                        AdsAnalyticsHelper.ElapsedMs(_loadStartedAt),
                        loadReason));

                if (!_showing)
                    NotifyAvailabilityChanged(true);
            });
        }

        private void HookFullScreenEvents(RewardedAd ad)
        {
            ad.OnAdFullScreenContentOpened += () =>
            {
                if (_activeRewardType == RewardType.None)
                    return;

                _analytics.TrackEvent(
                    AnalyticsEvents.Ads.RewardedShowStart,
                    AdsAnalyticsHelper.RewardedTypeParams(_activeRewardType));
            };

            ad.OnAdFullScreenContentClosed += () =>
            {
                if (!_showing || _activeRewardType == RewardType.None)
                    return;

                var completedRewardType = _activeRewardType;
                Debug.Log($"[Ads] Shared rewarded closed for {completedRewardType}. Reloading...");
                _analytics.TrackEvent(
                    AnalyticsEvents.Ads.RewardedClosed,
                    AdsAnalyticsHelper.RewardedClosedParams(_rewardEarnedInCurrentShow, completedRewardType));

                FinishShow(completedRewardType, AnalyticsEvents.Option.ReloadAfterClose);
            };

            ad.OnAdFullScreenContentFailed += error =>
            {
                if (!_showing || _activeRewardType == RewardType.None)
                    return;

                var failedRewardType = _activeRewardType;
                Debug.LogWarning($"[Ads] Shared rewarded fullscreen failed for {failedRewardType}: {error}");
                _analytics.TrackEvent(
                    AnalyticsEvents.Ads.RewardedShowFailed,
                    AdsAnalyticsHelper.RewardedErrorParams(
                        failedRewardType,
                        AdsAnalyticsHelper.NormalizeError(error?.ToString())));

                FinishShow(failedRewardType, AnalyticsEvents.Option.ReloadAfterFail);
            };
        }

        private void FinishShow(RewardType rewardType, string reloadReason)
        {
            if (!_showing || _activeRewardType != rewardType)
                return;

            EventBus.Raise(new AdsOverlayReleaseEvent());

            _ad?.Destroy();
            _ad = null;
            _showing = false;
            _activeRewardType = RewardType.None;
            _rewardEarnedInCurrentShow = false;

            NotifyShowingChanged(false);
            NotifyAvailabilityChanged(false);
            NotifyClosed(rewardType);
            EnsureSharedLoaded(rewardType, reloadReason);
        }

        protected void NotifyClosed(RewardType type) => OnClosed?.Invoke(type);

        private static bool IsSupported(RewardType type)
            => RewardedBoosterCatalog.TryGetBoosterType(type, out _);

        private void NotifyAvailabilityChanged(bool isReady)
        {
            foreach (var definition in RewardedBoosterCatalog.All)
                OnAvailabilityChanged?.Invoke(definition.RewardType, isReady);
        }

        private void NotifyShowingChanged(bool isShowing)
        {
            foreach (var definition in RewardedBoosterCatalog.All)
                OnShowingChanged?.Invoke(definition.RewardType, isShowing);
        }
    }
}
