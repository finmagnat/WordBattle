using System.IO;
using Core.Data;
using Cysharp.Threading.Tasks;
using Game.Logic;
using UnityEngine;
using Zenject;

namespace Core.Services
{
    /*
     * Локальное сохранение игры.
     */
    
    public class SaveService : ISaveService
    {
        [Inject] private GameController _gameController;
        [Inject] private ConfigService _configService;
        [Inject] private LocalizationService _localization;
        
        private const string FileName = "saved_game.json";
        private string _directoryPath =>
            Path.Combine(Application.persistentDataPath, _localization.CurrentLocale.Identifier.Code);

        private string _filePath => Path.Combine(_directoryPath, FileName);

        public UniTask InitializeAsync() => UniTask.CompletedTask;

        public bool HasSave() => File.Exists(_filePath);

        public async UniTask SaveAsync()
        {
            SaveGameData data = _gameController.GetGameData();
                
            var json = SaveGameStorage.Serialize(data);
            Directory.CreateDirectory(_directoryPath);
            await File.WriteAllTextAsync(_filePath, json);

            Debug.Log($"💾 Saved current game: {_filePath}\n{json}");
        }

        public async UniTask<SaveGameData> LoadAsync()
        {
            if (!HasSave())
                return null;

            var json = await File.ReadAllTextAsync(_filePath);
            var data = SaveGameStorage.Deserialize(json);

            Debug.Log($"📂 Loaded current game: {_filePath}");
            return data;
        }

        public UniTask ClearAsync()
        {
            if (HasSave())
                File.Delete(_filePath);
            return UniTask.CompletedTask;
        }

    }
}
