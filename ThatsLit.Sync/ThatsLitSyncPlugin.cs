using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using EFT.Communications;
using EFT.UI;
using Fika.Core.Main.GameMode;
using Fika.Core.Main.Players;
using Fika.Core.Main.Utils;
using Fika.Core.Modding;
using Fika.Core.Modding.Events;
using Fika.Core.Networking;
using Fika.Core.Networking.LiteNetLib;
using UnityEngine;
using static ThatsLit.Sync.AssemblyInfo;

namespace ThatsLit.Sync
{
    public static class AssemblyInfo
    {
        public const string Title = ModName;
        public const string Description = "The plugin syncs That's Lit player states between players.";
        public const string Configuration = SPTVersion;
        public const string Company = "";
        public const string Product = ModName;
        public const string Copyright = "Copyright © 2024 BA";
        public const string Trademark = "";
        public const string Culture = "";

        public const int TarkovVersion = 40743;
        public const string EscapeFromTarkov = "EscapeFromTarkov.exe";
        public const string ModName = "That's Lit Sync";
        public const string ModVersion = "1.4100.0";
        public const string SPTGUID = "com.SPT.core";
        public const string SPTVersion = "4.1.0";
    }

    [BepInPlugin("bastudio.thatslit.sync", ModName, ModVersion)]
    [BepInDependency(SPTGUID, SPTVersion)]
    [BepInDependency("bastudio.thatslit", "1.4100.0")]
    [BepInDependency("com.fika.core", "2.4.0")]
    [BepInDependency("com.fika.headless", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInProcess(EscapeFromTarkov)]
    [DefaultExecutionOrder(100)]
    public class ThatsLitSyncPlugin : BaseUnityPlugin
    {
        public static Dictionary<int, Player> ActivePlayers = new Dictionary<int, Player>();
        public static ConfigEntry<bool> ShowInfo;
        public static ConfigEntry<bool> DebugLog;
        public static ConfigEntry<bool> LogPackets;

        private IFikaNetworkManager networkManager;
        private bool raidEnded;
        private float lastScore;
        private float lastAmbienceScore;
        private float lastLitScoreFactor;
        private float lastBaseAmbienceScore;
        private float lastSent = float.NegativeInfinity;

        // ===== Terrain (grass) score sync =====
        // Minimum interval between terrain uploads (the local sample itself runs every ~0.41s while moving).
        private const float TerrainMinInterval = 0.4f;
        // Re-send unchanged values this often, well inside the host's 3.5s staleness window (ThatsLitGameworld.TerrainProxyMaxAge),
        // so one or two lost Unreliable packets never drop a stationary (prone-in-grass) player's concealment to zero.
        private const float TerrainHeartbeat = 1.0f;
        private float lastTerrainSent = float.NegativeInfinity;
        // Compile-time guard: fails to build (division by zero) if Core's bucket count and the packet layout ever diverge.
        private const int TerrainBucketCountGuard = 1 / (ThatsLitAPI.TerrainBucketCount == TerrainScorePacket.BucketCount ? 1 : 0);
        private bool hasLastTerrain;
        private readonly ushort[] lastTerrainProne = new ushort[TerrainScorePacket.BucketCount];
        private readonly ushort[] lastTerrainRegular = new ushort[TerrainScorePacket.BucketCount];
        private readonly ushort[] sendTerrainProne = new ushort[TerrainScorePacket.BucketCount];
        private readonly ushort[] sendTerrainRegular = new ushort[TerrainScorePacket.BucketCount];
        private readonly float[] terrainScratchProne = new float[TerrainScorePacket.BucketCount];
        private readonly float[] terrainScratchRegular = new float[TerrainScorePacket.BucketCount];
        private readonly List<int> stalePlayerIds = new List<int>();
        private readonly List<ThatsLitPlayer> staleGuiPlayers = new List<ThatsLitPlayer>();
        private readonly HashSet<ThatsLitPlayer> seenGuiPlayers = new HashSet<ThatsLitPlayer>();
        private readonly Dictionary<ThatsLitPlayer, CachedPlayerGuiLabel> guiLabelCache = new Dictionary<ThatsLitPlayer, CachedPlayerGuiLabel>();
        private float nextActivePlayersPruneTime;

        private sealed class CachedPlayerGuiLabel
        {
            public bool WasProxy;
            public string Nickname;
            public string Label;
        }

        private void Awake()
        {
            ShowInfo = Config.Bind("Main", "Show Info", true);
            LogPackets = Config.Bind("Main", "LogPackets", false);
            DebugLog = Config.Bind("Main", "DebugLog", false);

            FikaEventDispatcher.SubscribeEvent<FikaGameCreatedEvent>(OnFikaGameCreated);
            FikaEventDispatcher.SubscribeEvent<FikaGameEndedEvent>(OnFikaGameEnded);
            FikaEventDispatcher.SubscribeEvent<FikaNetworkManagerCreatedEvent>(OnFikaNetworkManagerCreated);
            FikaEventDispatcher.SubscribeEvent<FikaNetworkManagerDestroyedEvent>(OnFikaNetworkManagerDestroyed);
            FikaEventDispatcher.SubscribeEvent<PeerDisconnectedEvent>(OnPeerDisconnected);

            ThatsLitAPI.OnGameWorldDestroyed += OnGameWorldDestroyed;
            ThatsLitAPI.ShouldSetupPlayer += ShouldSetupPlayer;
            ThatsLitAPI.OnBeforePlayerSetupDirect += OnBeforePlayerSetupDirect;
            ThatsLitAPI.OnPlayerBrightnessScoreCalculatedDirect += OnPlayerBrightnessScoreCalculatedDirect;
            ThatsLitAPI.OnPlayerSurroundingTerrainSampledDirect += OnPlayerSurroundingTerrainSampledDirect;
            ThatsLitAPI.OnMainPlayerGUI += OnMainPlayerGUI;

            var manager = Singleton<IFikaNetworkManager>.Instance;
            if ((manager is FikaServer server && server.Started) ||
                (manager is FikaClient client && client.Started))
            {
                RegisterNetworkManager(manager);
            }
        }

        private void OnDestroy()
        {
            FikaEventDispatcher.UnsubscribeEvent<FikaGameCreatedEvent>(OnFikaGameCreated);
            FikaEventDispatcher.UnsubscribeEvent<FikaGameEndedEvent>(OnFikaGameEnded);
            FikaEventDispatcher.UnsubscribeEvent<FikaNetworkManagerCreatedEvent>(OnFikaNetworkManagerCreated);
            FikaEventDispatcher.UnsubscribeEvent<FikaNetworkManagerDestroyedEvent>(OnFikaNetworkManagerDestroyed);
            FikaEventDispatcher.UnsubscribeEvent<PeerDisconnectedEvent>(OnPeerDisconnected);

            ThatsLitAPI.OnGameWorldDestroyed -= OnGameWorldDestroyed;
            ThatsLitAPI.ShouldSetupPlayer -= ShouldSetupPlayer;
            ThatsLitAPI.OnBeforePlayerSetupDirect -= OnBeforePlayerSetupDirect;
            ThatsLitAPI.OnPlayerBrightnessScoreCalculatedDirect -= OnPlayerBrightnessScoreCalculatedDirect;
            ThatsLitAPI.OnPlayerSurroundingTerrainSampledDirect -= OnPlayerSurroundingTerrainSampledDirect;
            ThatsLitAPI.OnMainPlayerGUI -= OnMainPlayerGUI;

            UnregisterNetworkManager();
            ClearRaidState();
        }

        private bool ShouldSetupPlayer(Player player)
        {
            if (player == null || player.IsAI)
                return false;

            if (player is FikaPlayer fikaPlayer &&
                (raidEnded || fikaPlayer.IsObservedAI || fikaPlayer is FikaBot))
            {
                return false;
            }

            if (player.IsYourPlayer)
                return !FikaBackendUtils.IsHeadless && !Application.isBatchMode;

            // Fika reserves NetId 1 for the host. The dedicated HeadlessGame has no local player.
            return player is ObservedPlayer observedPlayer &&
                (!FikaBackendUtils.IsHeadlessGame || observedPlayer.NetId != 1);
        }

        private void OnBeforePlayerSetupDirect(ThatsLitPlayer player)
        {
            if (!(player.Player is FikaPlayer fikaPlayer) || !ShouldSetupPlayer(fikaPlayer))
                return;

            if (!fikaPlayer.IsYourPlayer)
            {
                ThatsLitAPI.ToggleBrightnessProxyDirect(player, true);
                // Grass scores come from the owning client too (Fika Headless has no GPUI detail data to sample).
                ThatsLitAPI.ToggleTerrainProxyDirect(player, true);
                if (networkManager is FikaServer)
                {
                    // Force immediate broadcast of host's current state to the newly joined / reconnected peer.
                    lastSent = float.NegativeInfinity;
                    ResetTerrainSendState();
                }
            }

            PruneDestroyedActivePlayers();
            ActivePlayers[fikaPlayer.NetId] = fikaPlayer;

            if (DebugLog.Value)
                Logger.LogInfo($"Player {fikaPlayer.Profile.Nickname} #{fikaPlayer.NetId} set up (proxy: {!fikaPlayer.IsYourPlayer}, terrain proxy: {ThatsLitAPI.IsTerrainProxyDirect(player)}).");
        }

        private void OnMainPlayerGUI()
        {
            if (!ShowInfo.Value)
                return;

            var world = Singleton<ThatsLitGameworld>.Instance;
            if (world == null || world.AllThatsLitPlayers.Count <= 1)
                return;

            bool isLayout = Event.current == null || Event.current.type == EventType.Layout;

            GUILayout.Label("  [That's Lit Sync]");
            if (!ThatsLitPlugin.EnabledLighting.Value)
                GUILayout.Label("  !Brightness Module OFF!");

            if (isLayout)
                seenGuiPlayers.Clear();

            foreach (var player in world.AllThatsLitPlayers.Values)
            {
                if (player == null || !(player.Player is ObservedPlayer))
                    continue;

                if (isLayout)
                    seenGuiPlayers.Add(player);

                bool isNew = !guiLabelCache.TryGetValue(player, out var cached);
                if (isNew)
                {
                    cached = new CachedPlayerGuiLabel();
                    guiLabelCache[player] = cached;
                }

                if (isLayout || isNew)
                {
                    bool isProxy = ThatsLitAPI.IsBrightnessProxyDirect(player);
                    string nickname = player.Player.Profile?.Nickname ?? string.Empty;
                    if (cached.Label == null || cached.WasProxy != isProxy || !string.Equals(cached.Nickname, nickname, StringComparison.Ordinal))
                    {
                        cached.WasProxy = isProxy;
                        cached.Nickname = nickname;
                        cached.Label = isProxy ? $"  {nickname}" : $"  {nickname} !Non-Proxy!";
                    }
                }

                GUILayout.Label(cached.Label);
                Utility.GUILayoutDrawAsymetricMeter((int)(ThatsLitAPI.GetBrightnessScoreDirect(player) / 0.0999f));
            }

            if (isLayout && guiLabelCache.Count > seenGuiPlayers.Count)
            {
                staleGuiPlayers.Clear();
                foreach (var cachedPlayer in guiLabelCache.Keys)
                {
                    if (cachedPlayer == null || !seenGuiPlayers.Contains(cachedPlayer))
                        staleGuiPlayers.Add(cachedPlayer);
                }
                for (int i = 0; i < staleGuiPlayers.Count; i++)
                    guiLabelCache.Remove(staleGuiPlayers[i]);
            }
        }

        private void OnGameWorldDestroyed()
        {
            ClearRaidState();
        }

        private void OnFikaGameCreated(FikaGameCreatedEvent ev)
        {
            raidEnded = false;

            if (ThatsLitPlugin.EnabledLighting.Value)
                return;

            const string message = "That's Lit brightness is disabled locally.";
            Logger.LogWarning(message);
            if (!FikaBackendUtils.IsHeadless)
            {
                NotificationManager.DisplayWarningNotification(message);
                ConsoleScreen.Log(message);
            }
        }

        private void OnFikaGameEnded(FikaGameEndedEvent ev)
        {
            raidEnded = true;
            ClearRaidState();
        }

        private void ClearRaidState()
        {
            ActivePlayers.Clear();
            guiLabelCache.Clear();
            lastSent = float.NegativeInfinity;
            ResetTerrainSendState();
        }

        private void Update()
        {
            if (raidEnded || ActivePlayers.Count == 0)
                return;

            float now = Time.unscaledTime;
            if (now >= nextActivePlayersPruneTime)
            {
                nextActivePlayersPruneTime = now + 5f;
                PruneDestroyedActivePlayers();
            }
        }

        private void PruneDestroyedActivePlayers()
        {
            if (ActivePlayers.Count == 0)
                return;

            stalePlayerIds.Clear();
            foreach (var kvp in ActivePlayers)
            {
                if (kvp.Value == null)
                    stalePlayerIds.Add(kvp.Key);
            }
            for (int i = 0; i < stalePlayerIds.Count; i++)
            {
                ActivePlayers.Remove(stalePlayerIds[i]);
            }
        }

        private void ResetTerrainSendState()
        {
            lastTerrainSent = float.NegativeInfinity;
            hasLastTerrain = false;
        }

        private void OnFikaNetworkManagerCreated(FikaNetworkManagerCreatedEvent ev)
        {
            RegisterNetworkManager(ev.Manager);
        }

        private void RegisterNetworkManager(IFikaNetworkManager manager)
        {
            if (ReferenceEquals(networkManager, manager))
                return;

            UnregisterNetworkManager();
            switch (manager)
            {
                case FikaServer server:
                    server.RegisterPacket<ScorePacket, NetPeer>(HandlePacketServer);
                    server.RegisterPacket<TerrainScorePacket, NetPeer>(HandleTerrainPacketServer);
                    break;
                case FikaClient client:
                    client.RegisterPacket<ScorePacket>(HandlePacketClient);
                    client.RegisterPacket<TerrainScorePacket>(HandleTerrainPacketClient);
                    break;
                default:
                    return;
            }

            networkManager = manager;
        }

        private void OnFikaNetworkManagerDestroyed(FikaNetworkManagerDestroyedEvent ev)
        {
            if (ReferenceEquals(networkManager, ev.Manager))
                UnregisterNetworkManager();
        }

        private void UnregisterNetworkManager()
        {
            networkManager?.UnregisterPacket<ScorePacket>();
            networkManager?.UnregisterPacket<TerrainScorePacket>();
            networkManager = null;
            lastSent = float.NegativeInfinity;
            ResetTerrainSendState();
        }

        private void OnPeerDisconnected(PeerDisconnectedEvent ev)
        {
            PruneDestroyedActivePlayers();
            if (ReferenceEquals(networkManager, ev.NetworkManager) && networkManager is FikaClient)
            {
                lastSent = float.NegativeInfinity;
                ResetTerrainSendState();
            }
        }

        private static bool IsValidPacket(in ScorePacket packet)
        {
            if (packet.version != ScorePacket.CurrentVersion)
                return false;
            if (float.IsNaN(packet.score) || float.IsInfinity(packet.score) || packet.score < -5f || packet.score > 5f)
                return false;
            if (float.IsNaN(packet.ambienceScore) || float.IsInfinity(packet.ambienceScore) || packet.ambienceScore < -5f || packet.ambienceScore > 5f)
                return false;
            if (float.IsNaN(packet.litScoreFactor) || float.IsInfinity(packet.litScoreFactor) || packet.litScoreFactor < 0f || packet.litScoreFactor > 10f)
                return false;
            if (float.IsNaN(packet.baseAmbienceScore) || float.IsInfinity(packet.baseAmbienceScore) || packet.baseAmbienceScore < -5f || packet.baseAmbienceScore > 5f)
                return false;
            return true;
        }

        private bool TryApplyRemoteScore(ScorePacket packet, NetPeer senderPeer = null)
        {
            if (raidEnded || !IsValidPacket(in packet) ||
                !ActivePlayers.TryGetValue(packet.netId, out var player))
            {
                return false;
            }
            if (player == null)
            {
                ActivePlayers.Remove(packet.netId);
                return false;
            }
            if (player.IsYourPlayer)
            {
                return false;
            }

            if (senderPeer != null)
            {
                var peerPlayer = senderPeer.Player;
                if (peerPlayer == null || peerPlayer.NetId != packet.netId || !ReferenceEquals(peerPlayer, player))
                    return false;
            }

            return ThatsLitAPI.TrySetProxyBrightnessState(player, packet.score, packet.ambienceScore, packet.litScoreFactor, packet.baseAmbienceScore);
        }

        private void HandlePacketServer(ScorePacket packet, NetPeer peer)
        {
            if (!(networkManager is FikaServer server) || !server.Started || !TryApplyRemoteScore(packet, peer))
                return;

            server.SendData(ref packet, DeliveryMethod.Unreliable, peer);
            if (LogPackets.Value)
                Logger.LogInfo($"Relayed #{packet.netId} {packet.score:F2}/{packet.ambienceScore:F2}/{packet.litScoreFactor:F2}/{packet.baseAmbienceScore:F2} at f{Time.frameCount}.");
        }

        private void HandlePacketClient(ScorePacket packet)
        {
            if (TryApplyRemoteScore(packet) && LogPackets.Value)
                Logger.LogInfo($"Received #{packet.netId} {packet.score}/{packet.ambienceScore} at f{Time.frameCount}.");
        }

        private void OnPlayerBrightnessScoreCalculatedDirect(ThatsLitPlayer player, float score, float ambienceScore)
        {
            if (raidEnded || FikaBackendUtils.IsHeadless ||
                !(player.Player is FikaPlayer fikaPlayer) || !fikaPlayer.IsYourPlayer ||
                Singleton<IFikaGame>.Instance?.GameController?.GameInstance?.Status != GameStatus.Started)
            {
                return;
            }

            FikaServer server = networkManager as FikaServer;
            FikaClient client = networkManager as FikaClient;
            bool canSendServer = server != null && server.Started && !FikaBackendUtils.IsSinglePlayer;
            bool canSendClient = !canSendServer && client != null &&
                                 client.ServerConnection?.ConnectionState == ConnectionState.Connected;
            if (!canSendServer && !canSendClient)
                return;

            float litScoreFactor = ThatsLitAPI.GetLitScoreFactorDirect(player);
            float baseAmbienceScore = ThatsLitAPI.GetBaseAmbienceScoreDirect(player);
            var now = Time.time;
            var threshold = Mathf.Abs(score) > 0.65f ? 0.015f : 0.035f;
            float delta = Mathf.Abs(lastScore - score)
                        + Mathf.Abs(lastAmbienceScore - ambienceScore)
                        + Mathf.Abs(lastLitScoreFactor - litScoreFactor)
                        + Mathf.Abs(lastBaseAmbienceScore - baseAmbienceScore);
            if (delta <= threshold && now - lastSent <= 5f)
            {
                return;
            }

            PruneDestroyedActivePlayers();
            var packet = new ScorePacket(fikaPlayer.NetId, score, ambienceScore, litScoreFactor, baseAmbienceScore);
            if (canSendServer)
            {
                server.SendData(ref packet, DeliveryMethod.Unreliable);
                if (LogPackets.Value)
                    Logger.LogInfo($"Broadcast #{packet.netId} {score:F2}/{ambienceScore:F2}/{litScoreFactor:F2}/{baseAmbienceScore:F2} at f{Time.frameCount}.");
            }
            else
            {
                client.SendData(ref packet, DeliveryMethod.Unreliable);
                if (LogPackets.Value)
                    Logger.LogInfo($"Uploaded #{packet.netId} {score:F2}/{ambienceScore:F2}/{litScoreFactor:F2}/{baseAmbienceScore:F2} at f{Time.frameCount}.");
            }

            lastSent = now;
            lastScore = score;
            lastAmbienceScore = ambienceScore;
            lastLitScoreFactor = litScoreFactor;
            lastBaseAmbienceScore = baseAmbienceScore;
        }

        // ===== Terrain (grass) score sync =====

        private static bool IsValidTerrainPacket(in TerrainScorePacket packet)
        {
            if (packet.version != TerrainScorePacket.CurrentVersion
             || packet.prone == null || packet.regular == null
             || packet.prone.Length != TerrainScorePacket.BucketCount || packet.regular.Length != TerrainScorePacket.BucketCount)
                return false;
            for (int i = 0; i < TerrainScorePacket.BucketCount; i++)
            {
                if (!TerrainScorePacket.IsValidEncoded(packet.prone[i]) || !TerrainScorePacket.IsValidEncoded(packet.regular[i]))
                    return false;
            }
            return true;
        }

        private bool TryApplyRemoteTerrain(in TerrainScorePacket packet, NetPeer senderPeer = null)
        {
            if (raidEnded || !IsValidTerrainPacket(in packet) ||
                !ActivePlayers.TryGetValue(packet.netId, out var player))
            {
                return false;
            }
            if (player == null)
            {
                ActivePlayers.Remove(packet.netId);
                return false;
            }
            if (player.IsYourPlayer)
            {
                return false;
            }

            if (senderPeer != null)
            {
                var peerPlayer = senderPeer.Player;
                if (peerPlayer == null || peerPlayer.NetId != packet.netId || !ReferenceEquals(peerPlayer, player))
                    return false;
            }

            for (int i = 0; i < TerrainScorePacket.BucketCount; i++)
            {
                terrainScratchProne[i] = TerrainScorePacket.Decode(packet.prone[i]);
                terrainScratchRegular[i] = TerrainScorePacket.Decode(packet.regular[i]);
            }
            return ThatsLitAPI.TrySetProxyTerrainScores(player, terrainScratchProne, terrainScratchRegular);
        }

        private void HandleTerrainPacketServer(TerrainScorePacket packet, NetPeer peer)
        {
            if (!(networkManager is FikaServer server) || !server.Started || !TryApplyRemoteTerrain(in packet, peer))
                return;

            server.SendData(ref packet, DeliveryMethod.Unreliable, peer);
            if (LogPackets.Value)
                Logger.LogInfo($"Relayed terrain #{packet.netId} (5: {TerrainScorePacket.Decode(packet.prone[0]):F3}/{TerrainScorePacket.Decode(packet.regular[0]):F3}) at f{Time.frameCount}.");
        }

        private void HandleTerrainPacketClient(TerrainScorePacket packet)
        {
            if (TryApplyRemoteTerrain(in packet) && LogPackets.Value)
                Logger.LogInfo($"Received terrain #{packet.netId} (5: {TerrainScorePacket.Decode(packet.prone[0]):F3}/{TerrainScorePacket.Decode(packet.regular[0]):F3}) at f{Time.frameCount}.");
        }

        private void OnPlayerSurroundingTerrainSampledDirect(ThatsLitPlayer player)
        {
            if (raidEnded || FikaBackendUtils.IsHeadless || Application.isBatchMode ||
                !(player.Player is FikaPlayer fikaPlayer) || !fikaPlayer.IsYourPlayer)
            {
                return;
            }

            // The sampled event can fire every frame while standing still: rate-limit before doing any work.
            float now = Time.time;
            if (now - lastTerrainSent < TerrainMinInterval)
                return;

            if (Singleton<IFikaGame>.Instance?.GameController?.GameInstance?.Status != GameStatus.Started)
                return;

            FikaServer server = networkManager as FikaServer;
            FikaClient client = networkManager as FikaClient;
            bool canSendServer = server != null && server.Started && !FikaBackendUtils.IsSinglePlayer;
            bool canSendClient = client != null && client.ServerConnection?.ConnectionState == ConnectionState.Connected;
            if (!canSendServer && !canSendClient)
                return;

            if (!ThatsLitAPI.SampleAllTerrainBucketsDirect(player, terrainScratchProne, terrainScratchRegular))
                return;

            bool changed = !hasLastTerrain;
            for (int i = 0; i < TerrainScorePacket.BucketCount; i++)
            {
                sendTerrainProne[i] = TerrainScorePacket.Encode(terrainScratchProne[i]);
                sendTerrainRegular[i] = TerrainScorePacket.Encode(terrainScratchRegular[i]);
                if (sendTerrainProne[i] != lastTerrainProne[i] || sendTerrainRegular[i] != lastTerrainRegular[i])
                    changed = true;
            }
            if (!changed && now - lastTerrainSent < TerrainHeartbeat)
                return;

            var packet = new TerrainScorePacket
            {
                version = TerrainScorePacket.CurrentVersion,
                netId = fikaPlayer.NetId,
                prone = sendTerrainProne,
                regular = sendTerrainRegular,
            };

            PruneDestroyedActivePlayers();
            if (canSendServer)
                server.SendData(ref packet, DeliveryMethod.Unreliable);
            else
                client.SendData(ref packet, DeliveryMethod.Unreliable);

            lastTerrainSent = now;
            hasLastTerrain = true;
            System.Array.Copy(sendTerrainProne, lastTerrainProne, TerrainScorePacket.BucketCount);
            System.Array.Copy(sendTerrainRegular, lastTerrainRegular, TerrainScorePacket.BucketCount);
            if (LogPackets.Value)
                Logger.LogInfo($"{(canSendServer ? "Broadcast" : "Uploaded")} terrain #{packet.netId} (5: {terrainScratchProne[0]:F3}/{terrainScratchRegular[0]:F3}, changed: {changed}) at f{Time.frameCount}.");
        }
    }
}
