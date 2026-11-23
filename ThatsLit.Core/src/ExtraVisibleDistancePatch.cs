#define DEBUG_DETAILS
using SPT.Reflection.Patching;
using HarmonyLib;
using System.Reflection;
using UnityEngine;
using Comfort.Common;
using System.Collections.Generic;
using EFT.InventoryLogic;
using System;
using EFT;


namespace ThatsLit
{
    public class ExtraVisibleDistancePatch : ModulePatch
    {

        protected override MethodBase GetTargetMethod()
        {
            // EFT 40743: CheckVisibility(part, ref addVisibility) is gone. The extra sensor distance is now
            // computed once per enemy per look tick by GetAdditionalSensorDistance and added to VisibleDist
            // in EnemyPartVision.CheckLineOfSight (VisibleDist + addSensorDistance < magnitude => no LOS).
            return AccessTools.Method(typeof(EnemyInfo), nameof(EnemyInfo.GetAdditionalSensorDistance));
        }

        [PatchPostfix]
        // SAIN's patches are SPT ModulePatches whose Harmony id is the patch class name, so "me.sol.sain" alone never matched.
        [HarmonyAfter("me.sol.sain", "VisionDistancePatch")]
        public static void PatchPostfix(EnemyInfo __instance, ref float __result)
        {
            if (__instance?.Owner?.LookSensor == null) return;
            // Effective sensor distance as used by EnemyPartVision.CheckLineOfSight (VisibleDist + addSensorDistance).
            // Vanilla returns 1 (or ENEMY_LIGHT_ADD); SAIN (VisionDistancePatch) adds visionDist - VisibleDist, so when SAIN
            // rejects the enemy (outside its vision cone: modifier 0) the effective distance becomes exactly 1.
            // Never resurrect a distance SAIN has rejected.
            if (__instance.Person == null || __instance.Person.IsAI) return;
            float vd = __instance.Owner.LookSensor.VisibleDist;
            var look = __instance.Owner.Settings?.FileSettings?.Look;
            float vanillaBase = (__instance.Person.AIData != null && __instance.Person.AIData.UsingLight && look != null && vd < look.ENEMY_LIGHT_START_DIST)
                ? look.ENEMY_LIGHT_ADD
                : 1f;
            // Separate EFT's vanilla base (1 or ENEMY_LIGHT_ADD) so SAIN's -VisibleDist rejection is still caught when a flashlight is active.
            if (vd + (__result - vanillaBase) <= 0.5f) return;
            float effectiveDist = vd + __result;

            ThatsLitPlugin.swExtraVisDis.MaybeResume();
            if (!ThatsLitPlugin.EnabledMod.Value
             || ThatsLitPlugin.ExtraVisionDistanceScale.Value == 0
             || !ThatsLitPlugin.EnabledLighting.Value
             || ThatsLitPlugin.PMCOnlyMode.Value && !Utility.IsPMCSpawnType(__instance.Owner?.Profile?.Info?.Settings?.Role))
            {
                ThatsLitPlugin.swExtraVisDis.Stop();
                return;
            }

            if (Singleton<ThatsLitGameworld>.Instance?.ScoreCalculator == null)
            {
                ThatsLitPlugin.swExtraVisDis.Stop();
                return;
            }

            float addVisibility = 0f;
            ThatsLitPlayer player = null;
            Singleton<ThatsLitGameworld>.Instance?.AllThatsLitPlayers?.TryGetValue(__instance.Person, out player);
            if (player == null || player.PlayerLitScoreProfile == null)
            {
                ThatsLitPlugin.swExtraVisDis.Stop();
                return;
            }


            bool isNearest = false;
            if (player.lastNearest == __instance.Owner)
            {
                isNearest = true;
                if (player.DebugInfo != null)
                {
                    player.DebugInfo.lastDisComp = 0f;
                    player.DebugInfo.lastDisCompThermal = 0f;
                    player.DebugInfo.lastDisCompNVG = 0f;
                    player.DebugInfo.lastDisCompDay = 0f;
                }
            }

            bool thermalActive = false, nvgActive = false;
            float thermalRange = 0;

            var botNVG = __instance.Owner?.NightVision;
            if (botNVG?.UsingNow == true) // goggles
            {
                NightVisionComponent.EMask? mask = botNVG.NightVisionItem?.Template?.Mask;
                thermalActive = mask == EFT.InventoryLogic.NightVisionComponent.EMask.Thermal;
                nvgActive = mask != null && mask != EFT.InventoryLogic.NightVisionComponent.EMask.Thermal;
                if (thermalActive)
                {
                    string goggleId = botNVG.NightVisionItem?.Item?.StringTemplateId ?? string.Empty;
                    if (ThatsLitCompat.Goggles.TryGetValue(goggleId, out var goggleCompat) && goggleCompat?.TemplateInstance?.thermal != null)
                        thermalRange = goggleCompat.TemplateInstance.thermal.effectiveDistance;
                    else
                        thermalRange = 200f;
                }
            }
            else
            {
                EFT.InventoryLogic.SightComponent sightMod = __instance.Owner?.GetPlayer?.ProceduralWeaponAnimation?.CurrentAimingMod;
                if (sightMod != null)
                {
                    ThatsLitCompat.Scopes.TryGetValue(sightMod.Item?.StringTemplateId ?? string.Empty, out var scopeCompat);
                    if (scopeCompat?.TemplateInstance?.thermal != null)
                    {
                        thermalActive = true;
                        thermalRange = scopeCompat.TemplateInstance.thermal.effectiveDistance;
                    }
                    else if (scopeCompat?.TemplateInstance?.nightVision != null)
                        nvgActive = true;
                }
            }

            FrameStats frame0 = player.PlayerLitScoreProfile?.frame0 ?? default;
            // Compensate from the effective (SAIN-scaled) distance, not the raw LookSensor distance: otherwise SAIN's scaling
            // is counted twice, and SAIN's +1000 "always visible" naturally yields compensation <= 0 here.
            float originalDist = effectiveDist;
            if (thermalActive)
            {
                float compensation = thermalRange - originalDist;
                if (compensation > 0) addVisibility += UnityEngine.Random.Range(0.5f, 1f) * compensation * ThatsLitPlugin.ExtraVisionDistanceScale.Value;
                if (isNearest && player.DebugInfo != null)
                    player.DebugInfo.lastDisCompThermal = compensation;
            }
            else if (nvgActive && frame0.ambienceScore < 0) // Base + Sun/Moon < 0
            {
                float fogFactor = Mathf.InverseLerp(0f, 0.35f, EFT.Weather.WeatherController.Instance?.WeatherCurve?.Fog ?? 0f);
                float scale;
                if (player.LightAndLaserState.IRLight)
                    scale = 3.5f;
                else if (player.LightAndLaserState.IRLaser)
                    scale = 3f;
                else
                    scale = 2.5f;
                scale = Mathf.Lerp(1, scale, Mathf.Clamp01(frame0.ambienceScore / -1f)); // The darker the ambience, the more effective the NVG is

                float compensation = __instance.Owner.Settings.FileSettings.Look.MIDDLE_DIST - originalDist;
                if (compensation > 0)
                {
                    compensation *= 1f - fogFactor;
                    compensation *= scale;
                    compensation *= player.PlayerLitScoreProfile.litScoreFactor * ThatsLitPlugin.ExtraVisionDistanceScale.Value;
                    addVisibility += compensation * UnityEngine.Random.Range(0.25f, 1f); // 0.25x~1x of extra capped at 100m
                }
                if (isNearest && player.DebugInfo != null)
                    player.DebugInfo.lastDisCompNVG = compensation;
            }
            else if (!nvgActive && frame0.ambienceScore > 0) // Base + Sun/Moon > 0
            {
                float fogFactor = Mathf.InverseLerp(0f, 0.35f, EFT.Weather.WeatherController.Instance?.WeatherCurve?.Fog ?? 0f);
                float compensation = __instance.Owner.Settings.FileSettings.Look.MIDDLE_DIST - originalDist;
                if (compensation > 0)
                {
                    compensation *= 1f - fogFactor;
                    compensation *= (1f + frame0.ambienceScore / 5f) * ThatsLitPlugin.ExtraVisionDistanceScale.Value;
                    addVisibility += compensation * UnityEngine.Random.Range(0.2f, 1f);
                }
                if (isNearest && player.DebugInfo != null)
                    player.DebugInfo.lastDisCompDay = compensation;
            }
            else if (!nvgActive)
            {
                float fogFactor = Mathf.InverseLerp(0f, 0.35f, EFT.Weather.WeatherController.Instance?.WeatherCurve?.Fog ?? 0f);
                float litDiff = frame0.multiFrameLitScore - frame0.baseAmbienceScore; // The visibility provided by sun/moon + lightings
                litDiff = Mathf.InverseLerp(0, 2, litDiff);
                litDiff *= 1f - fogFactor;

                float compensation = __instance.Owner.Settings.FileSettings.Look.MIDDLE_DIST - originalDist;
                if (compensation > 0)
                {
                    compensation *= litDiff * ThatsLitPlugin.ExtraVisionDistanceScale.Value;
                    addVisibility += compensation;
                }
                if (isNearest && player.DebugInfo != null)
                    player.DebugInfo.lastDisComp = compensation;
            }

            if (addVisibility > 0f && !float.IsNaN(addVisibility) && !float.IsInfinity(addVisibility))
                __result += addVisibility;

            ThatsLitPlugin.swExtraVisDis.Stop();
        }
    }
}