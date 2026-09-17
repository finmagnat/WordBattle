#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using Cysharp.Threading.Tasks;
using PlayFab;
using UnityEngine.Networking;

namespace Core.Services.DataDictionary.Editor
{
    public sealed class DictionaryPatchAdminClient
    {
        public const string SecretEnvironmentVariable = "PLAYFAB_DEV_SECRET_KEY";

        private const int RequestTimeoutSeconds = 15;

        public static bool HasSecretKey =>
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SecretEnvironmentVariable));

        public async UniTask<TitleDataReadResult> GetTitleDataAsync(string titleId, string key)
        {
            var body = new GetTitleDataRequestBody
            {
                Keys = new[] { key }
            };

            AdminApiResponse<GetTitleDataResponseData> response =
                await SendAsync<GetTitleDataRequestBody, GetTitleDataResponseData>(
                    titleId,
                    "GetTitleData",
                    body);

            if (response.data?.Data != null && response.data.Data.TryGetValue(key, out string value))
                return new TitleDataReadResult(true, value);

            return new TitleDataReadResult(false, string.Empty);
        }

        public async UniTask SetTitleDataAsync(string titleId, string key, string value)
        {
            var body = new SetTitleDataRequestBody
            {
                Key = key,
                Value = value
            };

            await SendAsync<SetTitleDataRequestBody, EmptyResponseData>(
                titleId,
                "SetTitleData",
                body);
        }

        private static async UniTask<AdminApiResponse<TResponse>> SendAsync<TRequest, TResponse>(
            string titleId,
            string operation,
            TRequest body)
        {
            string secretKey = Environment.GetEnvironmentVariable(SecretEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(secretKey))
                throw new InvalidOperationException($"Environment variable {SecretEnvironmentVariable} is missing.");

            if (string.IsNullOrWhiteSpace(titleId))
                throw new InvalidOperationException("PlayFab Title ID is empty.");

            string url = $"https://{titleId.Trim()}.playfabapi.com/Admin/{operation}";
            ISerializerPlugin serializer = PluginManager.GetPlugin<ISerializerPlugin>(PluginContract.PlayFab_Serializer);
            byte[] payload = Encoding.UTF8.GetBytes(serializer.SerializeObject(body));

            using var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(payload),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = RequestTimeoutSeconds
            };

            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("X-SecretKey", secretKey);

            try
            {
                await request.SendWebRequest().ToUniTask();
            }
            catch (Exception exception)
            {
                string transportDetails = !string.IsNullOrWhiteSpace(request.downloadHandler?.text)
                    ? TryReadErrorMessage(serializer, request.downloadHandler.text)
                    : exception.Message;
                throw new InvalidOperationException(
                    $"PlayFab Admin/{operation} failed (HTTP {request.responseCode}): {transportDetails}");
            }

            string responseText = request.downloadHandler?.text ?? string.Empty;
            AdminApiResponse<TResponse> response = null;
            if (!string.IsNullOrWhiteSpace(responseText))
            {
                try
                {
                    response = serializer.DeserializeObject<AdminApiResponse<TResponse>>(responseText);
                }
                catch
                {
                    // The transport error below remains understandable even when PlayFab returned non-JSON content.
                }
            }

            if (request.result != UnityWebRequest.Result.Success || response?.code != 200)
            {
                string details = !string.IsNullOrWhiteSpace(response?.errorMessage)
                    ? response.errorMessage
                    : request.error;
                throw new InvalidOperationException(
                    $"PlayFab Admin/{operation} failed (HTTP {request.responseCode}): {details}");
            }

            return response;
        }

        private static string TryReadErrorMessage(ISerializerPlugin serializer, string responseText)
        {
            try
            {
                AdminApiResponse<object> errorResponse =
                    serializer.DeserializeObject<AdminApiResponse<object>>(responseText);
                return !string.IsNullOrWhiteSpace(errorResponse?.errorMessage)
                    ? errorResponse.errorMessage
                    : "PlayFab returned an error response.";
            }
            catch
            {
                return "PlayFab returned an unreadable error response.";
            }
        }

        public readonly struct TitleDataReadResult
        {
            public TitleDataReadResult(bool exists, string value)
            {
                Exists = exists;
                Value = value;
            }

            public bool Exists { get; }
            public string Value { get; }
        }

        [Serializable]
        private sealed class GetTitleDataRequestBody
        {
            public string[] Keys;
        }

        [Serializable]
        private sealed class SetTitleDataRequestBody
        {
            public string Key;
            public string Value;
        }

        [Serializable]
        private sealed class AdminApiResponse<T>
        {
            public int code;
            public string status;
            public T data;
            public string error;
            public string errorMessage;
        }

        [Serializable]
        private sealed class GetTitleDataResponseData
        {
            public Dictionary<string, string> Data;
        }

        [Serializable]
        private sealed class EmptyResponseData
        {
        }
    }
}
#endif
