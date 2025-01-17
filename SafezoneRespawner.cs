using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;
using System.Collections.Generic;
using Newtonsoft.Json;
using System;

namespace Oxide.Plugins
{
    [Info("Safezone Respawner", "RubMyBricks", "1.2.0")]
    [Description("Allows players to respawn at safezones upon death with cooldown!")]
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
        private Dictionary<ulong, DateTime> playerCooldowns = new Dictionary<ulong, DateTime>();
        private bool isInitialized = false;
        private ConfigData config;
        #endregion

        #region Configuration
        private class ConfigData
        {
            [JsonProperty(PropertyName = "Enable Outpost Respawn")]
            public bool EnableOutpost { get; set; } = true;

            [JsonProperty(PropertyName = "Enable Bandit Camp Respawn")]
            public bool EnableBandit { get; set; } = false;

            [JsonProperty(PropertyName = "Use ImageLibrary for Icons")]
            public bool UseImageLibrary { get; set; } = false;

            [JsonProperty(PropertyName = "Cooldown Time (minutes) // change to 0 to disable the timer :)")]
            public float CooldownMinutes { get; set; } = 2f;

            [JsonProperty(PropertyName = "Custom Images")]
            public CustomImages Images { get; set; } = new CustomImages();

            [JsonProperty(PropertyName = "GUI Settings")]
            public GUISettings GuiSettings { get; set; } = new GUISettings();
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

            if (config.UseImageLibrary)
            {
                if (ImageLibrary == null)
                {
                    PrintWarning("Imagelibrary is enabled in config but not loaded! Plugin will use default icons.");
                }
                else
                {
                    ImageLibrary.Call("AddImage", config.Images.OutpostImage, OUTPOST_IMAGE_ID);
                    ImageLibrary.Call("AddImage", config.Images.BanditImage, BANDIT_IMAGE_ID);
                }
            }
        }

        private void InitializeSpawnPoints()
        {
            try
            {
                Puts("Searching for safezones...");

                var monuments = TerrainMeta.Path.Monuments;
                Puts($"Found {monuments.Count} safezones total");

                foreach (var monument in monuments)
                {
                    string monumentName = monument.name.ToLower();
                    Puts($"Checking monument: {monumentName}");

                    if (monumentName.Contains("outpost") || monumentName.Contains("compound"))
                    {
                        Puts($"Found Outpost at position: {monument.transform.position}");
                        outpostPosition = monument.transform.position;

                        outpostSpawnPoint = outpostPosition + new Vector3(0f, 0f, -23f);
                        float groundY = TerrainMeta.HeightMap.GetHeight(outpostSpawnPoint);
                        outpostSpawnPoint = new Vector3(outpostSpawnPoint.x, groundY + 0.3f, outpostSpawnPoint.z);

                        Puts($"Set outpost spawn point at: {outpostSpawnPoint}");
                    }
                    else if (monumentName.Contains("bandit"))
                    {
                        Puts($"Found Bandit Camp at position: {monument.transform.position}");
                        banditPosition = monument.transform.position;

                        banditSpawnPoint = banditPosition + new Vector3(0f, 0f, -25f);
                        float groundY = TerrainMeta.HeightMap.GetHeight(banditSpawnPoint);
                        banditSpawnPoint = new Vector3(banditSpawnPoint.x, groundY + 0.3f, banditSpawnPoint.z);

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
        #endregion

        #region Cooldown Methods
        private bool IsOnCooldown(ulong userId)
        {
            if (!playerCooldowns.ContainsKey(userId))
                return false;

            var timeRemaining = GetCooldownTimeRemaining(userId);
            return timeRemaining > 0;
        }

        private double GetCooldownTimeRemaining(ulong userId)
        {
            if (!playerCooldowns.ContainsKey(userId))
                return 0;

            var timeSinceLast = DateTime.UtcNow - playerCooldowns[userId];
            var cooldownSeconds = config.CooldownMinutes * 60;
            return Math.Max(0, cooldownSeconds - timeSinceLast.TotalSeconds);
        }

        private void StartCooldown(ulong userId)
        {
            playerCooldowns[userId] = DateTime.UtcNow;
        }
        #endregion

        #region GUI
        private void ShowRespawnGUI(BasePlayer player)
        {
            if (!isInitialized) return;

            var elements = new CuiElementContainer();

            if (config.EnableOutpost)
            {
                elements.Add(new CuiPanel
                {
                    Image = { Color = "0 0 0 0" },
                    RectTransform = { AnchorMin = "0.45 0.15", AnchorMax = "0.55 0.22" },
                    CursorEnabled = false
                }, "Overlay", $"{GUI_PANEL_NAME}_overlay");

                elements.Add(new CuiPanel
                {
                    Image = { Color = IsOnCooldown(player.userID) ? config.GuiSettings.DisabledColor : config.GuiSettings.OutpostColor },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                    CursorEnabled = true
                }, $"{GUI_PANEL_NAME}_overlay", GUI_PANEL_NAME);

                if (config.UseImageLibrary && ImageLibrary != null)
                {
                    elements.Add(new CuiElement
                    {
                        Name = $"{GUI_PANEL_NAME}_icon",
                        Parent = GUI_PANEL_NAME,
                        Components =
                        {
                            new CuiRawImageComponent { Png = (string)ImageLibrary.Call("GetImage", OUTPOST_IMAGE_ID) },
                            new CuiRectTransformComponent { AnchorMin = "0.02 0.1", AnchorMax = "0.18 0.9" }
                        }
                    });
                }
                else
                {
                    elements.Add(new CuiElement
                    {
                        Name = $"{GUI_PANEL_NAME}_icon",
                        Parent = GUI_PANEL_NAME,
                        Components =
                        {
                            new CuiImageComponent { Sprite = "assets/icons/arrow_right.png", Color = config.GuiSettings.TextColor },
                            new CuiRectTransformComponent { AnchorMin = "0.02 0.1", AnchorMax = "0.18 0.9" }
                        }
                    });
                }

                var timeRemaining = GetCooldownTimeRemaining(player.userID);
                string buttonText = timeRemaining > 0 ? $"OUTPOST ({timeRemaining:F0}s)" : "OUTPOST »";
                string textColor = timeRemaining > 0 ? config.GuiSettings.CooldownColor : config.GuiSettings.TextColor;

                elements.Add(new CuiLabel
                {
                    Text = { Text = buttonText, FontSize = 18, Align = TextAnchor.MiddleCenter, Color = textColor },
                    RectTransform = { AnchorMin = "0.2 0", AnchorMax = "1 1" }
                }, GUI_PANEL_NAME);

                if (timeRemaining <= 0)
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
                    RectTransform = { AnchorMin = "0.56 0.15", AnchorMax = "0.66 0.22" },
                    CursorEnabled = false
                }, "Overlay", $"{BANDIT_PANEL_NAME}_overlay");

                elements.Add(new CuiPanel
                {
                    Image = { Color = IsOnCooldown(player.userID) ? config.GuiSettings.DisabledColor : config.GuiSettings.BanditColor },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                    CursorEnabled = true
                }, $"{BANDIT_PANEL_NAME}_overlay", BANDIT_PANEL_NAME);

                if (config.UseImageLibrary && ImageLibrary != null)
                {
                    elements.Add(new CuiElement
                    {
                        Name = $"{BANDIT_PANEL_NAME}_icon",
                        Parent = BANDIT_PANEL_NAME,
                        Components =
                        {
                            new CuiRawImageComponent { Png = (string)ImageLibrary.Call("GetImage", BANDIT_IMAGE_ID) },
                            new CuiRectTransformComponent { AnchorMin = "0.02 0.1", AnchorMax = "0.18 0.9" }
                        }
                    });
                }
                else
                {
                    elements.Add(new CuiElement
                    {
                        Name = $"{BANDIT_PANEL_NAME}_icon",
                        Parent = BANDIT_PANEL_NAME,
                        Components =
                        {
                            new CuiImageComponent { Sprite = "assets/icons/arrow_right.png", Color = config.GuiSettings.TextColor },
                            new CuiRectTransformComponent { AnchorMin = "0.02 0.1", AnchorMax = "0.18 0.9" }
                        }
                    });
                }

                var timeRemaining = GetCooldownTimeRemaining(player.userID);
                string buttonText = timeRemaining > 0 ? $"BANDIT ({timeRemaining:F0}s)" : "BANDIT »";
                string textColor = timeRemaining > 0 ? config.GuiSettings.CooldownColor : config.GuiSettings.TextColor;

                elements.Add(new CuiLabel
                {
                    Text = { Text = buttonText, FontSize = 18, Align = TextAnchor.MiddleCenter, Color = textColor },
                    RectTransform = { AnchorMin = "0.2 0", AnchorMax = "1 1" }
                }, BANDIT_PANEL_NAME);

                if (timeRemaining <= 0)
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

            var cooldownTime = GetCooldownTimeRemaining(player.userID);
            if (cooldownTime > 0)
            {
                timer.Once(1f, () => ShowRespawnGUI(player));
            }
        }

        private void DestroyGUI(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, $"{GUI_PANEL_NAME}_overlay");
            CuiHelper.DestroyUi(player, $"{BANDIT_PANEL_NAME}_overlay");
        }
        #endregion

        #region Hooks
        void OnPlayerDeath(BasePlayer player, HitInfo info)
        {
            if (!permission.UserHasPermission(player.UserIDString, PermissionUse))
                return;

            if (!isInitialized)
            {
                InitializeSpawnPoints();
            }

            timer.Once(4.0f, () => ShowRespawnGUI(player));
            timer.Once(4.5f, () => ShowRespawnGUI(player));
            timer.Once(5.0f, () => ShowRespawnGUI(player));
        }

        void OnPlayerRespawned(BasePlayer player)
        {
            DestroyGUI(player);
        }

        void OnPlayerDisconnected(BasePlayer player)
        {
            DestroyGUI(player);
        }
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

            if (IsOnCooldown(player.userID))
            {
                var timeRemaining = GetCooldownTimeRemaining(player.userID);
                player.ChatMessage($"You must wait {timeRemaining:F0} seconds before using safezone respawn again");
                return;
            }

            string location = arg.GetString(0, "outpost").ToLower();

            try
            {
                if (location == "outpost" && config.EnableOutpost)
                {
                    Quaternion spawnRotation = Quaternion.LookRotation((outpostPosition - outpostSpawnPoint).normalized);
                    player.RespawnAt(outpostSpawnPoint, spawnRotation);
                    StartCooldown(player.userID);
                }
                else if (location == "bandit" && config.EnableBandit)
                {
                    Quaternion spawnRotation = Quaternion.LookRotation((banditPosition - banditSpawnPoint).normalized);
                    player.RespawnAt(banditSpawnPoint, spawnRotation);
                    StartCooldown(player.userID);
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
            foreach (var player in BasePlayer.activePlayerList)
            {
                DestroyGUI(player);
            }
        }
    }
}
