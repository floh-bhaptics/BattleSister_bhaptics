using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

using MelonLoader;
using HarmonyLib;

using MyBhapticsTactsuit;

using BattleSister.Ballistics;

namespace BattleSister_bhaptics
{
    public class BattleSister_bhaptics : MelonMod
    {
        public static TactsuitVR tactsuitVr;
        public static bool rightHanded = true;

        public override void OnApplicationStart()
        {
            base.OnApplicationStart();
            tactsuitVr = new TactsuitVR();
            tactsuitVr.PlaybackHaptics("HeartBeat");
        }

        #region Internal functions

        private static KeyValuePair<float, float> getAngleAndShift(Transform player, Vector3 hit)
        {
            // bhaptics pattern starts in the front, then rotates to the left. 0° is front, 90° is left, 270° is right.
            // y is "up", z is "forward" in local coordinates
            Vector3 patternOrigin = new Vector3(0f, 0f, 1f);
            Vector3 hitPosition = hit - player.position;
            Quaternion myPlayerRotation = player.rotation;
            Vector3 playerDir = myPlayerRotation.eulerAngles;
            // get rid of the up/down component to analyze xz-rotation
            Vector3 flattenedHit = new Vector3(hitPosition.x, 0f, hitPosition.z);

            // get angle. .Net < 4.0 does not have a "SignedAngle" function...
            float hitAngle = Vector3.Angle(flattenedHit, patternOrigin);
            // check if cross product points up or down, to make signed angle myself
            Vector3 crossProduct = Vector3.Cross(flattenedHit, patternOrigin);
            if (crossProduct.y < 0f) { hitAngle *= -1f; }
            // relative to player direction
            float myRotation = hitAngle - playerDir.y;
            // switch directions (bhaptics angles are in mathematically negative direction)
            myRotation *= -1f;
            // convert signed angle into [0, 360] rotation
            if (myRotation < 0f) { myRotation = 360f + myRotation; }


            // up/down shift is in y-direction
            // in Shadow Legend, the torso Transform has y=0 at the neck,
            // and the torso ends at roughly -0.5 (that's in meters)
            // so cap the shift to [-0.5, 0]...
            float hitShift = hitPosition.y;
            float upperBound = 0.0f;
            float lowerBound = -0.5f;
            if (hitShift > upperBound) { hitShift = 0.5f; }
            else if (hitShift < lowerBound) { hitShift = -0.5f; }
            // ...and then spread/shift it to [-0.5, 0.5]
            else { hitShift = (hitShift - lowerBound) / (upperBound - lowerBound) - 0.5f; }

            //tactsuitVr.LOG("Relative x-z-position: " + relativeHitDir.x.ToString() + " "  + relativeHitDir.z.ToString());
            //tactsuitVr.LOG("HitAngle: " + hitAngle.ToString());
            //tactsuitVr.LOG("HitShift: " + hitShift.ToString());

            // No tuple returns available in .NET < 4.0, so this is the easiest quickfix
            return new KeyValuePair<float, float>(myRotation, hitShift);
        }


        private static bool isRightHandFunc(bool isPrimaryHand)
        {
            if ((isPrimaryHand) && (rightHanded)) { return true; }
            if ((!isPrimaryHand) && (!rightHanded)) { return true; }
            return false;
        }

        #endregion

        [HarmonyPatch(typeof(VrRig), "SetHandedness")]
        public class bhaptics_SetHandedness
        {
            [HarmonyPostfix]
            public static void Postfix(Handedness handedness)
            {
                tactsuitVr.LOG("Handedness: " + handedness.ToString());
                if ( (handedness.ToString().Contains("Right")) | (handedness.ToString().Contains("right")) ) { rightHanded = true; }
                else { rightHanded = false; }
            }
        }

        [HarmonyPatch(typeof(VrMeleeAudio), "OnCollisionEnter")]
        public class bhaptics_MeleeCollide
        {
            [HarmonyPostfix]
            public static void Postfix(VrMeleeAudio __instance)
            {
                bool isRightHand = true;
                try { isRightHand = isRightHandFunc(__instance.m_item.AttachedHoldInteraction.ActivePrimaryHand.m_isPrimaryHand); }
                catch { return; }
                tactsuitVr.GunRecoil("Melee", isRightHand);
            }
        }

        [HarmonyPatch(typeof(VrGun), "Fire")]
        public class bhaptics_FireGun
        {
            [HarmonyPostfix]
            public static void Postfix(VrGun __instance, float shotPower)
            {
                bool isRightHand = true;
                string feedbackKey;
                DamageType damageType = DamageType.Bolt;
                try
                {
                    isRightHand = isRightHandFunc(__instance.AttachedHoldInteraction.ActivePrimaryHand.m_isPrimaryHand);
                    damageType = __instance.Magazine.damageType;
                }
                catch { return; }
                switch (damageType)
                {
                    case DamageType.GrenadeLauncherProjectile:
                        feedbackKey = "Shotgun";
                        break;

                    case DamageType.PowerSword:
                        feedbackKey = "Melee";
                        break;

                    case DamageType.Fire:
                        tactsuitVr.LOG("Fire gun!");
                        feedbackKey = "Melee";
                        break;

                    default:
                        feedbackKey = "";
                        break;
                }
                tactsuitVr.GunRecoil(feedbackKey, isRightHand);
                if ((__instance.AttachedHoldInteraction.HasPrimaryGrasp) && (__instance.AttachedHoldInteraction.HasSecondaryGrasp))
                { tactsuitVr.GunSecondHand(!isRightHand); }
                //tactsuitVr.LOG("damageType: " + damageType.ToString());
            }
        }



        /*
                [HarmonyPatch(typeof(FlameThrower), "SetFiringOn")]
                public class bhaptics_FlameThrowerOn
                {
                    [HarmonyPostfix]
                    public static void Postfix()
                    {
                        //tactsuitVr.LOG("Flame on");
                    }
                }

                [HarmonyPatch(typeof(FlameThrower), "SetFiringOff")]
                public class bhaptics_FlameThrowerOff
                {
                    [HarmonyPostfix]
                    public static void Postfix()
                    {
                        //tactsuitVr.LOG("Flame off");
                    }
                }
        */
        [HarmonyPatch(typeof(ImpactManager), "ProcessImpact")]
        public class bhaptics_ProcessImpact
        {
            [HarmonyPostfix]
            public static void Postfix(DamageType damageType, Collider impactCollider, Vector3 impactPosition)
            {
                Rigidbody myPlayer;
                string playerName;
                Vector3 myHit;
                DamageType myDamage;
                string playbackKey;
                try
                {
                    myPlayer = impactCollider.attachedRigidbody;
                    myHit = impactPosition;
                    playerName = myPlayer.name;
                    myDamage = damageType;
                }
                catch { return; }
                if (playerName != "PlayerRig") { return; }
                switch (myDamage)
                {
                    case DamageType.Axe:
                        playbackKey = "BladeHit";
                        break;

                    case DamageType.Explosion:
                        playbackKey = "Impact";
                        break;

                    case DamageType.Club:
                        playbackKey = "BladeHit";
                        break;

                    case DamageType.BloodletterSword:
                        playbackKey = "BladeHit";
                        break;

                    default:
                        playbackKey = "BulletHit";
                        break;
                }
                var angleShift = getAngleAndShift(myPlayer.transform, myHit);
                if (angleShift.Value >= 0.5f) tactsuitVr.HeadShot(playbackKey, angleShift.Key);
                else tactsuitVr.PlayBackHit(playbackKey, angleShift.Key, angleShift.Value);
            }
        }

        [HarmonyPatch(typeof(VrTimedExplosive), "Explode")]
        public class bhaptics_BombExplode
        {
            [HarmonyPostfix]
            public static void Postfix()
            {
                tactsuitVr.PlaybackHaptics("ExplosionBelly");
            }
        }


        [HarmonyPatch(typeof(HealthAudio), "OnDeath")]
        public class bhaptics_OnDeath
        {
            [HarmonyPostfix]
            public static void Postfix()
            {
                tactsuitVr.LOG("Player died.");
                tactsuitVr.StopThreads();
            }
        }
/*
        [HarmonyPatch(typeof(HealthAudio), "OnHealthDecreased")]
        public class bhaptics_OnHealthDecreased
        {
            [HarmonyPostfix]
            public static void Postfix(HealthAudio __instance)
            {
                if (__instance.m_healthStatus.m_currentHealth < 0.2 * __instance.m_healthStatus.m_startHealth) { tactsuitVr.StartHeartBeat(); }
                if (__instance.m_healthStatus.m_currentHealth >= 0.2 * __instance.m_healthStatus.m_startHealth) { tactsuitVr.StopHeartBeat(); }
                tactsuitVr.LOG("Lost health: " + __instance.m_healthStatus.m_currentHealth.ToString());
            }
        }

        [HarmonyPatch(typeof(HealthAudio), "OnHealthIncreased")]
        public class bhaptics_OnHealthIncreased
        {
            [HarmonyPostfix]
            public static void Postfix(HealthAudio __instance)
            {
                if (__instance.m_healthStatus.m_currentHealth >= 0.2 * __instance.m_healthStatus.m_startHealth) { tactsuitVr.StopHeartBeat(); }
                if (__instance.m_healthStatus.m_currentHealth < 0.2 * __instance.m_healthStatus.m_startHealth) { tactsuitVr.StartHeartBeat(); }
                tactsuitVr.LOG("Gained health: " + __instance.m_healthStatus.m_currentHealth.ToString());
            }
        }
*/
        [HarmonyPatch(typeof(HealthStatusReceiver_DamageHud), "OnApplyHealthStatusUpdate")]
        public class bhaptics_OnHealthUpdated
        {
            [HarmonyPostfix]
            public static void Postfix(HealthStatusReceiver_DamageHud __instance)
            {
                if (__instance.m_healthStatus.m_currentHealth >= 0.5 * __instance.m_healthStatus.m_startHealth) { tactsuitVr.StopHeartBeat(); }
                if (__instance.m_healthStatus.m_currentHealth < 0.5 * __instance.m_healthStatus.m_startHealth) { tactsuitVr.StartHeartBeat(); }
                //tactsuitVr.LOG("Updated health: " + __instance.m_healthStatus.m_currentHealth.ToString());
            }
        }

    }
}
