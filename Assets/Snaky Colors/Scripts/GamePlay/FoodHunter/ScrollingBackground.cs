using UnityEngine;
using System.Linq; // Needed for OrderBy (optional, but useful)


namespace SnakyColors
{
    using UnityEngine;
    using System.Linq; // Needed for OrderBy (optional)

    public class ScrollingBackground : MonoBehaviour
    {
        // === CONFIGURATION ===
        public GameObject tilePrefab;
        // We need 3 tiles minimum: one below the screen, one on the screen, and one above the screen.
        public int initialTileCount = 3;

        // === PRIVATE STATE ===
        private GameObject[] tiles;
        private float tileHeight;
        private Transform mainCameraTransform;

        void Start()
        {
            mainCameraTransform = Camera.main.transform;

            if (tilePrefab == null)
            {
                Debug.LogError("Tile Prefab is not assigned.");
                return;
            }

            // Calculate tile height from the SpriteRenderer's bounds
            SpriteRenderer sr = tilePrefab.GetComponent<SpriteRenderer>();
            if (sr == null)
            {
                Debug.LogError("Tile Prefab must have a SpriteRenderer component.");
                return;
            }
            tileHeight = sr.bounds.size.y;

            InitializeTiles();
        }

        private void InitializeTiles()
        {
            tiles = new GameObject[initialTileCount];

            // --- CORRECTED INITIALIZATION LOGIC ---
            // Start tiling from the camera's current Y position and stack them down.
            // We ensure the first tile is centered below the camera.
            float startY = mainCameraTransform.position.y - (tileHeight * 0.5f);

            for (int i = 0; i < initialTileCount; i++)
            {
                // Position: 
                // 0: below camera
                // 1: centered on camera
                // 2: above camera
                Vector3 pos = new Vector3(0, startY + (i - 1) * tileHeight, 0);
                tiles[i] = Instantiate(tilePrefab, pos, Quaternion.identity, transform);
            }
        }

        void LateUpdate()
        {
            if (tiles == null || tiles.Length == 0) return;

            // --- EFFICIENT MANUAL LOOP TO FIND LOWEST AND HIGHEST TILES ---
            GameObject lowestTile = tiles[0];
            GameObject highestTile = tiles[0];
            float minPosY = tiles[0].transform.position.y;
            float maxPosY = tiles[0].transform.position.y;

            // Loop through all tiles to find the actual min/max Y positions
            for (int i = 1; i < tiles.Length; i++)
            {
                float currentY = tiles[i].transform.position.y;

                if (currentY < minPosY)
                {
                    minPosY = currentY;
                    lowestTile = tiles[i];
                }
                else if (currentY > maxPosY)
                {
                    maxPosY = currentY;
                    highestTile = tiles[i];
                }
            }

            // --- REPOSITION LOGIC ---
            float cameraBottomEdge = mainCameraTransform.position.y - Camera.main.orthographicSize;

            // If the lowest tile has scrolled entirely past the bottom edge of the camera view...
            if (lowestTile.transform.position.y + tileHeight < cameraBottomEdge)
            {
                // Reposition the lowest tile to immediately above the highest tile.
                lowestTile.transform.position = highestTile.transform.position + Vector3.up * tileHeight;
            }
        }
    }
}
