
///////////////////////////////////////////////////////////////////////////
//                         Made by RubMyBricks                           //
//                         Discord: rubmybricks                          //
//                         DiscordServer: https://discord.gg/gPg92292HS  //
//                         Website: www.bricksx.xyz                      //
//                                                                       //
//                         Feel free to message me on                    //
//                         discord if you have any issues!               //
///////////////////////////////////////////////////////////////////////////       
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;
using System.Collections.Generic;
using Newtonsoft.Json;
using System;
using System.Linq;

namespace Oxide.Plugins
{
    [Info("Safezone Respawner", "RubMyBricks", "1.4.1")]
    [Description("Allows players to respawn at safezones upon death!")]
    public class SafezoneRespawner : RustPlugin
    {
        [PluginReference] private Plugin ImageLibrary;

        #region Constants
        private const string PermissionUse = "safezonerespawner.use";
        private const string GUI_PANEL_NAME = "SafezoneRespawnGUI";
        private const string BANDIT_PANEL_NAME = "BanditRespawnGUI";
        private const string OUTPOST_IMAGE_ID = "safezone_outpost_button";
        private const string BANDIT_IMAGE_ID = "safezone_bandit_button";
        #endregion

        #region Fields
        private Vector3 outpostPosition;
        private Vector3 banditPosition;
        private Vector3 outpostSpawnPoint;
        private Vector3 banditSpawnPoint;
        private Dictionary<ulong, Timer> guiTimers = new Dictionary<ulong, Timer>();
        private Dictionary<ulong, Dictionary<string, DateTime>> playerCooldowns = new Dictionary<ulong, Dictionary<string, DateTime>>();
        private HashSet<ulong> recentlyRespawned = new HashSet<ulong>();
        private bool isInitialized = false;
        private ConfigData config;
        //cache
        private HashSet<ulong> permissionCache = new HashSet<ulong>();
        private readonly Dictionary<ulong, BasePlayer> playerCache = new Dictionary<ulong, BasePlayer>();
        private Timer cacheCleanupTimer;
        private const int CACHE_CLEANUP_INTERVAL = 300; 
        #endregion

        #region Configuration
        private class ConfigData
        {
            [JsonProperty(PropertyName = "Enable Outpost Respawn")]
            public bool EnableOutpost { get; set; } = true;

            [JsonProperty(PropertyName = "Enable Bandit Camp Respawn")]
            public bool EnableBandit { get; set; } = false;

            [JsonProperty(PropertyName = "Remove Hostility On Respawn")]
            public bool RemoveHostilityOnRespawn { get; set; } = false;

            [JsonProperty(PropertyName = "Use ImageLibrary for Icons")]
            public bool UseImageLibrary { get; set; } = false;

            [JsonProperty(PropertyName = "Spawn Settings")]
            public SpawnSettings SpawnSettings { get; set; } = new SpawnSettings();

            [JsonProperty(PropertyName = "Custom Images")]
            public CustomImages Images { get; set; } = new CustomImages();

            [JsonProperty(PropertyName = "GUI Settings")]
            public GUISettings GuiSettings { get; set; } = new GUISettings();
        }

        private class SpawnSettings
        {
            [JsonProperty(PropertyName = "Outpost Cooldown (seconds)")]
            public float OutpostCooldown { get; set; } = 120f;

            [JsonProperty(PropertyName = "Bandit Camp Cooldown (seconds)")]
            public float BanditCooldown { get; set; } = 120f;

            [JsonProperty(PropertyName = "Enable Health Setting")]
            public bool EnableHealth { get; set; } = false;

            [JsonProperty(PropertyName = "Enable Food Setting")]
            public bool EnableFood { get; set; } = false;

            [JsonProperty(PropertyName = "Enable Water Setting")]
            public bool EnableWater { get; set; } = false;

            [JsonProperty(PropertyName = "Spawn Health (max =100")]
            public float SpawnHealth { get; set; } = 100f;

            [JsonProperty(PropertyName = "Spawn Food (max =300?)")]
            public float SpawnFood { get; set; } = 300f;

            [JsonProperty(PropertyName = "Spawn Water (max =250)")]
            public float SpawnWater { get; set; } = 200f;

            [JsonProperty(PropertyName = "Spawn Position Offsets")]
            public SpawnOffsets SpawnOffsets { get; set; } = new SpawnOffsets();
        }

        private class SpawnOffsets
        {
            [JsonProperty(PropertyName = "Outpost Offset (X,Y,Z)")]
            public Vector3 OutpostOffset { get; set; } = new Vector3(0f, 0f, -23f);

            [JsonProperty(PropertyName = "Bandit Camp Offset (X,Y,Z)")]
            public Vector3 BanditOffset { get; set; } = new Vector3(0f, 0f, -25f);

            [JsonProperty(PropertyName = "Height Above Ground")]
            public float HeightAboveGround { get; set; } = 0.3f;
        }

        private class CustomImages
        {
            [JsonProperty(PropertyName = "Outpost Button Image URL")]
            public string OutpostImage { get; set; } = "your_custom_image_url";

            [JsonProperty(PropertyName = "Bandit Button Image URL")]
            public string BanditImage { get; set; } = "your_custom_image_url";
        }

        private class GUISettings
        {
            [JsonProperty(PropertyName = "Outpost Button Color")]
            public string OutpostColor { get; set; } = "0.42 0.55 0.24 1";

            [JsonProperty(PropertyName = "Bandit Button Color")]
            public string BanditColor { get; set; } = "0.55 0.42 0.24 1";

            [JsonProperty(PropertyName = "Text Color")]
            public string TextColor { get; set; } = "1 1 1 1";

            [JsonProperty(PropertyName = "Cooldown Text Color")]
            public string CooldownColor { get; set; } = "0.7 0.7 0.7 1";

            [JsonProperty(PropertyName = "Disabled Button Color")]
            public string DisabledColor { get; set; } = "0.3 0.3 0.3 1";
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<ConfigData>();
                if (config == null) throw new System.Exception();
                SaveConfig();
            }
            catch
            {
                PrintWarning("Config file is corrupt :(, creating new config file!");
                LoadDefaultConfig();
            }
        }

        protected override void LoadDefaultConfig()
        {
            config = new ConfigData();
            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(config);
        #endregion

        #region Initialization
        void OnServerInitialized()
        {
            permission.RegisterPermission(PermissionUse, this);
            InitializeSpawnPoints();
            InitializeCaching();

            if (config.UseImageLibrary)
            {
                if (ImageLibrary == null)
                {
                    PrintWarning("ImageLibrary is not loaded! Plugin will use default icons.");
                    config.UseImageLibrary = false;
                    SaveConfig();
                }
                else
                {
                    string outpostUrl = EnsureHttps(config.Images.OutpostImage);
                    string banditUrl = EnsureHttps(config.Images.BanditImage);

                    bool outpostSuccess = ImageLibrary.Call<bool>("AddImage", outpostUrl, OUTPOST_IMAGE_ID);
                    if (!outpostSuccess)
                    {
                        PrintWarning($"Failed to load outpost image from {outpostUrl}");
                    }

                    bool banditSuccess = ImageLibrary.Call<bool>("AddImage", banditUrl, BANDIT_IMAGE_ID);
                    if (!banditSuccess)
                    {
                        PrintWarning($"Failed to load bandit camp image from {banditUrl}");
                    }
                }
            }
        }

        private string EnsureHttps(string url)
        {
            if (string.IsNullOrEmpty(url)) return url;

            if (url.StartsWith("http://i.imgur.com"))
            {
                return url.Replace("http://", "https://");
            }
            else if (url.Contains("imgur.com") && !url.Contains("i.imgur.com"))
            {
                string[] urlParts = url.Split('/');
                string imgurId = urlParts[urlParts.Length - 1];
                return $"https://i.imgur.com/{imgurId}.png";
            }
            return url;
        }

        private void InitializeSpawnPoints()
        {
            try
            {
                Puts("Searching for safezones...");

                var monuments = TerrainMeta.Path.Monuments;
                Puts($"Found {monuments.Count} monuments total");

                foreach (var monument in monuments)
                {
                    string monumentName = monument.name.ToLower();
                    Puts($"Checking monument: {monumentName}");

                    if (monumentName.Contains("outpost") || monumentName.Contains("compound"))
                    {
                        Puts($"Found Outpost at position: {monument.transform.position}");
                        outpostPosition = monument.transform.position;

                        outpostSpawnPoint = outpostPosition + config.SpawnSettings.SpawnOffsets.OutpostOffset;
                        float groundY = TerrainMeta.HeightMap.GetHeight(outpostSpawnPoint);
                        outpostSpawnPoint = new Vector3(outpostSpawnPoint.x,
                            groundY + config.SpawnSettings.SpawnOffsets.HeightAboveGround,
                            outpostSpawnPoint.z);

                        Puts($"Set outpost spawn point at: {outpostSpawnPoint}");
                    }
                    else if (monumentName.Contains("bandit"))
                    {
                        Puts($"Found Bandit Camp at position: {monument.transform.position}");
                        banditPosition = monument.transform.position;

                        banditSpawnPoint = banditPosition + config.SpawnSettings.SpawnOffsets.BanditOffset;
                        float groundY = TerrainMeta.HeightMap.GetHeight(banditSpawnPoint);
                        banditSpawnPoint = new Vector3(banditSpawnPoint.x,
                            groundY + config.SpawnSettings.SpawnOffsets.HeightAboveGround,
                            banditSpawnPoint.z);

                        Puts($"Set bandit spawn point at: {banditSpawnPoint}");
                    }
                }

                isInitialized = true;
                Puts("Spawn points initialized successfully");
            }
            catch (System.Exception ex)
            {
                PrintError($"Error initializing spawn points: {ex}");
            }
        }

        private void InitializeCaching()
        {
            cacheCleanupTimer?.Destroy();
            cacheCleanupTimer = timer.Every(CACHE_CLEANUP_INTERVAL, CleanupCaches);
        }

        private void CleanupCaches()
        {
            var offlinePermPlayers = permissionCache.ToList();
            foreach (var userId in offlinePermPlayers)
            {
                var player = BasePlayer.FindByID(userId);
                if (player == null || !player.IsConnected)
                {
                    permissionCache.Remove(userId);
                }
            }

            var offlinePlayers = playerCache.Keys.ToList();
            foreach (var userId in offlinePlayers)
            {
                var player = BasePlayer.FindByID(userId);
                if (player == null || !player.IsConnected)
                {
                    playerCache.Remove(userId);
                }
            }
        }

        #endregion

        #region Cooldown Methods
        private bool IsOnCooldown(ulong userId, string location)
        {
            if (!playerCooldowns.ContainsKey(userId))
                return false;

            if (!playerCooldowns[userId].ContainsKey(location))
                return false;

            var timeRemaining = GetCooldownTimeRemaining(userId, location);
            return timeRemaining > 0;
        }

        private double GetCooldownTimeRemaining(ulong userId, string location)
        {
            if (!playerCooldowns.ContainsKey(userId) || !playerCooldowns[userId].ContainsKey(location))
                return 0;

            var timeSinceLast = DateTime.UtcNow - playerCooldowns[userId][location];
            float cooldownSeconds = location == "outpost"
                ? config.SpawnSettings.OutpostCooldown
                : config.SpawnSettings.BanditCooldown;

            return Math.Max(0, cooldownSeconds - timeSinceLast.TotalSeconds);
        }

        private void StartCooldown(ulong userId, string location)
        {
            if (!playerCooldowns.ContainsKey(userId))
                playerCooldowns[userId] = new Dictionary<string, DateTime>();

            playerCooldowns[userId][location] = DateTime.UtcNow;
        }
        #endregion

        #region Spawn Methods
        private void SetPlayerSpawnConditions(BasePlayer player)
        {
            if (player == null) return;

            if (config.SpawnSettings.EnableHealth)
            {
                player.health = config.SpawnSettings.SpawnHealth;
            }

            var metabolism = player.metabolism;
            if (metabolism != null)
            {
                if (config.SpawnSettings.EnableFood)
                {
                    metabolism.calories.value = config.SpawnSettings.SpawnFood;
                }

                if (config.SpawnSettings.EnableWater)
                {
                    metabolism.hydration.value = config.SpawnSettings.SpawnWater;
                }
            }
        }
        #endregion

        #region GUI
        private void ShowRespawnGUI(BasePlayer player)
        {
            if (!isInitialized) return;

            var elements = new CuiElementContainer();

            const string BUTTON_HEIGHT_MIN = "0.15";
            const string BUTTON_HEIGHT_MAX = "0.22";
            const float OUTPOST_MIN_X = 0.45f;
            const float OUTPOST_MAX_X = 0.55f;
            const float BANDIT_MIN_X = 0.56f;
            const float BANDIT_MAX_X = 0.66f;

            if (config.EnableOutpost)
            {
                elements.Add(new CuiPanel
                {
                    Image = { Color = "0 0 0 0" },
                    RectTransform = { 
                        AnchorMin = $"{OUTPOST_MIN_X} {BUTTON_HEIGHT_MIN}", 
                        AnchorMax = $"{OUTPOST_MAX_X} {BUTTON_HEIGHT_MAX}" 
                    },
                    CursorEnabled = true
                }, "Overlay", $"{GUI_PANEL_NAME}_overlay");

                elements.Add(new CuiPanel
                {
                    Image = { Color = IsOnCooldown(player.userID, "outpost") ? config.GuiSettings.DisabledColor : config.GuiSettings.OutpostColor },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                    CursorEnabled = true
                }, $"{GUI_PANEL_NAME}_overlay", GUI_PANEL_NAME);

                elements.Add(new CuiElement
                {
                    Name = $"{GUI_PANEL_NAME}_icon",
                    Parent = GUI_PANEL_NAME,
                    Components =
                    {
                        config.UseImageLibrary && ImageLibrary != null
                        ? new CuiRawImageComponent {
                            Png = (string)ImageLibrary.Call("GetImage", OUTPOST_IMAGE_ID)
                        }
                        : new CuiImageComponent {
                            Sprite = "assets/icons/arrow_right.png",
                            Color = config.GuiSettings.TextColor
                        },
                        new CuiRectTransformComponent {
                            AnchorMin = "0.02 0.1",
                            AnchorMax = "0.25 0.9",
                            OffsetMin = "1 1",
                            OffsetMax = "-1 -1"
                        }
                    }
                });

                var outpostTimeRemaining = GetCooldownTimeRemaining(player.userID, "outpost");
                string outpostButtonText = outpostTimeRemaining > 0 ? $"OUTPOST ({outpostTimeRemaining:F0}s)" : "OUTPOST »";
                string outpostTextColor = outpostTimeRemaining > 0 ? config.GuiSettings.CooldownColor : config.GuiSettings.TextColor;

                elements.Add(new CuiLabel
                {
                    Text = { 
                        Text = outpostButtonText, 
                        FontSize = 18, 
                        Align = TextAnchor.MiddleLeft, 
                        Color = outpostTextColor 
                    },
                    RectTransform = { 
                        AnchorMin = "0.26 0",  
                        AnchorMax = "0.95 1" 
                    }
                }, GUI_PANEL_NAME);

                if (outpostTimeRemaining <= 0)
                {
                    elements.Add(new CuiButton
                    {
                        Button = { Command = "safezone.spawn outpost", Color = "0 0 0 0" },
                        RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                        Text = { Text = "" }
                    }, GUI_PANEL_NAME);
                }
            }

            if (config.EnableBandit)
            {
                elements.Add(new CuiPanel
                {
                    Image = { Color = "0 0 0 0" },
                    RectTransform = { 
                        AnchorMin = $"{BANDIT_MIN_X} {BUTTON_HEIGHT_MIN}", 
                        AnchorMax = $"{BANDIT_MAX_X} {BUTTON_HEIGHT_MAX}" 
                    },
                    CursorEnabled = true
                }, "Overlay", $"{BANDIT_PANEL_NAME}_overlay");

                elements.Add(new CuiPanel
                {
                    Image = { Color = IsOnCooldown(player.userID, "bandit") ? config.GuiSettings.DisabledColor : config.GuiSettings.BanditColor },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                    CursorEnabled = true
                }, $"{BANDIT_PANEL_NAME}_overlay", BANDIT_PANEL_NAME);

                elements.Add(new CuiElement
                {
                    Name = $"{BANDIT_PANEL_NAME}_icon",
                    Parent = BANDIT_PANEL_NAME,
                    Components =
                    {
                        config.UseImageLibrary && ImageLibrary != null
                        ? new CuiRawImageComponent {
                            Png = (string)ImageLibrary.Call("GetImage", BANDIT_IMAGE_ID)
                        }
                        : new CuiImageComponent {
                            Sprite = "assets/icons/arrow_right.png",
                            Color = config.GuiSettings.TextColor
                        },
                        new CuiRectTransformComponent {
                            AnchorMin = "0.02 0.1",
                            AnchorMax = "0.25 0.9",
                            OffsetMin = "1 1",
                            OffsetMax = "-1 -1"
                        }
                    }
                });

                var banditTimeRemaining = GetCooldownTimeRemaining(player.userID, "bandit");
                string banditButtonText = banditTimeRemaining > 0 ? $"BANDIT ({banditTimeRemaining:F0}s)" : "BANDIT »";
                string banditTextColor = banditTimeRemaining > 0 ? config.GuiSettings.CooldownColor : config.GuiSettings.TextColor;

                elements.Add(new CuiLabel
                {
                    Text = { 
                        Text = banditButtonText, 
                        FontSize = 18, 
                        Align = TextAnchor.MiddleCenter, 
                        Color = banditTextColor 
                    },
                    RectTransform = { 
                        AnchorMin = "0.26 0",  
                        AnchorMax = "0.95 1" 
                    }
                }, BANDIT_PANEL_NAME);

                if (banditTimeRemaining <= 0)
                {
                    elements.Add(new CuiButton
                    {
                        Button = { Command = "safezone.spawn bandit", Color = "0 0 0 0" },
                        RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                        Text = { Text = "" }
                    }, BANDIT_PANEL_NAME);
                }
            }

            DestroyGUI(player);
            CuiHelper.AddUi(player, elements);

            var outpostCooldown = GetCooldownTimeRemaining(player.userID, "outpost");
            var banditCooldown = GetCooldownTimeRemaining(player.userID, "bandit");
            var maxCooldown = Math.Max(outpostCooldown, banditCooldown);

            if (maxCooldown > 0)
            {
                if (guiTimers.ContainsKey(player.userID))
                {
                    guiTimers[player.userID]?.Destroy();
                }

                var updateTimer = timer.Once(1f, () => ShowRespawnGUI(player));
                guiTimers[player.userID] = updateTimer;
            }
        }

        private void DestroyGUI(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, $"{GUI_PANEL_NAME}_overlay");
            CuiHelper.DestroyUi(player, $"{BANDIT_PANEL_NAME}_overlay");
        }
        #endregion

        #region Hooks
        private Timer initialGUITimer;

        void OnPlayerDeath(BasePlayer player, HitInfo info)
        {
            if (!permission.UserHasPermission(player.UserIDString, PermissionUse))
                return;

            if (!isInitialized)
            {
                InitializeSpawnPoints();
            }

            initialGUITimer?.Destroy();

            initialGUITimer = timer.Once(4.0f, () =>
            {
                if (!player.IsDead()) return;
                ShowRespawnGUI(player);

                timer.Once(0.5f, () =>
                {
                    if (!player.IsDead()) return;
                    ShowRespawnGUI(player);

                    timer.Once(0.5f, () =>
                    {
                        if (!player.IsDead()) return;
                        ShowRespawnGUI(player);
                    });
                });
            });
        }

        void OnPlayerRespawned(BasePlayer player)
        {
            if (guiTimers.ContainsKey(player.userID))
            {
                guiTimers[player.userID]?.Destroy();
                guiTimers.Remove(player.userID);
            }

            DestroyGUI(player);

            if (config.RemoveHostilityOnRespawn)
            {
                recentlyRespawned.Add(player.userID);
                player.State.unHostileTimestamp = CurrentTime() + 300;  
                timer.Once(5f, () => {
                    if (recentlyRespawned.Contains(player.userID))
                    {
                        recentlyRespawned.Remove(player.userID);
                    }
                });
            }
        }

        object OnPlayerAssist(BasePlayer player, BasePlayer target)
        {
            if (config.RemoveHostilityOnRespawn && recentlyRespawned.Contains(player.userID))
                return true;
            return null;
        }

        object OnTimeSincePvP(BasePlayer player, float oldTimer)
        {
            if (config.RemoveHostilityOnRespawn && recentlyRespawned.Contains(player.userID))
                return 0f;
            return null;
        }

        object OnPlayerEnterAggressiveTrigger(BasePlayer player, TriggerBase trigger)
        {
            if (config.RemoveHostilityOnRespawn && recentlyRespawned.Contains(player.userID))
                return true;
            return null;
        }

        object OnPlayerExitAggressiveTrigger(BasePlayer player, TriggerBase trigger)
        {
            if (config.RemoveHostilityOnRespawn && recentlyRespawned.Contains(player.userID))
                return true;
            return null;
        }

        object OnPlayerMarkHostile(BasePlayer player, float duration)
        {
            if (player == null) return null;

            if (config.RemoveHostilityOnRespawn && recentlyRespawned.Contains(player.userID))
            {
                return true;
            }
            return null;
        }

        object CanBeHostile(BasePlayer player)
        {
            if (player == null) return null;

            if (config.RemoveHostilityOnRespawn && recentlyRespawned.Contains(player.userID))
            {
                return false;
            }
            return null;
        }

        void OnPlayerDisconnected(BasePlayer player)
        {
            if (guiTimers.ContainsKey(player.userID))
            {
                guiTimers[player.userID]?.Destroy();
                guiTimers.Remove(player.userID);
            }

            if (recentlyRespawned.Contains(player.userID))
                recentlyRespawned.Remove(player.userID);

            DestroyGUI(player);
        }

        private bool HasPermission(BasePlayer player)
        {
            if (permissionCache.Contains(player.userID))
                return true;

            if (permission.UserHasPermission(player.UserIDString, PermissionUse))
            {
                permissionCache.Add(player.userID);
                return true;
            }

            return false;
        }

        private float CurrentTime() => Time.realtimeSinceStartup;
        #endregion

        #region Commands
        [ConsoleCommand("safezone.spawn")]
        private void RespawnAtSafezoneCommand(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            if (!isInitialized)
            {
                player.ChatMessage("Safezone respawn locations have not initialized yet");
                return;
            }

            if (!permission.UserHasPermission(player.UserIDString, PermissionUse))
            {
                player.ChatMessage("You don't have permission to use safezone respawning");
                return;
            }

            string location = arg.GetString(0, "outpost").ToLower();

            if (IsOnCooldown(player.userID, location))
            {
                var timeRemaining = GetCooldownTimeRemaining(player.userID, location);
                player.ChatMessage($"You must wait {timeRemaining:F0} seconds before using this spawn point again");
                return;
            }

            try
            {
                if (location == "outpost" && config.EnableOutpost)
                {
                    Quaternion spawnRotation = Quaternion.LookRotation((outpostPosition - outpostSpawnPoint).normalized);
                    player.RespawnAt(outpostSpawnPoint, spawnRotation);
                    SetPlayerSpawnConditions(player);
                    StartCooldown(player.userID, "outpost");
                }
                else if (location == "bandit" && config.EnableBandit)
                {
                    Quaternion spawnRotation = Quaternion.LookRotation((banditPosition - banditSpawnPoint).normalized);
                    player.RespawnAt(banditSpawnPoint, spawnRotation);
                    SetPlayerSpawnConditions(player);
                    StartCooldown(player.userID, "bandit");
                }
                DestroyGUI(player);
            }
            catch (System.Exception ex)
            {
                PrintError($"Error during respawn: {ex}");
                player.ChatMessage("An error occurred while trying to respawn you");
            }
        }
        #endregion

        void Unload()
        {
            foreach (var timer in guiTimers.Values)
            {
                timer?.Destroy();
            }
            guiTimers.Clear();
            recentlyRespawned.Clear();
            cacheCleanupTimer?.Destroy();
            permissionCache.Clear();
            playerCache.Clear();

            foreach (var player in BasePlayer.activePlayerList)
            {
                DestroyGUI(player);
            }
        }
    }
}
