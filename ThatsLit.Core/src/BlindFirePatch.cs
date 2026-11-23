using System.Reflection;
using UnityEngine;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace ThatsLit.Patches.Vision
{
    public class BlindFirePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            // EFT 40743: the obfuscated aiming class is now BotAimingData; the end point is stored in
            // _endTargetPoint by UpdateEndTargetPoint() (the getter is a plain field read now).
            return AccessTools.Method(typeof(BotAimingData), nameof(BotAimingData.UpdateEndTargetPoint));
        }

        [PatchPostfix]
        public static void Postfix (BotAimingData __instance, BotOwner ____owner)
        {
            if (!ThatsLitPlugin.EnabledMod.Value) return;
            if (!ThatsLitPlugin.ForceBlindFireScatter.Value) return;
            ThatsLitPlugin.swBlindFireScatter.MaybeResume();
            Player botPlayer = ____owner?.GetPlayer;
            if (__instance == null
             || botPlayer == null
             || (botPlayer.HandsController as Player.FirearmController)?.Blindfire != true)
            {
                ThatsLitPlugin.swBlindFireScatter.Stop();
                return;
            }
            float dis = Vector3.Distance(__instance._endTargetPoint, botPlayer.Position);

            __instance._endTargetPoint += UnityEngine.Random.insideUnitSphere * 5 * Mathf.InverseLerp(15f, 200f, dis);
            __instance.UpdateLookDir();
            ThatsLitPlugin.swBlindFireScatter.Stop();
        }
    }
}
