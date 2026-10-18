using Core.Config;
using Core.Services;
using Core.Services.Inventory;
using Cysharp.Threading.Tasks;
using PlayFab;
using UnityEngine;
using Zenject;

namespace Core.DebugTools
{
    public sealed class SRDebugGameSettingsBridge : MonoBehaviour
    {
        public static SRDebugGameSettingsBridge Instance { get; private set; }

        [Inject] private ConfigService _configService;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        [Inject] private InventorySyncService _inventorySync;

        public void GrantLetterBoosters() => GrantBoosterAsync(BoosterType.Letter).Forget();
        public void GrantHintBoosters() => GrantBoosterAsync(BoosterType.Hint).Forget();

        private async UniTask GrantBoosterAsync(BoosterType type)
        {
            if (!PlayFabClientAPI.IsClientLoggedIn())
            {
                Debug.LogWarning("[Debug Inventory] Log in to PlayFab before granting boosters.");
                return;
            }

            bool granted = await _inventorySync.GrantBoosterAsync(type, 5);
            Debug.Log($"[Debug Inventory] {type}: +5, success={granted}");
        }
#endif

        public int DurationGameSeconds
        {
            get => GameDurationSettings.GetDurationGameSeconds(_configService != null ? _configService.Game : null);
            set => GameDurationSettings.SetDurationGameSeconds(value);
        }

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }
    }
}
