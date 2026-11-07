using UnityEngine;

namespace Gladiator.Character
{
    /// <summary>
    /// The 30-joint SOMA skeleton used by NVIDIA MotionBricks (joint names and order from soma_n1.json), so clips
    /// generated with the model can later be retargeted onto this rig joint by joint.
    /// Our rest pose is a T-pose in body space: Y up, character facing +Z, its left side on -X, and every joint
    /// with identity local rotation. Proportions come from <see cref="BodyMetrics"/>.
    /// </summary>
    public static class SomaSkeleton
    {
        public const int Count = 30;

        public const int Hips = 0, Spine1 = 1, Spine2 = 2, Chest = 3, Neck1 = 4, Neck2 = 5, Head = 6, Jaw = 7, LeftEye = 8, RightEye = 9;
        public const int LeftShoulder = 10, LeftArm = 11, LeftForeArm = 12, LeftHand = 13, LeftHandThumbEnd = 14, LeftHandMiddleEnd = 15;
        public const int RightShoulder = 16, RightArm = 17, RightForeArm = 18, RightHand = 19, RightHandThumbEnd = 20, RightHandMiddleEnd = 21;
        public const int LeftLeg = 22, LeftShin = 23, LeftFoot = 24, LeftToeBase = 25;
        public const int RightLeg = 26, RightShin = 27, RightFoot = 28, RightToeBase = 29;

        public static readonly string[] Names =
        {
            "Hips", "Spine1", "Spine2", "Chest", "Neck1", "Neck2", "Head", "Jaw", "LeftEye", "RightEye",
            "LeftShoulder", "LeftArm", "LeftForeArm", "LeftHand", "LeftHandThumbEnd", "LeftHandMiddleEnd",
            "RightShoulder", "RightArm", "RightForeArm", "RightHand", "RightHandThumbEnd", "RightHandMiddleEnd",
            "LeftLeg", "LeftShin", "LeftFoot", "LeftToeBase",
            "RightLeg", "RightShin", "RightFoot", "RightToeBase"
        };

        public static readonly int[] Parents =
        {
            -1, Hips, Spine1, Spine2, Chest, Neck1, Neck2, Head, Head, Head,
            Chest, LeftShoulder, LeftArm, LeftForeArm, LeftHand, LeftHand,
            Chest, RightShoulder, RightArm, RightForeArm, RightHand, RightHand,
            Hips, LeftLeg, LeftShin, LeftFoot,
            Hips, RightLeg, RightShin, RightFoot
        };

        /// <summary>Creates the joint hierarchy under <paramref name="parent"/> at the rest positions (body space).</summary>
        public static Transform[] Create(Transform parent, Vector3[] restPositions)
        {
            var bones = new Transform[Count];
            for (int i = 0; i < Count; i++)
            {
                bones[i] = new GameObject(Names[i]).transform;
                int p = Parents[i];
                bones[i].SetParent(p < 0 ? parent : bones[p], false);
                Vector3 parentPos = p < 0 ? Vector3.zero : restPositions[p];
                bones[i].localPosition = restPositions[i] - parentPos;
                bones[i].localRotation = Quaternion.identity;
            }
            return bones;
        }
    }

    /// <summary>
    /// All the body measurements derived from a <see cref="CharacterAppearance"/>, in metres, body space.
    /// The builder (mesh), the rig (joint positions) and the poser (limb lengths) all read from here.
    /// </summary>
    public class BodyMetrics
    {
        public float H, S, Fem;
        /// <summary>0..1 from the creator's POSTURA: how much the poser straightens the back.</summary>
        public float Posture;
        public float LegTopY, KneeY, AnkleY, HipsY, Neck1Y, ArmY;
        public float LegX, KneeX, ShoulderHalf, UpperArm, ForeArm, HandLen;
        public float HipHalf, WaistHalf, ChestHalf, ChestFront, BellyFront;
        public float NeckR, ThighR, KneeR, CalfR, AnkleR, DeltR, BicepR, ElbowR, ForeR, WristR;
        public float HeadScale, HeadH, ChinY, HeadHalfW, HeadHalfD;
        public readonly Vector3[] Joints = new Vector3[SomaSkeleton.Count];

        public BodyMetrics(CharacterAppearance a)
        {
            float fem = Fem = Mathf.Clamp01(a.Femininity);
            Posture = Mathf.Clamp01(a.Posture);
            float mus = a.Muscle, fat = a.Fat;
            H = a.HeightMeters;
            float s = S = H / 1.8f;

            LegTopY = H * (0.515f + (a.LegLength - 0.5f) * 0.05f);
            AnkleY = 0.075f * s;
            KneeY = AnkleY + (LegTopY - AnkleY) * 0.49f;
            HipsY = LegTopY + 0.04f * s;
            ArmY = H * 0.822f;
            Neck1Y = ArmY + 0.025f * s;

            LegX = s * (0.085f + fem * 0.012f + fat * 0.015f);
            KneeX = LegX * 0.9f;
            ShoulderHalf = s * (0.175f + (a.Shoulders - 0.5f) * 0.05f - fem * 0.025f + mus * 0.015f);
            float armLen = H * 0.33f * (1f + (a.ArmLength - 0.5f) * 0.12f);
            UpperArm = armLen * 0.54f;
            ForeArm = armLen * 0.46f;
            HandLen = 0.18f * s;

            HipHalf = s * (0.15f + fem * 0.035f + fat * 0.05f);
            WaistHalf = s * (0.125f - fem * 0.018f + fat * 0.085f + mus * 0.01f);
            ChestHalf = s * (0.155f + mus * 0.035f + fat * 0.035f + (a.Shoulders - 0.5f) * 0.03f - fem * 0.02f);
            ChestFront = s * (0.1f + mus * 0.03f + fat * 0.03f + fem * 0.03f);
            BellyFront = s * (0.09f + fat * 0.1f);

            NeckR = s * (0.05f + a.Neck * 0.022f + mus * 0.01f + fat * 0.008f - fem * 0.008f);
            ThighR = s * (0.07f + mus * 0.02f + fat * 0.035f + fem * 0.012f);
            KneeR = s * (0.045f + fat * 0.01f);
            CalfR = s * (0.05f + mus * 0.016f + fat * 0.012f);
            AnkleR = s * 0.03f;
            DeltR = s * (0.05f + mus * 0.024f + fat * 0.01f);
            BicepR = s * (0.04f + mus * 0.024f + fat * 0.016f - fem * 0.005f);
            ElbowR = s * (0.032f + fat * 0.006f);
            ForeR = s * (0.037f + mus * 0.014f + fat * 0.008f);
            WristR = s * 0.025f;

            HeadScale = Mathf.Lerp(0.9f, 1.12f, a.HeadSize) * (0.75f + 0.25f * s);
            HeadH = 0.245f * HeadScale;
            ChinY = H - HeadH;
            HeadHalfW = 0.08f * HeadScale * (1f - 0.05f * fem);
            HeadHalfD = 0.1f * HeadScale;

            var j = Joints;
            float tor = Neck1Y - HipsY;
            j[SomaSkeleton.Hips] = new Vector3(0, HipsY, 0);
            j[SomaSkeleton.Spine1] = new Vector3(0, HipsY + tor * 0.1f, 0);
            j[SomaSkeleton.Spine2] = new Vector3(0, HipsY + tor * 0.3f, 0);
            j[SomaSkeleton.Chest] = new Vector3(0, HipsY + tor * 0.52f, 0);
            j[SomaSkeleton.Neck1] = new Vector3(0, Neck1Y, 0);
            j[SomaSkeleton.Head] = new Vector3(0, ChinY + 0.05f * HeadScale, 0);
            j[SomaSkeleton.Neck2] = (j[SomaSkeleton.Neck1] + j[SomaSkeleton.Head]) * 0.5f;
            j[SomaSkeleton.Jaw] = j[SomaSkeleton.Head] + new Vector3(0, -0.01f, 0.03f) * HeadScale;
            j[SomaSkeleton.LeftEye] = new Vector3(-0.032f * HeadScale, ChinY + HeadH * 0.55f, HeadHalfD * 0.8f);
            j[SomaSkeleton.RightEye] = new Vector3(0.032f * HeadScale, ChinY + HeadH * 0.55f, HeadHalfD * 0.8f);

            for (int side = -1; side <= 1; side += 2)
            {
                bool left = side < 0;
                int sh = left ? SomaSkeleton.LeftShoulder : SomaSkeleton.RightShoulder;
                j[sh] = new Vector3(side * 0.04f * s, ArmY - 0.01f * s, 0);
                j[sh + 1] = new Vector3(side * ShoulderHalf, ArmY, -0.01f * s);
                j[sh + 2] = j[sh + 1] + new Vector3(side * UpperArm, 0, 0);
                j[sh + 3] = j[sh + 2] + new Vector3(side * ForeArm, 0, 0);
                j[sh + 4] = j[sh + 3] + new Vector3(side * 0.06f * s, -0.01f * s, 0.06f * s);
                j[sh + 5] = j[sh + 3] + new Vector3(side * HandLen, 0, 0);

                int leg = left ? SomaSkeleton.LeftLeg : SomaSkeleton.RightLeg;
                j[leg] = new Vector3(side * LegX, LegTopY, 0);
                j[leg + 1] = new Vector3(side * KneeX, KneeY, 0.005f * s);
                j[leg + 2] = new Vector3(side * KneeX, AnkleY, -0.01f * s);
                j[leg + 3] = new Vector3(side * KneeX, 0.025f * s, 0.12f * s);
            }
        }
    }
}
