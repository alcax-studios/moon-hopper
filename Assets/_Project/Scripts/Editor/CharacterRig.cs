using System;
using Alcax.Combat;
using UnityEngine;
using static Alcax.EditorTools.BuildUtil;

namespace Alcax.EditorTools
{
    /// <summary>
    /// The shared primitive character rig: Rig > LegL/LegR (> Knee > Shoe), Body (> Head > Jaw, ArmL, ArmR).
    /// Lobby NPCs use it as-is. Combat characters (combat = true) get elbows for two-hand weapon IK,
    /// gear (helmet, plate carrier), and per-part hit colliders with damage multipliers on the Actor layer.
    /// </summary>
    public static class CharacterRig
    {
        public enum Outfit { Uniform, Vest, LabCoat, FlightJacket, Casual, PlateCarrier }

        public struct Look
        {
            public Color jacket, pants, skin, hair, accent;
            public int hairStyle;        // 0 bun, 1 short crop, 2 cap, 3 helmet
            public float height, build;  // scale factors (about 1)
            public Outfit outfit;
        }

        public const int ActorLayer = 8;

        /// <param name="charMat">Makes (or reuses) a material for a key, color, and smoothness.</param>
        public static Transform Build(Transform parent, string objName, Vector3 pos, float yaw, Look look, string id,
            Func<string, Color, float, Material> charMat, Material dark, Material metalLight, bool combat = false)
        {
            var mJacket = charMat(id + "_jacket", look.jacket, 0.3f);
            var mPants = charMat(id + "_pants", look.pants, 0.3f);
            var mSkin = charMat(id + "_skin", look.skin, 0.35f);
            var mHair = charMat(id + "_hair", look.hair, 0.35f);
            var mAccent = charMat(id + "_accent", look.accent, 0.4f);
            var mDark = dark;

            var c = new GameObject(objName).transform;
            if (parent != null) c.SetParent(parent, false);
            c.position = pos;
            c.rotation = Quaternion.Euler(0f, yaw, 0f);
            var rig = new GameObject("Rig").transform;
            rig.SetParent(c, false);
            rig.localScale = new Vector3(look.build, look.height, look.build);

            GameObject Part(PrimitiveType type, string name, Transform p, Vector3 lp, Vector3 scale, Vector3 euler, Material mat, float zone = 0f, bool head = false)
            {
                var go = Prim(type, name, p, lp, scale, euler, mat, collider: combat && zone > 0f);
                if (combat && zone > 0f)
                {
                    var hz = go.AddComponent<HitZone>();
                    hz.damageMultiplier = zone;
                    hz.isHead = head;
                }
                return go;
            }

            foreach (float s in new[] { -1f, 1f })
            {
                var leg = new GameObject(s < 0 ? "LegL" : "LegR").transform;
                leg.SetParent(rig, false);
                leg.localPosition = new Vector3(s * 0.1f, 0.92f, 0f);
                Part(PrimitiveType.Capsule, "Thigh", leg, new Vector3(0f, -0.22f, 0f), new Vector3(0.15f, 0.23f, 0.15f), Vector3.zero, mPants, 0.7f);
                var knee = new GameObject("Knee").transform;
                knee.SetParent(leg, false);
                knee.localPosition = new Vector3(0f, -0.45f, 0f);
                Part(PrimitiveType.Capsule, "Shin", knee, new Vector3(0f, -0.21f, 0f), new Vector3(0.13f, 0.22f, 0.13f), Vector3.zero, mPants, 0.6f);
                Part(PrimitiveType.Cube, "Shoe", knee, new Vector3(0f, -0.43f, 0.05f), new Vector3(0.11f, 0.08f, 0.26f), Vector3.zero, mDark);
                if (combat && look.outfit == Outfit.PlateCarrier)
                    Part(PrimitiveType.Cube, "KneePad", knee, new Vector3(0f, 0.0f, 0.065f), new Vector3(0.1f, 0.1f, 0.03f), Vector3.zero, mAccent);
            }

            var body = new GameObject("Body").transform;
            body.SetParent(rig, false);
            body.localPosition = new Vector3(0f, 0.93f, 0f);
            Part(PrimitiveType.Capsule, "Hips", body, new Vector3(0f, 0.04f, 0f), new Vector3(0.34f, 0.13f, 0.22f), new Vector3(0f, 0f, 90f), mPants, 0.9f);
            Part(PrimitiveType.Capsule, "Torso", body, new Vector3(0f, 0.33f, 0f), new Vector3(0.36f, 0.3f, 0.22f), Vector3.zero, mJacket, 1f);
            Part(PrimitiveType.Cube, "Collar", body, new Vector3(0f, 0.56f, 0.02f), new Vector3(0.2f, 0.05f, 0.18f), Vector3.zero, mAccent);
            switch (look.outfit)
            {
                case Outfit.Vest:
                    Part(PrimitiveType.Cube, "Vest", body, new Vector3(0f, 0.34f, 0f), new Vector3(0.39f, 0.4f, 0.25f), Vector3.zero, mAccent);
                    Part(PrimitiveType.Cube, "Reflect", body, new Vector3(0f, 0.3f, 0f), new Vector3(0.395f, 0.04f, 0.255f), Vector3.zero, metalLight);
                    break;
                case Outfit.LabCoat:
                    Part(PrimitiveType.Cube, "CoatTail", body, new Vector3(0f, -0.12f, -0.01f), new Vector3(0.38f, 0.42f, 0.24f), Vector3.zero, mJacket);
                    Part(PrimitiveType.Cube, "Lapel", body, new Vector3(0f, 0.4f, 0.112f), new Vector3(0.06f, 0.3f, 0.01f), Vector3.zero, mPants);
                    break;
                case Outfit.FlightJacket:
                    Part(PrimitiveType.Cube, "Patch", body, new Vector3(-0.1f, 0.42f, 0.112f), new Vector3(0.07f, 0.05f, 0.01f), Vector3.zero, mAccent);
                    Part(PrimitiveType.Cube, "Zip", body, new Vector3(0f, 0.32f, 0.113f), new Vector3(0.012f, 0.4f, 0.01f), Vector3.zero, metalLight);
                    break;
                case Outfit.Uniform:
                    Part(PrimitiveType.Cube, "Badge", body, new Vector3(0.09f, 0.42f, 0.11f), new Vector3(0.06f, 0.03f, 0.01f), Vector3.zero, mAccent);
                    Part(PrimitiveType.Cube, "Stripe", body, new Vector3(0f, 0.3f, 0.115f), new Vector3(0.02f, 0.38f, 0.01f), Vector3.zero, mPants);
                    break;
                case Outfit.PlateCarrier:
                    Part(PrimitiveType.Cube, "Plate", body, new Vector3(0f, 0.36f, 0.005f), new Vector3(0.38f, 0.36f, 0.27f), Vector3.zero, mAccent);
                    for (int i = -1; i <= 1; i++)
                        Part(PrimitiveType.Cube, "Pouch", body, new Vector3(i * 0.1f, 0.24f, 0.14f), new Vector3(0.075f, 0.1f, 0.04f), Vector3.zero, mDark);
                    Part(PrimitiveType.Cube, "Radio", body, new Vector3(-0.15f, 0.44f, -0.12f), new Vector3(0.06f, 0.12f, 0.04f), Vector3.zero, mDark);
                    Part(PrimitiveType.Cube, "Belt", body, new Vector3(0f, 0.12f, 0f), new Vector3(0.37f, 0.05f, 0.24f), Vector3.zero, mDark);
                    break;
            }

            var head = new GameObject("Head").transform;
            head.SetParent(body, false);
            head.localPosition = new Vector3(0f, 0.62f, 0f);
            Part(PrimitiveType.Cylinder, "Neck", head, new Vector3(0f, -0.04f, 0f), new Vector3(0.09f, 0.05f, 0.09f), Vector3.zero, mSkin);
            Part(PrimitiveType.Sphere, "Skull", head, new Vector3(0f, 0.12f, 0f), new Vector3(0.19f, 0.23f, 0.21f), Vector3.zero, mSkin, 2.2f, true);
            switch (look.hairStyle)
            {
                case 0:
                    Part(PrimitiveType.Sphere, "Hair", head, new Vector3(0f, 0.17f, -0.02f), new Vector3(0.205f, 0.2f, 0.22f), Vector3.zero, mHair);
                    Part(PrimitiveType.Sphere, "Bun", head, new Vector3(0f, 0.2f, -0.12f), new Vector3(0.09f, 0.09f, 0.09f), Vector3.zero, mHair);
                    break;
                case 1:
                    Part(PrimitiveType.Sphere, "Hair", head, new Vector3(0f, 0.19f, -0.01f), new Vector3(0.2f, 0.16f, 0.215f), Vector3.zero, mHair);
                    break;
                case 2:
                    Part(PrimitiveType.Sphere, "Cap", head, new Vector3(0f, 0.21f, -0.005f), new Vector3(0.21f, 0.12f, 0.22f), Vector3.zero, mAccent);
                    Part(PrimitiveType.Cube, "Brim", head, new Vector3(0f, 0.19f, 0.11f), new Vector3(0.17f, 0.015f, 0.1f), new Vector3(-8f, 0f, 0f), mAccent);
                    break;
                default:
                    Part(PrimitiveType.Sphere, "Helmet", head, new Vector3(0f, 0.19f, -0.01f), new Vector3(0.235f, 0.19f, 0.25f), Vector3.zero, mAccent);
                    Part(PrimitiveType.Cube, "HelmetRim", head, new Vector3(0f, 0.15f, -0.01f), new Vector3(0.24f, 0.03f, 0.255f), Vector3.zero, mAccent);
                    Part(PrimitiveType.Cube, "Visor", head, new Vector3(0f, 0.135f, 0.1f), new Vector3(0.15f, 0.035f, 0.02f), Vector3.zero, mDark);
                    break;
            }
            Part(PrimitiveType.Cube, "Nose", head, new Vector3(0f, 0.1f, 0.105f), new Vector3(0.025f, 0.04f, 0.03f), new Vector3(10f, 0f, 0f), mSkin);
            foreach (float s in new[] { -1f, 1f })
            {
                Part(PrimitiveType.Sphere, "Eye", head, new Vector3(s * 0.038f, 0.135f, 0.095f), new Vector3(0.022f, 0.016f, 0.012f), Vector3.zero, mDark);
                Part(PrimitiveType.Cube, "Brow", head, new Vector3(s * 0.04f, 0.162f, 0.1f), new Vector3(0.04f, 0.008f, 0.01f), new Vector3(0f, 0f, s * -6f), mHair);
            }
            var jaw = new GameObject("Jaw").transform;
            jaw.SetParent(head, false);
            jaw.localPosition = new Vector3(0f, 0.06f, 0.04f);
            Part(PrimitiveType.Cube, "Mouth", jaw, new Vector3(0f, 0f, 0.055f), new Vector3(0.045f, 0.01f, 0.01f), Vector3.zero, mDark);
            Part(PrimitiveType.Sphere, "Chin", jaw, new Vector3(0f, -0.02f, 0.03f), new Vector3(0.11f, 0.06f, 0.09f), Vector3.zero, mSkin);

            foreach (float s in new[] { -1f, 1f })
            {
                var arm = new GameObject(s < 0 ? "ArmL" : "ArmR").transform;
                arm.SetParent(body, false);
                arm.localPosition = new Vector3(s * 0.22f, 0.5f, 0f);
                arm.localRotation = Quaternion.Euler(-8f, 0f, s * 6f);
                if (!combat)
                {
                    Part(PrimitiveType.Capsule, "Upper", arm, new Vector3(0f, -0.16f, 0f), new Vector3(0.1f, 0.17f, 0.1f), Vector3.zero, mJacket);
                    Part(PrimitiveType.Capsule, "Fore", arm, new Vector3(0f, -0.42f, 0.03f), new Vector3(0.085f, 0.15f, 0.085f), new Vector3(-12f, 0f, 0f), mJacket);
                    Part(PrimitiveType.Sphere, "Hand", arm, new Vector3(0f, -0.6f, 0.06f), new Vector3(0.07f, 0.09f, 0.04f), Vector3.zero, mSkin);
                }
                else
                {
                    // Two segments for weapon IK: shoulder to elbow 0.3, elbow to hand 0.3
                    Part(PrimitiveType.Capsule, "Upper", arm, new Vector3(0f, -0.15f, 0f), new Vector3(0.1f, 0.16f, 0.1f), Vector3.zero, mJacket, 0.6f);
                    var elbow = new GameObject("Elbow").transform;
                    elbow.SetParent(arm, false);
                    elbow.localPosition = new Vector3(0f, -0.3f, 0f);
                    Part(PrimitiveType.Capsule, "Fore", elbow, new Vector3(0f, -0.14f, 0f), new Vector3(0.085f, 0.14f, 0.085f), Vector3.zero, mJacket, 0.6f);
                    Part(PrimitiveType.Sphere, "Glove", elbow, new Vector3(0f, -0.3f, 0f), new Vector3(0.075f, 0.09f, 0.06f), Vector3.zero, mDark);
                }
            }

            if (!combat)
            {
                var cap = c.gameObject.AddComponent<CapsuleCollider>();
                cap.center = new Vector3(0f, 0.9f, 0f);
                cap.height = 1.8f;
                cap.radius = 0.28f;
                SetLayerRecursive(c.gameObject, 2);   // Ignore Raycast: foot IK rays and bullets pass through residents
            }
            else SetLayerRecursive(c.gameObject, ActorLayer);
            return c;
        }
    }
}
