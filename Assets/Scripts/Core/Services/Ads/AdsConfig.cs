using System;
using System.Collections.Generic;
using Core.Config;
using UnityEngine;

namespace Core.Services
{
    public enum AdsEnvironment
    {
        Test,
        Production
    }

    [Serializable]
    public sealed class AdsRewardItem
    {
        public BoosterType BoosterType;
        public RewardType RewardType;
        public int Count;

        [Min(0), Tooltip("More weight - more chance of showing")]
        public int Weight;

        public string[] LabelLocaleKeys;
    }

    [CreateAssetMenu(menuName = "Configs/AdsConfig")]
    public sealed class AdsConfig : ScriptableObject
    {
        [Header("Environment")]
        [TextArea]
        public string _ = "Test          ← Internal / Closed Testing\n    Production    ← Google Play Production";
        
        public AdsEnvironment Environment = AdsEnvironment.Test;

        [Header("AdMob Rewarded Ad Unit")]
        public string RewardedAd;

        [Header("AdMob Interstitial Ad Unit")]
        public bool InterstitialIsActive;
        public string InterstitialAd;

        [Header("Test Mode")]
        public string TestAd = "ca-app-pub-3940256099942544/5224354917";

        [Header("Rewarded Booster Offers (finish popups)")]
        public List<AdsRewardItem> AdsRewardItems = new();

        public string GetRewardedId()
        {
            return Environment == AdsEnvironment.Test
                ? TestAd
                : RewardedAd;
        }
    }
}