using System;
using System.Collections.Generic;
using UnityEngine;

namespace Core.Config
{
    [CreateAssetMenu(menuName = "Game/Shop/Shop Catalog", fileName = "ShopCatalog")]
    public sealed class ShopCatalog : ScriptableObject
    {
        public const string RemoveInterstitialProductId = "remove_interstitial_ads";
        
        [Header("Config Service")]
        public bool EnablePurchasePushToServer = true;

        [Header("Offers")]
        public List<ShopOfferConfig> Offers = new();
    }

    public enum ShopOfferType
    {
        IapPack = 0,
        RewardedAd = 1
    }

    [Serializable]
    public sealed class ShopOfferConfig
    {
        [Header("Common")]
        public bool IsActive = true;
        
        public ShopOfferType Type;
        public string Title;
        
        public Sprite SpriteHeader;
        [TextArea] public string Description;
        public List<ShopRewardConfig> Rewards = new();

        [Header("IAP only")]
        public string ProductId; // только для IAP

        // Заглушка для Этапа 1 (пока стора нет)
        public string DebugPriceText = "—";
        public bool DebugAvailable = true;

        [Header("Rewarded Ad only")]
        public RewardType RewardType;   // RewardedBoosterCatalog defines supported rewarded booster rewards.
        public int DailyLimit = 20;     // лимит на “фарм”
        public int CooldownSeconds = 60; // анти-спам
        
        [Header("Interstitial Ads only")]
        public bool DisableInterstitialAds; // ✅ Если игрок купит этот оффер — мы должны выставить entitlement на отключение interstitial (только interstitial)
        public bool IsNonConsumable = true; // ✅ для remove ads обычно true
    }

    [Serializable]
    public sealed class ShopRewardConfig
    {
        public BoosterType ItemId;
        public int Amount;
        public Sprite SpriteIcon;
    }
    
}
