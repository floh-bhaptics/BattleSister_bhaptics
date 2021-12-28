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
        
        private static (float, float) getAngleAndShift(Rigidbody player, Vector3 hit)
        {
            Vector3 patternOrigin = new Vector3(0f, 0f, 1f);
            // y is "up", z is "forward" in local coordinates
            Vector3 hitPosition = hit - player.position;
            Quaternion PlayerRotation = player.transform.rotation;
            Vector3 playerDir = PlayerRotation.eulerAngles;
            Vector3 flattenedHit = new Vector3(hitPosition.x, 0f, hitPosition.z);
            float earlyhitAngle = Vector3.Angle(flattenedHit, patternOrigin);
            Vector3 earlycrossProduct = Vector3.Cross(flattenedHit, patternOrigin);
            if (earlycrossProduct.y > 0f) { earlyhitAngle *= -1f; }
            //tactsuitVr.LOG("EarlyHitAngle: " + earlyhitAngle.ToString());
            float myRotation = earlyhitAngle - playerDir.y;
            myRotation *= -1f;
            if (myRotation < 0f) { myRotation = 360f + myRotation; }

            /*
            Vector3 relativeHitDir = Quaternion.Euler(playerDir) * hitPosition;
            Vector2 xzHitDir = new Vector2(relativeHitDir.x, relativeHitDir.z);
            Vector2 patternOrigin = new Vector2(0f, 1f);
            float hitAngle = Vector2.SignedAngle(xzHitDir, patternOrigin);
            //hitAngle *= -1;
            //hitAngle += 90f;
            hitAngle += 180f;
            //tactsuitVr.LOG("HitAngle: " + hitAngle.ToString());
            if (hitAngle < 0f) { hitAngle = 360f + hitAngle; }
            */
            float hitShift = hitPosition.y;
            //tactsuitVr.LOG("HitShift: " + hitShift.ToString());
            if (hitShift > 1.7f) { hitShift = 0.5f; }
            else if (hitShift < 1.0f) { hitShift = -0.5f; }
            else { hitShift = (hitShift - 1.0f) / 1.7f - 0.5f; }

            //tactsuitVr.LOG("Relative x-z-position: " + relativeHitDir.x.ToString() + " "  + relativeHitDir.z.ToString());
            //tactsuitVr.LOG("HitAngle: " + hitAngle.ToString());
            //tactsuitVr.LOG("HitShift: " + hitShift.ToString());
            return (myRotation, hitShift);
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
                (float angle, float shift) = getAngleAndShift(myPlayer, myHit);
                if (shift == 0.5) { tactsuitVr.HeadShot(playbackKey, angle); }
                tactsuitVr.PlayBackHit(playbackKey, angle, shift);
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
