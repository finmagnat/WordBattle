using Core.Services;
using TMPro;
using UnityEngine;
using Zenject;

namespace UI.Components
{
    public class PlayerPanel : MonoBehaviour
    {
        public uint Score { get; private set; }
        public uint Pass { get; private set; }
        public string PlayerName => _playerNameText.text;

        [SerializeField] private TextMeshProUGUI _playerNameText;
        [SerializeField] private TextMeshProUGUI _scoreText;
        [SerializeField] private TextMeshProUGUI _passText;
        
        [Inject] private SkinsService _skinsService;
        [Inject] private ISpriteService _spritesService;
        [Inject] private LocalizationService _localization;
        
        public void SetPlayerName(string name) => _playerNameText.text = name;
        
        public void SetData(uint score = 0, uint pass = 0, uint maxPasses = 0)
        {
            SetScore(score);
            SetPass(pass, maxPasses);
        }
        
        public void SetScore(uint score)
        {
            Score = score;            
            _scoreText.text = _localization.Get(LocalizationConst.TableUI, LocalizationConst.KeyLabelScore, score);
        }

        public void SetPass(uint pass, uint maxPasses)
        {
            Pass = pass;
            var label = _localization.Get(LocalizationConst.TableUI, LocalizationConst.KeyLabelPasses);
            _passText.text = maxPasses > 0 ? $"{label} {pass}/{maxPasses}" : $"{label} {pass}";
        }

        public void Reset()
        {
            Pass = 0;
            Score = 0;
            SetPlayerName("");
            SetData();
        }
        
    }
}