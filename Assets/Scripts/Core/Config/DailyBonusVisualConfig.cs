using System;
using System.Collections.Generic;
using UnityEngine;

namespace Core.Config
{
    [CreateAssetMenu(menuName = "Game/Config/Daily Bonus Visual Config", fileName = "DailyBonusVisualConfig")]
    public class DailyBonusVisualConfig : ScriptableObject
    {
        [Header("Localization keys")]
        public string activeRewardTitle = "DAILY_BONUS_ACTIVE_REWARD_TITLE";
        public string dayNumberTitle = "DAILY_BONUS_DAY_NUMBER_TITLE";
        
        [Header("Header sprites")]
        public Sprite activeRewardHeader;
        public Sprite dayNumberHeader;
        
        [Header("Bonus sprites")]
        public Sprite chestSprite;
        
        public List<RewardItem> boosterSprites;

        public RewardItem GetItemByType(BoosterType type) =>
            boosterSprites.Find(item => item.type == type);
    }
    
    [Serializable]
    public sealed class RewardItem
    {
        public BoosterType type;
        public Sprite iconImage;
    }
}