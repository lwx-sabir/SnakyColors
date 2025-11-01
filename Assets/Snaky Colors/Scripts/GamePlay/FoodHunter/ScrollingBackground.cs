using UnityEngine;
using System.Collections.Generic;

namespace SnakyColors
{
    [System.Serializable]
    public class ParallaxLayer
    {
        public GameObject tilePrefab; 
        [Range(0f, 1f)] public float parallaxFactorX = 0f;
        [Range(0f, 1f)] public float parallaxFactorY = 1f;
        [Range(0f, 1f)] public float smoothing = 0.1f;

        [HideInInspector] public List<GameObject> tiles = new List<GameObject>();
        [HideInInspector] public float tileWidth;
        [HideInInspector] public float tileHeight;
    }

    public class ScrollingBackground : MonoBehaviour
    {
        public ParallaxLayer[] layers;
        public Transform playerTransform;
        private Vector3 lastPlayerPos;

        void Start()
        {
            if (playerTransform == null)
            {
                var player = FindObjectOfType<PlayerMovement>();
                if (player != null) playerTransform = player.transform;
            }

            if (playerTransform != null)
                lastPlayerPos = playerTransform.position;

            foreach (var layer in layers)
            {
                if (layer.tilePrefab == null) continue;

                var sr = layer.tilePrefab.GetComponent<SpriteRenderer>();
                if (sr == null)
                {
                    Debug.LogError("Tile prefab must have SpriteRenderer!");
                    continue;
                }

                layer.tileWidth = sr.bounds.size.x;
                layer.tileHeight = sr.bounds.size.y;

                // Spawn 3 tiles vertically
                for (int i = -1; i <= 1; i++)
                {
                    Vector3 pos = new Vector3(0, i * layer.tileHeight, 0);
                    var tile = Instantiate(layer.tilePrefab, pos, Quaternion.identity, transform);
                    layer.tiles.Add(tile);
                }
            }
        }

        void LateUpdate()
        {
            if (playerTransform == null) return;

            Vector3 delta = playerTransform.position - lastPlayerPos;
            lastPlayerPos = playerTransform.position;

            foreach (var layer in layers)
            {
                Vector3 move = new Vector3(delta.x * layer.parallaxFactorX, delta.y * layer.parallaxFactorY, 0);
                for (int i = 0; i < layer.tiles.Count; i++)
                {
                    GameObject tile = layer.tiles[i];
                    tile.transform.position = Vector3.Lerp(tile.transform.position, tile.transform.position + move, layer.smoothing);


                    // Recycle tile if it goes below camera
                    if (tile.transform.position.y + layer.tileHeight < Camera.main.transform.position.y - Camera.main.orthographicSize)
                    {
                        GameObject highest = layer.tiles[0];
                        float maxY = highest.transform.position.y;
                        for (int j = 1; j < layer.tiles.Count; j++)
                        {
                            if (layer.tiles[j].transform.position.y > maxY)
                            {
                                highest = layer.tiles[j];
                                maxY = highest.transform.position.y;
                            }
                        }

                        tile.transform.position = highest.transform.position + new Vector3(0, layer.tileHeight, 0);
                    }
                }
            }
        }
    }
} 