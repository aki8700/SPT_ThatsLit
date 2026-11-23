using System;
using Comfort.Common;
using EFT;
using UnityEngine;

namespace ThatsLit
{
    public static class ThatsLitAPI
    {
        public static bool IsBrightnessProxyDirect (ThatsLitPlayer player)
        {
            return player?.PlayerLitScoreProfile?.IsProxy ?? false;
        }
        public static bool IsBrightnessProxy (Player player)
        {
            if (player == null || Singleton<ThatsLitGameworld>.Instance?.AllThatsLitPlayers?.TryGetValue(player, out ThatsLitPlayer tlp) != true)
                return false;
            return tlp.PlayerLitScoreProfile?.IsProxy ?? false;
        }

        public static void ToggleBrightnessProxyDirect (ThatsLitPlayer player, bool toggle)
        {
            if (player == null) return;
            if (player.PlayerLitScoreProfile == null)
                player.PlayerLitScoreProfile = new PlayerLitScoreProfile(player);
            if (player.PlayerLitScoreProfile.IsProxy == toggle)
                return;
            player.ToggleBrightnessProxy(toggle);
        }

        public static void ToggleBrightnessProxy (Player player, bool toggle)
        {
            if (player == null || Singleton<ThatsLitGameworld>.Instance?.AllThatsLitPlayers?.TryGetValue(player, out ThatsLitPlayer tlp) != true)
                return;

            ToggleBrightnessProxyDirect(tlp, toggle);
        }
        public static Action<ThatsLitPlayer> OnBeforePlayerSetupDirect;
        public static void TrySetProxyBrightnessScoreDirect(ThatsLitPlayer player, float score, float ambienceScore)
        {
            if (player?.PlayerLitScoreProfile?.IsProxy != true) return;

            player.PlayerLitScoreProfile.frame0.multiFrameLitScore = score;
            player.PlayerLitScoreProfile.frame0.ambienceScore = ambienceScore;
        }
        public static bool TrySetProxyBrightnessStateDirect(ThatsLitPlayer player, float score, float ambienceScore, float litScoreFactor, float baseAmbienceScore)
        {
            if (player == null) return false;
            if (player.IsBrightnessProxyRequested && player.PlayerLitScoreProfile == null)
                ToggleBrightnessProxyDirect(player, true);
            if (player.PlayerLitScoreProfile?.IsProxy != true) return false;

            player.PlayerLitScoreProfile.frame0.multiFrameLitScore = score;
            player.PlayerLitScoreProfile.frame0.ambienceScore = ambienceScore;
            player.PlayerLitScoreProfile.frame0.baseAmbienceScore = baseAmbienceScore;
            player.PlayerLitScoreProfile.litScoreFactor = litScoreFactor;
            return true;
        }
        public static Action<Player> OnBeforePlayerSetup;
        public static void TrySetProxyBrightnessScore(Player player, float score, float ambienceScore)
        {
            if (player == null || Singleton<ThatsLitGameworld>.Instance?.AllThatsLitPlayers?.TryGetValue(player, out ThatsLitPlayer tlp) != true)
                return;

            TrySetProxyBrightnessScoreDirect(tlp, score, ambienceScore);
        }
        public static bool TrySetProxyBrightnessState(Player player, float score, float ambienceScore, float litScoreFactor, float baseAmbienceScore)
        {
            if (player == null || Singleton<ThatsLitGameworld>.Instance?.AllThatsLitPlayers?.TryGetValue(player, out ThatsLitPlayer tlp) != true)
                return false;

            return TrySetProxyBrightnessStateDirect(tlp, score, ambienceScore, litScoreFactor, baseAmbienceScore);
        }
        public static float GetLitScoreFactorDirect(ThatsLitPlayer player)
        {
            return player?.PlayerLitScoreProfile?.litScoreFactor ?? 0f;
        }
        public static float GetBaseAmbienceScoreDirect(ThatsLitPlayer player)
        {
            return player?.PlayerLitScoreProfile?.frame0.baseAmbienceScore ?? 0f;
        }

        public static float GetBrightnessScore (Player player)
        {
            if (player == null || Singleton<ThatsLitGameworld>.Instance?.AllThatsLitPlayers?.TryGetValue(player, out ThatsLitPlayer tlp) != true)
                return 0;

            return GetBrightnessScoreDirect(tlp);
        }

        public static float GetAmbienceBrightnessScore (Player player)
        {
            if (player == null || Singleton<ThatsLitGameworld>.Instance?.AllThatsLitPlayers?.TryGetValue(player, out ThatsLitPlayer tlp) != true)
                return 0;

            return GetAmbienceBrightnessScoreDirect(tlp);
        }

        public static float GetBrightnessScoreDirect (ThatsLitPlayer player)
        {
            return player?.PlayerLitScoreProfile?.frame0.multiFrameLitScore ?? 0;
        }

        public static float GetAmbienceBrightnessScoreDirect (ThatsLitPlayer player)
        {
            return player?.PlayerLitScoreProfile?.frame0.ambienceScore ?? 0;
        }

        public static float GetFoliageScore (Player player)
        {
            if (player == null || Singleton<ThatsLitGameworld>.Instance?.AllThatsLitPlayers?.TryGetValue(player, out ThatsLitPlayer tlp) != true)
                return 0;

            return GetFoliageScoreDirect(tlp);
        }

        public static float GetFoliageScoreDirect (ThatsLitPlayer player)
        {
            return player?.Foliage?.FoliageScore ?? 0;
        }
        public static Func<Player, bool> ShouldSetupPlayer;
        public static Action<ThatsLitGameworld> OnGameWorldSetup;
        public static Action OnGameWorldDestroyed;
        public static Action OnMainPlayerGUI;
        public static Action<ThatsLitPlayer, float, float> OnPlayerBrightnessScoreCalculatedDirect;
        public static Action<Player, float, float> OnPlayerBrightnessScoreCalculated;
        public static Action<ThatsLitPlayer> OnPlayerSurroundingTerrainSampledDirect;
        public static Action<Player> OnPlayerSurroundingTerrainSampled;
        // ===== Terrain detail (grass) proxy: lets That's Lit Sync feed remotely sampled grass scores to hosts that
        // cannot sample GPUI detail maps themselves (Fika Headless). =====

        /// <summary>Number of terrain score buckets exchanged by <see cref="SampleAllTerrainBucketsDirect"/> / <see cref="TrySetProxyTerrainScoresDirect"/>.</summary>
        public const int TerrainBucketCount = ThatsLitGameworld.ActiveTerrainBucketCount;

        public static bool IsTerrainProxyDirect (ThatsLitPlayer player)
        {
            return player?.TerrainDetails?.IsProxy ?? false;
        }

        /// <summary>
        /// Request (or stop) receiving terrain detail scores for this player instead of sampling them locally.
        /// The proxy profile is created even in batchmode (headless) as soon as the game world is set up.
        /// </summary>
        public static void ToggleTerrainProxyDirect (ThatsLitPlayer player, bool toggle)
        {
            if (player == null) return;
            if (player.IsTerrainProxyRequested == toggle && (!toggle || player.TerrainDetails == null || player.TerrainDetails.IsProxy))
                return;
            player.ToggleTerrainProxy(toggle);
        }

        public static void ToggleTerrainProxy (Player player, bool toggle)
        {
            if (player == null || Singleton<ThatsLitGameworld>.Instance?.AllThatsLitPlayers?.TryGetValue(player, out ThatsLitPlayer tlp) != true)
                return;
            ToggleTerrainProxyDirect(tlp, toggle);
        }

        /// <summary>
        /// Sample the <see cref="TerrainBucketCount"/> terrain score buckets of a locally sampled player (wire order) into the arrays.
        /// Returns false if the player has no local terrain sample (module disabled / unavailable map / proxy).
        /// </summary>
        public static bool SampleAllTerrainBucketsDirect (ThatsLitPlayer player, float[] outProne, float[] outRegular)
        {
            var world = Singleton<ThatsLitGameworld>.Instance;
            if (player?.TerrainDetails == null || world == null) return false;
            return world.SampleAllTerrainBuckets(player.TerrainDetails, outProne, outRegular);
        }

        /// <summary>
        /// Apply remotely sampled terrain bucket scores (wire order) to a terrain proxy. Returns false if the player is not a terrain proxy
        /// (or the grass module is disabled / unavailable here).
        /// </summary>
        public static bool TrySetProxyTerrainScoresDirect (ThatsLitPlayer player, float[] prone, float[] regular)
        {
            if (player == null) return false;
            if (player.IsTerrainProxyRequested && player.TerrainDetails == null)
                player.EnsureTerrainProxyProfile();
            if (player.TerrainDetails?.IsProxy != true) return false;
            return ThatsLitGameworld.SetProxyTerrainBuckets(player.TerrainDetails, prone, regular);
        }

        public static bool TrySetProxyTerrainScores (Player player, float[] prone, float[] regular)
        {
            if (player == null || Singleton<ThatsLitGameworld>.Instance?.AllThatsLitPlayers?.TryGetValue(player, out ThatsLitPlayer tlp) != true)
                return false;
            return TrySetProxyTerrainScoresDirect(tlp, prone, regular);
        }

        public static int GetTerrainDetailCount3x3Direct (ThatsLitPlayer player)
        {
            return player?.TerrainDetails?.RecentDetailCount3x3 ?? 0;
        }
        public static int GetTerrainDetailCount5x5Direct (ThatsLitPlayer player)
        {
            return player?.TerrainDetails?.RecentDetailCount5x5 ?? 0;
        }
        public static int GetTerrainDetailCount3x3 (Player player)
        {
            if (player == null || Singleton<ThatsLitGameworld>.Instance?.AllThatsLitPlayers?.TryGetValue(player, out ThatsLitPlayer tlp) != true)
                return 0;

            return GetTerrainDetailCount3x3Direct(tlp);
        }
        public static int GetTerrainDetailCount5x5 (Player player)
        {
            if (player == null || Singleton<ThatsLitGameworld>.Instance?.AllThatsLitPlayers?.TryGetValue(player, out ThatsLitPlayer tlp) != true)
                return 0;

            return GetTerrainDetailCount5x5Direct(tlp);
        }
        public static float GetTerrainDetailScoreCenter3x3Direct (ThatsLitPlayer player)
        {
            var world = Singleton<ThatsLitGameworld>.Instance;
            if (player == null || player.TerrainDetails == null || player.Player == null || world?.AllThatsLitPlayers == null)
                return 0f;
            var score = world.CalculateDetailScore(player.TerrainDetails, Vector3.zero, 0, 0);
            if (player.Player.IsInPronePose)
                return score.prone;

            var pf = Utility.GetPoseFactor(player.Player.PoseLevel, (player.Player.Physical != null && player.Player.Physical.MaxPoseLevel > 0f ? player.Player.Physical.MaxPoseLevel : 1f), player.Player.IsInPronePose);
            return Utility.GetPoseWeightedRegularTerrainScore(pf, score);
        }
        public static LightAndLaserState GetLightAndLaserStateDirect (ThatsLitPlayer player)
        {
            return player != null ? player.LightAndLaserState : default;
        }
        public static bool AnyVisibleLight (Player player)
        {
            if (player != null && Singleton<ThatsLitGameworld>.Instance?.AllThatsLitPlayers?.TryGetValue(player, out ThatsLitPlayer tlp) == true)
                return tlp.LightAndLaserState.AnyVisibleLight;
            return false;
        }
        public static bool AnyVisibleLaser (Player player)
        {
            if (player != null && Singleton<ThatsLitGameworld>.Instance?.AllThatsLitPlayers?.TryGetValue(player, out ThatsLitPlayer tlp) == true)
                return tlp.LightAndLaserState.AnyVisibleLaser;
            return false;
        }
        public static bool AnyIRLight (Player player)
        {
            if (player != null && Singleton<ThatsLitGameworld>.Instance?.AllThatsLitPlayers?.TryGetValue(player, out ThatsLitPlayer tlp) == true)
                return tlp.LightAndLaserState.AnyIRLight;
            return false;
        }
        public static bool AnyIRLaser (Player player)
        {
            if (player != null && Singleton<ThatsLitGameworld>.Instance?.AllThatsLitPlayers?.TryGetValue(player, out ThatsLitPlayer tlp) == true)
                return tlp.LightAndLaserState.AnyIRLaser;
            return false;
        }
        public static bool AnyVisibleMain (Player player)
        {
            if (player != null && Singleton<ThatsLitGameworld>.Instance?.AllThatsLitPlayers?.TryGetValue(player, out ThatsLitPlayer tlp) == true)
                return tlp.LightAndLaserState.AnyVisibleMain;
            return false;
        }
        public static bool AnyVisibleSub (Player player)
        {
            if (player != null && Singleton<ThatsLitGameworld>.Instance?.AllThatsLitPlayers?.TryGetValue(player, out ThatsLitPlayer tlp) == true)
                return tlp.LightAndLaserState.AnyVisibleSub;
            return false;
        }
        public static bool AnyIRMain (Player player)
        {
            if (player != null && Singleton<ThatsLitGameworld>.Instance?.AllThatsLitPlayers?.TryGetValue(player, out ThatsLitPlayer tlp) == true)
                return tlp.LightAndLaserState.AnyIRMain;
            return false;
        }
        public static bool AnyIRSub (Player player)
        {
            if (player != null && Singleton<ThatsLitGameworld>.Instance?.AllThatsLitPlayers?.TryGetValue(player, out ThatsLitPlayer tlp) == true)
                return tlp.LightAndLaserState.AnyIRSub;
            return false;
        }
        public static float GetMainVisibleLight (Player player)
        {
            if (player != null && Singleton<ThatsLitGameworld>.Instance?.AllThatsLitPlayers?.TryGetValue(player, out ThatsLitPlayer tlp) == true)
                return tlp.LightAndLaserState.deviceStateCache.light;
            return 0;
        }
        public static float GetMainVisibleLaser (Player player)
        {
            if (player != null && Singleton<ThatsLitGameworld>.Instance?.AllThatsLitPlayers?.TryGetValue(player, out ThatsLitPlayer tlp) == true)
                return tlp.LightAndLaserState.deviceStateCache.laser;
            return 0;
        }
        public static float GetMainIRLight (Player player)
        {
            if (player != null && Singleton<ThatsLitGameworld>.Instance?.AllThatsLitPlayers?.TryGetValue(player, out ThatsLitPlayer tlp) == true)
                return tlp.LightAndLaserState.deviceStateCache.irLight;
            return 0;
        }
        public static float GetMainIRLaser (Player player)
        {
            if (player != null && Singleton<ThatsLitGameworld>.Instance?.AllThatsLitPlayers?.TryGetValue(player, out ThatsLitPlayer tlp) == true)
                return tlp.LightAndLaserState.deviceStateCache.irLaser;
            return 0;
        }
        public static float GetSheathedVisibleLight (Player player)
        {
            if (player != null && Singleton<ThatsLitGameworld>.Instance?.AllThatsLitPlayers?.TryGetValue(player, out ThatsLitPlayer tlp) == true)
                return tlp.LightAndLaserState.deviceStateCacheSub.light;
            return 0;
        }
        public static float GetSheathedVisibleLaser (Player player)
        {
            if (player != null && Singleton<ThatsLitGameworld>.Instance?.AllThatsLitPlayers?.TryGetValue(player, out ThatsLitPlayer tlp) == true)
                return tlp.LightAndLaserState.deviceStateCacheSub.laser;
            return 0;
        }
        public static float GetSheathedIRLight (Player player)
        {
            if (player != null && Singleton<ThatsLitGameworld>.Instance?.AllThatsLitPlayers?.TryGetValue(player, out ThatsLitPlayer tlp) == true)
                return tlp.LightAndLaserState.deviceStateCacheSub.irLight;
            return 0;
        }
        public static float GetSheathedIRLaser (Player player)
        {
            if (player != null && Singleton<ThatsLitGameworld>.Instance?.AllThatsLitPlayers?.TryGetValue(player, out ThatsLitPlayer tlp) == true)
                return tlp.LightAndLaserState.deviceStateCacheSub.irLaser;
            return 0;
        }
    }

}
