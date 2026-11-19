using System.Collections.Generic;
using UnityEngine;

namespace Skyreach
{
    /// <summary>Ring meshes: gold = next ring, white = upcoming, green = passed.</summary>
    public sealed class RingVisuals : MonoBehaviour
    {
        RaceCourse course;
        SkyreachMaterials mats;
        readonly List<Transform> rings = new List<Transform>();
        Transform glow;
        Material glowMat;

        public void Build(RaceCourse c, SkyreachMaterials m)
        {
            course = c;
            mats = m;
            var torus = MeshFactory.Torus(CourseConfig.RingRadius, CourseConfig.RingTube, 10, 40);
            for (int i = 0; i < course.Rings.Count; i++)
            {
                var spec = course.Rings[i];
                var go = MeshFactory.Spawn($"Ring{i + 1}", torus, mats.RingNext, transform, spec.Position,
                    Quaternion.LookRotation(spec.Normal, Vector3.up));
                // Ring number floating above the ring.
                var text = new GameObject("Number").AddComponent<TextMesh>();
                text.font = UIFactory.DefaultFont;
                text.GetComponent<MeshRenderer>().sharedMaterial = UIFactory.DefaultFont.material;
                text.transform.SetParent(go.transform, false);
                text.transform.localPosition = new Vector3(0, CourseConfig.RingRadius + 7, 0);
                text.text = (i + 1).ToString();
                text.fontSize = 64;
                text.characterSize = 0.5f;
                text.anchor = TextAnchor.MiddleCenter;
                text.alignment = TextAlignment.Center;
                text.color = Color.white;
                text.gameObject.AddComponent<Billboard>();
                rings.Add(go.transform);
            }
            glowMat = mats.RingGlow;
            var disc = MeshFactory.Cylinder(CourseConfig.RingRadius - CourseConfig.RingTube, CourseConfig.RingRadius - CourseConfig.RingTube, 0.05f, 32);
            glow = MeshFactory.Spawn("Glow", disc, glowMat, transform, Vector3.zero, Quaternion.Euler(90, 0, 0)).transform;
            Refresh();
        }

        public void Refresh()
        {
            for (int i = 0; i < rings.Count; i++)
            {
                var r = rings[i].GetComponent<MeshRenderer>();
                if (i < course.Index)
                {
                    r.sharedMaterial = mats.RingDone;
                    rings[i].gameObject.SetActive(i >= course.Index - 1);
                }
                else if (i == course.Index)
                {
                    r.sharedMaterial = mats.RingActive;
                    rings[i].gameObject.SetActive(true);
                    glow.SetParent(rings[i], false);
                    glow.localPosition = Vector3.zero;
                    glow.localRotation = Quaternion.Euler(90, 0, 0);
                }
                else
                {
                    r.sharedMaterial = mats.RingNext;
                    rings[i].gameObject.SetActive(i <= course.Index + 3);
                }
                rings[i].localScale = Vector3.one;
            }
            glow.gameObject.SetActive(!course.Finished);
        }

        void Update()
        {
            if (course == null || course.Finished || course.Index >= rings.Count) return;
            float pulse = 1 + Mathf.Sin(Time.time * 4) * 0.04f;
            rings[course.Index].localScale = Vector3.one * pulse;
            var c = glowMat.color;
            c.a = 0.14f + Mathf.Sin(Time.time * 4) * 0.06f;
            glowMat.color = c;
        }
    }

    /// <summary>Keeps a label facing the main camera.</summary>
    public sealed class Billboard : MonoBehaviour
    {
        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam) transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position, Vector3.up);
        }
    }
}
