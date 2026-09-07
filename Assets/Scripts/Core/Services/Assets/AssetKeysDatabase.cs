using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "AssetKeysDatabase", menuName = "Game/Config/Asset Keys Database")]
public class AssetKeysDatabase : ScriptableObject
{
    [System.Serializable]
    public class Entry
    {
        public string Id;
        public string AddressKey;
    }

    public List<Entry> Entries = new();
}