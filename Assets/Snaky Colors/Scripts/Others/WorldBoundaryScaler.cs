using System.Collections.Generic;
using UnityEngine;

namespace SnakyColors
{
    // Attach to your world boundary root. It updates BoxCollider2D or EdgeCollider2D
    // to match NetworkClient.CurrentWorldSize. Keep this GameObject at origin with
    // unit scale to avoid double transforms.
    public class WorldBoundaryScaler : MonoBehaviour
    {
        [Header("Colliders (assign one or both)")]
        public BoxCollider2D box2D;
        public EdgeCollider2D edge2D;

        [Header("Outline Generation")]
        [Tooltip("Prefer using EdgeCollider2D as a thin outline ring when available.")]
        public bool preferEdgeCollider = true;

        [Tooltip("If true and EdgeCollider2D is not used, generate 4 thin BoxCollider2D rims instead of a full-area box.")]
        public bool generateBoxRim = true;

        [Tooltip("Thickness (world units) of the rim colliders when generating box rims.")]
        public float rimThickness = 1f;

        [Header("Sizing Source")]
        [Tooltip("Use world size from server (NetworkClient). If false, uses fallbackWorldSize.")]
        public bool followServerSize = true;

        [Tooltip("Used before join / when server size is not available.")]
        public float fallbackWorldSize = 150f;

        [Header("Transform Safety")]
        [Tooltip("If true, keeps this transform at origin with unit scale.")]
        public bool enforceOriginAndUnitScale = true;

        private float _lastApplied = -1f;

        // Generated rim colliders
        private BoxCollider2D _rimTop, _rimBottom, _rimLeft, _rimRight;

        void Awake()
        {
            // Auto-resolve references if not assigned
            if (box2D == null) TryGetComponent(out box2D);
            if (edge2D == null) TryGetComponent(out edge2D);

            if (enforceOriginAndUnitScale)
            {
                transform.position = Vector3.zero;
                transform.localScale = Vector3.one;
            }
        }

        void LateUpdate()
        {
            float worldSize = ResolveWorldSize();
            if (worldSize <= 0f) return;

            if (!Mathf.Approximately(worldSize, _lastApplied))
            {
                if (preferEdgeCollider && edge2D != null)
                {
                    DisableCenterBoxIfAny();
                    DisableRimBoxes();
                    ApplyEdgeSize(worldSize);
                }
                else if (generateBoxRim)
                {
                    DisableEdgeIfAny();
                    DisableCenterBoxIfAny();
                    ApplyRimBoxes(worldSize);
                }
                else
                {
                    // Fallback: full-area box (use with caution; causes immediate overlap inside area)
                    DisableEdgeIfAny();
                    DisableRimBoxes();
                    ApplyCenterBox(worldSize);
                }
                _lastApplied = worldSize;
            }
        }

        float ResolveWorldSize()
        {
            if (followServerSize && NetworkClient.Instance != null)
            {
                float ws = NetworkClient.Instance.CurrentWorldSize;
                if (ws > 0f) return ws;
            }
            return fallbackWorldSize;
        }

        void ApplyCenterBox(float worldSize)
        {
            if (box2D == null) return;
            if (enforceOriginAndUnitScale)
            {
                box2D.offset = Vector2.zero;
            }
            box2D.size = new Vector2(worldSize, worldSize);
            box2D.isTrigger = true;
        }

        void ApplyEdgeSize(float worldSize)
        {
            if (edge2D == null) return;
            float half = worldSize * 0.5f;
            var pts = new List<Vector2>
            {
                new Vector2(-half, -half),
                new Vector2( half, -half),
                new Vector2( half,  half),
                new Vector2(-half,  half),
                new Vector2(-half, -half), // close loop
            };
            edge2D.SetPoints(pts);
            edge2D.isTrigger = true;
        }

        void ApplyRimBoxes(float worldSize)
        {
            EnsureRimBoxes();
            float half = worldSize * 0.5f;
            float t = Mathf.Max(0.001f, rimThickness);

            // Top & Bottom strips inside the boundary
            if (_rimTop != null)
            {
                _rimTop.size = new Vector2(worldSize, t);
                _rimTop.offset = Vector2.zero;
                _rimTop.transform.localPosition = new Vector3(0f, half - t * 0.5f, 0f);
                _rimTop.isTrigger = true;
            }
            if (_rimBottom != null)
            {
                _rimBottom.size = new Vector2(worldSize, t);
                _rimBottom.offset = Vector2.zero;
                _rimBottom.transform.localPosition = new Vector3(0f, -half + t * 0.5f, 0f);
                _rimBottom.isTrigger = true;
            }

            // Left & Right strips inside the boundary
            if (_rimLeft != null)
            {
                _rimLeft.size = new Vector2(t, worldSize);
                _rimLeft.offset = Vector2.zero;
                _rimLeft.transform.localPosition = new Vector3(-half + t * 0.5f, 0f, 0f);
                _rimLeft.isTrigger = true;
            }
            if (_rimRight != null)
            {
                _rimRight.size = new Vector2(t, worldSize);
                _rimRight.offset = Vector2.zero;
                _rimRight.transform.localPosition = new Vector3(half - t * 0.5f, 0f, 0f);
                _rimRight.isTrigger = true;
            }
        }

        void EnsureRimBoxes()
        {
            if (_rimTop != null && _rimBottom != null && _rimLeft != null && _rimRight != null)
            {
                // Ensure they are active if previously disabled
                _rimTop.gameObject.SetActive(true);
                _rimBottom.gameObject.SetActive(true);
                _rimLeft.gameObject.SetActive(true);
                _rimRight.gameObject.SetActive(true);
                return;
            }

            BoxCollider2D CreateChild(string name)
            {
                var go = new GameObject(name);
                go.transform.SetParent(this.transform, false);
                go.layer = this.gameObject.layer;
                go.tag = this.gameObject.tag;
                var bc = go.AddComponent<BoxCollider2D>();
                return bc;
            }

            if (_rimTop == null) _rimTop = CreateChild("RimTop");
            if (_rimBottom == null) _rimBottom = CreateChild("RimBottom");
            if (_rimLeft == null) _rimLeft = CreateChild("RimLeft");
            if (_rimRight == null) _rimRight = CreateChild("RimRight");
        }

        void DisableRimBoxes()
        {
            if (_rimTop != null) _rimTop.gameObject.SetActive(false);
            if (_rimBottom != null) _rimBottom.gameObject.SetActive(false);
            if (_rimLeft != null) _rimLeft.gameObject.SetActive(false);
            if (_rimRight != null) _rimRight.gameObject.SetActive(false);
        }

        void DisableCenterBoxIfAny()
        {
            if (box2D != null) box2D.enabled = false;
        }

        void DisableEdgeIfAny()
        {
            if (edge2D != null) edge2D.enabled = false;
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            if (box2D == null) TryGetComponent(out box2D);
            if (edge2D == null) TryGetComponent(out edge2D);
            if (enforceOriginAndUnitScale)
            {
                transform.position = Vector3.zero;
                transform.localScale = Vector3.one;
            }
        }
#endif
    }
}
