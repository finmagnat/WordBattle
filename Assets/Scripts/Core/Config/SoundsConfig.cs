using System;
using System.Collections.Generic;
using Core.Generated;
using UnityEngine;

namespace Core.Config
{
    [CreateAssetMenu(menuName = "Game/Config/Sounds", fileName = "SoundsConfig")]
    public class SoundsConfig : ScriptableObject
    {
        public static string StartNewGame => "start_new_game";
        public static string LetterPutSuccess => "letter_put_success";
        public static string LetterSelected => "letter_selected";
        public static string LetterUnblinking => "letter_unblinking";
        public static string LetterBlinking => "letter_blinking";
        public static string IMadeMove => "i_made_move";
        public static string OpponentMadeMove => "opponent_made_move";
        public static string OpponentWon => "opponent_won";
        public static string PopupQuestion => "popup_question";
        public static string PopupWarning => "popup_warning";
        public static string OpponentFindWordFail => "opponent_find_word_fail";
        public static string Pause => "pause";
        public static string Pass => "pass";
        public static string Draw => "draw";
        public static string IWon => "i_won";
        public static string SkinChanged => "skin_changed";
        public static string ButtonClick => "button_click";
        public static string BoosterFoundWord => "booster_found_word";
        public static string BoosterNotFoundWord => "booster_not_found_word";
        public static string BoosterSlowdownLaunch => "booster_slowdown_launch";
        public static string BoosterMixerLaunch => "booster_mixer_launch";
        public static string BoosterSwapLaunch => "booster_swap_launch";
        public static string BoosterEraserLaunch => "booster_eraser_launch";
        public static string PopupReward => "popup_reward";
       
        
        [TextArea]
        public string _ = "Перетащить аудиоклип в поле Clip. Запустить плеймод и проверить звук в игре. После завершения подбора звуков перенести аудио клипы в Addressables.SFX и отключить IsUseSoundsConfig";
        
        [Tooltip("Опция для настройки звуковой схемы (true = вместо Addressables используется SoundsConfig)")]
        public bool IsUseSoundsConfig = false;
        public List<SoundData> Sounds => _sounds;
        
        [SerializeField] private List<SoundData> _sounds = new ()
        {
            new SoundData{ Id = StartNewGame, Description = "Старт игры" },
            new SoundData{ Id = LetterPutSuccess, Description = "Буква установлена на поле" },
            new SoundData{ Id = LetterSelected, Description = "Буква выделена на поле" },
            new SoundData{ Id = LetterUnblinking, Description = "Буква на поле мигает (неподсвечена)" },
            new SoundData{ Id = LetterBlinking, Description = "Буква на поле мигает (подсвечена)" },
            new SoundData{ Id = IMadeMove, Description = "Игрок сделал ход" },
            new SoundData{ Id = OpponentMadeMove, Description = "Оппонент сделал ход" },
            new SoundData{ Id = OpponentWon, Description = "Выиграл оппонент" },
            new SoundData{ Id = OpponentFindWordFail, Description = "Оппонент пропустил ход" },
            new SoundData{ Id = Pause, Description = "Пауза (квл/выкл)" },
            new SoundData{ Id = Pass, Description = "Игрок пропустил ход" },
            new SoundData{ Id = Draw, Description = "Ничья" },
            new SoundData{ Id = IWon, Description = "Выиграл игрок" },
            new SoundData{ Id = SkinChanged, Description = "Скин изменился" },
            new SoundData{ Id = ButtonClick, Description = "Клик по кнопке" },
            new SoundData{ Id = PopupReward, Description = "Попап 'Награда'" },
            new SoundData{ Id = PopupQuestion, Description = "Попап 'Вопрос'" },
            new SoundData{ Id = PopupWarning, Description = "Попап 'Совет'" },
            new SoundData{ Id = BoosterFoundWord, Description = "Бустер 'Буковка' нашел слово" },
            new SoundData{ Id = BoosterNotFoundWord, Description = "Бустер 'Буковка' не нашел слово" },
            new SoundData{ Id = BoosterSlowdownLaunch, Description = "Запуск бустера 'Замедление'" },
            new SoundData{ Id = BoosterMixerLaunch, Description = "Запуск бустера 'Миксер'" },
            new SoundData{ Id = BoosterSwapLaunch, Description = "Запуск бустера 'Менялка'" },
            new SoundData{ Id = BoosterEraserLaunch, Description = "Запуск бустера 'Ластик'" },
        };

        public string GetAddressKey(string Id)
        {
            return _sounds.Find(item => item.Id == Id).AddressKey.ToString();
        }
    }
    
    [Serializable]
    public struct SoundData
    {
        [ReadOnly]
        public string Description;
        [ReadOnly]
        public string Id;
        public AssetKey AddressKey;
        public AudioClip Clip;
    }
}
