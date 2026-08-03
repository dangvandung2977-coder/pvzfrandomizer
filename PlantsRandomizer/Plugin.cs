using System;
using System.IO;
using System.Text;
using System.Reflection;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;
using Il2CppInterop.Runtime.Injection;

namespace PlantsRandomizer
{
    [BepInPlugin("com.duong.pvzfusion.plantsrandomizer", "Plants Randomizer", "1.0.3")]
    public class Plugin : BasePlugin
    {
        public static ManualLogSource LogSource = null!;
        public static BepInEx.Configuration.ConfigEntry<bool> IncludeColoredCards = null!;
        public static BonusUIManager BonusUIInstance = null!;

        public override void Load()
        {
            LogSource = Log;
            IncludeColoredCards = Config.Bind("General", "IncludeColoredCards", true, "Include base game special/colored card plants in post-adventure reward pool.");

            Log.LogInfo("Plants Randomizer Mod v1.0.3 initializing...");

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

    public class ProfileData
    {
        public int TotalWins = 0;
        public int LastBonusUnlockedPlant = -1;
        public HashSet<int> BonusUnlockedPlants = new HashSet<int>();
    }

    // Lightweight Notification Toast Manager - 0% button obstruction
    public class BonusUIManager : MonoBehaviour
    {
        public static string LastNotif = string.Empty;
        public static float NotifTimer = 0f;

        public BonusUIManager(IntPtr ptr) : base(ptr) { }

        private void Update()
        {
            if (NotifTimer > 0)
            {
                NotifTimer -= Time.deltaTime;
                if (NotifTimer <= 0) LastNotif = string.Empty;
            }

            try
            {
                if (Input.GetKeyDown(KeyCode.F11))
                {
                    AwardPatches.RerollCurrentProfileSeed();
                }
                if (Input.GetKeyDown(KeyCode.F12))
                {
                    AwardPatches.RerollLastLevelPlant();
                }
            }
            catch { }
        }

        public static void ShowNotif(string msg, float dur = 6f)
        {
            LastNotif = msg;
            NotifTimer = dur;
        }

        private void OnGUI()
        {
            try
            {
                if (NotifTimer > 0 && !string.IsNullOrEmpty(LastNotif))
                {
                    GUI.Box(new Rect(Screen.width / 2 - 300, 15, 600, 46), string.Empty);
                    GUI.Label(new Rect(Screen.width / 2 - 290, 23, 580, 30), $"<b><size=14><color=yellow>{LastNotif}</color></size></b>");
                }
            }
            catch { }
        }
    }

    [HarmonyPatch]
    public static class AwardPatches
    {
        private const string CONFIG_VERSION = "1.0.3";
        private static readonly object Sync = new object();
        private static bool _initialized = false;
        private static string _activeProfile = string.Empty;
        public static readonly Dictionary<AdvantureLevel, PlantType> LevelToPlantMap = new();
        public static ProfileData CurrentData = new ProfileData();

        [HarmonyPostfix, HarmonyPatch(typeof(GameAPP), nameof(GameAPP.Start))]
        public static void GameAPPStart_Postfix()
        {
            if (Plugin.BonusUIInstance == null)
            {
                try
                {
                    ClassInjector.RegisterTypeInIl2Cpp<BonusUIManager>();
                    GameObject uiObj = new GameObject("PlantsRandomizerBonusUI");
                    UnityEngine.Object.DontDestroyOnLoad(uiObj);
                    Plugin.BonusUIInstance = uiObj.AddComponent<BonusUIManager>();
                    Plugin.LogSource?.LogInfo("BonusUIManager successfully registered!");
                }
                catch (Exception ex)
                {
                    Plugin.LogSource?.LogError($"Failed to register BonusUIManager: {ex}");
                }
            }
        }

        public static readonly int[] SpecialBonusPlantIDs = new int[]
        {
            34, 220, 222, 223, 229, 235, 237, 238, 241, 242, 243, 248, 249, 252,
            256, 906, 1027, 1070, 1060, 1067, 1120, 1247
        };

        // Plants the game default-awards at adventure levels OUTSIDE the randomized mapping (1..100).
        // They must never be given by the game by default; only via the random pools above.
        public static readonly HashSet<int> BlockDefaultGivePlants = new HashSet<int> { 906, 1027, 1067, 1070, 1120, 1247 };

        // Candidate pool for Bonus rewards
        public static PlantType[] CreateBonusPlantPool()
        {
            var pool = new List<PlantType>();
            foreach (int id in SpecialBonusPlantIDs)
            {
                pool.Add((PlantType)id);
            }
            return pool.ToArray();
        }

        public static PlantType? DoBonusReward()
        {
            PlantType[] pool = CreateBonusPlantPool();
            if (pool.Length == 0) return null;

            var candidates = new List<PlantType>();
            foreach (var p in pool)
            {
                if (!CurrentData.BonusUnlockedPlants.Contains((int)p))
                    candidates.Add(p);
            }
            if (candidates.Count == 0) return null;

            var rand = new System.Random(Guid.NewGuid().GetHashCode() ^ Environment.TickCount);
            PlantType chosen = candidates[rand.Next(candidates.Count)];
            CurrentData.BonusUnlockedPlants.Add((int)chosen);
            CurrentData.LastBonusUnlockedPlant = (int)chosen;
            SaveCurrentData();
            return chosen;
        }

        public static PlantType[] GetSuperPlantList()
        {
            return Array.Empty<PlantType>();
        }

        public static PlantType[] CreateBasicPlantPool()
        {
            var pool = new List<PlantType>();
            foreach (var obj in Enum.GetValues(typeof(PlantType)))
            {
                PlantType p = (PlantType)obj;
                int id = (int)p;
                if (id >= 0 && id <= 47)
                {
                    if (p == PlantType.Peashooter || p == PlantType.SunFlower || p == PlantType.LilyPad || p == PlantType.Pot) continue;
                    string name = p.ToString();
                    if (name.EndsWith("Body") || name.EndsWith("_land") || name.EndsWith("_water") || name == "Nothing") continue;
                    pool.Add(p);
                }
            }
            pool.Add((PlantType)256);    // Present
            pool.Add((PlantType)906);    // ObsidianSpike
            pool.Add((PlantType)1027);   // TallNut
            pool.Add((PlantType)1070);   // GloomShroom
            pool.Add((PlantType)1060);   // SpikeRock
            pool.Add((PlantType)1067);   // CattailPlant
            pool.Add((PlantType)1120);   // CobCannon
            pool.Add((PlantType)1247);   // SpruceBallista
            return pool.ToArray();
        }

        // ONLY these levels give fixed terrain plants (NOT randomized):
        // Night6 (2-6, last level before Pool section) -> LilyPad
        // NightPool6 (4-6, last level before Roof section) -> Pot
        // All other levels are randomized.
        public static readonly Dictionary<AdvantureLevel, PlantType> FixedRewardLevels = new()
        {
            { AdvantureLevel.Night6, PlantType.LilyPad },
            { AdvantureLevel.NightPool6, PlantType.Pot }
        };

        public static string GetCurrentProfileKey()
        {
            string baseName = "default";
            try { if (!string.IsNullOrEmpty(GameAPP.playerName)) baseName = GameAPP.playerName; } catch { }
            try
            {
                if (SaveInfo.Instance != null && !string.IsNullOrEmpty(SaveInfo.Instance.FilePath))
                {
                    string fp = SaveInfo.Instance.FilePath;
                    if (!string.IsNullOrEmpty(fp))
                    {
                        string fn = Path.GetFileNameWithoutExtension(fp);
                        return $"{baseName}_{fn}";
                    }
                }
            }
            catch { }
            return baseName;
        }

        public static void ClearProfileMappingOnly(string key)
        {
            try
            {
                string configPath = GetConfigPath(key);
                if (File.Exists(configPath))
                {
                    File.Delete(configPath);
                    Plugin.LogSource?.LogInfo($"[PlantsRandomizer] Deleted mapping config for [{key}]");
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource?.LogWarning($"Failed to clear mapping config for key [{key}]: {ex.Message}");
            }
        }

        public static void ClearProfileAllData(string key)
        {
            ClearProfileMappingOnly(key);
            try
            {
                string dataPath = GetDataPath(key);
                if (File.Exists(dataPath))
                {
                    File.Delete(dataPath);
                    Plugin.LogSource?.LogInfo($"[PlantsRandomizer] Deleted data config for [{key}]");
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource?.LogWarning($"Failed to clear data config for key [{key}]: {ex.Message}");
            }
        }

        private static string GetConfigPath(string key)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) key = key.Replace(c, '_');
            return Path.Combine(Paths.ConfigPath, $"PlantsRandomizer_Mapping_{key}.txt");
        }

        private static string GetDataPath(string key)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) key = key.Replace(c, '_');
            return Path.Combine(Paths.ConfigPath, $"PlantsRandomizer_Data_{key}.txt");
        }

        public static int GetTotalCompletedLevels()
        {
            int count = 0;
            try
            {
                for (int i = 1; i <= 100; i++)
                {
                    if (IsLevelCompleted((AdvantureLevel)i))
                        count++;
                }
            }
            catch { }
            return count;
        }

        public static void EnsureInitialized()
        {
            string profile = GetCurrentProfileKey();
            if (_initialized && _activeProfile == profile) return;
            lock (Sync)
            {
                if (_initialized && _activeProfile == profile) return;
                LoadOrGenerateMapping(profile);
                LoadProfileData(profile);
                _activeProfile = profile;
                _initialized = true;
            }
        }

        private static void LoadProfileData(string key)
        {
            CurrentData = new ProfileData();
            string path = GetDataPath(key);
            if (File.Exists(path))
            {
                try
                {
                    foreach (string line in File.ReadAllLines(path))
                    {
                        string t = line.Trim();
                        if (string.IsNullOrEmpty(t) || t.StartsWith("#")) continue;
                        int eq = t.IndexOf('=');
                        if (eq <= 0) continue;
                        string k = t.Substring(0, eq).Trim(), v = t.Substring(eq + 1).Trim();
                        if (k == "TotalWins" && int.TryParse(v, out int tw)) CurrentData.TotalWins = tw;
                        if (k == "LastBonusUnlockedPlant" && int.TryParse(v, out int lbp)) CurrentData.LastBonusUnlockedPlant = lbp;
                        if (k == "BonusUnlockedPlants") foreach (string s in v.Split(',', StringSplitOptions.RemoveEmptyEntries)) if (int.TryParse(s.Trim(), out int p)) CurrentData.BonusUnlockedPlants.Add(p);
                    }
                }
                catch (Exception ex) { Plugin.LogSource?.LogWarning($"Load data error: {ex.Message}"); }
            }

            int actualCompleted = GetTotalCompletedLevels();
            if (actualCompleted > CurrentData.TotalWins)
            {
                CurrentData.TotalWins = actualCompleted;
            }
            SaveCurrentData();
        }

        public static void SaveCurrentData()
        {
            try
            {
                string path = GetDataPath(GetCurrentProfileKey());
                var sb = new StringBuilder();
                sb.AppendLine($"TotalWins={CurrentData.TotalWins}");
                sb.AppendLine($"LastBonusUnlockedPlant={CurrentData.LastBonusUnlockedPlant}");
                sb.AppendLine($"BonusUnlockedPlants={string.Join(",", CurrentData.BonusUnlockedPlants)}");
                File.WriteAllText(path, sb.ToString());
            }
            catch (Exception ex) { Plugin.LogSource?.LogWarning($"Save data error: {ex.Message}"); }
        }

        public static void SaveMapping(string key)
        {
            try
            {
                string configPath = GetConfigPath(key);
                var sb = new StringBuilder();
                sb.AppendLine($"# Version: {CONFIG_VERSION}");
                foreach (var kvp in LevelToPlantMap) sb.AppendLine($"{(int)kvp.Key}={(int)kvp.Value}");
                File.WriteAllText(configPath, sb.ToString());
            }
            catch (Exception ex) { Plugin.LogSource?.LogWarning($"Save mapping error: {ex.Message}"); }
        }

        public static void RerollLastLevelPlant()
        {
            lock (Sync)
            {
                EnsureInitialized();

                AdvantureLevel lastLvl = AdvantureLevel.Day1;
                bool found = false;

                try
                {
                    if (AdvantureManager.Instance != null && LevelToPlantMap.ContainsKey(AdvantureManager.Instance.level))
                    {
                        lastLvl = AdvantureManager.Instance.level;
                        found = true;
                    }
                    else if (GameAPP.theBoardLevel > 0 && LevelToPlantMap.ContainsKey((AdvantureLevel)GameAPP.theBoardLevel))
                    {
                        lastLvl = (AdvantureLevel)GameAPP.theBoardLevel;
                        found = true;
                    }
                }
                catch { }

                if (!found)
                {
                    for (int i = 100; i >= 1; i--)
                    {
                        var lvl = (AdvantureLevel)i;
                        if (LevelToPlantMap.ContainsKey(lvl) && IsLevelCompleted(lvl))
                        {
                            lastLvl = lvl;
                            found = true;
                            break;
                        }
                    }
                }

                if (!found)
                {
                    foreach (var kvp in LevelToPlantMap)
                    {
                        lastLvl = kvp.Key;
                        found = true;
                        break;
                    }
                }

                if (!found || !LevelToPlantMap.TryGetValue(lastLvl, out PlantType oldPlant))
                {
                    string msg = "\u26a0\ufe0f KH\u00d4NG T\u00ccM TH\u1ea4Y M\u00c0N CH\u01a0I \u0110\u1ec2 REROLL!";
                    Plugin.LogSource?.LogInfo(msg);
                    BonusUIManager.ShowNotif(msg, 5f);
                    return;
                }

                if (FixedRewardLevels.ContainsKey(lastLvl))
                {
                    string msg = $"\u26a0\ufe0f M\u00c0N [{lastLvl}] L\u00c0 M\u00c0N TH\u01af\u1edeNG C\u1ed0 \u0110\u1ecaNH ([{oldPlant}]), KH\u00d4NG TH\u1ec2 REROLL!";
                    Plugin.LogSource?.LogInfo(msg);
                    BonusUIManager.ShowNotif(msg, 5f);
                    return;
                }

                var pool = new List<PlantType>(CreateBasicPlantPool());
                var candidates = pool.FindAll(p => p != oldPlant);
                if (candidates.Count == 0) candidates = pool;

                var rand = new System.Random(Guid.NewGuid().GetHashCode() ^ Environment.TickCount);
                PlantType chosen = candidates[rand.Next(candidates.Count)];

                LevelToPlantMap[lastLvl] = chosen;

                try
                {
                    if (AdvantureConfig.unlockLevels != null)
                        AdvantureConfig.unlockLevels[chosen] = lastLvl;
                }
                catch { }

                SaveMapping(GetCurrentProfileKey());

                string notif = $"\ud83c\udfb2 F12 REROLL C\u00c2Y M\u00c0N [{lastLvl}]: [{oldPlant}] \u2794 [{chosen}]!";
                Plugin.LogSource?.LogInfo(notif);
                BonusUIManager.ShowNotif(notif, 7f);
            }
        }

        public static void RerollCurrentProfileSeed()
        {
            lock (Sync)
            {
                string profile = GetCurrentProfileKey();
                ClearProfileMappingOnly(profile);
                _initialized = false;
                _activeProfile = string.Empty;
                EnsureInitialized();
                int newSeed = 0;
                string configPath = GetConfigPath(profile);
                if (File.Exists(configPath))
                {
                    foreach (string line in File.ReadAllLines(configPath))
                    {
                        if (line.StartsWith("# Seed:"))
                            int.TryParse(line.Substring("# Seed:".Length).Trim(), out newSeed);
                    }
                }
                string notif = $"\ud83c\udfb2 \u0110\xc3 T\u1ea0O SEED M\u1edaI TH\u00c0NH C\u00d4NG! (Seed: {newSeed})";
                Plugin.LogSource?.LogInfo(notif);
                BonusUIManager.ShowNotif(notif, 7f);
            }
        }

        private static void LoadOrGenerateMapping(string key)
        {
            LevelToPlantMap.Clear();
            string configPath = GetConfigPath(key);
            int seed = 0;
            long savedCreationTicks = 0;

            long currentCreationTicks = 0;
            try
            {
                if (SaveInfo.Instance != null && !string.IsNullOrEmpty(SaveInfo.Instance.FilePath) && File.Exists(SaveInfo.Instance.FilePath))
                {
                    currentCreationTicks = File.GetCreationTimeUtc(SaveInfo.Instance.FilePath).Ticks;
                }
            }
            catch { }

            if (File.Exists(configPath))
            {
                try
                {
                    bool valid = false;
                    foreach (string line in File.ReadAllLines(configPath))
                    {
                        string t = line.Trim();
                        if (t.StartsWith("# Version:")) valid = t.Contains(CONFIG_VERSION);
                        if (t.StartsWith("# Seed:")) int.TryParse(t.Substring("# Seed:".Length).Trim(), out seed);
                        if (t.StartsWith("# SaveCreationTicks:")) long.TryParse(t.Substring("# SaveCreationTicks:".Length).Trim(), out savedCreationTicks);
                        if (string.IsNullOrEmpty(t) || t.StartsWith("#")) continue;
                        int eq = t.IndexOf('=');
                        if (eq > 0 && int.TryParse(t.Substring(0, eq), out int lv) && int.TryParse(t.Substring(eq + 1), out int pv))
                        {
                            var lvl = (AdvantureLevel)lv;
                            if (FixedRewardLevels.TryGetValue(lvl, out PlantType fixedPlant))
                            {
                                LevelToPlantMap[lvl] = fixedPlant;
                            }
                            else if (lv >= 1 && lv <= 100)
                            {
                                LevelToPlantMap[lvl] = (PlantType)pv;
                            }
                        }
                    }

                    // Check if save file was recreated since the mapping was created
                    bool isStale = false;
                    if (currentCreationTicks != 0 && savedCreationTicks != 0 && currentCreationTicks != savedCreationTicks)
                    {
                        isStale = true;
                    }
                    else if (savedCreationTicks == 0 && SaveInfo.Instance != null && !string.IsNullOrEmpty(SaveInfo.Instance.FilePath) && File.Exists(SaveInfo.Instance.FilePath))
                    {
                        try
                        {
                            if (File.GetCreationTimeUtc(configPath) < File.GetCreationTimeUtc(SaveInfo.Instance.FilePath))
                            {
                                isStale = true;
                            }
                        }
                        catch { }
                    }

                    if (isStale)
                    {
                        Plugin.LogSource?.LogInfo($"Save file creation time changed or legacy mapping stale for [{key}]. Regenerating mapping with new seed.");
                        valid = false;
                        ClearProfileAllData(key);
                        LevelToPlantMap.Clear();
                    }

                    if (valid && LevelToPlantMap.Count >= 30)
                    {
                        Plugin.LogSource?.LogInfo($"Loaded {LevelToPlantMap.Count} mappings for [{key}] (Seed: {seed})");
                        return;
                    }
                    LevelToPlantMap.Clear();
                }
                catch { }
            }

            // Always generate a fresh random seed for new mappings
            seed = Guid.NewGuid().GetHashCode() ^ Environment.TickCount ^ (int)(DateTime.UtcNow.Ticks & 0x7FFFFFFF);
            if (currentCreationTicks != 0)
            {
                seed ^= (int)(currentCreationTicks & 0x7FFFFFFF);
            }
            var rand = new System.Random(seed);

            var levels = new List<AdvantureLevel>();
            foreach (var obj in Enum.GetValues(typeof(AdvantureLevel)))
            {
                var lvl = (AdvantureLevel)obj;
                int n = (int)lvl;
                if (n >= 1 && n <= 100) levels.Add(lvl);
            }

            var basic = new List<PlantType>(CreateBasicPlantPool());
            var super = new List<PlantType>(GetSuperPlantList());
            Shuffle(basic, rand); Shuffle(super, rand);
            int bi = 0, si = 0;
            bool includeColored = Plugin.IncludeColoredCards?.Value ?? true;

            foreach (var lvl in levels)
            {
                PlantType chosen;
                if (FixedRewardLevels.TryGetValue(lvl, out PlantType fixedPlant))
                {
                    chosen = fixedPlant;
                }
                else if (bi < basic.Count) chosen = basic[bi++];
                else if (includeColored && si < super.Count) chosen = super[si++];
                else { if (bi >= basic.Count) { Shuffle(basic, rand); bi = 0; } chosen = basic[bi++]; }
                LevelToPlantMap[lvl] = chosen;
            }

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine($"# Version: {CONFIG_VERSION}");
                sb.AppendLine($"# Seed: {seed}");
                if (currentCreationTicks != 0)
                    sb.AppendLine($"# SaveCreationTicks: {currentCreationTicks}");
                foreach (var kvp in LevelToPlantMap) sb.AppendLine($"{(int)kvp.Key}={(int)kvp.Value}");
                File.WriteAllText(configPath, sb.ToString());
            }
            catch { }

            Plugin.LogSource?.LogInfo($"Generated v{CONFIG_VERSION} mapping for [{key}] ({LevelToPlantMap.Count} levels, seed {seed})");
        }

        private static void Shuffle<T>(List<T> list, System.Random rand)
        {
            int n = list.Count;
            while (n > 1) { n--; int k = rand.Next(n + 1); var v = list[k]; list[k] = list[n]; list[n] = v; }
        }

        public static bool IsLevelCompleted(AdvantureLevel lvl)
        {
            try
            {
                if (GameAPP.developerMode) return true;
                int n = (int)lvl;
                if (n <= 0) return false;
                var arr = GameAPP.advLevelCompleted;
                if (arr != null && n < arr.Length && arr[n]) return true;
                if (GameAPP.advantureLevel > n) return true;
                var d = AdvantureConfig.data;
                if (d != null)
                {
                    if (d.levelCompleted != null && d.levelCompleted.Contains(lvl)) return true;
                    if (d.levelCompletedHard != null && d.levelCompletedHard.Contains(lvl)) return true;
                }
            }
            catch { }
            return false;
        }

        public static HashSet<PlantType> GetReceivedPlants(AdvantureLevel excludeLevel)
        {
            var received = new HashSet<PlantType>();
            foreach (var kvp in LevelToPlantMap)
            {
                if (kvp.Key == excludeLevel) continue;
                if (IsLevelCompleted(kvp.Key))
                    received.Add(kvp.Value);
            }
            foreach (int id in CurrentData.BonusUnlockedPlants)
                received.Add((PlantType)id);
            return received;
        }

        // Ensures the plant awarded for a just-completed level is NOT a plant already received before.
        // If the mapped plant is already received, re-rolls to a not-yet-received plant and persists the change.
        public static PlantType ResolveUniqueRewardForLevel(AdvantureLevel lvl)
        {
            EnsureInitialized();
            if (FixedRewardLevels.TryGetValue(lvl, out PlantType fixedPlant))
                return fixedPlant;
            if (!LevelToPlantMap.TryGetValue(lvl, out PlantType current))
                return current;

            var received = GetReceivedPlants(lvl);
            if (!received.Contains(current))
                return current;

            var pool = new List<PlantType>();
            pool.AddRange(CreateBasicPlantPool());
            pool.AddRange(CreateBonusPlantPool());
            var candidates = pool.FindAll(p => !received.Contains(p));
            if (candidates.Count == 0) return current;

            var rand = new System.Random(Guid.NewGuid().GetHashCode() ^ Environment.TickCount);
            PlantType chosen = candidates[rand.Next(candidates.Count)];
            LevelToPlantMap[lvl] = chosen;
            try
            {
                if (AdvantureConfig.unlockLevels != null)
                    AdvantureConfig.unlockLevels[chosen] = lvl;
            }
            catch { }
            SaveMapping(GetCurrentProfileKey());
            return chosen;
        }

        // Random plant from the full pool, preferring ones never received before.
        public static PlantType PickRandomPoolPlant(HashSet<PlantType> exclude)
        {
            var pool = new List<PlantType>(CreateBasicPlantPool());
            var candidates = pool.FindAll(p => exclude == null || !exclude.Contains(p));
            if (candidates.Count == 0) candidates = pool;
            var rand = new System.Random(Guid.NewGuid().GetHashCode() ^ Environment.TickCount);
            return candidates[rand.Next(candidates.Count)];
        }

        public static bool IsPlantUnlocked(PlantType pt)
        {
            if (GameAPP.developerMode) return true;
            int id = (int)pt;
            if (CurrentData.BonusUnlockedPlants.Contains(id)) return true;

            if (pt == PlantType.Peashooter || pt == PlantType.SunFlower) return true;
            if (pt == PlantType.LilyPad) return IsLevelCompleted(AdvantureLevel.Night6);
            if (pt == PlantType.Pot) return IsLevelCompleted(AdvantureLevel.NightPool6);

            bool isMapped = false;
            bool anyCompleted = false;

            foreach (var kvp in LevelToPlantMap)
            {
                if (kvp.Value == pt)
                {
                    isMapped = true;
                    if (IsLevelCompleted(kvp.Key))
                    {
                        anyCompleted = true;
                        break;
                    }
                }
            }

            if (isMapped) return anyCompleted;
            return false;
        }

        // --- Profile Change Hooks ---
        [HarmonyPostfix, HarmonyPatch(typeof(SaveInfo), nameof(SaveInfo.SaveLastSelectedSave))]
        public static void SaveLastSelectedSave_Postfix() { lock (Sync) { _initialized = false; _activeProfile = string.Empty; } }

        [HarmonyPostfix, HarmonyPatch(typeof(SaveInfo), nameof(SaveInfo.LoadPlayerData))]
        public static void LoadPlayerData_Postfix() { lock (Sync) { _initialized = false; _activeProfile = string.Empty; } }

        public static readonly Dictionary<PlantType, AdvantureLevel> OriginalUnlockLevels = new();

        // --- Harmony Patches ---
        [HarmonyPrefix, HarmonyPatch(typeof(InitBoard), nameof(InitBoard.CreateCard), new Type[] { typeof(PlantType), typeof(bool), typeof(bool) })]
        public static void CreateCard_Prefix(ref PlantType theSeedType, ref bool shadow, ref bool quick)
        {
            EnsureInitialized();
            if (theSeedType == PlantType.LilyPad || theSeedType == PlantType.Pot)
            {
                bool unlocked = IsPlantUnlocked(theSeedType);
                shadow = !unlocked;
                return;
            }
            if (OriginalUnlockLevels.TryGetValue(theSeedType, out AdvantureLevel orig) && LevelToPlantMap.TryGetValue(orig, out PlantType rnd))
            {
                theSeedType = rnd;
                bool unlocked = IsPlantUnlocked(rnd);
                shadow = !unlocked;
            }
            else if (AdvantureConfig.unlockLevels != null && AdvantureConfig.unlockLevels.TryGetValue(theSeedType, out orig) && LevelToPlantMap.TryGetValue(orig, out rnd))
            {
                theSeedType = rnd;
                bool unlocked = IsPlantUnlocked(rnd);
                shadow = !unlocked;
            }
            else
            {
                bool unlocked = IsPlantUnlocked(theSeedType);
                shadow = !unlocked;
            }
        }

        [HarmonyPrefix, HarmonyPatch(typeof(InGameUI), nameof(InGameUI.UnlockCard))]
        public static void UnlockCard_Prefix(ref PlantType theSeedType)
        {
            EnsureInitialized();
            AdvantureLevel lvl;
            try { lvl = AdvantureManager.Instance != null ? AdvantureManager.Instance.level : (AdvantureLevel)GameAPP.theBoardLevel; }
            catch { lvl = AdvantureLevel.Day1; }
            if (LevelToPlantMap.TryGetValue(lvl, out _))
            {
                theSeedType = ResolveUniqueRewardForLevel(lvl);
            }
            else if (BlockDefaultGivePlants.Contains((int)theSeedType))
            {
                theSeedType = PickRandomPoolPlant(GetReceivedPlants(lvl));
            }
        }

        [HarmonyPrefix, HarmonyPatch(typeof(AdvantureConfig), nameof(AdvantureConfig.GetBasicPlantType))]
        public static bool GetBasicPlantType_Prefix(AdvantureLevel level, ref PlantType __result)
        {
            EnsureInitialized();
            if (LevelToPlantMap.TryGetValue(level, out PlantType rnd)) { __result = rnd; return false; }
            // Level outside the randomized mapping: never let the game default-award its vanilla plant.
            __result = PickRandomPoolPlant(GetReceivedPlants(level));
            return false;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(AdvantureConfig), nameof(AdvantureConfig.CheckPlantUnlock))]
        public static bool CheckPlantUnlock_Prefix(PlantType plantType, ref bool __result)
        {
            EnsureInitialized();
            __result = IsPlantUnlocked(plantType);
            return false;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(Lawnf), nameof(Lawnf.CheckIfPlantUnlock))]
        public static bool CheckIfPlantUnlock_Prefix(PlantType thePlantType, ref UnlockType __result)
        {
            EnsureInitialized();
            bool unlocked = IsPlantUnlocked(thePlantType);
            __result = unlocked ? UnlockType.Unlocked : UnlockType.NotUnlocked;
            return false;
        }

        // --- Milestone Victory Trigger: Every 5 Wins -> Award Bonus Random Plant ---
        [HarmonyPostfix, HarmonyPatch(typeof(Lawnf), nameof(Lawnf.SetAward))]
        public static void SetAward_Postfix(Board board, Vector2 position, bool killZombie, bool fake, PrizeMgr __result)
        {
            EnsureInitialized();
            if (board == null || __result == null) return;
            try
            {
                int completed = GetTotalCompletedLevels();
                if (completed > CurrentData.TotalWins) CurrentData.TotalWins = completed;
                else CurrentData.TotalWins++;
                SaveCurrentData();

                Plugin.LogSource?.LogInfo($"[Victory] Total wins: {CurrentData.TotalWins}");

                // Chance to receive a Special Bonus Reward Plant when winning ANY level (5% chance or guaranteed on 5-win milestone)
                var rand = new System.Random(Guid.NewGuid().GetHashCode() ^ Environment.TickCount);
                bool triggerBonus = (CurrentData.TotalWins % 5 == 0) || (rand.Next(100) < 5);
                if (triggerBonus)
                {
                    PlantType? bonusPlant = DoBonusReward();
                    if (bonusPlant.HasValue)
                    {
                        string notif = $"\ud83c\udf81 B\u1ea0N \u0110\xc3 NH\u1eacN \u0110\u01af\u1ee2C C\xc2Y TH\u01af\u1edeNG \u0110\u1eb6C BI\u1ec6T: [{bonusPlant.Value}]!";
                        Plugin.LogSource?.LogInfo(notif);
                        try
                        {
                            if (Core.InGameText.Instance != null)
                                Core.InGameText.Instance.ShowText(notif, 5f, false);
                        }
                        catch { }
                    }
                }

                AdvantureLevel lvl;
                try { lvl = AdvantureManager.Instance != null ? AdvantureManager.Instance.level : (AdvantureLevel)GameAPP.theBoardLevel; }
                catch { lvl = AdvantureLevel.Day1; }

                if (LevelToPlantMap.TryGetValue(lvl, out _))
                {
                    PlantType rnd = ResolveUniqueRewardForLevel(lvl);

                    CardUI cu = __result.GetComponent<CardUI>() ?? __result.GetComponentInChildren<CardUI>();
                    if (cu != null)
                    {
                        cu.thePlantType = rnd; cu.theSeedType = (int)rnd;
                        try { cu.ChangeCardSprite(); } catch { }
                    }

                    string notif = $"\ud83c\udf89 B\u1ea0N \u0110\xc3 NH\u1eacN \u0110\u01af\u1ee2C C\xc2Y M\u1edaI: [{rnd}]!";
                    Plugin.LogSource?.LogInfo(notif);
                    try
                    {
                        if (Core.InGameText.Instance != null)
                            Core.InGameText.Instance.ShowText(notif, 5f, false);
                    }
                    catch { }
                }
                else
                {
                    PlantType rnd = PickRandomPoolPlant(GetReceivedPlants(lvl));
                    CardUI cu = __result.GetComponent<CardUI>() ?? __result.GetComponentInChildren<CardUI>();
                    if (cu != null)
                    {
                        cu.thePlantType = rnd; cu.theSeedType = (int)rnd;
                        try { cu.ChangeCardSprite(); } catch { }
                    }
                    Plugin.LogSource?.LogInfo($"Award blocked vanilla default, gave random: [{rnd}] (level {lvl})");
                }
            }
            catch (Exception ex) { Plugin.LogSource?.LogWarning($"SetAward error: {ex.Message}"); }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(AdvantureConfig), nameof(AdvantureConfig.LoadData))]
        public static void LoadData_Postfix()
        {
            EnsureInitialized();
            try
            {
                var dict = AdvantureConfig.unlockLevels;
                if (dict != null && dict.Count > 0)
                {
                    if (OriginalUnlockLevels.Count == 0)
                    {
                        foreach (var kvp in dict)
                        {
                            OriginalUnlockLevels[kvp.Key] = kvp.Value;
                        }
                    }
                    // Remove vanilla default-unlocks so the game can never give these plants outside the random pool.
                    foreach (var kvp in OriginalUnlockLevels)
                    {
                        if (BlockDefaultGivePlants.Contains((int)kvp.Key))
                        {
                            dict.Remove(kvp.Key);
                        }
                    }
                }
            }
            catch (Exception ex) { Plugin.LogSource?.LogWarning($"LoadData_Postfix error: {ex.Message}"); }
        }
    }
}
