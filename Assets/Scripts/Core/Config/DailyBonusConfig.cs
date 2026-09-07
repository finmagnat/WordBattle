using System;
using System.Collections.Generic;
using Core.Data;
using UnityEngine;

namespace Core.Config
{
    [CreateAssetMenu(menuName = "Game/Config/Daily Bonus TitleData Template", fileName = "DailyBonusConfig")]
    public class DailyBonusConfig : ScriptableObject
    {
        [Header("Authoring template only")]
        [TextArea]
        public string Note =
            "Runtime Daily Bonus balance is loaded from PlayFab Title Data key 'daily_bonus_config'. " +
            "Use the inspector button to copy JSON for PlayFab Primary Title Data.";

        [Min(1)] public int CycleLength = 7;

        public List<DailyBonusDayConfig> Days = new()
        {
            DailyBonusDayConfig.Fixed(1, BoosterType.Letter, 1),
            DailyBonusDayConfig.Fixed(2, BoosterType.Slowdown, 1),
            DailyBonusDayConfig.Fixed(3, BoosterType.Eraser, 1),
            DailyBonusDayConfig.Fixed(4, BoosterType.Swap, 1),
            DailyBonusDayConfig.Fixed(5, BoosterType.Letter, 1),
            DailyBonusDayConfig.Fixed(6, BoosterType.Eraser, 1),
            DailyBonusDayConfig.Chest(7)
        };

        public DailyBonusDayConfig GetDay(int day)
        {
            int normalizedDay = NormalizeDay(day);

            foreach (var dayConfig in Days)
            {
                if (dayConfig != null && dayConfig.Day == normalizedDay)
                    return dayConfig;
            }

            return null;
        }

        public int GetNextDay(int day)
        {
            int normalizedDay = NormalizeDay(day);
            return normalizedDay >= CycleLength ? 1 : normalizedDay + 1;
        }

        public int NormalizeDay(int day)
        {
            if (CycleLength <= 0)
                return 1;

            if (day <= 0)
                return 1;

            int zeroBased = (day - 1) % CycleLength;
            return zeroBased + 1;
        }
    }

    public enum DailyBonusRewardKind
    {
        Fixed = 0,
        Chest = 1
    }

    [Serializable]
    public sealed class DailyBonusDayConfig
    {
        [Range(1, 7)] public int Day = 1;
        public DailyBonusRewardKind RewardKind = DailyBonusRewardKind.Fixed;
        public List<RewardDto> Rewards = new();
        public List<DailyBonusChestDropConfig> ChestDrops = new();

        public static DailyBonusDayConfig Fixed(int day, BoosterType itemId, int amount)
        {
            return new DailyBonusDayConfig
            {
                Day = day,
                RewardKind = DailyBonusRewardKind.Fixed,
                Rewards = new List<RewardDto>
                {
                    new() { ItemId = itemId, Amount = amount }
                }
            };
        }

        public static DailyBonusDayConfig Chest(int day)
        {
            return new DailyBonusDayConfig
            {
                Day = day,
                RewardKind = DailyBonusRewardKind.Chest,
                ChestDrops = new List<DailyBonusChestDropConfig>
                {
                    DailyBonusChestDropConfig.RandomSingleBooster(80, 1),
                    DailyBonusChestDropConfig.RandomSingleBooster(15, 2),
                    DailyBonusChestDropConfig.RandomSingleBooster(4, 3),
                    DailyBonusChestDropConfig.Jackpot(1)
                }
            };
        }
    }

    [Serializable]
    public sealed class DailyBonusChestDropConfig
    {
        [Min(1)] public int Weight = 1;
        public DailyBonusChestDropMode Mode = DailyBonusChestDropMode.RandomSingle;
        [Min(1)] public int Multiplier = 1;
        public List<BoosterType> Pool = new();
        public List<RewardDto> Rewards = new();

        public static DailyBonusChestDropConfig RandomSingleBooster(int weight, int multiplier)
        {
            return new DailyBonusChestDropConfig
            {
                Weight = weight,
                Mode = DailyBonusChestDropMode.RandomSingle,
                Multiplier = multiplier,
                Pool = CreateAllBoosterPool()
            };
        }

        public static DailyBonusChestDropConfig Jackpot(int weight)
        {
            return new DailyBonusChestDropConfig
            {
                Weight = weight,
                Mode = DailyBonusChestDropMode.Jackpot,
                Multiplier = 1,
                Rewards = CreateAllBoosterRewards(1)
            };
        }

        private static List<BoosterType> CreateAllBoosterPool()
        {
            return new List<BoosterType>
            {
                BoosterType.Letter,
                BoosterType.Eraser,
                BoosterType.Slowdown,
                BoosterType.Mixer,
                BoosterType.Swap
            };
        }

        private static List<RewardDto> CreateAllBoosterRewards(int amount)
        {
            return new List<RewardDto>
            {
                new() { ItemId = BoosterType.Letter, Amount = amount },
                new() { ItemId = BoosterType.Eraser, Amount = amount },
                new() { ItemId = BoosterType.Slowdown, Amount = amount },
                new() { ItemId = BoosterType.Mixer, Amount = amount },
                new() { ItemId = BoosterType.Swap, Amount = amount }
            };
        }
    }

    public enum DailyBonusChestDropMode
    {
        RandomSingle = 0,
        Jackpot = 1
    }
}
