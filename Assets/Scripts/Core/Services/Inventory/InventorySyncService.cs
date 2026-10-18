using System;
using System.Collections.Generic;
using Core.Config;
using Cysharp.Threading.Tasks;
using PlayFab;
using PlayFab.ClientModels;
using PlayFab.Json;
using UnityEngine;
using Zenject;

namespace Core.Services.Inventory
{
    public class InventorySyncService : IService
    {
        public event Action InventoryChanged;

        [Inject] private IInventoryService _inventory;

        public bool HasServerSnapshot { get; private set; }

        public async UniTask InitializeAsync()
        {
            await SyncFromServerAsync();
        }

        /// <summary>
        /// Скачивает бустеры с PlayFab и перезаписывает локальный инвентарь.
        /// PlayFab — источник истины.
        /// </summary>
        public async UniTask SyncFromServerAsync()
        {
            var keys = PlayFabInventoryKeys.GetKeys();
            var result = await GetUserReadOnlyDataAsync(keys);

            foreach (var definition in PlayFabInventoryKeys.All)
                _inventory.SetQuantity(definition.Key, ReadInt(result.Data, definition.Value));

            HasServerSnapshot = true;
            InventoryChanged?.Invoke();
        }

        /// <summary>
        /// Пытается использовать бустер: списание делаем на сервере через CloudScript,
        /// затем обновляем локальный инвентарь по серверному ответу.
        /// </summary>
        public virtual async UniTask<bool> TryUseBoosterAsync(BoosterType type)
        {
            string key = PlayFabInventoryKeys.ToKey(type);

            // Можно сделать оптимистичную локальную проверку, чтобы не дергать сервер зря:
            if (HasServerSnapshot && _inventory.GetQuantity(type) <= 0)
            {
                Debug.LogWarning($"ConsumeBooster denied locally: booster={type}, key={key}, local_total={_inventory.GetQuantity(type)}");
                return false;
            }

            var request = new ExecuteCloudScriptRequest
            {
                FunctionName = "ConsumeBooster",
                FunctionParameter = new Dictionary<string, object>
                {
                    { "key", key },
                    { "amount", 1 }
                },
                GeneratePlayStreamEvent = true
            };

            ExecuteCloudScriptResult exec;
            try
            {
                exec = await ExecuteCloudScriptAsync(request);
            }
            catch (Exception e)
            {
                Debug.LogError($"ConsumeBooster failed: {e}");
                return false;
            }

            // CloudScript часто возвращает PlayFab.Json.JsonObject, а не Dictionary<string, object>
            var fr = exec.FunctionResult;
            if (fr == null)
            {
                Debug.LogWarning($"ConsumeBooster denied by server: booster={type}, key={key}, reason=empty_function_result");
                await SyncFromServerAsync();
                return false;
            }

            if (!TryGetBool(fr, "ok", out var ok) || !ok)
            {
                int serverTotal = TryGetInt(fr, "total", out var deniedTotal) ? deniedTotal : -1;
                Debug.LogWarning($"ConsumeBooster denied by server: booster={type}, key={key}, server_total={serverTotal}");
                await SyncFromServerAsync();
                return false;
            }

            int total = TryGetInt(fr, "total", out var t) ? t : -1;

            if (total >= 0)
            {
                _inventory.SetQuantity(type, total);
                HasServerSnapshot = true;
                InventoryChanged?.Invoke();
                return true;
            }

            // если total не пришел — ресинк
            await SyncFromServerAsync();
            return true;
        }

        /// <summary>
        /// Серверно добавляет бустеры (используется для "возврата" при неудаче ивента/алгоритма).
        /// CloudScript: AddBooster { key, amount } -> { ok, key, total }
        /// </summary>
        public virtual async UniTask<bool> GrantBoosterAsync(BoosterType type, int quantity)
        {
            if (quantity == 0)
                return true;

            var request = new ExecuteCloudScriptRequest
            {
                FunctionName = "AddBooster",
                FunctionParameter = new Dictionary<string, object>
                {
                    { "key", PlayFabInventoryKeys.ToKey(type) },
                    { "amount", quantity }
                },
                GeneratePlayStreamEvent = true
            };

            ExecuteCloudScriptResult exec;
            try
            {
                exec = await ExecuteCloudScriptAsync(request);
            }
            catch (Exception e)
            {
                Debug.LogError($"GrantBooster failed: {e}");
                return false;
            }

            var fr = exec.FunctionResult;
            if (fr == null)
            {
                await SyncFromServerAsync();
                return false;
            }

            if (!TryGetBool(fr, "ok", out var ok) || !ok)
                return false;

            int total = TryGetInt(fr, "total", out var t) ? t : -1;

            if (total >= 0)
            {
                _inventory.SetQuantity(type, total);
                HasServerSnapshot = true;
                InventoryChanged?.Invoke();
                return true;
            }

            await SyncFromServerAsync();
            return true;
        }

        // ---------- PlayFab wrappers (UniTask) ----------

        private static UniTask<GetUserDataResult> GetUserReadOnlyDataAsync(List<string> keys)
        {
            var tcs = new UniTaskCompletionSource<GetUserDataResult>();

            PlayFabClientAPI.GetUserReadOnlyData(
                new GetUserDataRequest { Keys = keys },
                r => tcs.TrySetResult(r),
                e => tcs.TrySetException(new Exception(e.GenerateErrorReport()))
            );

            return tcs.Task;
        }

        private static UniTask<ExecuteCloudScriptResult> ExecuteCloudScriptAsync(ExecuteCloudScriptRequest request)
        {
            var tcs = new UniTaskCompletionSource<ExecuteCloudScriptResult>();

            PlayFabClientAPI.ExecuteCloudScript(
                request,
                r =>
                {
                    // Если CloudScript упал внутри — PlayFab может вернуть Error в result.Error
                    if (r.Error != null)
                    {
                        // В разных версиях SDK поля отличаются, поэтому максимально безопасно:
                        var msg = r.Error.Message ?? r.Error.Message ?? "Unknown CloudScript error";
                        tcs.TrySetException(new Exception($"CloudScript error: {msg}"));
                        return;
                    }

                    tcs.TrySetResult(r);
                },
                e => tcs.TrySetException(new Exception(e.GenerateErrorReport()))
            );

            return tcs.Task;
        }

        private static int ReadInt(Dictionary<string, UserDataRecord> data, string key)
        {
            if (data != null && data.TryGetValue(key, out var record) && int.TryParse(record.Value, out int v))
                return v;

            return 0;
        }

        // ---------- CloudScript FunctionResult helpers ----------

        private static bool TryGetBool(object fr, string key, out bool value)
        {
            value = false;

            if (fr is Dictionary<string, object> d && d.TryGetValue(key, out var o))
            {
                value = Convert.ToBoolean(o);
                return true;
            }

            if (fr is JsonObject j && j.TryGetValue(key, out var o2))
            {
                value = Convert.ToBoolean(o2);
                return true;
            }

            return false;
        }

        private static bool TryGetInt(object fr, string key, out int value)
        {
            value = 0;

            if (fr is Dictionary<string, object> d && d.TryGetValue(key, out var o))
            {
                value = Convert.ToInt32(o);
                return true;
            }

            if (fr is JsonObject j && j.TryGetValue(key, out var o2))
            {
                value = Convert.ToInt32(o2);
                return true;
            }

            return false;
        }
    }
}
