using System.Collections.Generic;
using Core.Data;
using UnityEngine;

namespace Core.Config
{
    [CreateAssetMenu(menuName = "Game/Config/Skins", fileName = "SkinsConfig")]
    public class SkinsConfig : ScriptableObject
    {
        public List<SkinData> Skins => _skins;
        
        [Space(10)]
        [Tooltip("Скин по умолчанию")]
        public SkinType SkinByDefault = SkinType.Blue;
        
        [SerializeField] private List<SkinData> _skins = new ();
        
        public SkinData GetSkinByType(SkinType skinType) =>
            _skins.Find(item => item.SkinType == skinType);

        public SkinData GetSkinRandom() =>
            _skins[Random.Range(0, _skins.Count)];
    }
}