using System;
using System.Collections.Generic;
using Core.Config;
using UnityEngine;

namespace Core.Data
{
    [Serializable]
    public struct SkinData
    {
        public SkinType SkinType;
        public Color ColorPreviewTile; // Цвет тайла в окне выбора скина 
        
        [Header("Фоны, панели, рамки, скролы")]
        public string MainBackgroundAlias; // Главный фон для экранов/попапов
        public string PlayerPanelBackgroundAlias; // Фон панели игрока
        public string FrameBackgroundAlias; // Фон рамки панелей/полей
        public string HandleBackgroundAlias; // Фон ползунка на скролах
        public string ProgressBackgroundAlias; // Фон прогресбара
        
        [Header("Игровое поле")]
        public string CellBackgroundDefaultAlias; // Фон ячейки поля по умолчанию (темный)
        public string CellBackgroundFilledAlias; // Фон ячейки поля с установленной буквой (светлый) 
        public string CellSelectedAlias; // Фон выбранной ячейки поля (оранжевый)
        public string LettersSelectedAlias; // Фон выделенных букв на поле (желтый)
        public Color LettersFieldColor; // Цвет букв на поле 
        
        [Header("Клавиатура")]
        public string KeyboardTileAlias; // Фон кнопки на клавиатуре
        public Color KeyboardLetterColor; // Цвет кнопки на клавиатуре
        
        [Header("Кнопки игрового экрана")]
        public string ButtonsBkgAlias;
        public string HomeButtonAlias; // Домой
        public string OptionsButtonAlias; // Опции
        public string PauseButtonAlias; // Пауза 
        public string CancelButtonAlias; // Отменить
        public string GoButtonAlias; // Применить
        public string PassButtonAlias; // Пропустить
        public string RepeatGameButtonAlias; // Играть снова
        public string StatisticButtonAlias; // Статистика
        
        [Header("Кнопки и фон домашнего экрана и попапов")]
        public string DefaultButtonAlias;
        
        public MainScreenThemeData MainScreenTheme;

        [Header("Новое runtime-ядро скинов")]
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
    
    [Serializable]
    public class MainScreenThemeData
    {
        [Header("Главный фон для домашнего экрана")]
        public string HomeBackgroundAlias;
        
        [Header("Кнопки меню домашнего экрана")]
        public string ButtonsBkgAlias;
        public string SettingsButtonAlias;
        public string SkinButtonAlias;
        public string InfoButtonAlias;
        public string ShopButtonAlias;
    }
}
