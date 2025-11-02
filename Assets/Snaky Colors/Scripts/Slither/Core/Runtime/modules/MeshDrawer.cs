using System.Collections.Generic;
using UnityEngine;

namespace SnakyColors
{
    public class MeshDrawer
    {
        public Dictionary<int, Mesh> meshForIndex = new();
        public Dictionary<int, Material> matForindex = new();

        public void DrawTextureAtMatix(Sprite sprite, Matrix4x4 matrix, int orderInLayer, Material baseMat, int index, bool flipY = false)
        {
            if (sprite == null) return;

            if (!meshForIndex.TryGetValue(index, out Mesh mesh))
            {
                mesh = CreateMeshForTexture(sprite, false, flipY); // Pass flipX as false
                meshForIndex[index] = mesh;
            }

            if (!matForindex.TryGetValue(index, out Material mat))
            {
                mat = new Material(baseMat);
                matForindex[index] = mat;
            }

            mat.mainTexture = sprite.texture;
            SetSegmentSorting(ref mat, orderInLayer);

            Graphics.RenderMesh(new RenderParams(mat), mesh, 0, matrix);
        }

        public void SetSegmentSorting(ref Material baseMat, int orderInLayer)
        {
            baseMat.renderQueue = 3000 + orderInLayer;
        }

        // === FIXED METHOD (Handles Sprite Atlases / UVs) ===
        Mesh CreateMeshForTexture(Sprite sprite, bool flipX, bool flipY)
        {
            Mesh mesh = new();

            // --- 1. Vertex Positions (using pixelsPerUnit) ---
            float ppu = sprite.pixelsPerUnit;
            float width = sprite.rect.width / ppu;
            float height = sprite.rect.height / ppu;

            // Pivot offset in world units
            float pivotX = sprite.pivot.x / ppu;
            float pivotY = sprite.pivot.y / ppu;

            Vector3[] vertices = new Vector3[4];
            vertices[0] = new Vector3(0 - pivotX, 0 - pivotY, 0);       // Bottom-Left
            vertices[1] = new Vector3(width - pivotX, 0 - pivotY, 0);  // Bottom-Right
            vertices[2] = new Vector3(width - pivotX, height - pivotY, 0); // Top-Right
            vertices[3] = new Vector3(0 - pivotX, height - pivotY, 0);  // Top-Left

            // Adjusted triangle winding order
            int[] triangles = new int[] { 0, 3, 2, 2, 1, 0 };

            // --- 2. UV Coordinates (for Sprite Atlas) ---
            Rect r = sprite.textureRect;
            float texWidth = sprite.texture.width;
            float texHeight = sprite.texture.height;

            float uvMinX = r.xMin / texWidth;
            float uvMinY = r.yMin / texHeight;
            float uvMaxX = r.xMax / texWidth;
            float uvMaxY = r.yMax / texHeight;

            Vector2[] uv = new Vector2[4];
            uv[0] = new Vector2(flipX ? uvMaxX : uvMinX, flipY ? uvMaxY : uvMinY); // BL
            uv[1] = new Vector2(flipX ? uvMinX : uvMaxX, flipY ? uvMaxY : uvMinY); // BR
            uv[2] = new Vector2(flipX ? uvMinX : uvMaxX, flipY ? uvMinY : uvMaxY); // TR
            uv[3] = new Vector2(flipX ? uvMaxX : uvMinX, flipY ? uvMinY : uvMaxY); // TL

            // --- 3. Assign to Mesh ---
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.uv = uv;
            mesh.RecalculateNormals();

            return mesh;
        }

        // === NEW METHOD (Fixes Memory Leak) ===
        public void Clear()
        {
            foreach (var pair in matForindex)
            {
                if (pair.Value != null)
                {
                    if (Application.isPlaying)
                        Object.Destroy(pair.Value);
                    else
                        Object.DestroyImmediate(pair.Value);
                }
            }

            foreach (var pair in meshForIndex)
            {
                if (pair.Value != null)
                {
                    if (Application.isPlaying)
                        Object.Destroy(pair.Value);
                    else
                        Object.DestroyImmediate(pair.Value);
                }
            }

            matForindex.Clear();
            meshForIndex.Clear();
        }
    }
}