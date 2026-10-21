using System;
using System.Collections.Generic;
using Core.Data;
using UnityEditor;
using UnityEngine;

namespace Core.Config.EditorTools
{
    [CustomEditor(typeof(DailyBonusConfig))]
    public sealed class DailyBonusConfigEditor : Editor
    {
        private const string TitleDataKey = "daily_bonus_config";

        private string _jsonPreview;
        private Vector2 _scrollPosition;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var config = (DailyBonusConfig)target;
            var warnings = DailyBonusTitleDataJsonExporter.Validate(config);

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("PlayFab Title Data", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                $"Copy this JSON into Primary Title Data key '{TitleDataKey}'. Runtime Daily Bonus balance is loaded from PlayFab, not from this asset.",
                MessageType.Info);

            if (warnings.Count > 0)
                EditorGUILayout.HelpBox(string.Join("\n", warnings), MessageType.Warning);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button($"Copy {TitleDataKey} JSON"))
                {
                    RefreshPreview(config);
                    EditorGUIUtility.systemCopyBuffer = _jsonPreview;

                    foreach (var warning in warnings)
                        Debug.LogWarning($"DailyBonusConfig export warning: {warning}", config);

                    Debug.Log($"{TitleDataKey} JSON copied to clipboard.", config);
                }

                if (GUILayout.Button("Refresh Preview"))
                    RefreshPreview(config);
            }

            if (string.IsNullOrEmpty(_jsonPreview))
                return;

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUILayout.MinHeight(180));
            EditorGUILayout.TextArea(_jsonPreview, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        private void RefreshPreview(DailyBonusConfig config)
        {
            _jsonPreview = DailyBonusTitleDataJsonExporter.ToJson(config);
        }

        public static class DailyBonusTitleDataJsonExporter
        {
            public static string ToJson(DailyBonusConfig config)
            {
                var dto = new DailyBonusConfigDto
                {
                    cycleLength = Mathf.Max(1, config.CycleLength),
                    days = BuildDays(config)
                };

                return JsonUtility.ToJson(dto, true);
            }

            public static List<string> Validate(DailyBonusConfig config)
            {
                var warnings = new List<string>();

                if (config.CycleLength <= 0)
                    warnings.Add("CycleLength should be greater than zero.");

                if (config.Days == null || config.Days.Count == 0)
                {
                    warnings.Add("Days list is empty.");
                    return warnings;
                }

                var usedDays = new HashSet<int>();
                foreach (var dayConfig in config.Days)
                {
                    if (dayConfig == null)
                    {
                        warnings.Add("Days contains a null entry.");
                        continue;
                    }

                    int day = config.NormalizeDay(dayConfig.Day);
                    if (!usedDays.Add(day))
                        warnings.Add($"Day {day} is duplicated after normalization.");

                    if (dayConfig.RewardKind == DailyBonusRewardKind.Fixed)
                    {
                        ValidateRewards(dayConfig.Rewards, $"Day {day}", warnings);
                        continue;
                    }

                    ValidateChestDrops(dayConfig.ChestDrops, $"Day {day}", warnings);
                }

                return warnings;
            }

            private static List<DailyBonusDayDto> BuildDays(DailyBonusConfig config)
            {
                var result = new List<DailyBonusDayDto>();
                if (config.Days == null)
                    return result;

                foreach (var dayConfig in config.Days)
                {
                    if (dayConfig == null)
                        continue;

                    var dto = new DailyBonusDayDto
                    {
                        day = config.NormalizeDay(dayConfig.Day),
                        rewardKind = ToRewardKind(dayConfig.RewardKind)
                    };

                    if (dayConfig.RewardKind == DailyBonusRewardKind.Chest)
                        dto.chestDrops = BuildChestDrops(dayConfig.ChestDrops);
                    else
                        dto.rewards = BuildRewards(dayConfig.Rewards);

                    result.Add(dto);
                }

                return result;
            }

            private static List<DailyBonusRewardDto> BuildRewards(List<RewardDto> rewards)
            {
                var result = new List<DailyBonusRewardDto>();
                if (rewards == null)
                    return result;

                foreach (var reward in rewards)
                {
                    if (reward == null || reward.ItemId == BoosterType.None || reward.Amount <= 0)
                        continue;

                    result.Add(new DailyBonusRewardDto
                    {
                        boosterType = reward.ItemId.ToString(),
                        amount = reward.Amount
                    });
                }

                return result;
            }

            private static List<DailyBonusChestDropDto> BuildChestDrops(List<DailyBonusChestDropConfig> drops)
            {
                var result = new List<DailyBonusChestDropDto>();
                if (drops == null)
                    return result;

                foreach (var drop in drops)
                {
                    if (drop == null || drop.Weight <= 0)
                        continue;

                    var dto = new DailyBonusChestDropDto
                    {
                        weight = drop.Weight,
                        mode = ToDropMode(drop.Mode),
                        multiplier = Mathf.Max(1, drop.Multiplier)
                    };

                    if (drop.Mode == DailyBonusChestDropMode.Jackpot)
                        dto.rewards = BuildRewards(drop.Rewards);
                    else
                        dto.pool = BuildBoosterPool(drop.Pool);

                    result.Add(dto);
                }

                return result;
            }

            private static List<string> BuildBoosterPool(List<BoosterType> pool)
            {
                var result = new List<string>();
                if (pool == null)
                    return result;

                foreach (var boosterType in pool)
                {
                    if (boosterType != BoosterType.None)
                        result.Add(boosterType.ToString());
                }

                return result;
            }

            private static void ValidateRewards(List<RewardDto> rewards, string prefix, List<string> warnings)
            {
                if (rewards == null || rewards.Count == 0)
                {
                    warnings.Add($"{prefix}: rewards list is empty.");
                    return;
                }

                bool hasValidReward = false;
                foreach (var reward in rewards)
                {
                    if (reward == null)
                    {
                        warnings.Add($"{prefix}: rewards contains a null entry.");
                        continue;
                    }

                    if (reward.ItemId == BoosterType.None)
                        warnings.Add($"{prefix}: reward has ItemId None.");

                    if (reward.Amount <= 0)
                        warnings.Add($"{prefix}: reward amount should be greater than zero.");

                    hasValidReward |= reward.ItemId != BoosterType.None && reward.Amount > 0;
                }

                if (!hasValidReward)
                    warnings.Add($"{prefix}: no valid rewards will be exported.");
            }

            private static void ValidateChestDrops(
                List<DailyBonusChestDropConfig> chestDrops,
                string prefix,
                List<string> warnings)
            {
                if (chestDrops == null || chestDrops.Count == 0)
                {
                    warnings.Add($"{prefix}: chest drops list is empty.");
                    return;
                }

                bool hasValidDrop = false;
                for (int i = 0; i < chestDrops.Count; i++)
                {
                    var drop = chestDrops[i];
                    var dropPrefix = $"{prefix}, chest drop {i + 1}";

                    if (drop == null)
                    {
                        warnings.Add($"{dropPrefix}: entry is null.");
                        continue;
                    }

                    if (drop.Weight <= 0)
                    {
                        warnings.Add($"{dropPrefix}: weight should be greater than zero.");
                        continue;
                    }

                    if (drop.Mode == DailyBonusChestDropMode.Jackpot)
                    {
                        ValidateRewards(drop.Rewards, dropPrefix, warnings);
                    }
                    else
                    {
                        var pool = BuildBoosterPool(drop.Pool);
                        if (pool.Count == 0)
                            warnings.Add($"{dropPrefix}: random booster pool is empty; CloudScript will use the default pool.");

                        if (drop.Multiplier <= 0)
                            warnings.Add($"{dropPrefix}: multiplier should be greater than zero.");
                    }

                    hasValidDrop = true;
                }

                if (!hasValidDrop)
                    warnings.Add($"{prefix}: no valid chest drops will be exported.");
            }

            private static string ToRewardKind(DailyBonusRewardKind rewardKind)
            {
                return rewardKind == DailyBonusRewardKind.Chest ? "chest" : "fixed";
            }

            private static string ToDropMode(DailyBonusChestDropMode mode)
            {
                return mode == DailyBonusChestDropMode.Jackpot ? "jackpot" : "randomSingle";
            }
        }

        [Serializable]
        private sealed class DailyBonusConfigDto
        {
            public int cycleLength;
            public List<DailyBonusDayDto> days;
        }

        [Serializable]
        private sealed class DailyBonusDayDto
        {
            public int day;
            public string rewardKind;
            public List<DailyBonusRewardDto> rewards;
            public List<DailyBonusChestDropDto> chestDrops;
        }

        [Serializable]
        private sealed class DailyBonusRewardDto
        {
            public string boosterType;
            public int amount;
        }

        [Serializable]
        private sealed class DailyBonusChestDropDto
        {
            public int weight;
            public string mode;
            public int multiplier;
            public List<string> pool;
            public List<DailyBonusRewardDto> rewards;
        }
    }
}
