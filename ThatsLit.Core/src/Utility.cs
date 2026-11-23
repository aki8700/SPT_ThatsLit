using System;
using System.Collections.Generic;
using System.Linq;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using UnityEngine;

namespace ThatsLit
{
    internal enum DetailKind : byte { Unknown, Ke2eb60, Kdf6e82, K27bbce, Kfa097b, Keb7931, Kadb33a, Kf83e15, Kead4fa, K40d9d4, K4ad690, Kbf0a23, Kb6cf18, Ka84c21, Kd17f80, Kd17180, Ke84f39, Ke9cd39 }
    public enum BotImpactType
    {
        BOSS,
        FOLLOWER,
        DEFAULT
    }
    public static class Utility
    {
        /// <summary>
        /// Shows an in-game warning toast. No-op on headless (batchmode) and never throws
        /// (the notification UI may not exist yet, e.g. during Awake or on dedicated hosts).
        /// </summary>
        internal static void SafeWarningNotification(string message, EFT.Communications.ENotificationDurationType duration = EFT.Communications.ENotificationDurationType.Default)
        {
            if (Application.isBatchMode) return;
            try
            {
                EFT.Communications.NotificationManager.DisplayWarningNotification(message, duration);
            }
            catch (Exception e)
            {
                Logger.LogWarning($"[That's Lit] Notification failed: {e.Message}");
            }
        }

        
        public static BotImpactType GetBotImpactType (WildSpawnType type) => type switch {
            WildSpawnType.bossBoar => BotImpactType.BOSS,
            WildSpawnType.bossBoarSniper => BotImpactType.BOSS,
            WildSpawnType.bossBully => BotImpactType.BOSS,
            WildSpawnType.bossGluhar => BotImpactType.BOSS,
            WildSpawnType.bossKilla => BotImpactType.BOSS,
            WildSpawnType.bossKnight => BotImpactType.BOSS,
            WildSpawnType.bossKojaniy => BotImpactType.BOSS,
            WildSpawnType.bossSanitar => BotImpactType.BOSS,
            WildSpawnType.bossTagilla => BotImpactType.BOSS,
            WildSpawnType.bossZryachiy => BotImpactType.BOSS,
            WildSpawnType.ravangeZryachiyEvent => BotImpactType.BOSS,
            WildSpawnType.peacefullZryachiyEvent => BotImpactType.BOSS,
            WildSpawnType.bossKolontay => BotImpactType.BOSS,
            WildSpawnType.arenaFighter => BotImpactType.FOLLOWER,
            WildSpawnType.arenaFighterEvent => BotImpactType.FOLLOWER,
            WildSpawnType.followerGluharScout => BotImpactType.FOLLOWER,
            WildSpawnType.followerGluharAssault => BotImpactType.FOLLOWER,
            WildSpawnType.followerGluharSecurity => BotImpactType.FOLLOWER,
            WildSpawnType.followerGluharSnipe => BotImpactType.FOLLOWER,
            WildSpawnType.followerBigPipe => BotImpactType.FOLLOWER,
            WildSpawnType.followerBirdEye => BotImpactType.FOLLOWER,
            WildSpawnType.followerBoar => BotImpactType.FOLLOWER,
            WildSpawnType.followerBoarClose1 => BotImpactType.FOLLOWER,
            WildSpawnType.followerBoarClose2 => BotImpactType.FOLLOWER,
            WildSpawnType.followerBully => BotImpactType.FOLLOWER,
            WildSpawnType.followerKojaniy => BotImpactType.FOLLOWER,
            WildSpawnType.followerSanitar => BotImpactType.FOLLOWER,
            WildSpawnType.followerTagilla => BotImpactType.FOLLOWER,
            WildSpawnType.followerZryachiy => BotImpactType.FOLLOWER,
            WildSpawnType.followerKolontayAssault => BotImpactType.FOLLOWER,
            WildSpawnType.followerKolontaySecurity => BotImpactType.FOLLOWER,
            WildSpawnType.sectantPriest => BotImpactType.BOSS,
            WildSpawnType.sectantWarrior => BotImpactType.FOLLOWER,
            WildSpawnType.sectactPriestEvent => BotImpactType.BOSS,
            WildSpawnType.gifter => BotImpactType.BOSS,
            WildSpawnType.pmcBot => BotImpactType.FOLLOWER,
            WildSpawnType.exUsec => BotImpactType.FOLLOWER,
            WildSpawnType.crazyAssaultEvent => BotImpactType.FOLLOWER,
            WildSpawnType.marksman => BotImpactType.FOLLOWER, // scav_sniper
            _ => (int)type switch {
                199 => BotImpactType.BOSS,
                _ => BotImpactType.DEFAULT
            }
        };
        public static bool IsExcludedSpawnType (WildSpawnType type) => type switch {
            // WildSpawnType.bossTagilla => true,
            // WildSpawnType.followerTagilla => true,
            _ => false
        };

        internal static float GetInGameDayTime()
        {
            if (Singleton<GameWorld>.Instance?.GameDateTime == null) return 19f;

            var GameDateTime = Singleton<GameWorld>.Instance.GameDateTime.Calculate();

            float minutes = GameDateTime.Minute / 59f;
            return GameDateTime.Hour + minutes;
        }

        internal static float GetNightProgress()
        {
            var time = GetInGameDayTime();
            if (time < 12) time += 4; // 20 => 0, 24 => 4, 0 => 4, 6 => 10 
            return Mathf.InverseLerp(0f, 10f, time);
        }
        internal static float GetDayProgress()
        {
            var time = GetInGameDayTime();
            return Mathf.InverseLerp(6f, 20f, time);
        }

        static string lastLogged;
        internal static DetailKind ClassifyDetailName(string name)
        {
            if (name == null) return DetailKind.Unknown;
            if (name.EndsWith("e2eb60", System.StringComparison.Ordinal)) return DetailKind.Ke2eb60;
            if (name.EndsWith("df6e82", System.StringComparison.Ordinal) || name.EndsWith("7c58e7", System.StringComparison.Ordinal) || name.EndsWith("994963", System.StringComparison.Ordinal)) return DetailKind.Kdf6e82;
            if (name.EndsWith("27bbce", System.StringComparison.Ordinal)) return DetailKind.K27bbce;
            if (name.EndsWith("fa097b", System.StringComparison.Ordinal) || name.EndsWith("2adee9", System.StringComparison.Ordinal)) return DetailKind.Kfa097b;
            if (name.EndsWith("eb7931", System.StringComparison.Ordinal)) return DetailKind.Keb7931;
            if (name.EndsWith("adb33a", System.StringComparison.Ordinal)) return DetailKind.Kadb33a;
            if (name.EndsWith("f83e15", System.StringComparison.Ordinal)) return DetailKind.Kf83e15;
            if (name.EndsWith("ead4fa", System.StringComparison.Ordinal)) return DetailKind.Kead4fa;
            if (name.EndsWith("40d9d4", System.StringComparison.Ordinal)) return DetailKind.K40d9d4;
            if (name.EndsWith("4ad690", System.StringComparison.Ordinal)) return DetailKind.K4ad690;
            if (name.EndsWith("bf0a23", System.StringComparison.Ordinal)) return DetailKind.Kbf0a23;
            if (name.EndsWith("b6cf18", System.StringComparison.Ordinal)) return DetailKind.Kb6cf18;
            if (name.EndsWith("a84c21", System.StringComparison.Ordinal)) return DetailKind.Ka84c21;
            if (name.EndsWith("d17f80", System.StringComparison.Ordinal)) return DetailKind.Kd17f80;
            if (name.EndsWith("d17180", System.StringComparison.Ordinal)) return DetailKind.Kd17180;
            if (name.EndsWith("e84f39", System.StringComparison.Ordinal)) return DetailKind.Ke84f39;
            if (name.EndsWith("e9cd39", System.StringComparison.Ordinal)) return DetailKind.Ke9cd39;
            return DetailKind.Unknown;
        }
        internal static void CalculateDetailScore(string name, int num, out float prone, out float crouch)
        {
            var kind = ClassifyDetailName(name);
            CalculateDetailScore(kind, num, out prone, out crouch);
            LogUnknownDetail(kind, name, num);
        }
        internal static void LogUnknownDetail(DetailKind kind, string name, int num)
        {
            if (kind != DetailKind.Unknown || name == null || num == 0 || !ThatsLitPlugin.DebugInfo.Value) return;
            if (Time.frameCount % 47 == 0 && name != lastLogged)
            {
                string message = string.Format("That's Lit: Missing terrain detail: {0}", name);
                SafeWarningNotification(message);
                Logger.LogWarning(message);
                lastLogged = name;
            }
        }
        internal static void CalculateDetailScore(DetailKind kind, int num, out float prone, out float crouch)
        {
            prone = crouch = 0f;
            if (num == 0) return;
            switch (kind)
            {
                case DetailKind.Ke2eb60:
                {
                    prone = 0.05f * Mathf.Pow(Mathf.Clamp01(num / 20f), 2) * Mathf.Min(num, 50); // Needs cluster
                    crouch = 0.003f * Mathf.Min(num, 10);
                    break;
                }
                case DetailKind.Kdf6e82:
                {
                    prone = 0.05f * Mathf.Pow(Mathf.Clamp01(num / 20f), 2) * Mathf.Min(num, 50); // Needs cluster
                    crouch = 0.0035f * Mathf.Min(num, 5);
                    break;
                }
                case DetailKind.K27bbce:
                {
                    prone = 0.008f * Mathf.Min(num, 25);
                    crouch = 0;
                    break;
                }
                case DetailKind.Kfa097b:
                {
                    prone = 0.06f * num; 
                    crouch = 0.009f * Mathf.Min(num, 15);
                    break;
                }
                case DetailKind.Keb7931:
                {
                    prone = 0.06f * num; 
                    crouch = 0.01f * num;
                    break;
                }
                case DetailKind.Kadb33a:
                {
                    prone = 0.02f * num;
                    crouch = 0.02f * num;
                    break;
                }
                case DetailKind.Kf83e15:
                {
                    prone = 0.04f * num;
                    crouch = 0.018f * num;
                    break;
                }
                case DetailKind.Kead4fa:
                {
                    prone = 0.06f * num;
                    crouch = 0.008f * num;
                    break;
                }
                case DetailKind.K40d9d4:
                {
                    prone = 0.007f * num;
                    crouch = 0.009f * num;
                    break;
                }
                case DetailKind.K4ad690:
                {
                    prone = 0.015f * num;
                    crouch = 0.015f * num;
                    break;
                }
                case DetailKind.Kbf0a23:
                {
                    prone = 0.007f * num;
                    crouch = 0.007f * num;
                    break;
                }
                case DetailKind.Kb6cf18:
                {
                    prone = 0.01f * num;
                    crouch = 0.01f * num;
                    break;
                }
                case DetailKind.Ka84c21:
                {
                    prone = 0.007f * num;
                    crouch = 0.006f * num;
                    break;
                }
                case DetailKind.Kd17f80:
                {
                    prone = 0.008f * Mathf.Min(num, 25);
                    crouch = 0;
                    break;
                }
                case DetailKind.Kd17180:
                {
                    break;
                }
                case DetailKind.Ke84f39:
                {
                    break;
                }
                case DetailKind.Ke9cd39:
                {
                    break;
                }
            }
        }

        static ThatsLitCompat.DeviceMode CheckDevicesOnItem(Item item)
        {
            ThatsLitCompat.DeviceMode result = default;
            if (item == null)
                return result;

            // Zero-iterator equivalent of `foreach (var it in item.GetAllItems())` (EFT.InventoryLogic.ItemExtensions):
            // self first, then for each IContainer of ContainerCollection.Containers (in order) each contained item, recursively.
            CollectDevices(item, ref result);
            return result;
        }

        /// <summary>
        /// Body of the former GetAllItems() loop, for a single item. ExtraDevices take precedence over the item's LightComponent;
        /// children are visited regardless (GetAllItems flattens the tree).
        /// </summary>
        static void AccumulateDevice(Item it, ref ThatsLitCompat.DeviceMode result)
        {
            if (string.IsNullOrWhiteSpace(it?.StringTemplateId)) return;
            if (ThatsLitCompat.ExtraDevices.TryGetValue(it.StringTemplateId, out var extraDevice)
             && extraDevice?.TemplateInstance != null)
            {
                if (it is EFT.InventoryLogic.SightMod sightMod)
                {
                    result = ThatsLitCompat.DeviceMode.MergeMax(result, extraDevice.TemplateInstance.SafeGetMode(sightMod.Sight?.SelectedScopeMode ?? 0));
                }
                else
                    result = ThatsLitCompat.DeviceMode.MergeMax(result, extraDevice.TemplateInstance.SafeGetMode(0));
                return;
            }

            LightComponent light = it.GetItemComponent<LightComponent>();
            if (light == null || !light.IsActive) return;
            var mode = GetDeviceMode(it.StringTemplateId, light.SelectedMode);
            result = ThatsLitCompat.DeviceMode.MergeMax(result, mode);
        }

        /// <summary>
        /// How a ContainerCollection type enumerates its Containers (decided by which class declares the Containers override,
        /// verified against Assembly-CSharp: CompoundItem = Slots then Grids; Weapon = Slots, Grids, Chambers;
        /// Magazine / AmmoBox = Cartridges). Any other override (Launcher, CylinderMagazine, SortingTable, unknown) uses the
        /// generic path, which enumerates Containers/Items exactly like GetAllItems (allocating, but exact).
        /// </summary>
        enum ContainerLayout : byte { Generic, Compound, Weapon, Magazine, AmmoBox }
        static readonly Dictionary<Type, ContainerLayout> containerLayouts = new Dictionary<Type, ContainerLayout>();

        static ContainerLayout GetContainerLayout(Type type)
        {
            if (containerLayouts.TryGetValue(type, out var layout))
                return layout;
            layout = ContainerLayout.Generic;
            try
            {
                Type declaring = type.GetMethod("get_Containers", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)?.DeclaringType;
                if (declaring == typeof(CompoundItem)) layout = ContainerLayout.Compound;
                else if (declaring == typeof(Weapon)) layout = ContainerLayout.Weapon;
                else if (declaring == typeof(Magazine)) layout = ContainerLayout.Magazine;
                else if (declaring == typeof(AmmoBox)) layout = ContainerLayout.AmmoBox;
            }
            catch (Exception)
            {
                layout = ContainerLayout.Generic;
            }
            containerLayouts[type] = layout;
            return layout;
        }

        static void CollectDevices(Item it, ref ThatsLitCompat.DeviceMode result)
        {
            if (it == null) return;
            AccumulateDevice(it, ref result);
            if (!(it is ContainerCollection collection)) return;

            switch (GetContainerLayout(collection.GetType()))
            {
                case ContainerLayout.Compound:
                {
                    var compound = (CompoundItem)collection;
                    CollectDevicesInSlots(compound.Slots, ref result);
                    CollectDevicesInGrids(compound.Grids, ref result);
                    break;
                }
                case ContainerLayout.Weapon:
                {
                    var weapon = (Weapon)collection;
                    CollectDevicesInSlots(weapon.Slots, ref result);
                    CollectDevicesInGrids(weapon.Grids, ref result);
                    CollectDevicesInSlots(weapon.Chambers, ref result);
                    break;
                }
                case ContainerLayout.Magazine:
                    CollectDevicesInStackSlot(((Magazine)collection).Cartridges, ref result);
                    break;
                case ContainerLayout.AmmoBox:
                    CollectDevicesInStackSlot(((AmmoBox)collection).Cartridges, ref result);
                    break;
                default:
                    foreach (EFT.InventoryLogic.IContainer container in collection.Containers)
                        foreach (Item child in container.Items)
                            CollectDevices(child, ref result);
                    break;
            }
        }

        static void CollectDevicesInSlots(EFT.InventoryLogic.Slot[] slots, ref ThatsLitCompat.DeviceMode result)
        {
            if (slots == null) return;
            for (int i = 0; i < slots.Length; i++)
            {
                Item contained = slots[i]?.ContainedItem; // Slot.Items yields ContainedItem only when non-null
                if (contained != null) CollectDevices(contained, ref result);
            }
        }

        static void CollectDevicesInGrids(EFT.InventoryLogic.Grid[] grids, ref ThatsLitCompat.DeviceMode result)
        {
            if (grids == null) return;
            for (int i = 0; i < grids.Length; i++)
            {
                var collection = grids[i]?.ItemCollection;
                if (collection == null) continue;
                // Grid.Items == ItemCollection.Keys; struct enumerator, same order
                foreach (Item child in collection.Keys)
                    CollectDevices(child, ref result);
            }
        }

        static void CollectDevicesInStackSlot(EFT.InventoryLogic.StackSlot stackSlot, ref ThatsLitCompat.DeviceMode result)
        {
            if (stackSlot == null) return;
            if (stackSlot.Items is List<Item> list)
            {
                for (int i = 0; i < list.Count; i++)
                    CollectDevices(list[i], ref result);
            }
            else
            {
                foreach (Item child in stackSlot.Items)
                    CollectDevices(child, ref result);
            }
        }

        internal static (ThatsLitCompat.DeviceMode mode, ThatsLitCompat.DeviceMode modeSub) DetermineShiningEquipments(Player player)
        {
            ThatsLitCompat.DeviceMode mode, modeSub;
            Weapon activeWeapon = player?.ActiveSlot?.ContainedItem as Weapon;
            mode = CheckDevicesOnItem(activeWeapon);
            modeSub = default;
         
            InventoryEquipment equipment = player.Inventory?.Equipment;
            if (equipment == null)
                return (mode, modeSub);

            mode = ThatsLitCompat.DeviceMode.MergeMax(mode, CheckDevicesOnItem(equipment.GetSlot(EquipmentSlot.Headwear)?.ContainedItem));

            // If not ActiveWeapon (which is already checked on top)
            if (player?.ActiveSlot != equipment.GetSlot(EquipmentSlot.FirstPrimaryWeapon))
            {
                modeSub = ThatsLitCompat.DeviceMode.MergeMax(modeSub, CheckDevicesOnItem(equipment.GetSlot(EquipmentSlot.FirstPrimaryWeapon)?.ContainedItem));
            }


            if (player?.ActiveSlot != equipment.GetSlot(EquipmentSlot.SecondPrimaryWeapon))
            {
                modeSub = ThatsLitCompat.DeviceMode.MergeMax(modeSub, CheckDevicesOnItem(equipment.GetSlot(EquipmentSlot.SecondPrimaryWeapon)?.ContainedItem));
            }


            if (player?.ActiveSlot != equipment.GetSlot(EquipmentSlot.Holster))
            {
                modeSub = ThatsLitCompat.DeviceMode.MergeMax(modeSub, CheckDevicesOnItem(equipment.GetSlot(EquipmentSlot.Holster)?.ContainedItem));
            }

            return (mode, modeSub);
        }
        static ThatsLitCompat.DeviceMode GetDeviceMode(string itemTemplateId, int selectedMode)
        {
            ThatsLitCompat.Devices.TryGetValue(itemTemplateId, out var compat);
            if (compat == null) return default;

            if (compat.TemplateInstance?.modes == null || compat.TemplateInstance.modes.Length <= selectedMode)
            {
                if (ThatsLitPlayer.IsDebugSampleFrame)
                {
                    string message = $"[That's Lit] Unknown device or mode: {itemTemplateId} {Singleton<ItemFactory>.Instance?.GetPresetItem(itemTemplateId)?.Name} mode {selectedMode}";
                    Utility.SafeWarningNotification(message);
                    Logger.LogWarning(message);
                    EFT.UI.ConsoleScreen.Log(message);
                }
                return default;
            }
            return compat.TemplateInstance.modes[selectedMode];
        }

        public static void GUILayoutDrawAsymetricMeter(int level, bool alternative = false, GUIStyle style = null)
        {
            if (style == null) style = GUI.skin.label;
            if (alternative)
            {
                if (level < -10)
                {
                    GUILayout.Label("  ##########|----------", style);
                    return;
                }
                if (level > 10)
                {
                    GUILayout.Label("  ----------|##########", style);
                    return;
                }
                switch (level)
                {
                    case -11:
                        GUILayout.Label("  ##########|----------", style);
                        break;
                    case -10:
                        GUILayout.Label("  ##########|----------", style);
                        break;
                    case -9:
                        GUILayout.Label("  -#########|----------", style);
                        break;
                    case -8:
                        GUILayout.Label("  --########|----------", style);
                        break;
                    case -7:
                        GUILayout.Label("  ---#######|----------", style);
                        break;
                    case -6:
                        GUILayout.Label("  ----######|----------", style);
                        break;
                    case -5:
                        GUILayout.Label("  -----#####|----------", style);
                        break;
                    case -4:
                        GUILayout.Label("  ------####|----------", style);
                        break;
                    case -3:
                        GUILayout.Label("  -------###|----------", style);
                        break;
                    case -2:
                        GUILayout.Label("  --------##|----------", style);
                        break;
                    case -1:
                        GUILayout.Label("  ---------#|----------", style);
                        break;
                    case 0:
                        GUILayout.Label("  ----------|----------", style);
                        break;
                    case 1:
                        GUILayout.Label("  ----------|#---------", style);
                        break;
                    case 2:
                        GUILayout.Label("  ----------|##--------", style);
                        break;
                    case 3:
                        GUILayout.Label("  ----------|###-------", style);
                        break;
                    case 4:
                        GUILayout.Label("  ----------|####------", style);
                        break;
                    case 5:
                        GUILayout.Label("  ----------|#####-----", style);
                        break;
                    case 6:
                        GUILayout.Label("  ----------|######----", style);
                        break;
                    case 7:
                        GUILayout.Label("  ----------|#######---", style);
                        break;
                    case 8:
                        GUILayout.Label("  ----------|########--", style);
                        break;
                    case 9:
                        GUILayout.Label("  ----------|#########-", style);
                        break;
                    case 10:
                        GUILayout.Label("  ----------|##########", style);
                        break;
                    case 11:
                        GUILayout.Label("  ----------|##########", style);
                        break;
                }
                return;
            }

            if (level < -10)
            {
                GUILayout.Label("  ▰▰▰▰▰▰▰▰▰▰ ▱▱▱▱▱▱▱▱▱▱", style);
                return;
            }
            if (level > 10)
            {
                GUILayout.Label("  ▱▱▱▱▱▱▱▱▱▱  ▰▰▰▰▰▰▰▰▰▰", style);
                return;
            }
            switch (level)
            {
                case -11:
                    GUILayout.Label("  ▰▰▰▰▰▰▰▰▰▰  ▱▱▱▱▱▱▱▱▱▱", style);
                    break;
                case -10:
                    GUILayout.Label("  ▰▰▰▰▰▰▰▰▰▰  ▱▱▱▱▱▱▱▱▱▱", style);
                    break;
                case -9:
                    GUILayout.Label("  ▱▰▰▰▰▰▰▰▰▰  ▱▱▱▱▱▱▱▱▱▱", style);
                    break;
                case -8:
                    GUILayout.Label("  ▱▱▰▰▰▰▰▰▰▰  ▱▱▱▱▱▱▱▱▱▱", style);
                    break;
                case -7:
                    GUILayout.Label("  ▱▱▱▰▰▰▰▰▰▰  ▱▱▱▱▱▱▱▱▱▱", style);
                    break;
                case -6:
                    GUILayout.Label("  ▱▱▱▱▰▰▰▰▰▰  ▱▱▱▱▱▱▱▱▱▱", style);
                    break;
                case -5:
                    GUILayout.Label("  ▱▱▱▱▱▰▰▰▰▰  ▱▱▱▱▱▱▱▱▱▱", style);
                    break;
                case -4:
                    GUILayout.Label("  ▱▱▱▱▱▱▰▰▰▰  ▱▱▱▱▱▱▱▱▱▱", style);
                    break;
                case -3:
                    GUILayout.Label("  ▱▱▱▱▱▱▱▰▰▰  ▱▱▱▱▱▱▱▱▱▱", style);
                    break;
                case -2:
                    GUILayout.Label("  ▱▱▱▱▱▱▱▱▰▰  ▱▱▱▱▱▱▱▱▱▱", style);
                    break;
                case -1:
                    GUILayout.Label("  ▱▱▱▱▱▱▱▱▱▰  ▱▱▱▱▱▱▱▱▱▱", style);
                    break;
                case 0:
                    GUILayout.Label("  ▱▱▱▱▱▱▱▱▱▱  ▱▱▱▱▱▱▱▱▱▱", style);
                    break;
                case 1:
                    GUILayout.Label("  ▱▱▱▱▱▱▱▱▱▱  ▰▱▱▱▱▱▱▱▱▱", style);
                    break;
                case 2:
                    GUILayout.Label("  ▱▱▱▱▱▱▱▱▱▱  ▰▰▱▱▱▱▱▱▱▱", style);
                    break;
                case 3:
                    GUILayout.Label("  ▱▱▱▱▱▱▱▱▱▱  ▰▰▰▱▱▱▱▱▱▱", style);
                    break;
                case 4:
                    GUILayout.Label("  ▱▱▱▱▱▱▱▱▱▱  ▰▰▰▰▱▱▱▱▱▱", style);
                    break;
                case 5:
                    GUILayout.Label("  ▱▱▱▱▱▱▱▱▱▱  ▰▰▰▰▰▱▱▱▱▱", style);
                    break;
                case 6:
                    GUILayout.Label("  ▱▱▱▱▱▱▱▱▱▱  ▰▰▰▰▰▰▱▱▱▱", style);
                    break;
                case 7:
                    GUILayout.Label("  ▱▱▱▱▱▱▱▱▱▱  ▰▰▰▰▰▰▰▱▱▱", style);
                    break;
                case 8:
                    GUILayout.Label("  ▱▱▱▱▱▱▱▱▱▱  ▰▰▰▰▰▰▰▰▱▱", style);
                    break;
                case 9:
                    GUILayout.Label("  ▱▱▱▱▱▱▱▱▱▱  ▰▰▰▰▰▰▰▰▰▱", style);
                    break;
                case 10:
                    GUILayout.Label("  ▱▱▱▱▱▱▱▱▱▱  ▰▰▰▰▰▰▰▰▰▰", style);
                    break;
                case 11:
                    GUILayout.Label("  ▱▱▱▱▱▱▱▱▱▱  ▰▰▰▰▰▰▰▰▰▰", style);
                    break;
            }
        }

        internal static void RightAlignedGUILabel (string str)
        {
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label(str);
            }
        }

        internal static void GUILayoutFoliageMeter(int level, bool alternative = false, GUIStyle style= null)
        {
            if (style == null) style = GUI.skin.label;
            if (alternative)
            {
                if (level <= 0)
                {
                    GUILayout.Label("  FOLIAGE  ----------|", style);
                    return;
                }
                if (level >= 10)
                {
                    GUILayout.Label("  FOLIAGE  ##########|", style);
                    return;
                }
                switch (level)
                {
                    case 1:
                        GUILayout.Label("  FOLIAGE  #---------|", style);
                        break;
                    case 2:
                        GUILayout.Label("  FOLIAGE  ##--------|", style);
                        break;
                    case 3:
                        GUILayout.Label("  FOLIAGE  ###-------|", style);
                        break;
                    case 4:
                        GUILayout.Label("  FOLIAGE  ####------|", style);
                        break;
                    case 5:
                        GUILayout.Label("  FOLIAGE  #####-----|", style);
                        break;
                    case 6:
                        GUILayout.Label("  FOLIAGE  ######----|", style);
                        break;
                    case 7:
                        GUILayout.Label("  FOLIAGE  #######---|", style);
                        break;
                    case 8:
                        GUILayout.Label("  FOLIAGE  ########--|", style);
                        break;
                    case 9:
                        GUILayout.Label("  FOLIAGE  #########-|", style);
                        break;
                }
                return;
            }

            if (level <= 0)
            {
                GUILayout.Label("  FOLIAGE  ▱▱▱▱▱▱▱▱▱▱", style);
                return;
            }
            if (level >= 10)
            {
                GUILayout.Label("  FOLIAGE  ▰▰▰▰▰▰▰▰▰▰", style);
                return;
            }
            switch (level)
            {
                case 1:
                    GUILayout.Label("  FOLIAGE  ▰▱▱▱▱▱▱▱▱▱", style);
                    break;
                case 2:
                    GUILayout.Label("  FOLIAGE  ▰▰▱▱▱▱▱▱▱▱", style);
                    break;
                case 3:
                    GUILayout.Label("  FOLIAGE  ▰▰▰▱▱▱▱▱▱▱", style);
                    break;
                case 4:
                    GUILayout.Label("  FOLIAGE  ▰▰▰▰▱▱▱▱▱▱", style);
                    break;
                case 5:
                    GUILayout.Label("  FOLIAGE  ▰▰▰▰▰▱▱▱▱▱", style);
                    break;
                case 6:
                    GUILayout.Label("  FOLIAGE  ▰▰▰▰▰▰▱▱▱▱", style);
                    break;
                case 7:
                    GUILayout.Label("  FOLIAGE  ▰▰▰▰▰▰▰▱▱▱", style);
                    break;
                case 8:
                    GUILayout.Label("  FOLIAGE  ▰▰▰▰▰▰▰▰▱▱", style);
                    break;
                case 9:
                    GUILayout.Label("  FOLIAGE  ▰▰▰▰▰▰▰▰▰▱", style);
                    break;
            }
        }

        internal static void GUILayoutTerrainMeter(int level, bool alternative = false, GUIStyle style = null)
        {
            if (style == null) style = GUI.skin.label;
            if (alternative)
            {
                if (level <= 0)
                {
                    GUILayout.Label("  TERRAIN  ----------|", style);
                    return;
                }
                if (level >= 10)
                {
                    GUILayout.Label("  TERRAIN  ##########|", style);
                    return;
                }
                switch (level)
                {
                    case 1:
                        GUILayout.Label("  TERRAIN  #---------|", style);
                        break;
                    case 2:
                        GUILayout.Label("  TERRAIN  ##--------|", style);
                        break;
                    case 3:
                        GUILayout.Label("  TERRAIN  ###-------|", style);
                        break;
                    case 4:
                        GUILayout.Label("  TERRAIN  ####------|", style);
                        break;
                    case 5:
                        GUILayout.Label("  TERRAIN  #####-----|", style);
                        break;
                    case 6:
                        GUILayout.Label("  TERRAIN  ######----|", style);
                        break;
                    case 7:
                        GUILayout.Label("  TERRAIN  #######---|", style);
                        break;
                    case 8:
                        GUILayout.Label("  TERRAIN  ########--|", style);
                        break;
                    case 9:
                        GUILayout.Label("  TERRAIN  #########-|", style);
                        break;
                }
                return;
            }

            if (level <= 0)
            {
                GUILayout.Label("  TERRAIN  ▱▱▱▱▱▱▱▱▱▱", style);
                return;
            }
            if (level >= 10)
            {
                GUILayout.Label("  TERRAIN  ▰▰▰▰▰▰▰▰▰▰", style);
                return;
            }
            switch (level)
            {
                case 1:
                    GUILayout.Label("  TERRAIN  ▰▱▱▱▱▱▱▱▱▱", style);
                    break;
                case 2:
                    GUILayout.Label("  TERRAIN  ▰▰▱▱▱▱▱▱▱▱", style);
                    break;
                case 3:
                    GUILayout.Label("  TERRAIN  ▰▰▰▱▱▱▱▱▱▱", style);
                    break;
                case 4:
                    GUILayout.Label("  TERRAIN  ▰▰▰▰▱▱▱▱▱▱", style);
                    break;
                case 5:
                    GUILayout.Label("  TERRAIN  ▰▰▰▰▰▱▱▱▱▱", style);
                    break;
                case 6:
                    GUILayout.Label("  TERRAIN  ▰▰▰▰▰▰▱▱▱▱", style);
                    break;
                case 7:
                    GUILayout.Label("  TERRAIN  ▰▰▰▰▰▰▰▱▱▱", style);
                    break;
                case 8:
                    GUILayout.Label("  TERRAIN  ▰▰▰▰▰▰▰▰▱▱", style);
                    break;
                case 9:
                    GUILayout.Label("  TERRAIN  ▰▰▰▰▰▰▰▰▰▱", style);
                    break;
            }
        }

        public static string DetermineDir (Vector3 dir)
        {
            var dirFlat = (new Vector2 (dir.x, dir.z)).normalized;
            var angle = Vector2.SignedAngle(Vector2.up, dirFlat);
            if (angle >= -22.5f && angle <= 22.5f)
            {
                return "N";
            }
            else if (angle >= 22.5f && angle <= 67.5f)
            {
                return "NE";
            }
            else if (angle >= 67.5f && angle <= 112.5f)
            {
                return "E";
            }
            else if (angle >= 112.5f && angle <= 157.5f)
            {
                return "SE";
            }
            else if (angle >= 157.5f && angle <= 180f || angle >= -180f && angle <= -157.5f)
            {
                return "S";
            }
            else if (angle >= -157.5f && angle <= -112.5f)
            {
                return "SW";
            }
            else if (angle >= -112.5f && angle <= -67.5f)
            {
                return "W";
            }
            else if (angle >= -67.5f && angle <= -22.5f)
            {
                return "NW";
            }
            else return "?";
        }
        internal static T ExpensiveCopyComponent<T>(T original, GameObject destination, System.Reflection.BindingFlags bindingFlags) where T : Component
        {
            System.Type type = original.GetType();
            Component copy = destination.AddComponent(type);
            System.Reflection.FieldInfo[] fields = type.GetFields(bindingFlags);
            foreach (System.Reflection.FieldInfo field in fields)
            {
                field.SetValue(copy, field.GetValue(original));
            }
            return copy as T;
        }

        internal static float GetPoseFactor(float currentPoseLevel, float maxPoseLevel, bool isInPronePose)
        {
            var pPoseFactor = currentPoseLevel / maxPoseLevel * 0.6f + 0.4f; // crouch: 0.4f
            if (isInPronePose) pPoseFactor -= 0.4f; // prone: 0
            pPoseFactor += 0.05f; // base -> prone -> 0.05f, crouch -> 0.45f (Prevent devide by zero)
            pPoseFactor = Mathf.Clamp01(pPoseFactor);
            return pPoseFactor;
        }

        internal static float GetPoseWeightedRegularTerrainScore(float pPoseFactor, TerrainDetailScore terrainScore)
        {
            return terrainScore.regular / (1f + 0.35f * Mathf.InverseLerp(0.45f, 1f, pPoseFactor));
        }

        internal static float GetObservedTerrainDetailScoreProne (Player player, Vector3 observerEyePos, Vector3 observerLookDir, float base3x3TerrainScore)
        {
            // Calculating cutoff from lying on slopes
            var playerSurfaceNormal = player.MovementContext.SurfaceNormal;
            System.Collections.Generic.Dictionary<BodyPartType, EnemyPart> playerParts = player.MainParts;
            Vector3 playerLegPos = (playerParts[BodyPartType.leftLeg].Position + playerParts[BodyPartType.rightLeg].Position) / 2f;
            var playerLegToHead = playerParts[BodyPartType.head].Position - playerLegPos;
            var playerLegToBotEye = observerEyePos - playerLegPos;
            var playerDirInView = Vector3.ProjectOnPlane(playerLegToHead, -playerLegToBotEye);
            // projected length = 1.0x original => perpendicular (most visible)
            var sizeInViewFactor = playerDirInView.magnitude / playerLegToHead.magnitude;
            // BUT this means players lying flat which should get covers could be considered very visible (lying horizontally in view)
            
            // bot -> ↖ player (opposite side of ground, must ignore) #1
            // bot -> ↘ player (opposite side of ground, must ignore) #2
            // bot -> ↗ player
            // bot -> ↙ player
            // <-player->
            var facingAngle = Vector3.Angle(playerLegToHead, playerLegToBotEye);
            if (facingAngle > 90f) sizeInViewFactor *= Mathf.InverseLerp(180f, 90f, facingAngle);
            else sizeInViewFactor *= Mathf.InverseLerp(0, 90f, facingAngle);

            var surfaceNormalAngle = Vector3.Angle(playerSurfaceNormal, playerLegToBotEye);
            sizeInViewFactor *= Mathf.InverseLerp(0f, 90f, surfaceNormalAngle);

            return base3x3TerrainScore * sizeInViewFactor;
        }

        internal static bool IsPMCSpawnType (WildSpawnType? spawnType)
        {
            return spawnType != null && spawnType == WildSpawnType.pmcBEAR || spawnType == WildSpawnType.pmcUSEC;
        }
    }
}