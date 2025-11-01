using UnityEngine;

namespace SnakyColors
{
    public class ParallaxControl : MonoBehaviour
    {
        [System.Serializable]
        public class ParallaxLayer
        {
            [Tooltip("The SpriteRenderer for this layer.")]
            public SpriteRenderer spriteRenderer;

            [Tooltip("Vertical parallax factor. 1 = matches camera exactly.")]
            [Range(0f, 1f)]
            public float parallaxFactorY = 1f;

            [Tooltip("Horizontal parallax factor.")]
            [Range(0f, 1f)]
            public float parallaxFactorX = 0f;

            [Tooltip("Smoothing factor (0 = instant).")]
            [Range(0f, 0.99f)]
            public float smoothing = 0f;

            [HideInInspector] public Material materialInstance;
            [HideInInspector] public float spriteHeight;
            [HideInInspector] public float spriteWidth;
            [HideInInspector] public bool initialized = false;
        }

        [Header("Layers")]
        public ParallaxLayer[] parallaxLayers;

        private Transform cameraTransform;
        private Vector3 lastCameraPos;
        private bool isInitialized = false;

        void Start()
        {
            cameraTransform = Camera.main.transform;
            lastCameraPos = cameraTransform.position;

            InitializeLayers();
        }

        [ContextMenu("Reinitialize Layers")]
        void InitializeLayers()
        {
            if (isInitialized) return;

            foreach (var layer in parallaxLayers)
            {
                if (layer.spriteRenderer == null)
                {
                    Debug.LogError("Parallax layer missing SpriteRenderer!", this);
                    continue;
                }

                layer.materialInstance = layer.spriteRenderer.material;
                if (layer.materialInstance == null)
                {
                    Debug.LogError($"Layer {layer.spriteRenderer.name} has no material assigned!", this);
                    continue;
                }

                if (layer.materialInstance.mainTexture != null &&
                    layer.materialInstance.mainTexture.wrapMode != TextureWrapMode.Repeat)
                {
                    Debug.LogError($"Texture wrap mode must be 'Repeat' for {layer.spriteRenderer.name}!", layer.materialInstance.mainTexture);
                }

                layer.spriteHeight = layer.spriteRenderer.bounds.size.y;
                layer.spriteWidth = layer.spriteRenderer.bounds.size.x;

                layer.initialized = true;
            }

            isInitialized = true;
        }

        void LateUpdate()
        {
            if (!isInitialized) return;

            Vector3 cameraDelta = cameraTransform.position - lastCameraPos;
            lastCameraPos = cameraTransform.position;

            if (cameraDelta.sqrMagnitude < 0.0001f) return;

            foreach (var layer in parallaxLayers)
            {
                if (!layer.initialized || layer.materialInstance == null) continue;

                float offsetY = (cameraDelta.y * layer.parallaxFactorY) / layer.spriteHeight;
                float offsetX = (cameraDelta.x * layer.parallaxFactorX) / layer.spriteWidth;

                Vector2 targetOffset = layer.materialInstance.mainTextureOffset + new Vector2(offsetX, offsetY);

                // Apply smoothing
                float smoothFactor = 1f - layer.smoothing;
                Vector2 smoothedOffset = Vector2.Lerp(layer.materialInstance.mainTextureOffset, targetOffset, smoothFactor * Time.deltaTime * 10f);

                if (layer.smoothing <= 0.01f)
                    smoothedOffset = targetOffset;

                layer.materialInstance.SetTextureOffset("_MainTex", smoothedOffset);
            }
        }
    }
}
