using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

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
            Pointer pointer = Pointer.current;

            if (pointer == null)
                return;

            Vector2 pos = pointer.position.ReadValue();

            // Check if pressed this frame
            bool pressedThisFrame = pointer.press.wasPressedThisFrame;

            // Check if pointer is over UI
            bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

            if (pressedThisFrame && !overUI)
            {
                SpawnTapParticle(pos);
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

