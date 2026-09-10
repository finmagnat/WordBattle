using System.Collections.Generic;
using Core.Data;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Core.Config
{
    [CreateAssetMenu(menuName = "Game/Config/Game Config", fileName = "GameConfig")]
    public class GameConfig : ScriptableObject
    {
        //---------------------------------
        [Header("Address")]
        public string Privacy = "https://finmagnat.github.io/wordbattle-privacy/";
        public string Terms = "https://finmagnat.github.io/wordbattle-terms/";
        public string Support = "semantica.dev@gmail.com";
        
        //---------------------------------
        [Header("UI")]
        [Tooltip("Базовое разрешение для Canvas Scaler (ландшафт)")]
        public Vector2Int referenceResolution = new Vector2Int(1080, 1920);

        [Range(0f, 1f)]
        [Tooltip("Матчинг ширины/высоты для Canvas Scaler (0=по ширине, 1=по высоте)")]
        public float screenMatch = 0.5f;

        //---------------------------------
        [Header("APPLICATION")]
        public int targetFps = 60;
        
        //---------------------------------
        [Header("SERVICES")]
        public bool enableAnalytics = true;
        public bool enableAds = true;
        public bool useDebugLogs = true;

        [Tooltip("Firebase Realtime DB URL")]
        public string firebaseRealtimeDbUrl;

        [Header("ADS (Unity Ads, при необходимости)")]
        public string unityAdsGameIdAndroid;
        public bool unityAdsTestMode = true;

        //---------------------------------
        [Header("GAMEPLAY")]
        [Tooltip("Версия игровой механики (влияет на пользовательскиие сейвы)")]
        public int version = 1;
        
        [Tooltip("Размер поля по умолчанию")]
        public uint defaultBoardSize = 5;
        
        [Space(10)]
        [Tooltip("Время хода игрока (секунды)")]
        public int durationGameSeconds = 120;
        
        [Tooltip("Минимальное время хода игрока (секунды)")]
        public const int MinDurationGameSeconds = 10;
        
        [Tooltip("Максимум пропуска ходов (в режиме игры с человеком). 0 = безлимит")]
        public uint maxPassesByDefault = 2;
        
        [Tooltip("Кулдаун между переключением паузы (секунды). 0 = выкл")]
        public int pauseCooldownSeconds = 1;
        
        [Tooltip("Задержка перед ходом ИИ (секунды). 0 = выкл")]
        public float delayAIPlaySeconds = 1.0f;
        
        [Tooltip("Задержка перед ходом ИИ после пропуска хода Игрока (секунды). 0 = выкл")]
        public float delayAIPlayAfterPlayerPassSeconds = 2.0f;
        
        //---------------------------------
        [Header("СЛОЖНОСТЬ ИГРЫ С ИИ")]
        [Tooltip("Сложность игры с ИИ по умолчанию")]
        public ComplexityAI complexityAiByDefault = ComplexityAI.Normal;
       
        [Space(10), Tooltip("Уровни сложности игры с ИИ")]
        public List<ComplexityAISettings> _сomplexityAIList;
        
        public ComplexityAISettings GetComplexityAIItem(ComplexityAI сomplexityAI) =>
            _сomplexityAIList.Find(item => item.СomplexityAiLevel == сomplexityAI);
        
        //---------------------------------
        [Header("БУСТЕРЫ")]
        [Tooltip("Время замедления игрового таймера (секунды)")]
        public int slowdownDelay;
        [Tooltip("Время перед переходом хода, после найденного бустером слова (секунды)")]
        public float autoApplyDelay = 2;
        [Tooltip("Настройки алгоритма поиска слов для бустера")]
        public ComplexityAISettings boosterLetterAiSettings;
        
        //---------------------------------
        [Header("LEADERBOARD")]
        [Tooltip("Сколько строк показывать в Top-N")]
        public int leaderboardTopN = 10;

        [Tooltip("Кэш Score (секунды), чтобы не спамить PlayFab")]
        public int scoreCacheSeconds = 15;
        
        //---------------------------------
        [Header("NEW WORDS MODERATION")]
        [Tooltip("Максимум отправок новых слов в сутки. 0 = безлимит")]
        public int newWordsDailyLimit = 5;

        [Tooltip("Кулдаун между отправками новых слов (секунды). 0 = выкл")]
        public int newWordsCooldownSeconds = 90;
        
        //---------------------------------
        [Header("REPORT WORDS MODERATION")]
        [Tooltip("Максимум отправок жалоб на слова в сутки. 0 = безлимит")]
        public int reportWordsDailyLimit = 5;

        [Tooltip("Кулдаун между отправками жалоб на слова (секунды). 0 = выкл")]
        public int reportWordsCooldownSeconds = 90;

        //---------------------------------
        [Tooltip("Минимальное время отображения загрузочного экрана с баннерами (миллисекунды)")]
        public int minLoadingScreenDurationMs = 2000;

        //---------------------------------
        [Header("REWARDS")]
        [Tooltip("Количество побед в серии для получения награды.")]
        public int WinsInSeries = 3;
        
        [Tooltip("Награды за победу")]
        public List<RewardDto> WinRewards = new ();
        public List<RewardDto> GetWinReward()
        {
            return new List<RewardDto> { WinRewards[Random.Range(0, WinRewards.Count)] };
        }
    }
}