using System;
using System.Collections.Generic;

namespace Core.Services.DataDictionary
{
    [Serializable]
    public sealed class DictionaryPatchCache
    {
        public int buildNumber;
        public int schemaVersion;
        public int revision;
        public List<DictionaryPatchEntry> upsert;
        public List<string> remove;

        public DictionaryPatchModel ToPatch()
        {
            return new DictionaryPatchModel
            {
                schemaVersion = schemaVersion,
                revision = revision,
                upsert = upsert,
                remove = remove
            };
        }

        public static DictionaryPatchCache FromPatch(int buildNumber, DictionaryPatchModel patch)
        {
            return new DictionaryPatchCache
            {
                buildNumber = buildNumber,
                schemaVersion = patch.schemaVersion,
                revision = patch.revision,
                upsert = patch.upsert,
                remove = patch.remove
            };
        }
    }
}
