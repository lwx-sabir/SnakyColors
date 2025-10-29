using UnityEngine;
using UnityEngine.EventSystems;

namespace SnakyColors
{
    public class TapParticleSpawner : MonoBehaviour
    {
        [Header("Particle Prefab (World Space)")]
        public ParticleSystem tapParticlePrefab;

        private Camera mainCam;

        void Start()
        {
            mainCam = Camera.main;
        }

        void Update()
        {
            // Allow multi-touch or mouse click
            if (Input.touchCount > 0)
            {
                foreach (Touch touch in Input.touches)
                {
                    if (touch.phase == TouchPhase.Began)
                        SpawnTapParticle(touch.position);
                }
            }
            else if (Input.GetMouseButtonDown(0))
            {
                if (!EventSystem.current.IsPointerOverGameObject())
                    SpawnTapParticle(Input.mousePosition);
            }
        }

        void SpawnTapParticle(Vector2 screenPos)
        {
            // Convert screen to world position
            Vector3 worldPos = mainCam.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, 10f));
            worldPos.z = 0f;

            // Instantiate particle
            ParticleSystem ps = Instantiate(tapParticlePrefab, worldPos, Quaternion.identity);

            // Pick random color
            var main = ps.main;
            if (ColorLib.Colors.Length > 0)
                main.startColor = ColorLib.Colors[Random.Range(0, ColorLib.Colors.Length)];

            ps.Play();

            // Auto destroy when done
            Destroy(ps.gameObject, main.duration + main.startLifetime.constantMax);
        }
    }
}

