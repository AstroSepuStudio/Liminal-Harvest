using System.Collections.Generic;
using System.IO;
using System;
using UnityEngine;

namespace WS_ProceduralGeneration
{
    public static class GenSeedSaver
    {
        [Serializable]
        private class SeedEntry
        {
            public int seed;
            public string date;
        }

        [Serializable]
        private class SeedFile
        {
            public List<SeedEntry> seeds = new();
        }

        private static string FilePath => Path.Combine(Application.persistentDataPath, "seeds.json");

        public static void SaveSeed(int seed)
        {
            SeedFile file;

            if (File.Exists(FilePath))
            {
                string existing = File.ReadAllText(FilePath);
                file = JsonUtility.FromJson<SeedFile>(existing) ?? new SeedFile();
            }
            else
            {
                file = new SeedFile();
            }

            file.seeds.Add(new SeedEntry
            {
                seed = seed,
                date = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            });

            File.WriteAllText(FilePath, JsonUtility.ToJson(file, prettyPrint: true));
            Debug.Log($"[SeedLogger] Seed {seed} saved to {FilePath}");
        }
    }
}
