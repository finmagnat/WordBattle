using System;
using System.Collections.Generic;
using System.Linq;
using Core.Config;
using Core.Data;
using Core.Services.Common;
using Cysharp.Threading.Tasks;
using Core.Services.Inventory;
using UnityEngine;
using UnityEngine.Purchasing;
using Zenject;

namespace Core.Services.Shop
{
    /// <summary>
    /// Покупка и выдача наград через Unity IAP (Google Play).
    /// Rewarded офферы не покупаются — они триггерят показ рекламы.
    /// </summary>
    public class GooglePlayShopService : IShopService, IStoreListener
    {
        private readonly ShopCatalog _catalog;
        private readonly PlayFabShopGateway _gateway;
        private readonly HashSet<string> _processing = new();
        private readonly HashSet<string> _completed = new();
        private readonly InventorySyncService _inventorySync;
        private readonly RewardedBoosterGrantService _rewardGrant;
        private readonly LocalizationService _localization;
        private readonly AdsEntitlementService _adsEntitlement;

        private IStoreController _controller;
        private IExtensionProvider _extensions;

        private UniTaskCompletionSource<bool> _initTcs;
        private bool _initStarted;

        private UniTaskCompletionSource<PurchaseResult> _purchaseTcs;
        private string _pendingPurchaseId;


        [Inject]
        public GooglePlayShopService(
            ShopCatalog catalog,
            PlayFabShopGateway gateway,
            InventorySyncService inventorySync,
            RewardedBoosterGrantService rewardGrant,
            LocalizationService localization,
            AdsEntitlementService adsEntitlement)
        {
            _catalog = catalog;
            _gateway = gateway;
            _inventorySync = inventorySync;
            _rewardGrant = rewardGrant;
            _localization = localization;
            _adsEntitlement = adsEntitlement;
        }

        // -------- IShopService --------

        public UniTask InitializeAsync() => UniTask.CompletedTask;

        public async UniTask<IReadOnlyList<ShopOfferDto>> GetCatalogAsync()
        {
            // Инициализируем IAP ТОЛЬКО если есть IAP офферы
            if (HasAnyIapOffers())
                await EnsureInitializedAsync();

            var list = new List<ShopOfferDto>(_catalog.Offers.Count);

            foreach (var o in _catalog.Offers)
            {
                if (!o.IsActive || (o.DisableInterstitialAds && _adsEntitlement.NoInterstitialAds))
                    continue;
                
                var dto = new ShopOfferDto
                {
                    Type = (ShopOfferTypeDto)o.Type,
                    ProductId = o.ProductId,
                    RewardType = o.RewardType,

                    Title = o.Title,
                    SpriteHeader = o.SpriteHeader,
                    Description = o.Description,

                    Rewards = o.Rewards.Select(r => new RewardDto
                    {
                        ItemId = r.ItemId,
                        Amount = r.Amount,
                    }).ToList(),

                    // По умолчанию
                    IsAvailable = o.DebugAvailable,
                    IsDisableInterstitialAds = o.DisableInterstitialAds,
                    CtaText = o.Type == ShopOfferType.RewardedAd ? _localization.Get(LocalizationConst.TableUI, LocalizationConst.KeyTextLook) : o.DebugPriceText
                };

                // Для IAP пробуем подставить реальную цену из Unity IAP
                if (o.Type == ShopOfferType.IapPack && !string.IsNullOrWhiteSpace(o.ProductId) && _controller != null)
                {
                    var product = _controller.products.WithID(o.ProductId);
                    if (product != null)
                    {
                        dto.IsAvailable = product.availableToPurchase;

                        var price = product.metadata?.localizedPriceString;
                        if (!string.IsNullOrWhiteSpace(price))
                            dto.CtaText = price;
                    }
                }

                list.Add(dto);
            }

            return list;
        }

        public async UniTask<PurchaseResult> ExecuteOfferAsync(ShopOfferDto offer)
        {
            if (offer == null)
                return PurchaseResult.Fail("Offer is null");

            switch (offer.Type)
            {
                case ShopOfferTypeDto.IapPack:
                    return await PurchaseAsync(offer.ProductId);

                case ShopOfferTypeDto.RewardedAd:
                    Debug.Log($"[ShopService] Execute rewarded: rewardType={offer.RewardType}");

                    return await _rewardGrant.ShowAndGrantAsync(offer.RewardType, offer.Rewards)
                        ? PurchaseResult.Ok() : PurchaseResult.Fail("Reward was not granted");

                default:
                    return PurchaseResult.Fail($"Unknown offer type: {offer.Type}");
            }
        }

        public async UniTask<PurchaseResult> PurchaseAsync(string productId)
        {
            await EnsureInitializedAsync();

            if (_purchaseTcs != null)
                return PurchaseResult.Fail("Purchase already in progress");

            var product = _controller.products.WithID(productId);
            if (product == null)
                return PurchaseResult.Fail($"Unknown productId: {productId}");
            if (!product.availableToPurchase)
                return PurchaseResult.Fail("Product not available");

            _pendingPurchaseId = productId;
            _purchaseTcs = new UniTaskCompletionSource<PurchaseResult>();

            var pending = _purchaseTcs.Task;
            _controller.InitiatePurchase(product);

            return await pending;
        }

        // -------- Unity IAP init --------

        private bool HasAnyIapOffers()
        {
            return _catalog.Offers.Any(o =>
                o.Type == ShopOfferType.IapPack &&
                !string.IsNullOrWhiteSpace(o.ProductId));
        }

        private UniTask EnsureInitializedAsync()
        {
            if (_controller != null)
                return UniTask.CompletedTask;

            _initTcs ??= new UniTaskCompletionSource<bool>();

            if (!_initStarted)
            {
                _initStarted = true;

                var module = StandardPurchasingModule.Instance(AppStore.GooglePlay);
                var builder = ConfigurationBuilder.Instance(module);

                // Добавляем ТОЛЬКО IAP офферы
                foreach (var o in _catalog.Offers)
                {
                    if (o.Type != ShopOfferType.IapPack) continue;
                    if (string.IsNullOrWhiteSpace(o.ProductId)) continue;
                    
                    var type = o.IsNonConsumable ? ProductType.NonConsumable : ProductType.Consumable;
                    builder.AddProduct(o.ProductId, type);
                }

                UnityPurchasing.Initialize(this, builder);
            }

            return _initTcs.Task.AsUniTask();
        }

        // -------- IStoreListener --------

        public void OnInitialized(IStoreController controller, IExtensionProvider extensions)
        {
            _controller = controller;
            _extensions = extensions;

            _initTcs?.TrySetResult(true);
            _initTcs = null;
        }

        public void OnInitializeFailed(InitializationFailureReason error)
        {
            _initTcs?.TrySetException(new Exception($"IAP init failed: {error}"));
            _initTcs = null;
        }

        public void OnInitializeFailed(InitializationFailureReason error, string message)
        {
            _initTcs?.TrySetException(new Exception($"IAP init failed: {error}. {message}"));
            _initTcs = null;
        }

        public PurchaseProcessingResult ProcessPurchase(PurchaseEventArgs e)
        {
            // Validation, the whole server pack and inventory sync precede store consumption.
            // Catalog rewards are never granted locally.
            var product = e.purchasedProduct;
            string transaction = product.transactionID;
            if (string.IsNullOrEmpty(transaction))
            {
                CompletePurchase(product.definition.id, PurchaseResult.Fail("Missing transaction ID"));
                return PurchaseProcessingResult.Pending;
            }
            if (_completed.Contains(transaction)) return PurchaseProcessingResult.Complete;
            if (_processing.Add(transaction)) ValidateAndGrantAsync(product, transaction).Forget();
            return PurchaseProcessingResult.Pending;
        }

        private async UniTaskVoid ValidateAndGrantAsync(Product product, string transaction)
        {
            string productId = product.definition.id;
            try
            {
                var pack = _catalog.Offers.FirstOrDefault(p =>
                    p.Type == ShopOfferType.IapPack && p.ProductId == productId);
                if (pack == null) throw new InvalidOperationException("Unknown store product: " + productId);
                var receipt = GoogleReceiptExtractor.Extract(product);
                await _gateway.GrantPackOnServerAsync("google", productId,
                    receipt.receiptJson, receipt.signature, product.metadata.isoCurrencyCode,
                    decimal.ToInt32(decimal.Round(product.metadata.localizedPrice * 100)), transaction);
                await _inventorySync.SyncFromServerAsync();
                if (pack.DisableInterstitialAds) _adsEntitlement.SetNoInterstitialAdsLocal(true);
                ConfirmPurchase(product);
                _completed.Add(transaction);
                CompletePurchase(productId, PurchaseResult.Ok());
            }
            catch (Exception exception)
            {
                // Store redelivery can retry a failed or unverified transaction.
                Debug.LogWarning("[IAP] Purchase remains pending: " + productId + ": " + exception.Message);
                CompletePurchase(productId, PurchaseResult.ErrorResult(exception.Message));
            }
            finally { _processing.Remove(transaction); }
        }

        protected virtual void ConfirmPurchase(Product product) => _controller.ConfirmPendingPurchase(product);

        private void CompletePurchase(string productId, PurchaseResult result)
        {
            if (_purchaseTcs == null || _pendingPurchaseId != productId) return;
            _purchaseTcs.TrySetResult(result);
            _purchaseTcs = null;
            _pendingPurchaseId = null;
        }

        public void OnPurchaseFailed(Product product, PurchaseFailureReason failureReason)
        {
            Debug.LogError($"[IAP] Purchase failed: {product?.definition?.id} reason={failureReason}");

            if (_purchaseTcs != null)
            {
                _purchaseTcs.TrySetResult(PurchaseResult.Fail(failureReason.ToString()));
                _purchaseTcs = null;
                _pendingPurchaseId = null;
            }
        }

    }
}
