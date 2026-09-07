using System;
using System.Collections.Generic;
using Core.Generated;
using Unity.Collections;
using UnityEngine;

namespace Core.Config
{
    [CreateAssetMenu(menuName = "Game/Config/Sounds", fileName = "SoundsConfig")]
    public class SoundsConfig : ScriptableObject
    {
        public static string StartNewGame => nameof(AssetKey.sfx_start_new_game);
        public static string LetterPutSuccess => nameof(AssetKey.sfx_letter_put_success);
        public static string LetterSelected => nameof(AssetKey.sfx_letter_selected);
        public static string LetterUnblinking => nameof(AssetKey.sfx_letter_unblinking);
        public static string LetterBlinking => nameof(AssetKey.sfx_letter_blinking);
        public static string IMadeMove => nameof(AssetKey.sfx_i_made_move);
        public static string OpponentMadeMove => nameof(AssetKey.sfx_opponent_made_move);
        public static string OpponentWon => nameof(AssetKey.sfx_opponent_won);
        public static string PopupQuestion => nameof(AssetKey.sfx_popup_question);
        public static string PopupWarning => nameof(AssetKey.sfx_popup_warning);
        public static string OpponentFindWordFail => nameof(AssetKey.sfx_opponent_find_word_fail);
        public static string Pause => nameof(AssetKey.sfx_pause);
        public static string Pass => nameof(AssetKey.sfx_pass);
        public static string Draw => nameof(AssetKey.sfx_draw);
        public static string IWon => nameof(AssetKey.sfx_i_won);
        public static string SkinChanged => nameof(AssetKey.sfx_skin_changed);
        public static string ButtonClick => nameof(AssetKey.sfx_button_click);
        public static string BoosterFoundWord => nameof(AssetKey.sfx_booster_found_word);
        public static string BoosterNotFoundWord => nameof(AssetKey.sfx_booster_not_found_word);
        public static string BoosterSlowdownLaunch => nameof(AssetKey.sfx_booster_slowdown_launch);
        public static string PopupReward => nameof(AssetKey.sfx_popup_reward);
       
        
        [TextArea]
        public string _ = "Перетащить аудиоклип в поле Clip. Запустить плеймод и проверить звук в игре. После завершения подбора звуков перенести аудио клипы в Addressables.SFX и отключить IsUseSoundsConfig";
        
        [Tooltip("Опция для настройки звуковой схемы (true = вместо Addressables используется SoundsConfig)")]
        public bool IsUseSoundsConfig = false;
        public List<SoundData> Sounds => _sounds;
        
        [SerializeField] private List<SoundData> _sounds = new ()
        {
            new SoundData{ Key = StartNewGame, Description = "Старт игры" },
            new SoundData{ Key = LetterPutSuccess, Description = "Буква установлена на поле" },
            new SoundData{ Key = LetterSelected, Description = "Буква выделена на поле" },
            new SoundData{ Key = LetterUnblinking, Description = "Буква на поле мигает (неподсвечена)" },
            new SoundData{ Key = LetterBlinking, Description = "Буква на поле мигает (подсвечена)" },
            new SoundData{ Key = IMadeMove, Description = "Игрок сделал ход" },
            new SoundData{ Key = OpponentMadeMove, Description = "Оппонент сделал ход" },
            new SoundData{ Key = OpponentWon, Description = "Выиграл оппонент" },
            new SoundData{ Key = OpponentFindWordFail, Description = "Оппонент не нашел букву и время вышло (пропуск хода)" },
            new SoundData{ Key = Pause, Description = "Пауза (квл/выкл)" },
            new SoundData{ Key = Pass, Description = "Игрок пропустил ход" },
            new SoundData{ Key = Draw, Description = "Ничья" },
            new SoundData{ Key = IWon, Description = "Выиграл игрок" },
            new SoundData{ Key = SkinChanged, Description = "Скин изменился" },
            new SoundData{ Key = ButtonClick, Description = "Клик по кнопке" },
            new SoundData{ Key = BoosterFoundWord, Description = "Бустер 'Буковка' нашел слово" },
            new SoundData{ Key = BoosterNotFoundWord, Description = "Бустер 'Буковка' не нашел слово" },
            new SoundData{ Key = BoosterSlowdownLaunch, Description = "Запуск бустера 'Замедление'" },
            new SoundData{ Key = PopupReward, Description = "Попап с наградой'" },
            new SoundData{ Key = PopupQuestion, Description = "Попап с вопросом" },
            new SoundData{ Key = PopupWarning, Description = "Отображение попапа 'Совет'" },
        };

        
    }
    
    [Serializable]
    public struct SoundData
    {
        [ReadOnly]
        public string Key;
        public string Description;
        public AudioClip Clip;
    }
}
