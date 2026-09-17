using System;
using Cysharp.Threading.Tasks;
using PlayFab;
using PlayFab.ClientModels;

namespace Core.Services
{
    public static class PlayFabAsync
    {
        // ----------------- PlayFab async wrappers -----------------
       public static UniTask<UpdateUserTitleDisplayNameResult> UpdateUserTitleDisplayNameAsync(UpdateUserTitleDisplayNameRequest req)
           => Wrap<UpdateUserTitleDisplayNameResult>((ok, fail) => PlayFabClientAPI.UpdateUserTitleDisplayName(req, ok, fail));

       public static UniTask<GetPlayerStatisticsResult> GetPlayerStatisticsAsync(GetPlayerStatisticsRequest req)
           => Wrap<GetPlayerStatisticsResult>((ok, fail) => PlayFabClientAPI.GetPlayerStatistics(req, ok, fail));

       public static UniTask<UpdatePlayerStatisticsResult> UpdatePlayerStatisticsAsync(UpdatePlayerStatisticsRequest req)
           => Wrap<UpdatePlayerStatisticsResult>((ok, fail) => PlayFabClientAPI.UpdatePlayerStatistics(req, ok, fail));

       public static UniTask<GetLeaderboardResult> GetLeaderboardAsync(GetLeaderboardRequest req)
           => Wrap<GetLeaderboardResult>((ok, fail) => PlayFabClientAPI.GetLeaderboard(req, ok, fail));

       public static UniTask<GetLeaderboardAroundPlayerResult> GetLeaderboardAroundPlayerAsync(GetLeaderboardAroundPlayerRequest req)
           => Wrap<GetLeaderboardAroundPlayerResult>((ok, fail) => PlayFabClientAPI.GetLeaderboardAroundPlayer(req, ok, fail));

       public static UniTask<GetTitleDataResult> GetTitleDataAsync(GetTitleDataRequest req)
           => Wrap<GetTitleDataResult>((ok, fail) => PlayFabClientAPI.GetTitleData(req, ok, fail));

       private static UniTask<T> Wrap<T>(Action<Action<T>, Action<PlayFabError>> invoke)
       {
           var tcs = new UniTaskCompletionSource<T>();

           invoke(
               res => tcs.TrySetResult(res),
               err => tcs.TrySetException(new Exception($"PlayFab error: {err.Error} - {err.ErrorMessage}"))
           );

           return tcs.Task;
       }
    }
}
