using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PlayFab;
using PlayFab.ClientModels;

namespace Core.Services.Shop
{
    public class PlayFabShopGateway
    {
        public virtual UniTask<ExecuteCloudScriptResult> GrantPackOnServerAsync(
            string source,
            string productId,
            string receiptJson = null,
            string signature = null,
            string currencyCode = null,
            int purchasePriceMinor = 0,
            string transactionId = null,
            string debugSecret = null)
        {
            var tcs = new UniTaskCompletionSource<ExecuteCloudScriptResult>();

            var param = new Dictionary<string, object>
            {
                { "source", source },
                { "productId", productId },
                { "transactionId", transactionId ?? "" }
            };

            if (source == "google")
            {
                param["sessionTicket"] = PlayFabSettings.staticPlayer.ClientSessionTicket;
                param["receiptJson"] = receiptJson ?? "";
                param["signature"] = signature ?? "";
                param["currencyCode"] = currencyCode ?? "USD";
                param["purchasePrice"] = purchasePriceMinor;
            }
            else if (source == "debug")
            {
                param["debugSecret"] = debugSecret ?? "";
            }

            PlayFabClientAPI.ExecuteCloudScript(
                new ExecuteCloudScriptRequest
                {
                    FunctionName = "GrantPack",
                    FunctionParameter = param,
                    // Google parameters include a session credential; never mirror them to PlayStream.
                    GeneratePlayStreamEvent = source != "google"
                },
                r =>
                {
                    if (r.Error != null)
                    {
                        var msg = r.Error.Message ?? r.Error.Message ?? "Unknown CloudScript error";
                        tcs.TrySetException(new Exception($"CloudScript error: {msg}"));
                        return;
                    }
                    if (!(r.FunctionResult is IDictionary<string, object> payload) ||
                        !payload.TryGetValue("ok", out var accepted) || !(accepted is bool ok) || !ok)
                    {
                        tcs.TrySetException(new Exception("Pack was not accepted by the server"));
                        return;
                    }
                    tcs.TrySetResult(r);
                },
                e => tcs.TrySetException(new Exception(e.GenerateErrorReport()))
            );

            return tcs.Task;
        }
    }
}
