namespace Core.Config
{
    public enum GameOpponent
    {
        AI = 1,
        Friend
    }
    
    public enum ComplexityAI
    {
        Easy = 1,
        Normal,
        Hard,
        Master
    }

    public enum ResultGame
    {
        Draw,
        OwnerWin,
        OwnerLose
    }

    public enum GameError
    {
        None = 0, // Ошибок нет
        NoLetterInstalled, // Не установлена новая буква на поле
        SetLetterNoSelected, // Установленная буква не использована в слове
        WordNoSelected, // Не выделено ни одной буквы на поле
        WordAlreadyBeen // Такое слово уже было в текущем сеансе игры
    }

    public enum PopupType
    {
        MessageBoxApplicationRestart,
    }
    
    public enum SkinType
    {
        Blue = 1,       
        Pink
    }

    public enum SkinSpriteKey
    {
        None = 0,
        Example = 1,
    }

    public enum SkinPrefabKey
    {
        None = 0,
        Example = 1,
    }

    public enum SkinColorKey
    {
        None = 0,
        Example = 1,
    }
    
    public enum BannerType
    {
        UseBusters = 1,
        PlayVsAI,
        BusterLetter,
        BusterSlowdown,
        BusterEraser,
        BusterSwap,
    }
    
    public enum BoosterType
    {
        None = 0,
        Letter,      // “Буковка”
        Slowdown,    // “Замедлялка”
        Eraser,      // “Ластик”
        Mixer,       // “Миксер”
        Swap,        // “Менялка”
    }
    
    public enum RewardType
    {
        None = 0,
        Letter,
        Slowdown,
        Eraser, 
        Mixer,
        Swap, 
    }
}
