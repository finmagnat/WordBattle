#if UNITY_EDITOR
#pragma warning disable CS0618 // Exercise the same legacy IAP callback used by production, without a real store.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Core.Config;
using Core.Data;
using Core.Services;
using Core.Services.Inventory;
using Core.Services.Shop;
using Cysharp.Threading.Tasks;
using PlayFab.ClientModels;
using TMPro;
using UI.Popups;
using UnityEditor;
using UnityEngine;
using UnityEngine.Purchasing;
using Zenject;
using Object = UnityEngine.Object;

namespace Tests.AutoRecovery
{
    public static partial class AutoRecoveryPlayModeSuite
    {
        private static async UniTask EconomyCases()
        {
            var shop = AssetDatabase.LoadAssetAtPath<ShopCatalog>("Assets/Configs/ShopCatalog.asset");
            var pack = shop.Offers.Single(x => x.ProductId == "wordlet_pack_best");
            Check(pack.Rewards.Count == 6 && pack.Rewards.All(x => x.Amount == 5), "Economy: common paid pack contains all six boosters x5");
            Check(shop.Offers.Single(x => x.ProductId == "wordlet_pack_hint_10").Rewards.Single().ItemId == BoosterType.Letter,
                "Economy: legacy hint_10 product is still Letter x10, not reinterpreted as Hint");
            var free = shop.Offers.Where(x => x.Type == ShopOfferType.RewardedAd).ToList();
            Check(free.Count == 6 && free.SelectMany(x => x.Rewards).Select(x => x.ItemId).Distinct().Count() == 6,
                "Economy: shop rewarded retains all five old types and adds Hint");
            Check(free.Single(x => x.RewardType == RewardType.Hint).Rewards.Single().Amount == 1 &&
                free.Single(x => x.RewardType == RewardType.Hint).DailyLimit == 3 && free.Single(x => x.RewardType == RewardType.Letter).DailyLimit == 2,
                "Economy: Hint ad amount/limits follow ordinary offers; Letter limit unchanged");
            Check(PlayFabInventoryKeys.ToKey(BoosterType.Hint) == "boost_hint" &&
                RewardedBoosterCatalog.TryGetPlayFabKey(BoosterType.Hint, out string key) && key == "boost_hint",
                "Economy: paid inventory and rewarded Hint use the same boost_hint key");
            Check(Enum.GetValues(typeof(BoosterType)).Cast<BoosterType>().Select(x => (int)x).SequenceEqual(Enumerable.Range(0, 7)),
                "Economy: all BoosterType numeric values preserved");
            var daily = AssetDatabase.LoadAssetAtPath<DailyBonusConfig>("Assets/Configs/DailyBonusConfig.asset");
            Check(daily.CycleLength == 7 && daily.GetDay(6).Rewards.Any(x => x.ItemId == BoosterType.Hint && x.Amount == 1),
                "Economy: existing sixth daily slot adds Hint x1 without changing the calendar");
            Check(daily.GetDay(7).ChestDrops.Select(x => x.Weight).SequenceEqual(new[] {70, 20, 10}) &&
                daily.GetDay(7).ChestDrops.Where(x => x.Mode == DailyBonusChestDropMode.RandomSingle).All(x => x.Pool.Count == 6 && x.Pool.Contains(BoosterType.Hint) && x.Pool.Contains(BoosterType.Letter)),
                "Economy: daily chest weights unchanged, random pools contain Hint and Letter");

            var inventory = new InventoryService();
            var sync = new EconomySync(inventory);
            var ads = new EconomyAds();
            var limits = new EconomyLimits();
            var grants = new RewardedBoosterGrantService();
            Set(grants, "_ads", ads); Set(grants, "_inventorySync", sync); Set(grants, "_limits", limits);
            var rewards = new List<RewardDto> { new() { ItemId = BoosterType.Hint, Amount = 1 } };
            var adTask = grants.ShowAndGrantAsync(RewardType.Hint, rewards);
            ads.Close();
            Check(!await adTask && sync.Grants == 0, "Economy: closing shop ad without earning grants nothing");
            ads.Ready = false;
            Check(!await grants.ShowAndGrantAsync(RewardType.Hint, rewards) && sync.Grants == 0,
                "Economy: failed ad start grants nothing");
            ads.Ready = true;
            adTask = grants.ShowAndGrantAsync(RewardType.Hint, rewards);
            Check(!await grants.ShowAndGrantAsync(RewardType.Hint, rewards), "Economy: another shop ad is rejected while its grant is pending");
            ads.Earn(); ads.Earn();
            Check(await adTask && sync.Grants == 1 && inventory.GetQuantity(BoosterType.Hint) == 1 && limits.Claims == 1,
                "Economy: duplicated shop rewarded callback produces one Hint and one limit claim");
            ads.Close();

            var gateway = new EconomyGateway(inventory, pack);
            var google = new EconomyGoogle(shop, gateway, sync, grants);
            var product = (Product)Activator.CreateInstance(typeof(Product), BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { new ProductDefinition(pack.ProductId, ProductType.Consumable), new ProductMetadata("$29.99", "Pack", "", "USD", 29.99m) }, null);
            typeof(Product).GetProperty("transactionID").SetValue(product, "test-transaction");
            var payload = UnityEngine.Purchasing.MiniJSON.Json.Serialize(new Dictionary<string, object> { ["json"] = "{\"productId\":\"wordlet_pack_best\",\"purchaseToken\":\"fake\"}", ["signature"] = "fake-signature" });
            typeof(Product).GetProperty("receipt").SetValue(product, UnityEngine.Purchasing.MiniJSON.Json.Serialize(new Dictionary<string, object> { ["Store"] = "GooglePlay", ["Payload"] = payload }));
            var args = (PurchaseEventArgs)Activator.CreateInstance(typeof(PurchaseEventArgs), BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { product }, null);
            Check(google.ProcessPurchase(args) == PurchaseProcessingResult.Pending && google.Confirmations == 0,
                "Economy: Google purchase remains pending while fake validation is incomplete");
            google.ProcessPurchase(args);
            Check(gateway.Calls == 1 && inventory.GetQuantity(BoosterType.Hint) == 1,
                "Economy: concurrent store callback does not start a second validation or local grant");
            gateway.Gate.TrySetResult();
            await UniTask.Yield(); await UniTask.Yield();
            Check(google.Confirmations == 1 && inventory.GetQuantity(BoosterType.Hint) == 6 && sync.Grants == 1,
                "Economy: one validated whole pack sync precedes confirmation, with no separate AddHint call");
            google.ProcessPurchase(args);
            Check(gateway.Calls == 1 && inventory.GetQuantity(BoosterType.Hint) == 6 && gateway.Source == "google" && gateway.ProductId == pack.ProductId,
                "Economy: duplicate completed purchase neither revalidates nor grants again");

            await NewRound(slot: "economy");
            var gameConfig = _fixture.Config.Game;
            Check(gameConfig.WinsInSeries == 3 && gameConfig.WinRewards.Select(x => x.ItemId).SequenceEqual(new[] {BoosterType.Letter, BoosterType.Slowdown, BoosterType.Eraser, BoosterType.Swap, BoosterType.Hint}) && gameConfig.WinRewards.All(x => x.Weight == 1),
                "Economy: win streak threshold and original entries/weights preserved, Hint weight1 added");
            gameConfig.WinRewards = new List<WeightedBoosterReward> { new() { ItemId = BoosterType.Hint, Amount = 1, Weight = 1 } };
            Set(_fixture.Game, "_winsInSeries", 2);
            var winMethod = _fixture.Game.GetType().GetMethod("GetWinRewardAsync", Fields);
            var win = await (UniTask<FinishRewardData>)winMethod.Invoke(_fixture.Game, new object[] { ResultGame.OwnerWin });
            Check(win.Rewards.Single().ItemId == BoosterType.Hint && win.WinsInSeriesCount == 0 && _fixture.Network.Refunds(BoosterType.Hint) == 1,
                "Economy: real win streak flow grants Hint once and resets existing counter");

            var config = _fixture.Config;
            var adsConfig = Object.Instantiate(AssetDatabase.LoadAssetAtPath<AdsConfig>("Assets/Configs/AdsConfig.asset"));
            Set(config, "_adsConfig", adsConfig);
            Check(adsConfig.AdsRewardItems.Select(x => x.Weight).SequenceEqual(new[] {1,2,3,4,5,3}) && adsConfig.AdsRewardItems.Select(x => x.BoosterType).Distinct().Count() == 6,
                "Economy: end-round pool preserves old weights1/2/3/4/5 and adds Hint weight3");
            adsConfig.AdsRewardItems = new List<AdsRewardItem> { adsConfig.AdsRewardItems.Single(x => x.BoosterType == BoosterType.Hint) };
            var finish = new AdvertisingBoosterService();
            Set(finish, "_configs", config); Set(finish, "_ads", ads); Set(finish, "_inventorySync", sync); Set(finish, "_ui", Get(_fixture, "_ui"));
            var picked = finish.GetData();
            Check(picked.BoosterType == BoosterType.Hint && picked.RewardType == RewardType.Hint && picked.Count == 1,
                "Economy: weighted end-round selector can choose Hint");
            int before = sync.Grants;
            finish.Execute(picked); ads.Close();
            Check(sync.Grants == before, "Economy: canceled end-round ad grants nothing");
            finish.Execute(picked); ads.Earn(); ads.Earn(); ads.Close();
            await UniTask.Yield(); await UniTask.Yield();
            var ui = Get(_fixture, "_ui");
            var popupData = (RewardPopupData)Get(ui, "LastReward");
            Check(sync.Grants == before + 1 && (int)Get(ui, "RewardPopups") == 1 && popupData.Rewards.Single().ItemId == BoosterType.Hint,
                "Economy: end-round duplicate earned callback grants once and opens one generic popup");

            var iconConfig = AssetDatabase.LoadAssetAtPath<BoostersIconsConfig>("Assets/Configs/BoostersIconsConfig.asset");
            Set(config, "_boostersIconsConfig", iconConfig);
            var container = (DiContainer)Get(_fixture, "_container");
            container.BindInstance(config); container.Bind<ISpriteService>().FromInstance(new EconomySprites(shop));
            var popup = _fixture.Clone<RewardPopup>("Assets/UI/Popups/RewardPopup/RewardPopup.prefab");
            var data = RewardPopupData.FromAdsReward(picked);
            await popup.PrepareAsync(data);
            await UniTask.Yield(); // Remove the prefab preview items scheduled for destruction by Bind.
            var item = popup.GetComponentInChildren<ShopItemView>(true);
            Check(((UnityEngine.UI.Image)Get(item, "_iconImage")).sprite == pack.Rewards.Single(x => x.ItemId == BoosterType.Hint).SpriteIcon &&
                ((TMP_Text)Get(item, "_countText")).text == "1" && iconConfig.GetAlias(BoosterType.Hint) == "BoosterHint",
                "Economy: real generic popup binds BoosterHint icon and correct count");
            await popup.PrepareAsync(data); await UniTask.Yield();
            Check(sync.Grants == before + 1 && _fixture.Network.Refunds(BoosterType.Hint) == 1,
                "Economy: rebuilding reward popup cannot grant inventory again");
            Check(AnalyticsPayloadHelper.GetRewardsPayload(pack.Rewards.Select(x => new RewardDto {ItemId=x.ItemId,Amount=x.Amount})).Contains("Hint") &&
                AdsAnalyticsHelper.RewardedTypeParams(RewardType.Hint)[AnalyticsEvents.Parameter.RewardType].ToString() == "Hint",
                "Economy: purchase contents and rewarded analytics identify Hint separately from Letter");
            var popupParams = (Dictionary<string, object>)typeof(RewardPopup).GetMethod("GetAnalyticsParams", Fields).Invoke(popup, null);
            Check(popupParams[AnalyticsEvents.Parameter.Reward].ToString().Contains("Hint") &&
                popupParams[AnalyticsEvents.Parameter.Source].ToString() == RewardPopupData.SourceFinishRound,
                "Economy: actual reward popup analytics reports Hint and the original finish source");
            var sprites = AssetDatabase.LoadAssetAtPath<TMP_SpriteAsset>("Assets/Sprites/UI/TextMeshPro/TMPSpriteAtlases/Generated/TMP-IconsPackge.asset");
            Check(sprites.fallbackSpriteAssets.Any(x => x && x.GetSpriteIndexFromName("hint") >= 0),
                "Economy: existing TMP icon pack resolves the Hint sprite through its fallback");
            foreach (var locale in new[] { "ru", "uk", "en-US" })
            {
                var text = AssetDatabase.LoadAssetAtPath<UnityEngine.Localization.Tables.StringTable>("Assets/Localization/INFO_Texts_"+locale+".asset").GetEntry("STARTER_BONUS_POPUP_INFO").LocalizedValue;
                var adText = AssetDatabase.LoadAssetAtPath<UnityEngine.Localization.Tables.StringTable>("Assets/Localization/AdsReward_Texts_"+locale+".asset").GetEntry("Hint_TEXT_001");
                Check(text.Contains("sprite name=\"hint\"") && adText != null && !string.IsNullOrWhiteSpace(adText.LocalizedValue),
                    "Economy: Hint starter and rewarded text localized for " + locale);
            }
            Object.Destroy(adsConfig);
        }

        private sealed class EconomyAds : RewardedAdsService
        {
            private Action<RewardType> _earned;
            private RewardType _type;
            public bool Ready = true;
            public override bool ShowFor(RewardType type, Action<RewardType> earned) { _type=type; _earned=Ready?earned:null; return Ready; }
            public void Earn() => _earned?.Invoke(_type);
            public void Close() { NotifyClosed(_type); _earned=null; }
        }
        private sealed class EconomyLimits : RewardedLimitsService
        {
            public int Claims;
            public override void RegisterSuccessfulClaim(RewardType type) => Claims++;
        }
        private sealed class EconomySync : InventorySyncService
        {
            private readonly InventoryService _inventory;
            public int Grants;
            public EconomySync(InventoryService inventory) => _inventory=inventory;
            public override async UniTask<bool> GrantBoosterAsync(BoosterType type, int amount) { await UniTask.Yield(); Grants++; _inventory.Add(type,amount); return true; }
            public override UniTask SyncFromServerAsync() => UniTask.CompletedTask;
        }
        private sealed class EconomyGateway : PlayFabShopGateway
        {
            public readonly UniTaskCompletionSource Gate = new();
            private readonly InventoryService _inventory;
            private readonly ShopOfferConfig _pack;
            public int Calls; public string Source; public string ProductId;
            public EconomyGateway(InventoryService inventory, ShopOfferConfig pack) { _inventory=inventory; _pack=pack; }
            public override async UniTask<ExecuteCloudScriptResult> GrantPackOnServerAsync(string source, string productId, string receiptJson=null, string signature=null, string currencyCode=null, int purchasePriceMinor=0, string transactionId=null, string debugSecret=null)
            {
                Calls++; Source=source; ProductId=productId; await Gate.Task;
                foreach(var reward in _pack.Rewards) _inventory.Add(reward.ItemId,reward.Amount);
                return new ExecuteCloudScriptResult { FunctionResult = new Dictionary<string,object> {{"ok",true}} };
            }
        }
        private sealed class EconomyGoogle : GooglePlayShopService
        {
            public int Confirmations;
            public EconomyGoogle(ShopCatalog catalog, PlayFabShopGateway gateway, InventorySyncService sync, RewardedBoosterGrantService grant)
                : base(catalog,gateway,sync,grant,null,null) { }
            protected override void ConfirmPurchase(Product product) => Confirmations++;
        }
        private sealed class EconomySprites : ISpriteService
        {
            private readonly ShopCatalog _shop;
            public EconomySprites(ShopCatalog shop) => _shop=shop;
            public UniTask<Sprite> GetSpriteAsync(string alias) => UniTask.FromResult(_shop.Offers.First(x=>x.ProductId=="wordlet_pack_best").Rewards.First(x=>"Booster"+x.ItemId==alias || (x.ItemId==BoosterType.Slowdown && alias=="BoosterSlowtime")).SpriteIcon);
            public bool IsLoaded(string alias) => true;
            public void Unload(string alias) { }
        }
    }
}
#endif
