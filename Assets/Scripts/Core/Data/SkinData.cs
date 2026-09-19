using System;
using System.Collections.Generic;
using Core.Config;
using UnityEngine;

namespace Core.Data
{
    
    public enum SkinSpriteKey
    {
        None = 0,
        
        //"Фоны, панели, рамки, скролы"
        MainBackgroundAlias = 1, // Главный фон для экранов/попапов
        PlayerPanelBackgroundAlias = 2, // Фон панели игрока
        FrameBackgroundAlias = 3, // Фон рамки панелей/полей
        HandleBackgroundAlias = 4, // Фон ползунка на скролах
        ProgressBackgroundAlias = 5, // Фон прогресбара
        BoosterBackgroundAlias = 6, // Фон бустера
        
        //"Кнопки меню домашнего экрана"
        SettingsButtonAlias = 8,
        SkinButtonAlias = 9,
        InfoButtonAlias = 10,
        ShopButtonAlias = 11,
        
        //"Игровое поле"
        CellBackgroundDefaultAlias = 12, // Фон ячейки поля по умолчанию (темный)
        CellBackgroundFilledAlias = 13, // Фон ячейки поля с установленной буквой (светлый) 
        CellSelectedAlias = 14, // Фон выбранной ячейки поля (оранжевый)
        LettersSelectedAlias = 15, // Фон выделенных букв на поле (желтый)
        
        //"Клавиатура"
        KeyboardTileAlias = 16, // Фон кнопки на клавиатуре
        
        //"Кнопки игрового экрана"
        HomeButtonAlias = 17, // Домой
        OptionsButtonAlias = 18, // Опции
        PauseButtonAlias = 19, // Пауза 
        CancelButtonAlias = 20, // Отменить
        GoButtonAlias = 21, // Применить
        PassButtonAlias = 22, // Пропустить
        RepeatGameButtonAlias = 23, // Играть снова
        StatisticButtonAlias = 25, // Статистика
        
        //"Кнопки и фон домашнего экрана и попапов"
        DefaultButtonAlias = 26,
    }

    public enum SkinPrefabKey
    {
        None = 0,
        
        HomeBackgroundAlias = 1, //"Главный фон для домашнего экрана"
    }

    public enum SkinColorKey
    {
        None = 0,
        
        LettersFieldColor = 1, // Цвет букв на поле 
        KeyboardLetterColor = 2, // Цвет кнопки на клавиатуре
    }
    
    [Serializable]
    public struct SkinData
    {
        public SkinType SkinType;

        public string SkinPreviewAlias; // Кнопки превьюшек скинов в SkinsPopup
        
        public List<SkinSpriteEntry> Sprites;
        public List<SkinPrefabEntry> Prefabs;
        public List<SkinColorEntry> Colors;
    }

    [Serializable]
    public struct SkinSpriteEntry
    {
        public SkinSpriteKey Key;
        public string Alias;
    }

    [Serializable]
    public struct SkinPrefabEntry
    {
        public SkinPrefabKey Key;
        public string Alias;
    }

    [Serializable]
    public struct SkinColorEntry
    {
        public SkinColorKey Key;
        public Color Value;
    }
    
}
