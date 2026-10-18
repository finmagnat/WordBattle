using System;
using System.Collections.Generic;
using Core.Config;

namespace Core.Services
{
    public class PlayFabInventoryKeys
    {
        // Inventory includes boosters that are not available as rewarded-ad rewards.
        public static IReadOnlyDictionary<BoosterType, string> All { get; } =
            new Dictionary<BoosterType, string>
            {
                [BoosterType.Letter] = "boost_letter",
                [BoosterType.Slowdown] = "boost_slow",
                [BoosterType.Eraser] = "boost_eraser",
                [BoosterType.Mixer] = "boost_mixer",
                [BoosterType.Swap] = "boost_swap",
                [BoosterType.Hint] = "boost_hint"
            };

        public static string ToKey(BoosterType type) => All.TryGetValue(type, out string key)
            ? key
            : throw new ArgumentOutOfRangeException(nameof(type), type, null);

        public static List<string> GetKeys() => new(All.Values);
    }
}
