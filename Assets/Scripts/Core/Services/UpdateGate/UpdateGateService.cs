using System;
using System.Collections.Generic;
using Core.Build;
using Core.Generated;
using Core.UI;
using Cysharp.Threading.Tasks;
using PlayFab.ClientModels;
using UI.Popups;
using UnityEngine;

namespace Core.Services.UpdateGate
{
    public sealed class UpdateGateService
    {
        public const string MinimumSupportedBuildTitleDataKey = "MinimumSupportedBuild_Android";

        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

        private readonly IUIManager _uiManager;

        public UpdateGateService(IUIManager uiManager)
        {
            _uiManager = uiManager;
        }

        public async UniTask<bool> IsUpdateRequiredAsync()
        {
            int currentBuild = BuildInfo.AndroidVersionCode;
            if (currentBuild <= 0)
            {
                Debug.LogWarning(
                    $"[UpdateGate] Current Android build number is invalid ({currentBuild}). " +
                    "Startup will continue to avoid blocking users.");
                return false;
            }

            GetTitleDataResult result;
            try
            {
                result = await PlayFabAsync.GetTitleDataAsync(new GetTitleDataRequest
                {
                    Keys = new List<string> { MinimumSupportedBuildTitleDataKey }
                }).Timeout(RequestTimeout);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"[UpdateGate] Failed to get PlayFab Title Data: {exception.Message}. " +
                    "Startup will continue.");
                return false;
            }

            if (!TryReadMinimumSupportedBuild(result, out int minimumSupportedBuild, out string validationError))
            {
                Debug.LogWarning($"[UpdateGate] {validationError} Startup will continue.");
                return false;
            }

            bool updateRequired = RequiresUpdate(currentBuild, minimumSupportedBuild);
            Debug.Log(
                $"[UpdateGate] Current build: {currentBuild}, minimum supported build: " +
                $"{minimumSupportedBuild}, update required: {updateRequired}.");
            return updateRequired;
        }

        public async UniTask ShowBlockingPopupAsync()
        {
            var popup = await _uiManager.ShowPopupAsync<ForceUpdatePopup>(AssetKey.ForceUpdatePopup);
            if (popup == null)
                Debug.LogError("[UpdateGate] Failed to show the mandatory update popup.");
        }

        public static bool RequiresUpdate(int currentBuild, int minimumSupportedBuild)
        {
            return currentBuild < minimumSupportedBuild;
        }

        public static bool TryReadMinimumSupportedBuild(
            GetTitleDataResult result,
            out int minimumSupportedBuild,
            out string error)
        {
            minimumSupportedBuild = 0;

            if (result?.Data == null
                || !result.Data.TryGetValue(MinimumSupportedBuildTitleDataKey, out string rawValue))
            {
                error = $"Title Data key '{MinimumSupportedBuildTitleDataKey}' is missing.";
                return false;
            }

            if (!int.TryParse(rawValue, out minimumSupportedBuild) || minimumSupportedBuild < 0)
            {
                error = $"Title Data key '{MinimumSupportedBuildTitleDataKey}' has invalid value " +
                        $"'{rawValue}'. Expected a non-negative integer.";
                return false;
            }

            error = string.Empty;
            return true;
        }
    }
}
