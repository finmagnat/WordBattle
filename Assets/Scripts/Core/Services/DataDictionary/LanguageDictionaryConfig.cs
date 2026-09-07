using UnityEngine;

namespace Core.Services.DataDictionary
{
    [CreateAssetMenu(menuName = "Game/Dictionary/Language Config", fileName = "LanguageDictionaryConfig")]
    public class LanguageDictionaryConfig : ScriptableObject
    {
        [Header("Language Info")]
        public string languageCode;       // "ru", "en", "uk"
        public string languageName;       // Русский, English, Українська

        [Header("Alphabet")]
        [TextArea] 
        public string alphabet;

        [Header("Mixer Letter Groups")]
        [TextArea]
        public string consonants;

        [Header("Addressables Key for Dictionary File")]
        public string dictionaryAddressKey; // пример: "dict_ru"
    }
}
