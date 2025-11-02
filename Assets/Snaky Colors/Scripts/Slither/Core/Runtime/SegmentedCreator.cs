using UnityEngine;
using UnityEditor;
using System.Collections.Generic; 
using System;
  
namespace SnakyColors
{
   // [AddComponentMenu("SV Assets/Procedural Creatures/Segmented Creator")]
    // [Icon("Assets/Procedural Creatures/Core/Editor/UI/Icons/segmented creature.png")] // Uncomment if you have the icon
    [ExecuteInEditMode]
    public class SegmentedCreator : MonoBehaviour
    {
        public SlitherPathType basePathAlgorithm = SlitherPathType.SlidingChain;
        public bool preview = true;
        public bool UIPath = true; // Not used in code, but part of your original
        public float prevScale; // Not used in code, but part of your original

        // database
        public List<Quaternion> RibRotations = new();
        public List<Vector3> RibPositions = new();
        public List<Vector3> MainPoints = new();

        // public List<Sprite> sprites; // No longer used as a class field, it's a local var
        public List<SpriteOverride> spriteOverrides = new();
        public float perSegmentDist = 0.87f;

        [Range(3, 100)]
        public int ribCount = 10;

        public MoveToTarget moveToTarget = new();
        public MeshDrawer meshDrawer = new();
        public Skin skin;

        public int mainNavIndex = 0; // editor ui header index
        public bool spritesOrderinverted = true;
        public int orderInLayer = 0;
        public Vector3 wobblingPoint;

        private bool lastPathWasBasePath1 = false;
        private bool mainPointsSet = false;
        private Vector3 lastTransformPos = Vector3.zero;

        // Use LateUpdate for follow logic to prevent jitter
        void LateUpdate()
        {
            UpdateShapeWithPath();
        }

        public void UpdateShapeWithPath(bool exclusiveRun = false)
        {
            if (moveToTarget.enableMoving && RibPositions.Count > 1 && moveToTarget.Target != null)
            {
                moveToTarget.MoveTransformToTarget(transform, (RibPositions.Count > 1) ? (RibPositions[^1] - RibPositions[^2]).normalized : transform.forward);
                wobblingPoint = moveToTarget.wobbleHeadPos;
            }

            if (!moveToTarget.enableMoving) wobblingPoint = transform.position;
            if (RibPositions.Count > 0)
            {
                RibPositions[^1] = wobblingPoint;
            }
            else
            {
                // Ensure list is initialized if empty
                if (RibPositions.Count == 0 && ribCount > 0)
                {
                    for (int i = 0; i < ribCount; i++) RibPositions.Add(transform.position);
                }
            }


            if (preview)
            {
                if (basePathAlgorithm == SlitherPathType.PenStroke)
                {
                    if (MainPoints.Count != RibPositions.Count || !mainPointsSet)
                    {
                        MainPoints = new List<Vector3>(RibPositions);
                        mainPointsSet = true;
                    }

                    bool hasAnyPoint = RibPositions.Count > 0;
                    bool hasEnoughDistance = !hasAnyPoint || (MainPoints.Count > 0 && Vector3.Distance(MainPoints[^1], wobblingPoint) >= perSegmentDist * transform.localScale.x);
                    bool transformMoved = transform.position != lastTransformPos;
                    bool pathTypeChanged = lastPathWasBasePath1 != (basePathAlgorithm == SlitherPathType.PenStroke);

                    if (hasEnoughDistance || transformMoved || pathTypeChanged || exclusiveRun)
                    {
                        PenStrokePath();
                    }
                }
                else
                {
                    if (mainPointsSet) mainPointsSet = false;

                    bool transformMoved = transform.position != lastTransformPos;
                    bool pathTypeChanged = lastPathWasBasePath1 != (basePathAlgorithm == SlitherPathType.SlidingChain);

                    if (transformMoved || pathTypeChanged || exclusiveRun) // Added exclusiveRun here too
                    {
                        SlidingChainPath();
                    }
                }

                DrawSpriteOnEachPointSegment();
            }

            if (!preview) ResetSnake(); // This will also call meshDrawer.Clear()

            lastTransformPos = transform.position;
            lastPathWasBasePath1 = basePathAlgorithm == SlitherPathType.PenStroke;
        }

        private void FlowPoints(List<Vector3> GivenPoints, List<Vector3> basePath, bool enable = true)
        {
            if (GivenPoints == null || GivenPoints.Count < 1 || basePath.Count < 2)
                return;

            if (enable)
            {
                float scaledDropDist = perSegmentDist * transform.localScale.x;
                float dist = Vector3.Distance(basePath[^1], wobblingPoint);
                float distPerc = Mathf.Clamp01(dist / scaledDropDist);

                for (int i = 0; i < basePath.Count - 1; i++)
                {
                    if (i < RibPositions.Count) // Safety check
                        RibPositions[i] = Vector3.Lerp(basePath[i], basePath[i + 1], distPerc);
                }

                RibRotations = GetRotations(RibPositions);
            }
        }

        private List<Quaternion> GetRotations(List<Vector3> GivenPoints)
        {
            if (GivenPoints == null || GivenPoints.Count < 1)
                return new();

            List<Quaternion> rots = new();
            if (GivenPoints.Count == 1)
            {
                rots.Add(transform.rotation); // Default rotation if only one point
            }

            for (int i = 0; i < GivenPoints.Count - 1; i++)
            {
                Vector3 direction = GivenPoints[i + 1] - GivenPoints[i];
                if (direction == Vector3.zero)
                {
                    // If no direction, use previous rotation or default
                    rots.Add(rots.Count > 0 ? rots[^1] : Quaternion.identity);
                }
                else
                {
                    Quaternion rot = Quaternion.LookRotation(transform.forward, direction);
                    if (skin != null) rot *= Quaternion.Euler(0, 0, skin.EachSpriteRotAngle);
                    rots.Add(rot);
                }
            }

            // Add a rotation for the last point
            if (rots.Count > 0)
                rots.Add(rots[^1]);
            else
                rots.Add(Quaternion.identity);


            return rots;
        }

        // === FULLY FIXED METHOD ===
        private void DrawSpriteOnEachPointSegment()
        {
            if (skin == null) return;
            if (skin.HeadSprite == null || skin.BodySprite == null || (skin.useTail && skin.TailSprite == null) || skin.mat == null) return;
            if (RibPositions.Count < 3 || RibRotations.Count < RibPositions.Count) return; // Wait for lists to be valid

            // 1. Get the BASE patterns from the new methods
            List<Sprite> bodyPattern = skin.GetSpritePattern();
            List<bool> flipPattern = skin.GetFlipPattern();

            // 2. Check if the patterns are valid (not null and not empty)
            bool hasValidBodyPattern = (bodyPattern != null && bodyPattern.Count > 0);
            bool hasValidFlipPattern = (flipPattern != null && flipPattern.Count > 0);

            int bodySegmentIndex = 0;
            int flipListIndex = 0;

            for (int i = RibPositions.Count - 1; i > 0; i--)
            {
                int LayersOffset = orderInLayer * RibPositions.Count;
                int currentOrderInLayer = spritesOrderinverted ? LayersOffset + (i - 1) : LayersOffset - (i - 1);
                currentOrderInLayer = Mathf.Clamp(currentOrderInLayer, -500, 500);

                int index = spriteOverrides.FindIndex(so => so.Position == i);
                if (index != -1)
                {
                    Transform obj = spriteOverrides[index].prefab;
                    if (obj != null && i < RibPositions.Count)
                    {
                        obj.SetPositionAndRotation(RibPositions[i], RibRotations[i - 1]);
                        obj.localScale = transform.localScale;
                    }
                    continue;
                }

                Matrix4x4 matrix = Matrix4x4.TRS(RibPositions[i], RibRotations[i - 1], transform.localScale);

                // --- Part 1: Draw the Head ---
                if (i == RibPositions.Count - 1)
                {
                    meshDrawer.DrawTextureAtMatix(skin.HeadSprite, matrix, currentOrderInLayer, skin.mat, RibPositions.Count - 1, false);
                }
                // --- Part 2: Draw the Tail ---
                else if (i == 1)
                {
                    Sprite tailSprite = skin.useTail ? skin.TailSprite : skin.BodySprite;

                    if (!skin.useTail && hasValidBodyPattern)
                    {
                        int lastBodySegmentIndex = bodySegmentIndex - 1;
                        if (lastBodySegmentIndex >= 0)
                        {
                            tailSprite = bodyPattern[lastBodySegmentIndex % bodyPattern.Count];
                        }
                    }

                    bool flipTail = hasValidFlipPattern ? flipPattern[flipListIndex % flipPattern.Count] : false;
                    meshDrawer.DrawTextureAtMatix(tailSprite, matrix, currentOrderInLayer, skin.mat, 1, flipTail);
                }
                // --- Part 3: Draw the Body ---
                else
                {
                    Sprite spriteToDraw = skin.BodySprite;
                    if (hasValidBodyPattern)
                    {
                        spriteToDraw = bodyPattern[bodySegmentIndex % bodyPattern.Count];
                    }

                    bool flipBody = hasValidFlipPattern ? flipPattern[flipListIndex % flipPattern.Count] : false;
                    meshDrawer.DrawTextureAtMatix(spriteToDraw, matrix, currentOrderInLayer, skin.mat, i, flipBody);

                    bodySegmentIndex++;
                }

                if (i != RibPositions.Count - 1) flipListIndex++;
            }
        }


        public void DrawLinesOnEachPointSegment(List<Vector3> GivenPoints, bool enable)
        {
            if (GivenPoints == null || GivenPoints.Count < 2) return;

            if (enable)
            {
                for (int i = 0; i < GivenPoints.Count - 1; i++)
                {
#if UNITY_EDITOR
                    Handles.DrawLine(GivenPoints[i], GivenPoints[i + 1]);
#endif
                }
            }
        }

        private void PenStrokePath()
        {
            if (ribCount != RibPositions.Count)
            {
                UpdateRibListSize();
            }

            if (RibPositions.Count > 0 && MainPoints.Count > 0)
            {
                Vector3 lastMainPointsDist = wobblingPoint - MainPoints[^1];
                float dist = lastMainPointsDist.magnitude;
                float scaledDropDist = perSegmentDist * transform.localScale.x;

                if (dist > scaledDropDist)
                {
                    int steps = Mathf.FloorToInt(dist / scaledDropDist);
                    for (int i = 1; i <= steps; i++)
                    {
                        Vector3 newPoint = MainPoints[^1] + lastMainPointsDist.normalized * scaledDropDist;
                        MainPoints.Add(newPoint);
                    }
                }

                if (MainPoints.Count > ribCount)
                {
                    MainPoints.RemoveRange(0, MainPoints.Count - ribCount);
                }
            }

            FlowPoints(RibPositions, MainPoints);
        }

        private void SlidingChainPath()
        {
            if (ribCount != RibPositions.Count)
            {
                UpdateRibListSize();
            }

            // Maintain path constraints
            for (int i = RibPositions.Count - 2; i >= 0; i--)
            {
                if (i + 1 >= RibPositions.Count) continue; // Safety check

                Vector3 dir = RibPositions[i] - RibPositions[i + 1];
                float dist = dir.magnitude;
                float scaledDist = perSegmentDist * transform.localScale.x;

                if (dist > scaledDist && dist > 0.001f) // Added small threshold
                {
                    RibPositions[i] = RibPositions[i + 1] + dir.normalized * scaledDist;
                }
            }

            RibRotations = GetRotations(RibPositions);
        }

        private void UpdateRibListSize()
        {
            int diff = ribCount - RibPositions.Count;

            if (diff > 0) // Need to add points
            {
                Vector3 pointToAdd = (RibPositions.Count == 0) ? transform.position : RibPositions[0];
                for (int i = 0; i < diff; i++)
                {
                    RibPositions.Insert(0, pointToAdd);
                    if (basePathAlgorithm == SlitherPathType.PenStroke)
                    {
                        MainPoints.Insert(0, pointToAdd);
                    }
                }
            }
            else if (diff < 0) // Need to remove points
            {
                RibPositions.RemoveRange(0, -diff);
                if (basePathAlgorithm == SlitherPathType.PenStroke)
                {
                    MainPoints.RemoveRange(0, -diff);
                }
            }

            RefreshSprites();
        }

        #region  Cleanup
        public void RefreshSprites()
        {
            meshDrawer.Clear(); // FIXED: Call Clear to destroy old assets
            UpdateShapeWithPath(true);
#if UNITY_EDITOR
            SceneView.RepaintAll();
#endif
        }

        public void ResetSnake()
        {
            meshDrawer.Clear(); // FIXED: Call Clear to destroy old assets
            RibPositions?.Clear();
            RibRotations?.Clear();
            MainPoints?.Clear();

            if (preview)
            {
                RefreshSprites();
            }
        }

        public void OnDestroy()
        {
            ResetSnake();
        }

        #endregion
    } 
}