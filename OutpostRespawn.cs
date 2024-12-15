///////////////////////////////////////////////////////////////////////////
//                         Made by RubMyBricks                           //
//                         Discord: rubmybricks                          //
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

namespace Oxide.Plugins
{
    [Info("OutpostRespawn", "RubMyBricks", "1.0.6")]
    [Description("Allows players to spawn at safezones upon death!")]
    public class OutpostRespawn : RustPlugin
    {
        private const string PermissionUse = "outpostrespawn.use";
        private Vector3 outpostPosition;
        private Dictionary<ulong, Timer> guiTimers = new Dictionary<ulong, Timer>();
        private const string GUI_PANEL_NAME = "OutpostRespawnGUI";
        private Vector3 spawnPoint;
        private bool isInitialized = false;

        void OnServerInitialized()
        {
            InitializeSpawnPoints();
        }

        private void InitializeSpawnPoints()
        {
            try
            {
                Puts("Searching for Outpost monument...");

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

                        spawnPoint = outpostPosition + new Vector3(0f, 0f, -30f);
                        float groundY = TerrainMeta.HeightMap.GetHeight(spawnPoint);
                        spawnPoint = new Vector3(spawnPoint.x, groundY + 0.3f, spawnPoint.z);

                        Puts($"Set spawn point at: {spawnPoint}");

                        isInitialized = true;
                        Puts("Spawn point initialized successfully");
                        return;
                    }
                }

                if (!isInitialized)
                {
                    PrintError("Failed to find outpost");
                }
            }
            catch (System.Exception ex)
            {
                PrintError($"Error initializing spawn points: {ex}");
            }
        }

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

        private void ShowRespawnGUI(BasePlayer player)
        {
            if (!isInitialized) return;

            var elements = new CuiElementContainer();

            
            elements.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0" },
                RectTransform = { AnchorMin = "0.45 0.15", AnchorMax = "0.55 0.22" }, 
                CursorEnabled = false 
            }, "Overlay", $"{GUI_PANEL_NAME}_overlay");

            elements.Add(new CuiPanel
            {
                Image = { Color = "0.42 0.55 0.24 1" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                CursorEnabled = true
            }, $"{GUI_PANEL_NAME}_overlay", GUI_PANEL_NAME);

            elements.Add(new CuiElement
            {
                Name = $"{GUI_PANEL_NAME}_icon",
                Parent = GUI_PANEL_NAME,
                Components =
        {
            new CuiImageComponent { Sprite = "assets/icons/arrow_right.png", Color = "1 1 1 1" },
            new CuiRectTransformComponent { AnchorMin = "0.02 0.1", AnchorMax = "0.18 0.9" }
        }
            });

            elements.Add(new CuiLabel
            {
                Text = { Text = "OUTPOST »", FontSize = 18, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = "0.2 0", AnchorMax = "1 1" }
            }, GUI_PANEL_NAME);

            elements.Add(new CuiButton
            {
                Button = { Command = "respawnoutpost.spawn", Color = "0 0 0 0" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                Text = { Text = "" }
            }, GUI_PANEL_NAME);

            DestroyGUI(player);
            CuiHelper.AddUi(player, elements);
        }

        void OnPlayerRespawned(BasePlayer player)
        {
            DestroyGUI(player);
        }

        void OnPlayerDisconnected(BasePlayer player)
        {
            DestroyGUI(player);
        }

        private void DestroyGUI(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, $"{GUI_PANEL_NAME}_overlay");
        }

        [ConsoleCommand("respawnoutpost.spawn")]
        private void RespawnAtOutpostCommand(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            if (!isInitialized)
            {
                player.ChatMessage("Outpost respawn has not initialized yet");
                return;
            }

            if (!permission.UserHasPermission(player.UserIDString, PermissionUse))
            {
                player.ChatMessage("You don't have permission to respawn at ooutpost");
                return;
            }

            try
            {
                Quaternion spawnRotation = Quaternion.LookRotation((outpostPosition - spawnPoint).normalized);
                player.RespawnAt(spawnPoint, spawnRotation);
                DestroyGUI(player);
            }
            catch (System.Exception ex)
            {
                PrintError($"Error during respawn: {ex}");
                player.ChatMessage("An error occurred while trying to respawn you");
            }
        }

        void Unload()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                DestroyGUI(player);
            }
        }

        protected override void LoadDefaultConfig()
        {
            PrintWarning("Creating new configuration file");
        }
    }
}
