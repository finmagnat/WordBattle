using System;
using System.Collections.Generic;
using System.Linq;
using Core.Config;
using Core.Data;
using Cysharp.Threading.Tasks;
using Core.Services.Inventory;
using Zenject;

namespace Core.Services.Shop
{
    public sealed class StubShopService : IShopService
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private const string DEBUG_SECRET = "SOME_LONG_RANDOM_SECRET_123";
#else
        private const string DEBUG_SECRET = "";
#endif
        
        private readonly ShopCatalog _catalog;
        private readonly PlayFabShopGateway _gateway;
        private readonly InventorySyncService _inventorySync;
        private readonly RewardedBoosterGrantService _rewardGrant;
        private readonly LocalizationService _localization;
        private readonly AdsEntitlementService _adsEntitlement;
        
        [Inject]
        public StubShopService(ConfigService configService, PlayFabShopGateway gateway,
            InventorySyncService inventorySync, 
            RewardedBoosterGrantService rewardGrant,
            LocalizationService localization,
            AdsEntitlementService adsEntitlement)
        {
            _catalog = configService.Shop;
            _gateway = gateway;
            _inventorySync = inventorySync;
            _rewardGrant = rewardGrant;
            _localization = localization;
            _adsEntitlement = adsEntitlement;
        }

        public UniTask InitializeAsync() => UniTask.CompletedTask;

        public UniTask<IReadOnlyList<ShopOfferDto>> GetCatalogAsync()
        {
            var list = new List<ShopOfferDto>(_catalog.Offers.Count);

            foreach (var o in _catalog.Offers)
            {
                if (!o.IsActive || (o.DisableInterstitialAds && _adsEntitlement.NoInterstitialAds))
                    continue;

                list.Add(new ShopOfferDto
                {
                    Type = (ShopOfferTypeDto)o.Type,
                    ProductId = o.ProductId,
                    RewardType = o.RewardType,
                    Title = o.Title,
                    SpriteHeader = o.SpriteHeader,
                    Description = o.Description,
                    CtaText = o.Type == ShopOfferType.IapPack
                        ? o.DebugPriceText
                        : _localization.Get(LocalizationConst.TableUI, LocalizationConst.KeyTextLook),
                    IsAvailable = o.DebugAvailable,
                    IsDisableInterstitialAds = o.DisableInterstitialAds,
                    Rewards = o.Rewards.Select(r => new RewardDto { ItemId = r.ItemId, Amount = r.Amount }).ToList()
                });
            }

            return UniTask.FromResult((IReadOnlyList<ShopOfferDto>)list);
        }
        
        public async UniTask<PurchaseResult> ExecuteOfferAsync(ShopOfferDto offer)
        {
            if (offer == null) return PurchaseResult.Fail("Offer is null");
            
            switch (offer.Type)
            {
                case ShopOfferTypeDto.IapPack:
                    return await PurchaseAsync(offer.ProductId);

                case ShopOfferTypeDto.RewardedAd:
                    return await _rewardGrant.ShowAndGrantAsync(offer.RewardType, offer.Rewards)
                        ? PurchaseResult.Ok() : PurchaseResult.Fail("Reward was not granted");

                default:
                    return PurchaseResult.Fail("Unknown offer type");
            }
        }


        public async UniTask<PurchaseResult> PurchaseAsync(string productId)
        {
            var pack = _catalog.Offers.FirstOrDefault(p => p.ProductId == productId);
            if (pack == null) return PurchaseResult.Fail("Pack not found");
            if (!pack.DebugAvailable) return PurchaseResult.Fail("Not available");

            try
            {
#if !(UNITY_EDITOR || DEVELOPMENT_BUILD)
                return PurchaseResult.Fail("Debug purchases disabled");
#endif

                await _gateway.GrantPackOnServerAsync(
                    source: "debug",
                    productId: productId,
                    transactionId: Guid.NewGuid().ToString("N"),
                    debugSecret: DEBUG_SECRET
                );

                if (pack.DisableInterstitialAds) _adsEntitlement.SetNoInterstitialAdsLocal(true);

                // подтягиваем серверный источник истины
                await _inventorySync.SyncFromServerAsync();

                return PurchaseResult.Ok();

            }
            catch (Exception e)
            {
                return PurchaseResult.ErrorResult(e.Message);
            }
        }

    }
}
