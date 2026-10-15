using System.IO;
using System.Text;
using Core.Data;
using UnityEngine;

namespace Core.Services
{
    internal static class SaveGameStorage
    {
        public static string Serialize(SaveGameData data) => JsonUtility.ToJson(data, false);

        public static SaveGameData Deserialize(string json) => JsonUtility.FromJson<SaveGameData>(json);

        public static void WriteAtomic(string path, SaveGameData data)
        {
            string json = Serialize(data);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporaryPath = path + ".tmp";
            // Small snapshots are committed synchronously, before the next turn can mutate the board.
            // A failed write/replace leaves the previous committed snapshot intact.
            using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                byte[] bytes = new UTF8Encoding(false).GetBytes(json);
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }

            if (File.Exists(path))
                File.Replace(temporaryPath, path, null);
            else
                File.Move(temporaryPath, path);
        }
    }
}
