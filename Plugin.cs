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
    [BepInPlugin("com.duong.pvzfusion.plantsrandomizer", "Plants Randomizer", "1.0.0")]
    public class Plugin : BasePlugin
    {
        public static ManualLogSource LogSource = null!;
        public static BepInEx.Configuration.ConfigEntry<bool> IncludeColoredCards = null!;
        public static BonusUIManager BonusUIInstance = null!;

        public override void Load()
        {
            LogSource = Log;
            IncludeColoredCards = Config.Bind("General", "IncludeColoredCards", true, "Include base game special/colored card plants in post-adventure reward pool.");

            Log.LogInfo("Plants Randomizer Mod v1.0.0 initializing...");

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
        private const string CONFIG_VERSION = "1.0.0";
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

        // Candidate pool for 5-win Bonus rewards (Basic Plants + Food / Special Challenge Plants)
        public static PlantType[] CreateBonusPlantPool()
        {
            var pool = new List<PlantType>();
            // 1. Basic Plants (IDs 0..47: Peashooter, Sunflower, Wallnut, CherryBomb, PotatoMine, etc.)
            foreach (var obj in Enum.GetValues(typeof(PlantType)))
            {
                PlantType p = (PlantType)obj;
                int id = (int)p;
                if (id >= 0 && id <= 47)
                {
                    string name = p.ToString();
                    if (!name.EndsWith("Body") && !name.EndsWith("_land") && !name.EndsWith("_water") && name != "Nothing")
                        pool.Add(p);
                }
            }
            // 2. Food & Special Challenge Plants (IDs 1000..1999)
            foreach (var obj in Enum.GetValues(typeof(PlantType)))
            {
                PlantType p = (PlantType)obj;
                int id = (int)p;
                if (id >= 1000 && id < 2000)
                {
                    string name = p.ToString();
                    if (!name.StartsWith("EnumValue") && !name.EndsWith("Body") && !name.EndsWith("_land") && !name.EndsWith("_water") && name != "Nothing")
                        pool.Add(p);
                }
            }
            return pool.ToArray();
        }

        public static PlantType DoBonusReward()
        {
            PlantType[] pool = CreateBonusPlantPool();
            if (pool.Length == 0) return PlantType.Peashooter;

            var candidates = new List<PlantType>();
            foreach (var p in pool)
            {
                if (!CurrentData.BonusUnlockedPlants.Contains((int)p))
                    candidates.Add(p);
            }
            if (candidates.Count == 0) candidates.AddRange(pool);

            var rand = new System.Random(Guid.NewGuid().GetHashCode() ^ Environment.TickCount);
            PlantType chosen = candidates[rand.Next(candidates.Count)];
            CurrentData.BonusUnlockedPlants.Add((int)chosen);
            SaveCurrentData();
            return chosen;
        }

        public static PlantType[] GetSuperPlantList()
        {
            var list = new List<PlantType>();
            foreach (var obj in Enum.GetValues(typeof(PlantType)))
            {
                PlantType p = (PlantType)obj;
                int id = (int)p;
                if (id >= 1000 && id < 2000)
                {
                    string name = p.ToString();
                    if (!name.StartsWith("EnumValue") && !name.EndsWith("Body") && !name.EndsWith("_land") && !name.EndsWith("_water") && name != "Nothing")
                        list.Add(p);
                }
            }
            return list.ToArray();
        }

        public static PlantType[] CreateBasicPlantPool()
        {
            var pool = new List<PlantType>();
            foreach (var obj in Enum.GetValues(typeof(PlantType)))
            {
                PlantType p = (PlantType)obj;
                int id = (int)p;
                if (id < 0 || id > 47) continue;
                if (p == PlantType.Peashooter || p == PlantType.SunFlower || p == PlantType.LilyPad || p == PlantType.Pot) continue;
                string name = p.ToString();
                if (name.EndsWith("Body") || name.EndsWith("_land") || name.EndsWith("_water") || name == "Nothing") continue;
                pool.Add(p);
            }
            return pool.ToArray();
        }

        public static readonly HashSet<AdvantureLevel> ExcludedSpecialLevels = new()
        {
            AdvantureLevel.Pool1, AdvantureLevel.Roof1, AdvantureLevel.Day4,
            AdvantureLevel.Day_sub1, AdvantureLevel.Night5, AdvantureLevel.Pool5,
            AdvantureLevel.Roof5, AdvantureLevel.Roof6
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
                    if (File.Exists(fp))
                    {
                        string fn = Path.GetFileNameWithoutExtension(fp);
                        long ticks = File.GetCreationTimeUtc(fp).Ticks;
                        return $"{baseName}_{fn}_{ticks}";
                    }
                }
            }
            catch { }
            return baseName;
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
            if (!File.Exists(path)) { SaveCurrentData(); return; }
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
                    if (k == "BonusUnlockedPlants") foreach (string s in v.Split(',', StringSplitOptions.RemoveEmptyEntries)) if (int.TryParse(s.Trim(), out int p)) CurrentData.BonusUnlockedPlants.Add(p);
                }
            }
            catch (Exception ex) { Plugin.LogSource?.LogWarning($"Load data error: {ex.Message}"); }
        }

        public static void SaveCurrentData()
        {
            try
            {
                string path = GetDataPath(GetCurrentProfileKey());
                var sb = new StringBuilder();
                sb.AppendLine($"TotalWins={CurrentData.TotalWins}");
                sb.AppendLine($"BonusUnlockedPlants={string.Join(",", CurrentData.BonusUnlockedPlants)}");
                File.WriteAllText(path, sb.ToString());
            }
            catch (Exception ex) { Plugin.LogSource?.LogWarning($"Save data error: {ex.Message}"); }
        }

        private static void LoadOrGenerateMapping(string key)
        {
            LevelToPlantMap.Clear();
            string configPath = GetConfigPath(key);
            int seed = 0;

            if (File.Exists(configPath))
            {
                try
                {
                    bool valid = false;
                    foreach (string line in File.ReadAllLines(configPath))
                    {
                        string t = line.Trim();
                        if (t.StartsWith("# Version:") && t.Contains(CONFIG_VERSION)) valid = true;
                        if (t.StartsWith("# Seed:")) int.TryParse(t.Substring("# Seed:".Length).Trim(), out seed);
                        if (string.IsNullOrEmpty(t) || t.StartsWith("#")) continue;
                        int eq = t.IndexOf('=');
                        if (eq > 0 && int.TryParse(t.Substring(0, eq), out int lv) && int.TryParse(t.Substring(eq + 1), out int pv))
                        {
                            var lvl = (AdvantureLevel)lv;
                            if (!ExcludedSpecialLevels.Contains(lvl) && lv >= 1 && lv <= 100)
                                LevelToPlantMap[lvl] = (PlantType)pv;
                        }
                    }
                    if (valid && LevelToPlantMap.Count >= 30)
                    {
                        Plugin.LogSource?.LogInfo($"Loaded {LevelToPlantMap.Count} mappings for [{key}]");
                        return;
                    }
                    LevelToPlantMap.Clear();
                }
                catch { }
            }

            if (seed == 0) seed = Guid.NewGuid().GetHashCode() ^ Environment.TickCount ^ (int)DateTime.UtcNow.Ticks;
            var rand = new System.Random(seed);

            var levels = new List<AdvantureLevel>();
            foreach (var obj in Enum.GetValues(typeof(AdvantureLevel)))
            {
                var lvl = (AdvantureLevel)obj;
                int n = (int)lvl;
                if (n >= 1 && n <= 100 && !ExcludedSpecialLevels.Contains(lvl)) levels.Add(lvl);
            }

            var basic = new List<PlantType>(CreateBasicPlantPool());
            var super = new List<PlantType>(GetSuperPlantList());
            Shuffle(basic, rand); Shuffle(super, rand);
            int bi = 0, si = 0;
            bool includeColored = Plugin.IncludeColoredCards?.Value ?? true;

            foreach (var lvl in levels)
            {
                PlantType chosen;
                if (bi < basic.Count) chosen = basic[bi++];
                else if (includeColored && si < super.Count) chosen = super[si++];
                else { if (bi >= basic.Count) { Shuffle(basic, rand); bi = 0; } chosen = basic[bi++]; }
                LevelToPlantMap[lvl] = chosen;
            }

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine($"# Version: {CONFIG_VERSION}");
                sb.AppendLine($"# Seed: {seed}");
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

        public static bool IsPlantUnlocked(PlantType pt)
        {
            if (GameAPP.developerMode) return true;
            int id = (int)pt;
            if (CurrentData.BonusUnlockedPlants.Contains(id)) return true;

            if (pt == PlantType.Peashooter || pt == PlantType.SunFlower) return true;
            if (pt == PlantType.LilyPad) return IsLevelCompleted(AdvantureLevel.Pool1);
            if (pt == PlantType.Pot) return IsLevelCompleted(AdvantureLevel.Roof1);

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

        // --- Harmony Patches ---
        [HarmonyPrefix, HarmonyPatch(typeof(InitBoard), nameof(InitBoard.CreateCard), new Type[] { typeof(PlantType), typeof(bool), typeof(bool) })]
        public static void CreateCard_Prefix(ref PlantType theSeedType)
        {
            EnsureInitialized();
            if (AdvantureConfig.unlockLevels != null && AdvantureConfig.unlockLevels.TryGetValue(theSeedType, out AdvantureLevel orig) && LevelToPlantMap.TryGetValue(orig, out PlantType rnd))
                theSeedType = rnd;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(InGameUI), nameof(InGameUI.UnlockCard))]
        public static void UnlockCard_Prefix(ref PlantType theSeedType)
        {
            EnsureInitialized();
            AdvantureLevel lvl;
            try { lvl = AdvantureManager.Instance != null ? AdvantureManager.Instance.level : (AdvantureLevel)GameAPP.theBoardLevel; }
            catch { lvl = AdvantureLevel.Day1; }
            if (LevelToPlantMap.TryGetValue(lvl, out PlantType rnd)) theSeedType = rnd;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(AdvantureConfig), nameof(AdvantureConfig.GetBasicPlantType))]
        public static bool GetBasicPlantType_Prefix(AdvantureLevel level, ref PlantType __result)
        {
            EnsureInitialized();
            if (LevelToPlantMap.TryGetValue(level, out PlantType rnd)) { __result = rnd; return false; }
            return true;
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
                CurrentData.TotalWins++;
                SaveCurrentData();

                Plugin.LogSource?.LogInfo($"[Victory] Total wins: {CurrentData.TotalWins}");

                if (CurrentData.TotalWins % 5 == 0)
                {
                    PlantType bonusPlant = DoBonusReward();
                    string notif = $"🎉 THẮNG MỐC {CurrentData.TotalWins} TRẬN! Thưởng BONUS Cây: [{bonusPlant}]!";
                    Plugin.LogSource?.LogInfo(notif);
                    BonusUIManager.ShowNotif(notif, 7f);
                }

                AdvantureLevel lvl;
                try { lvl = AdvantureManager.Instance != null ? AdvantureManager.Instance.level : (AdvantureLevel)GameAPP.theBoardLevel; }
                catch { lvl = AdvantureLevel.Day1; }

                if (LevelToPlantMap.TryGetValue(lvl, out PlantType rnd))
                {
                    CardUI cu = __result.GetComponent<CardUI>() ?? __result.GetComponentInChildren<CardUI>();
                    if (cu != null)
                    {
                        cu.thePlantType = rnd; cu.theSeedType = (int)rnd;
                        try { cu.ChangeCardSprite(); } catch { }
                    }
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
                if (dict != null)
                {
                    dict.Clear();
                    dict[PlantType.Peashooter] = AdvantureLevel.Day1;
                    dict[PlantType.SunFlower] = AdvantureLevel.Day1;
                    foreach (var kvp in LevelToPlantMap) dict[kvp.Value] = kvp.Key;
                }
            }
            catch (Exception ex) { Plugin.LogSource?.LogWarning($"LoadData_Postfix error: {ex.Message}"); }
        }
    }
}
