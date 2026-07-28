using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;
using Il2CppSystem.Collections.Generic;

namespace PlantsRandomizer
{
    [BepInPlugin("com.duong.pvzfusion.plantsrandomizer", "Plants Randomizer", "1.3.0")]
    public class Plugin : BasePlugin
    {
        public static ManualLogSource LogSource = null!;
        public static BepInEx.Configuration.ConfigEntry<bool> IncludeColoredCards = null!;

        public override void Load()
        {
            LogSource = Log;
            IncludeColoredCards = Config.Bind("General", "IncludeColoredCards", true, "Include base game special/colored card plants in post-adventure reward pool.");

            Log.LogInfo("Plants Randomizer Mod v1.3.0 initializing...");

            try
            {
                Harmony harmony = new Harmony("com.duong.pvzfusion.plantsrandomizer");
                harmony.PatchAll(typeof(AwardPatches));
                Log.LogInfo("Harmony patches registered successfully!");
            }
            catch (Exception ex)
            {
                Log.LogError($"Failed to register Harmony patches: {ex}");
            }
        }
    }

    [HarmonyPatch]
    public static class AwardPatches
    {
        private const string CONFIG_VERSION = "1.3.0";
        private static readonly object Sync = new object();
        private static bool _initialized = false;
        private static string _activeProfile = string.Empty;
        public static readonly System.Collections.Generic.Dictionary<AdvantureLevel, PlantType> LevelToPlantMap = new();

        // Special levels that unlock gameplay functions (e.g. Shovel at Day4) or fixed terrain (LilyPad at Pool1, Pot at Roof1).
        // These levels MUST NOT give any extra random plant reward.
        public static readonly System.Collections.Generic.HashSet<AdvantureLevel> ExcludedSpecialLevels = new()
        {
            AdvantureLevel.Pool1,      // 3-1: Fixed LilyPad
            AdvantureLevel.Roof1,      // 5-1: Fixed Pot
            AdvantureLevel.Day4,       // 1-4: Function Shovel (Xẻng)
            AdvantureLevel.Day_sub1,   // Function / Mail
            AdvantureLevel.Night5,     // 2-5: Minigame
            AdvantureLevel.Pool5,      // 3-5: Minigame
            AdvantureLevel.Roof5,      // 5-5: Minigame
            AdvantureLevel.Roof6       // 5-10: Final Boss Trophy
        };

        public static PlantType[] CreateBasicPlantPool()
        {
            System.Collections.Generic.List<PlantType> pool = new System.Collections.Generic.List<PlantType>();
            Array allValues = Enum.GetValues(typeof(PlantType));

            foreach (var obj in allValues)
            {
                PlantType p = (PlantType)obj;
                int id = (int)p;

                // Basic plants are strictly IDs 0 to 47 in base game
                if (id < 0 || id > 47) continue;

                // Exclude default starting plants and fixed terrain plants
                if (p == PlantType.Peashooter || p == PlantType.SunFlower || p == PlantType.LilyPad || p == PlantType.Pot)
                {
                    continue;
                }

                string name = p.ToString();
                if (name.EndsWith("Body") || name.EndsWith("_land") || name.EndsWith("_water") || name == "Nothing")
                {
                    continue;
                }

                pool.Add(p);
            }

            return pool.ToArray();
        }

        public static PlantType[] CreateColoredPlantPool()
        {
            System.Collections.Generic.List<PlantType> pool = new System.Collections.Generic.List<PlantType>();
            Array allValues = Enum.GetValues(typeof(PlantType));

            foreach (var obj in allValues)
            {
                PlantType p = (PlantType)obj;
                int id = (int)p;

                // Base game special plants (IDs 200..299 e.g. Hamburger, Pudding, Apple)
                // and base game fusion cards (IDs 1000..1999 e.g. SniperPea, SuperGatling, ObsidianJalapeno)
                bool isSpecialOrColored = (id >= 200 && id <= 299) || (id >= 1000 && id < 2000);
                if (!isSpecialOrColored) continue;

                // Exclude any modded or non-standard plant enum values
                if (!Enum.IsDefined(typeof(PlantType), p)) continue;

                string name = p.ToString();
                if (name.EndsWith("Body") || name.EndsWith("_land") || name.EndsWith("_water") || name == "Nothing" || name.StartsWith("EnumValue"))
                {
                    continue;
                }

                pool.Add(p);
            }

            return pool.ToArray();
        }

        public static string GetCurrentProfileKey()
        {
            try
            {
                string name = GameAPP.playerName;
                if (!string.IsNullOrEmpty(name)) return name;

                string key = SaveInfo.LAST_SAVE_KEY;
                if (!string.IsNullOrEmpty(key)) return key;

                if (SaveInfo.Instance != null && !string.IsNullOrEmpty(SaveInfo.Instance.FilePath))
                {
                    return Path.GetFileNameWithoutExtension(SaveInfo.Instance.FilePath);
                }
            }
            catch { }

            return "default";
        }

        private static int GenerateUniqueRandomSeed()
        {
            unchecked
            {
                int hash1 = Guid.NewGuid().GetHashCode();
                int hash2 = Environment.TickCount;
                int hash3 = (int)DateTime.UtcNow.Ticks;
                return hash1 ^ hash2 ^ hash3;
            }
        }

        private static string GetConfigPath(string profileKey)
        {
            if (string.IsNullOrEmpty(profileKey)) profileKey = "default";
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                profileKey = profileKey.Replace(c, '_');
            }
            return Path.Combine(Paths.ConfigPath, $"PlantsRandomizer_Mapping_{profileKey}.txt");
        }

        public static void EnsureInitialized()
        {
            string currentProfile = GetCurrentProfileKey();

            if (_initialized && _activeProfile == currentProfile) return;

            lock (Sync)
            {
                if (_initialized && _activeProfile == currentProfile) return;

                LoadOrGenerateMapping(currentProfile);
                _activeProfile = currentProfile;
                _initialized = true;
            }
        }

        private static void LoadOrGenerateMapping(string profileKey)
        {
            LevelToPlantMap.Clear();

            string configPath = GetConfigPath(profileKey);
            bool includeColored = Plugin.IncludeColoredCards != null ? Plugin.IncludeColoredCards.Value : true;

            int savedSeed = 0;

            if (File.Exists(configPath))
            {
                try
                {
                    string[] lines = File.ReadAllLines(configPath);
                    bool isVersionValid = false;

                    foreach (string line in lines)
                    {
                        string trimmed = line.Trim();
                        if (trimmed.StartsWith("# Version:"))
                        {
                            if (trimmed.Contains(CONFIG_VERSION))
                            {
                                isVersionValid = true;
                            }
                            continue;
                        }

                        if (trimmed.StartsWith("# Seed:"))
                        {
                            string seedStr = trimmed.Substring("# Seed:".Length).Trim();
                            int.TryParse(seedStr, out savedSeed);
                            continue;
                        }

                        if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#")) continue;

                        int idx = trimmed.IndexOf('=');
                        if (idx > 0 &&
                            int.TryParse(trimmed.Substring(0, idx), out int levelVal) &&
                            int.TryParse(trimmed.Substring(idx + 1), out int plantVal))
                        {
                            AdvantureLevel lvl = (AdvantureLevel)levelVal;
                            if (!ExcludedSpecialLevels.Contains(lvl) && (int)lvl >= 1 && (int)lvl <= 100)
                            {
                                LevelToPlantMap[lvl] = (PlantType)plantVal;
                            }
                        }
                    }

                    if (isVersionValid && LevelToPlantMap.Count >= 30)
                    {
                        Plugin.LogSource?.LogInfo($"Loaded {LevelToPlantMap.Count} plant reward mappings for profile [{profileKey}] from {configPath}");
                        return;
                    }

                    Plugin.LogSource?.LogInfo($"Config file for profile [{profileKey}] is outdated or invalid. Regenerating v{CONFIG_VERSION} mapping...");
                    LevelToPlantMap.Clear();
                }
                catch (Exception ex)
                {
                    Plugin.LogSource?.LogWarning($"Error reading config mapping for profile [{profileKey}]: {ex.Message}");
                }
            }

            // Generate a fresh unique random seed per profile mapping creation
            int seed = savedSeed != 0 ? savedSeed : GenerateUniqueRandomSeed();
            System.Random rand = new System.Random(seed);

            Array levelValues = Enum.GetValues(typeof(AdvantureLevel));
            System.Collections.Generic.List<AdvantureLevel> allLevels = new System.Collections.Generic.List<AdvantureLevel>();
            foreach (var lvlObj in levelValues)
            {
                AdvantureLevel lvl = (AdvantureLevel)lvlObj;
                int lvlNum = (int)lvl;

                // STRICT FILTER: Only include valid playable Adventure levels (lvlNum >= 1 && lvlNum <= 100).
                // Exclude Default (0) and challenge/endless level enum entries.
                if (lvlNum >= 1 && lvlNum <= 100 && !ExcludedSpecialLevels.Contains(lvl))
                {
                    allLevels.Add(lvl);
                }
            }

            PlantType[] basicPlants = CreateBasicPlantPool();
            PlantType[] coloredPlants = CreateColoredPlantPool();

            System.Collections.Generic.List<PlantType> basicPool = new System.Collections.Generic.List<PlantType>(basicPlants);
            ShuffleList(basicPool, rand);

            System.Collections.Generic.List<PlantType> coloredPool = new System.Collections.Generic.List<PlantType>(coloredPlants);
            ShuffleList(coloredPool, rand);

            int basicIdx = 0;
            int coloredIdx = 0;

            foreach (AdvantureLevel lvl in allLevels)
            {
                PlantType chosenPlant;

                // Priority: Map all basic plants to adventure levels first
                if (basicIdx < basicPool.Count)
                {
                    chosenPlant = basicPool[basicIdx++];
                }
                else if (includeColored && coloredIdx < coloredPool.Count)
                {
                    chosenPlant = coloredPool[coloredIdx++];
                }
                else
                {
                    if (basicIdx >= basicPool.Count)
                    {
                        ShuffleList(basicPool, rand);
                        basicIdx = 0;
                    }
                    chosenPlant = basicPool[basicIdx++];
                }

                LevelToPlantMap[lvl] = chosenPlant;
            }

            SaveMapping(configPath, seed);
            Plugin.LogSource?.LogInfo($"Generated new v{CONFIG_VERSION} random plant reward mapping (seed: {seed}, basic: {basicPool.Count}, base game special/colored: {coloredPool.Count}) for profile [{profileKey}] ({LevelToPlantMap.Count} levels).");
        }

        private static void ShuffleList<T>(System.Collections.Generic.List<T> list, System.Random rand)
        {
            int n = list.Count;
            while (n > 1)
            {
                n--;
                int k = rand.Next(n + 1);
                T value = list[k];
                list[k] = list[n];
                list[n] = value;
            }
        }

        private static void SaveMapping(string configPath, int seed)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine($"# Version: {CONFIG_VERSION}");
                sb.AppendLine($"# Seed: {seed}");
                sb.AppendLine("# PlantsRandomizer per-account level->plant mapping (Base Game Cards Only)");
                sb.AppendLine("# Function & Fixed terrain levels excluded from random plant rewards.");
                foreach (var kvp in LevelToPlantMap)
                {
                    sb.AppendLine($"{(int)kvp.Key}={(int)kvp.Value}");
                }
                File.WriteAllText(configPath, sb.ToString());
            }
            catch (Exception ex)
            {
                Plugin.LogSource?.LogWarning($"Failed to save mapping file: {ex.Message}");
            }
        }

        public static bool IsLevelCompleted(AdvantureLevel lvl)
        {
            try
            {
                if (GameAPP.developerMode) return true;

                int lvlNum = (int)lvl;
                if (lvlNum <= 0) return false;

                var advCompleted = GameAPP.advLevelCompleted;
                if (advCompleted != null && lvlNum >= 0 && lvlNum < advCompleted.Length)
                {
                    if (advCompleted[lvlNum]) return true;
                }

                int currentAdvLevel = GameAPP.advantureLevel;
                if (currentAdvLevel > lvlNum) return true;

                var data = AdvantureConfig.data;
                if (data != null)
                {
                    if (data.levelCompleted != null && data.levelCompleted.Contains(lvl)) return true;
                    if (data.levelCompletedHard != null && data.levelCompletedHard.Contains(lvl)) return true;
                }
            }
            catch { }

            return false;
        }

        private static bool IsPlantUnlockedInternal(PlantType plantType)
        {
            if (GameAPP.developerMode) return true;

            // At start of Level 1-1, player ONLY has Peashooter (0) and Sunflower (1)
            if (plantType == PlantType.Peashooter || plantType == PlantType.SunFlower)
            {
                return true;
            }

            // LilyPad: Unlocked ONLY when Pool1 (3-1) is completed
            if (plantType == PlantType.LilyPad)
            {
                return IsLevelCompleted(AdvantureLevel.Pool1);
            }

            // Pot: Unlocked ONLY when Roof1 (5-1) is completed
            if (plantType == PlantType.Pot)
            {
                return IsLevelCompleted(AdvantureLevel.Roof1);
            }

            // Standard plants: Unlocked ONLY if their mapped level in LevelToPlantMap is COMPLETED
            bool isMapped = false;
            bool anyCompleted = false;

            foreach (var kvp in LevelToPlantMap)
            {
                if (kvp.Value == plantType)
                {
                    isMapped = true;
                    if (IsLevelCompleted(kvp.Key))
                    {
                        anyCompleted = true;
                        break;
                    }
                }
            }

            if (isMapped)
            {
                return anyCompleted;
            }

            return false;
        }

        // --- Profile Change Hooks ---

        [HarmonyPostfix]
        [HarmonyPatch(typeof(SaveInfo), nameof(SaveInfo.SaveLastSelectedSave))]
        public static void SaveLastSelectedSave_Postfix()
        {
            lock (Sync)
            {
                _initialized = false;
                _activeProfile = string.Empty;
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(SaveInfo), nameof(SaveInfo.LoadPlayerData))]
        public static void LoadPlayerData_Postfix()
        {
            lock (Sync)
            {
                _initialized = false;
                _activeProfile = string.Empty;
            }
        }

        // --- Harmony Patches ---

        [HarmonyPrefix]
        [HarmonyPatch(typeof(InitBoard), nameof(InitBoard.CreateCard), new Type[] { typeof(PlantType), typeof(bool), typeof(bool) })]
        public static void CreateCard_Prefix(ref PlantType theSeedType)
        {
            EnsureInitialized();

            if (AdvantureConfig.unlockLevels != null && AdvantureConfig.unlockLevels.TryGetValue(theSeedType, out AdvantureLevel originalLevel))
            {
                if (LevelToPlantMap.TryGetValue(originalLevel, out PlantType randomPlant))
                {
                    Plugin.LogSource?.LogInfo($"[InitBoard.CreateCard] Replacing original level reward {theSeedType} (Level {originalLevel}) -> {randomPlant}");
                    theSeedType = randomPlant;
                }
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(InGameUI), nameof(InGameUI.UnlockCard))]
        public static void UnlockCard_Prefix(ref PlantType theSeedType)
        {
            EnsureInitialized();

            AdvantureLevel lvl = AdvantureLevel.Day1;
            if (AdvantureManager.Instance != null)
            {
                lvl = AdvantureManager.Instance.level;
            }
            else
            {
                lvl = (AdvantureLevel)GameAPP.theBoardLevel;
            }

            if (LevelToPlantMap.TryGetValue(lvl, out PlantType targetPlant))
            {
                Plugin.LogSource?.LogInfo($"[UnlockCard_Prefix] Overriding unlock for level {lvl}: {theSeedType} -> {targetPlant}");
                theSeedType = targetPlant;
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(AdvantureConfig), nameof(AdvantureConfig.GetBasicPlantType))]
        public static bool GetBasicPlantType_Prefix(AdvantureLevel level, ref PlantType __result)
        {
            EnsureInitialized();

            if (LevelToPlantMap.TryGetValue(level, out PlantType randomPlant))
            {
                __result = randomPlant;
                Plugin.LogSource?.LogInfo($"[AdvantureConfig.GetBasicPlantType] Level {level} -> Randomized Reward: {randomPlant}");
                return false;
            }

            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(AdvantureConfig), nameof(AdvantureConfig.CheckPlantUnlock))]
        public static bool CheckPlantUnlock_Prefix(PlantType plantType, ref bool __result)
        {
            EnsureInitialized();

            __result = IsPlantUnlockedInternal(plantType);
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Lawnf), nameof(Lawnf.CheckIfPlantUnlock))]
        public static bool CheckIfPlantUnlock_Prefix(PlantType thePlantType, ref UnlockType __result)
        {
            EnsureInitialized();

            bool unlocked = IsPlantUnlockedInternal(thePlantType);
            __result = unlocked ? UnlockType.Unlocked : UnlockType.NotUnlocked;
            return false;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Lawnf), nameof(Lawnf.SetAward))]
        public static void SetAward_Postfix(Board board, Vector2 position, bool killZombie, bool fake, PrizeMgr __result)
        {
            EnsureInitialized();

            if (board == null || __result == null) return;

            try
            {
                AdvantureLevel lvl = AdvantureLevel.Day1;
                if (AdvantureManager.Instance != null)
                {
                    lvl = AdvantureManager.Instance.level;
                }
                else
                {
                    lvl = (AdvantureLevel)GameAPP.theBoardLevel;
                }

                if (LevelToPlantMap.TryGetValue(lvl, out PlantType targetPlant))
                {
                    CardUI cardUI = __result.GetComponent<CardUI>();
                    if (cardUI == null)
                    {
                        cardUI = __result.GetComponentInChildren<CardUI>();
                    }

                    if (cardUI != null)
                    {
                        Plugin.LogSource?.LogInfo($"[SetAward] Swapping trophy card for level {lvl} to {targetPlant}");
                        cardUI.thePlantType = targetPlant;
                        cardUI.theSeedType = (int)targetPlant;
                        try { cardUI.ChangeCardSprite(); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource?.LogWarning($"Error in SetAward_Postfix: {ex.Message}");
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(AdvantureConfig), nameof(AdvantureConfig.LoadData))]
        public static void LoadData_Postfix()
        {
            EnsureInitialized();

            try
            {
                var unlockDict = AdvantureConfig.unlockLevels;
                if (unlockDict != null)
                {
                    unlockDict.Clear();

                    unlockDict[PlantType.Peashooter] = AdvantureLevel.Day1;
                    unlockDict[PlantType.SunFlower] = AdvantureLevel.Day1;

                    foreach (var kvp in LevelToPlantMap)
                    {
                        unlockDict[kvp.Value] = kvp.Key;
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource?.LogWarning($"Non-fatal error updating unlockLevels: {ex.Message}");
            }
        }
    }
}
