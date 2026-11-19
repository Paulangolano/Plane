using UnityEngine;

namespace Skyreach
{
    /// <summary>
    /// Darkens peripheral vision during fast rotation, which strongly reduces VR motion sickness.
    /// A quad parented to the head camera, drawn with the Skyreach/Vignette shader.
    /// </summary>
    public sealed class ComfortVignette : MonoBehaviour
    {
        public static readonly string[] LevelNames = { "Off", "Light", "Medium", "Strong" };
        static readonly float[] Strengths = { 0f, 0.35f, 0.6f, 0.9f };

        public int LevelIndex { get; private set; }
        public string LevelName => LevelNames[LevelIndex];
        Material mat;
        MeshRenderer rend;
        float amount;
        static readonly int AmountId = Shader.PropertyToID("_Amount");

        public static ComfortVignette Create(Camera head, Shader shader, int level)
        {
            var go = MeshFactory.Spawn("ComfortVignette", MeshFactory.Quad(1, 1), new Material(shader), head.transform,
                new Vector3(0, 0, 0.12f), Quaternion.identity, Vector3.one * 0.42f);
            var v = go.AddComponent<ComfortVignette>();
            v.rend = go.GetComponent<MeshRenderer>();
            v.mat = v.rend.sharedMaterial;
            v.mat.renderQueue = 4000;
            v.LevelIndex = Mathf.Clamp(level, 0, Strengths.Length - 1);
            return v;
        }

        public string Cycle()
        {
            LevelIndex = (LevelIndex + 1) % Strengths.Length;
            return LevelName;
        }

        /// <param name="angularSpeed">Aircraft rotation rate in rad/s.</param>
        public void Tick(float dt, float angularSpeed)
        {
            float target = Mathf.Clamp01((angularSpeed - 0.35f) / 1.6f) * Strengths[LevelIndex];
            amount += (target - amount) * Mathf.Min(1, dt * 5);
            mat.SetFloat(AmountId, amount);
            rend.enabled = amount > 0.01f;
        }
    }
}
