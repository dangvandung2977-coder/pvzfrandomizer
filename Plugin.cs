using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;
using Il2CppInterop.Runtime.Injection;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace PlantsRandomizer
{
    [BepInPlugin("com.duong.pvzfusion.plantsrandomizer", "Plants Randomizer", "2.0.0")]
    public class Plugin : BasePlugin
    {
        public static ManualLogSource LogSource = null!;
        public static BepInEx.Configuration.ConfigEntry<bool> IncludeColoredCards = null!;
        public static ShopUIManager ShopInstance = null!;

        public override void Load()
        {
            LogSource = Log;
            IncludeColoredCards = Config.Bind("General", "IncludeColoredCards", true, "Include base game special/colored card plants in post-adventure reward pool.");

            Log.LogInfo("Plants Randomizer Mod v2.0.0 (Shop, Coins, Rental & Gacha) initializing...");

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

            try
            {
                ClassInjector.RegisterTypeInIl2Cpp<ShopUIManager>();
                GameObject shopObj = new GameObject("PlantsRandomizerShopUI");
                UnityEngine.Object.DontDestroyOnLoad(shopObj);
                ShopInstance = shopObj.AddComponent<ShopUIManager>();
                Log.LogInfo("ShopUIManager UI Component registered and created successfully!");
            }
            catch (Exception ex)
            {
                Log.LogWarning($"Could not register ShopUIManager component: {ex.Message}");
            }
        }
    }

    public class ProfileData
    {
        public int Coins = 200;
        public int TotalRolls = 0;
        public System.Collections.Generic.HashSet<int> UnlockedGachaPlants = new System.Collections.Generic.HashSet<int>();
        public System.Collections.Generic.HashSet<int> RentedPlants = new System.Collections.Generic.HashSet<int>();
    }

    public class ShopUIManager : MonoBehaviour
    {
        private bool _showWindow = false;
        private Rect _windowRect = new Rect(Screen.width / 2 - 330, Screen.height / 2 - 275, 660, 550);
        private int _selectedTab = 0; // 0 = Gacha, 1 = Rental Shop, 2 = Inventory
        private string _lastNotification = string.Empty;
        private float _notifTimer = 0f;
        private Vector2 _scrollPos = Vector2.zero;

        private Texture2D _bannerTex = null!;
        private Texture2D _coinTex = null!;

        public ShopUIManager(IntPtr ptr) : base(ptr) { }

        private void Start()
        {
            LoadGUITextures();
        }

        private void LoadGUITextures()
        {
            try
            {
                string assetsDir = Path.Combine(Paths.PluginPath, "PlantsRandomizer_Assets");
                string bannerPath = Path.Combine(assetsDir, "gacha_banner_bg.png");
                string coinPath = Path.Combine(assetsDir, "fusion_coin_icon.png");

                if (File.Exists(bannerPath))
                {
                    byte[] bData = File.ReadAllBytes(bannerPath);
                    Il2CppStructArray<byte> ilBannerData = bData;
                    _bannerTex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    ImageConversion.LoadImage(_bannerTex, ilBannerData);
                }

                if (File.Exists(coinPath))
                {
                    byte[] cData = File.ReadAllBytes(coinPath);
                    Il2CppStructArray<byte> ilCoinData = cData;
                    _coinTex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    ImageConversion.LoadImage(_coinTex, ilCoinData);
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource?.LogWarning($"Error loading GUI textures: {ex.Message}");
            }
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F3))
            {
                _showWindow = !_showWindow;
            }

            if (_notifTimer > 0)
            {
                _notifTimer -= Time.deltaTime;
                if (_notifTimer <= 0)
                {
                    _lastNotification = string.Empty;
                }
            }
        }

        public void ShowNotification(string msg, float duration = 4.0f)
        {
            _lastNotification = msg;
            _notifTimer = duration;
        }

        private void OnGUI()
        {
            AwardPatches.EnsureInitialized();
            var data = AwardPatches.CurrentData;

            // Draw Coin HUD
            GUI.Box(new Rect(10, 10, 230, 45), string.Empty);
            if (_coinTex != null)
            {
                GUI.DrawTexture(new Rect(15, 12, 40, 40), _coinTex);
            }
            GUI.Label(new Rect(60, 20, 175, 30), $"<b><color=yellow>Fusion Coins: {data.Coins}</color></b>");

            if (GUI.Button(new Rect(250, 12, 130, 40), _showWindow ? "Đóng Shop [F3]" : "🛒 Gacha Shop [F3]"))
            {
                _showWindow = !_showWindow;
            }

            if (!string.IsNullOrEmpty(_lastNotification))
            {
                GUI.Box(new Rect(Screen.width / 2 - 220, 20, 440, 45), string.Empty);
                GUI.Label(new Rect(Screen.width / 2 - 210, 30, 420, 30), $"<b><color=lime>✨ {_lastNotification}</color></b>");
            }

            if (!_showWindow) return;

            _windowRect = GUI.Window(9928, _windowRect, (GUI.WindowFunction)DrawShopWindow, "🛒 Plants Randomizer - Gacha & Shop Center");
        }

        private void DrawShopWindow(int windowID)
        {
            GUI.DragWindow(new Rect(0, 0, 660, 25));

            var data = AwardPatches.CurrentData;

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("🎰 Quay Gacha Banner", GUILayout.Height(35))) _selectedTab = 0;
            if (GUILayout.Button("⏳ Thuê Cây Theo Trận", GUILayout.Height(35))) _selectedTab = 1;
            if (GUILayout.Button("📦 Cây Đã Mở Khóa", GUILayout.Height(35))) _selectedTab = 2;
            GUILayout.EndHorizontal();

            GUILayout.Space(10);
            GUILayout.Label($"<b>Số dư: <color=yellow>🪙 {data.Coins} Coins</color></b>  |  <b>Tổng lượt quay: <color=cyan>{data.TotalRolls}</color></b>");
            GUILayout.Space(10);

            if (_selectedTab == 0)
            {
                DrawGachaTab(data);
            }
            else if (_selectedTab == 1)
            {
                DrawRentalTab(data);
            }
            else if (_selectedTab == 2)
            {
                DrawInventoryTab(data);
            }

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Đóng Cửa Hàng [F3]", GUILayout.Height(32)))
            {
                _showWindow = false;
            }
        }

        private void DrawGachaTab(ProfileData data)
        {
            if (_bannerTex != null)
            {
                Rect bannerRect = GUILayoutUtility.GetRect(640, 160);
                GUI.DrawTexture(bannerRect, _bannerTex);
            }
            else
            {
                GUILayout.Box("🎰 Gacha Banner - Mở Khóa Cây Vĩnh Viễn\n🟢 Common (60%) | 🔵 Rare (30%) | 🟡 Legendary/Ultimate (10%)");
            }

            GUILayout.Space(15);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("🎲 Quay 1 Lần (100 Coins)", GUILayout.Height(50)))
            {
                if (data.Coins >= 100)
                {
                    data.Coins -= 100;
                    data.TotalRolls++;
                    PlantType rolled = AwardPatches.DoGachaRoll(data);
                    ShowNotification($"Quay trúng: [{rolled}]!");
                    AwardPatches.SaveCurrentData();
                }
                else
                {
                    ShowNotification("❌ Bạn không đủ Coins! (Cần 100 Coins)");
                }
            }

            if (GUILayout.Button("🎰 Quay 10 Lần (900 Coins)", GUILayout.Height(50)))
            {
                if (data.Coins >= 900)
                {
                    data.Coins -= 900;
                    data.TotalRolls += 10;
                    System.Collections.Generic.List<string> results = new System.Collections.Generic.List<string>();
                    for (int i = 0; i < 10; i++)
                    {
                        PlantType rolled = AwardPatches.DoGachaRoll(data);
                        results.Add(rolled.ToString());
                    }
                    ShowNotification($"10x Roll: {string.Join(", ", results)}");
                    AwardPatches.SaveCurrentData();
                }
                else
                {
                    ShowNotification("❌ Bạn không đủ Coins! (Cần 900 Coins)");
                }
            }
            GUILayout.EndHorizontal();
        }

        private void DrawRentalTab(ProfileData data)
        {
            GUILayout.Box("⏳ Cửa Hàng Cho Thuê Cây - Thuê Cây Dùng Cho 1 Trận Đấu (30 Coins/Cây)");
            GUILayout.Space(10);

            _scrollPos = GUILayout.BeginScrollView(_scrollPos, GUILayout.Height(280));
            
            PlantType[] coloredPool = AwardPatches.CreateColoredPlantPool();
            foreach (PlantType pt in coloredPool)
            {
                int ptId = (int)pt;
                bool isRented = data.RentedPlants.Contains(ptId);
                bool isUnlocked = data.UnlockedGachaPlants.Contains(ptId) || AwardPatches.IsPlantUnlockedInternal(pt);

                GUILayout.BeginHorizontal();
                GUILayout.Label($"{pt} (ID {ptId})", GUILayout.Width(250));

                if (isUnlocked)
                {
                    GUILayout.Label("✅ Đã mở khóa", GUILayout.Width(150));
                }
                else if (isRented)
                {
                    GUILayout.Label("⏳ Đã thuê (1 trận)", GUILayout.Width(150));
                }
                else
                {
                    if (GUILayout.Button("Thuê 30 Coins", GUILayout.Width(150)))
                    {
                        if (data.Coins >= 30)
                        {
                            data.Coins -= 30;
                            data.RentedPlants.Add(ptId);
                            ShowNotification($"Đã thuê [{pt}] cho trận đấu này!");
                            AwardPatches.SaveCurrentData();
                        }
                        else
                        {
                            ShowNotification("❌ Bạn không đủ Coins!");
                        }
                    }
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.EndScrollView();
        }

        private void DrawInventoryTab(ProfileData data)
        {
            GUILayout.Box($"📦 Danh sách cây đã mở khóa qua Gacha ({data.UnlockedGachaPlants.Count} cây)");
            GUILayout.Space(10);

            _scrollPos = GUILayout.BeginScrollView(_scrollPos, GUILayout.Height(280));
            foreach (int ptId in data.UnlockedGachaPlants)
            {
                PlantType pt = (PlantType)ptId;
                GUILayout.Label($"✨ {pt} (ID {ptId})");
            }
            GUILayout.EndScrollView();
        }
    }

    [HarmonyPatch]
    public static class AwardPatches
    {
        private const string CONFIG_VERSION = "2.0.0";
        private static readonly object Sync = new object();
        private static bool _initialized = false;
        private static string _activeProfile = string.Empty;
        public static readonly System.Collections.Generic.Dictionary<AdvantureLevel, PlantType> LevelToPlantMap = new();
        public static ProfileData CurrentData = new ProfileData();

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

                if (id < 0 || id > 47) continue;

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

                bool isSpecialOrColored = (id >= 200 && id <= 299) || (id >= 1000 && id < 2000);
                if (!isSpecialOrColored) continue;

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

        public static PlantType DoGachaRoll(ProfileData data)
        {
            System.Random rand = new System.Random(Guid.NewGuid().GetHashCode() ^ Environment.TickCount);
            int roll = rand.Next(100);

            PlantType[] pool;
            if (roll < 60)
            {
                pool = CreateBasicPlantPool();
            }
            else
            {
                pool = CreateColoredPlantPool();
            }

            if (pool.Length == 0) pool = CreateBasicPlantPool();

            PlantType chosen = pool[rand.Next(pool.Length)];
            data.UnlockedGachaPlants.Add((int)chosen);
            return chosen;
        }

        public static string GetCurrentProfileKey()
        {
            string baseName = "default";

            try
            {
                if (!string.IsNullOrEmpty(GameAPP.playerName))
                {
                    baseName = GameAPP.playerName;
                }
                else if (!string.IsNullOrEmpty(SaveInfo.LAST_SAVE_KEY))
                {
                    baseName = SaveInfo.LAST_SAVE_KEY;
                }
                else if (SaveInfo.Instance != null && !string.IsNullOrEmpty(SaveInfo.Instance.FilePath))
                {
                    baseName = Path.GetFileNameWithoutExtension(SaveInfo.Instance.FilePath);
                }
            }
            catch { }

            try
            {
                if (SaveInfo.Instance != null && !string.IsNullOrEmpty(SaveInfo.Instance.FilePath))
                {
                    string filePath = SaveInfo.Instance.FilePath;
                    if (File.Exists(filePath))
                    {
                        DateTime cTime = File.GetCreationTimeUtc(filePath);
                        string fileName = Path.GetFileNameWithoutExtension(filePath);
                        return $"{baseName}_{fileName}_{cTime.Ticks}";
                    }
                }
            }
            catch { }

            return baseName;
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

        private static string GetDataPath(string profileKey)
        {
            if (string.IsNullOrEmpty(profileKey)) profileKey = "default";
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                profileKey = profileKey.Replace(c, '_');
            }
            return Path.Combine(Paths.ConfigPath, $"PlantsRandomizer_Data_{profileKey}.txt");
        }

        public static void EnsureInitialized()
        {
            string currentProfile = GetCurrentProfileKey();

            if (_initialized && _activeProfile == currentProfile) return;

            lock (Sync)
            {
                if (_initialized && _activeProfile == currentProfile) return;

                LoadOrGenerateMapping(currentProfile);
                LoadProfileData(currentProfile);
                _activeProfile = currentProfile;
                _initialized = true;
            }
        }

        private static void LoadProfileData(string profileKey)
        {
            CurrentData = new ProfileData();
            string dataPath = GetDataPath(profileKey);

            if (File.Exists(dataPath))
            {
                try
                {
                    string[] lines = File.ReadAllLines(dataPath);
                    foreach (string line in lines)
                    {
                        string trimmed = line.Trim();
                        if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#")) continue;

                        int idx = trimmed.IndexOf('=');
                        if (idx > 0)
                        {
                            string key = trimmed.Substring(0, idx).Trim();
                            string val = trimmed.Substring(idx + 1).Trim();

                            if (key == "Coins" && int.TryParse(val, out int c)) CurrentData.Coins = c;
                            if (key == "TotalRolls" && int.TryParse(val, out int tr)) CurrentData.TotalRolls = tr;
                            if (key == "UnlockedGachaPlants")
                            {
                                foreach (string s in val.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                                {
                                    if (int.TryParse(s.Trim(), out int pid)) CurrentData.UnlockedGachaPlants.Add(pid);
                                }
                            }
                            if (key == "RentedPlants")
                            {
                                foreach (string s in val.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                                {
                                    if (int.TryParse(s.Trim(), out int pid)) CurrentData.RentedPlants.Add(pid);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Plugin.LogSource?.LogWarning($"Failed to load profile data for [{profileKey}]: {ex.Message}");
                }
            }
            else
            {
                SaveCurrentData();
            }
        }

        public static void SaveCurrentData()
        {
            try
            {
                string profileKey = GetCurrentProfileKey();
                string dataPath = GetDataPath(profileKey);

                StringBuilder sb = new StringBuilder();
                sb.AppendLine($"Coins={CurrentData.Coins}");
                sb.AppendLine($"TotalRolls={CurrentData.TotalRolls}");
                sb.AppendLine($"UnlockedGachaPlants={string.Join(",", CurrentData.UnlockedGachaPlants)}");
                sb.AppendLine($"RentedPlants={string.Join(",", CurrentData.RentedPlants)}");

                File.WriteAllText(dataPath, sb.ToString());
            }
            catch (Exception ex)
            {
                Plugin.LogSource?.LogWarning($"Failed to save profile data: {ex.Message}");
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

            int seed = savedSeed != 0 ? savedSeed : GenerateUniqueRandomSeed();
            System.Random rand = new System.Random(seed);

            Array levelValues = Enum.GetValues(typeof(AdvantureLevel));
            System.Collections.Generic.List<AdvantureLevel> allLevels = new System.Collections.Generic.List<AdvantureLevel>();
            foreach (var lvlObj in levelValues)
            {
                AdvantureLevel lvl = (AdvantureLevel)lvlObj;
                int lvlNum = (int)lvl;

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

        public static bool IsPlantUnlockedInternal(PlantType plantType)
        {
            if (GameAPP.developerMode) return true;

            int ptId = (int)plantType;

            if (CurrentData != null)
            {
                if (CurrentData.UnlockedGachaPlants.Contains(ptId)) return true;
                if (CurrentData.RentedPlants.Contains(ptId)) return true;
            }

            if (plantType == PlantType.Peashooter || plantType == PlantType.SunFlower)
            {
                return true;
            }

            if (plantType == PlantType.LilyPad)
            {
                return IsLevelCompleted(AdvantureLevel.Pool1);
            }

            if (plantType == PlantType.Pot)
            {
                return IsLevelCompleted(AdvantureLevel.Roof1);
            }

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
                CurrentData.Coins += 100;
                CurrentData.RentedPlants.Clear();
                SaveCurrentData();

                if (Plugin.ShopInstance != null)
                {
                    Plugin.ShopInstance.ShowNotification("🎉 Thắng màn chơi! Nhận +100 Fusion Coins 🪙");
                }

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
