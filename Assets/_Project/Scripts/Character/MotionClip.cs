using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gladiator.Character
{
    /// <summary>
    /// A looping clip generated offline with NVIDIA MotionBricks (Bridge/Python/generate_clips.py).
    /// Each frame stores, per SOMA joint, the body-space rotation relative to the SOMA bind pose (already in
    /// Unity's space), plus the in-place hips offset. <see cref="bindDirs"/> are the bind-pose bone directions,
    /// used to retarget onto our own rest pose.
    /// </summary>
    [Serializable]
    public class MotionClip
    {
        public string name;
        public float fps;
        public int frames;
        public bool loop;
        public float syncPhase;
        public float[] velocity;
        public float bindHipsHeight;
        public string[] joints;
        public float[] bindDirs;
        public float[] hips;
        public float[] rotations;
        /// <summary>Normalized times of the key poses (attacks: end of wind-up, end of strike).</summary>
        public float[] markers;
        /// <summary>Traversal clips: hand height / forward reach at each marker (SOMA metres, -1 = feet on top).</summary>
        public float[] contactY, contactFwd;
        public string source;

        public float Marker(int i, float fallback) => markers != null && i < markers.Length ? markers[i] : fallback;

        [NonSerialized] public Quaternion[] Rot;   // [frame * J + joint]
        [NonSerialized] public Vector3[] Hips;
        [NonSerialized] public Vector3[] BindDir;
        [NonSerialized] public int[] ToRig;        // clip joint -> SomaSkeleton index

        public float Duration => frames / fps;
        public Vector3 Velocity => new Vector3(velocity[0], 0f, velocity[2]);
        public float Speed => Velocity.magnitude;

        public void Prepare()
        {
            int J = joints.Length;
            Rot = new Quaternion[frames * J];
            for (int i = 0; i < Rot.Length; i++)
                Rot[i] = new Quaternion(rotations[i * 4], rotations[i * 4 + 1], rotations[i * 4 + 2], rotations[i * 4 + 3]);
            Hips = new Vector3[frames];
            for (int f = 0; f < frames; f++) Hips[f] = new Vector3(hips[f * 3], hips[f * 3 + 1], hips[f * 3 + 2]);
            BindDir = new Vector3[J];
            for (int j = 0; j < J; j++) BindDir[j] = new Vector3(bindDirs[j * 3], bindDirs[j * 3 + 1], bindDirs[j * 3 + 2]);
            ToRig = new int[J];
            for (int j = 0; j < J; j++) ToRig[j] = Array.IndexOf(SomaSkeleton.Names, joints[j]);
            rotations = null; // free the parsed floats
        }

        /// <summary>Samples at normalized time t (0..1, wraps). Writes rotations (rig joint order) and the hips offset.</summary>
        public void Sample(float t, Quaternion[] outRot, out Vector3 hipsOffset)
        {
            float ft = Mathf.Repeat(t, 1f) * frames;
            int f0 = Mathf.FloorToInt(ft) % frames;
            int f1 = loop ? (f0 + 1) % frames : Mathf.Min(f0 + 1, frames - 1);
            float k = ft - Mathf.Floor(ft);
            int J = joints.Length;
            for (int j = 0; j < J; j++)
            {
                int r = ToRig[j];
                if (r < 0) continue;
                outRot[r] = Quaternion.Slerp(Rot[f0 * J + j], Rot[f1 * J + j], k);
            }
            hipsOffset = Vector3.Lerp(Hips[f0], Hips[f1], k);
        }
    }

    /// <summary>Loads the MotionBricks clips from Resources/MotionBricks once.</summary>
    public static class MotionLibrary
    {
        static Dictionary<string, MotionClip> clips;

        public static bool Available => Load().Count > 0;

        public static MotionClip Get(string name) => Load().TryGetValue(name, out var c) ? c : null;

        public static Dictionary<string, MotionClip> Load()
        {
            if (clips != null) return clips;
            clips = new Dictionary<string, MotionClip>();
            foreach (var ta in Resources.LoadAll<TextAsset>("MotionBricks"))
            {
                try
                {
                    var c = JsonUtility.FromJson<MotionClip>(ta.text);
                    if (c == null || c.frames <= 0 || c.joints == null) continue;
                    c.Prepare();
                    clips[c.name] = c;
                }
                catch (Exception e) { Debug.LogWarning("[MOTION] bad clip " + ta.name + ": " + e.Message); }
            }
            Debug.Log("[MOTION] loaded " + clips.Count + " MotionBricks clips: " + string.Join(", ", clips.Keys));
            return clips;
        }
    }
}
