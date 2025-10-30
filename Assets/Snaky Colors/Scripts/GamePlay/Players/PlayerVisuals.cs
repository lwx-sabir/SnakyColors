using UnityEngine;
using System.Collections; // Required for Coroutines

namespace SnakyColors
{
    public class PlayerVisuals : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private SpriteRenderer sr;
        [SerializeField] private TrailRenderer mainTrailRenderer; // Assign in Inspector
        [SerializeField] private CircleCollider2D playerCollider; // Assign in Inspector
        [SerializeField] private PlayerMovement playerMovement; // Assign in Inspector

        [Header("Visual Setup")]
        [Tooltip("Texture to apply to the main trail's existing material.")]
        public Texture2D trailTexture; // Assign the trail texture in Inspector
        public Sprite[] sprite; // Head sprites
        public float headScaleFactor = 0.4f;
        public float trailerWidthFactorToLose = 0.85f;

        [Header("Audio Setup")]
        [SerializeField] public AudioClip basicHitClip;

        [Header("Color Change")]
        public float colorChangeInterval = 30f;
        // Assuming ColorLib exists and is accessible

        [Header("Depth Trail")]
        [Tooltip("Shader for the depth trail. Consider creating a Material asset.")]
        public Shader depthTrailShader; // Assign "Legacy Shaders/Particles/Anim Alpha Blended" or similar

        // Private variables
        private float colorChangeTimer = 0f;
        private TrailRenderer depthTrailRenderer; // Reference to the created depth trail

        private void Start()
        {
            if (sr == null) sr = GetComponent<SpriteRenderer>();
            if (playerCollider == null) playerCollider = GetComponent<CircleCollider2D>();
            if (playerMovement == null) playerMovement = GetComponent<PlayerMovement>();

            if (mainTrailRenderer == null)
            {
                Debug.LogError("Main Trail Renderer is not assigned!", this);
                return; // Stop if trail is missing
            }
            mainTrailRenderer.material = new Material(mainTrailRenderer.material);


            sr.color = Color.white;
            mainTrailRenderer.sortingLayerName = "Default";
            mainTrailRenderer.sortingOrder = 100;

            LoadPlayerTexture();
             
            // Only set the texture at runtime.
            if (trailTexture != null && mainTrailRenderer.material != null)
            {
                mainTrailRenderer.material.mainTexture = trailTexture;
            }
            else if (mainTrailRenderer.material == null)
            {
                Debug.LogError("Main Trail Renderer is missing its Material!", this);
            } 
             
            mainTrailRenderer.textureMode = LineTextureMode.Tile;
            // Check if material exists before accessing it
            if (mainTrailRenderer.material != null)
                mainTrailRenderer.material.mainTextureScale = new Vector2(1, 1);
            mainTrailRenderer.numCapVertices = 3;
            mainTrailRenderer.numCornerVertices = 1;
            mainTrailRenderer.startColor = ColorLib.GreenSea; // Assuming ColorLib exists
            mainTrailRenderer.endColor = ColorLib.GreenSea;

            CreateDepthTrail(); // Depth trail still creates its own material
        }
         
        private void Update()
        {
            colorChangeTimer += Time.deltaTime;
            if (colorChangeTimer >= colorChangeInterval)
            {
                colorChangeTimer = 0f;
                // Ensure ColorLib exists and has colors
                if (ColorLib.Colors != null && ColorLib.Colors.Length > 0)
                {
                    mainTrailRenderer.startColor = ColorLib.Colors[Random.Range(0, ColorLib.Colors.Length)];
                    // Optional: Sync depth trail color or keep it dark
                    // if (depthTrailRenderer != null) { /* Update depth color? */ }
                }
            }
        }

        void LoadPlayerTexture()
        {
            if (sr == null || sprite == null || sprite.Length == 0) return;

            int chosenItemIndex = PlayerPrefs.GetInt("ChoosenItem", 0);
            chosenItemIndex = Mathf.Clamp(chosenItemIndex, 0, sprite.Length);

            if (chosenItemIndex == 0)
            {
                sr.sprite = sprite[0];
            }
            else
            {
                int adjustedIndex = Mathf.Clamp(chosenItemIndex - 1, 0, sprite.Length - 1);
                sr.sprite = sprite[adjustedIndex];
            }

            if (sr.sprite == null) return;

            float spriteWidth = sr.sprite.bounds.size.x;
            if (spriteWidth <= 0) return;
            float baseSize = 1f / spriteWidth;
            transform.localScale = Vector3.one * baseSize * headScaleFactor;

            float headWidth = spriteWidth * transform.localScale.x;

            if (mainTrailRenderer != null)
            {
                mainTrailRenderer.startWidth = headWidth * trailerWidthFactorToLose;
                mainTrailRenderer.endWidth = headWidth * trailerWidthFactorToLose;
            }

            if (playerCollider != null)
            {
                playerCollider.radius = (sr.sprite.bounds.extents.x) * 0.75f;
            }
        }

        private void CreateDepthTrail()
        {
            if (mainTrailRenderer == null) return;

            GameObject depthTrailObj = Instantiate(mainTrailRenderer.gameObject, mainTrailRenderer.transform.parent);
            depthTrailObj.name = "DepthTrail";
            depthTrailRenderer = depthTrailObj.GetComponent<TrailRenderer>();

            // Ensure the material instance is unique for the depth trail
            if (depthTrailRenderer.material != null)
            {
                depthTrailRenderer.material = new Material(depthTrailRenderer.material);
            }


            depthTrailRenderer.time = mainTrailRenderer.time * 0.8f;
            depthTrailRenderer.widthCurve = mainTrailRenderer.widthCurve;
            depthTrailRenderer.widthMultiplier = mainTrailRenderer.widthMultiplier * 0.98f;
            depthTrailRenderer.numCapVertices = mainTrailRenderer.numCapVertices;
            depthTrailRenderer.numCornerVertices = mainTrailRenderer.numCornerVertices;
            depthTrailRenderer.textureMode = mainTrailRenderer.textureMode;

            // --- Material Setup
            if (depthTrailShader != null)
            {
                depthTrailRenderer.material = new Material(depthTrailShader);
                // Copy texture from main trail if needed, or leave blank/default
                if (mainTrailRenderer.material != null)
                {
                    depthTrailRenderer.material.mainTexture = mainTrailRenderer.material.mainTexture;
                    depthTrailRenderer.material.mainTextureScale = mainTrailRenderer.material.mainTextureScale;
                }

            }
            else
            {
                Debug.LogWarning("Depth Trail Shader not assigned, attempting fallback.", this); 
                var existingMat = depthTrailRenderer.material; // Get whatever was duplicated
                if (existingMat != null)
                {
                    depthTrailRenderer.material = new Material(existingMat); // Make it unique
                }
                else
                { 
                    depthTrailRenderer.material = new Material(Shader.Find("Sprites/Default"));
                }

            }

            depthTrailRenderer.startColor = new Color(0, 0, 0, 0.15f);
            depthTrailRenderer.endColor = new Color(0, 0, 0, 0f);
            depthTrailRenderer.transform.localPosition = new Vector3(0, -0.05f, 0.1f);

            depthTrailRenderer.sortingLayerName = mainTrailRenderer.sortingLayerName;
            depthTrailRenderer.sortingOrder = mainTrailRenderer.sortingOrder - 1;
        } 

        public void Disable()
        {
            gameObject.SetActive(false);
        }
    }
}