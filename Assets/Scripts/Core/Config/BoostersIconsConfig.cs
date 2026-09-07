using System;
using System.Collections.Generic;
using UnityEngine;

namespace Core.Config
{
    [CreateAssetMenu(menuName = "Game/Config/Boosters", fileName = "BoostersIconsConfig")]
    public class BoostersIconsConfig : ScriptableObject
    {
        public List<BoosterIconData> Items => _items;
        
        [SerializeField] private List<BoosterIconData> _items = new ();
        
        private readonly Dictionary<string, string> _cache = new();

        public string GetAlias(BoosterType type)
        {
            if(_cache.Count == 0)
                foreach (BoosterIconData item in _items)
                    _cache[item.Type.ToString()] = item.IconAssetKey;
            
            return _cache[type.ToString()];
        }
    }
    
    [Serializable]
    public struct BoosterIconData
    {
        public BoosterType Type;
        public string IconAssetKey;
    }
}
