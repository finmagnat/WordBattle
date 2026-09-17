using System;
using System.Collections.Generic;

namespace Core.Services.DataDictionary
{
    [Serializable]
    public sealed class DictionaryPatchModel
    {
        public int schemaVersion;
        public int revision;
        public List<DictionaryPatchEntry> upsert;
        public List<string> remove;
    }

    [Serializable]
    public sealed class DictionaryPatchEntry
    {
        public string word;
        public string definition;
    }
}
