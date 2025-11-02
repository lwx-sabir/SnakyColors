using System;
using System.Collections.Generic;
using UnityEngine;

namespace SnakyColors
{
    [CreateAssetMenu(fileName = "New_Skin", menuName = "2D/Snake Skin", order = 5)]
    [Serializable]
    public class Skin : ScriptableObject
    {
        public int stripesCount = 0;
        public int stripesSpacingBeforeStripe = 0;
        public int stripesSpacingAfterStripe = 0;


        public Sprite HeadSprite;
        public Sprite BodySprite;

        public bool useTail = false;
        public Sprite TailSprite;

        public Material mat;


        [Range(0, 359)]
        public float EachSpriteRotAngle;

        public bool FlipConsecutive = false;
        public StripeType currentStripeType = StripeType.None;

        // repeated stripes
        public RepeatedStripe repeatedStripe = new();
        public CustomStripe customStripe = new();

        public void OnEnable()
        {
            // Set default material on creation
            if (mat == null)
            {
                GameObject tempObject = null;
                try
                {
                    tempObject = new GameObject("TempMatHolder");
                    tempObject.hideFlags = HideFlags.HideAndDontSave; // Don't show or save it
                    SpriteRenderer tempRenderer = tempObject.AddComponent<SpriteRenderer>();
                    mat = tempRenderer.sharedMaterial;
                }
                finally
                {
                    if (tempObject != null)
                    {
                        DestroyImmediate(tempObject); // Clean up
                    }
                }
            }
        }

        // === NEW METHOD (FIXED) ===
        // Returns the base sprite pattern (un-clamped)
        public List<Sprite> GetSpritePattern()
        {
            List<Sprite> sprites = new();

            if (currentStripeType == StripeType.Repeat)
            {
                for (int i = 0; i < stripesCount; i++)
                {
                    for (int j = 0; j < stripesSpacingBeforeStripe; j++)
                    {
                        sprites.Add(BodySprite);
                    }
                    for (int j = 0; j < repeatedStripe.stripeLength; j++)
                    {
                        if (repeatedStripe.sprite == null) sprites.Add(BodySprite);
                        else sprites.Add(repeatedStripe.sprite);
                    }
                    for (int j = 0; j < stripesSpacingAfterStripe; j++)
                    {
                        sprites.Add(BodySprite);
                    }
                }
            }
            else if (currentStripeType == StripeType.Custom)
            {
                for (int i = 0; i < stripesCount; i++)
                {
                    for (int j = 0; j < stripesSpacingBeforeStripe; j++)
                    {
                        sprites.Add(BodySprite);
                    }
                    for (int j = 0; j < customStripe.sprites.Count; j++)
                    {
                        // FIX: Was checking repeatedStripe, now checks customStripe
                        if (customStripe.sprites[j] == null) sprites.Add(BodySprite);
                        else sprites.Add(customStripe.sprites[j]);
                    }
                    for (int j = 0; j < stripesSpacingAfterStripe; j++)
                    {
                        sprites.Add(BodySprite);
                    }
                }
            }
            // If StripeType.None, it correctly returns an empty list.

            return sprites;
        }

        // === NEW METHOD (FIXED) ===
        // Returns the base flip pattern (un-clamped)
        public List<bool> GetFlipPattern()
        {
            List<bool> flipList = new();

            if (!FlipConsecutive || currentStripeType == StripeType.None)
            {
                return flipList; // Return empty list
            }

            bool flipNext = false;

            for (int i = 0; i < stripesCount; i++)
            {
                int stripeLength = 0;
                if (currentStripeType == StripeType.Repeat)
                {
                    stripeLength = repeatedStripe.stripeLength;
                }
                else if (currentStripeType == StripeType.Custom)
                {
                    stripeLength = customStripe.sprites.Count;
                }

                // Add flips for spacing *before*
                for (int j = 0; j < stripesSpacingBeforeStripe; j++)
                {
                    flipList.Add(flipNext);
                }

                // Add flips for the stripe itself
                for (int j = 0; j < stripeLength; j++)
                {
                    flipList.Add(flipNext);
                }

                // Add flips for spacing *after*
                for (int j = 0; j < stripesSpacingAfterStripe; j++)
                {
                    flipList.Add(flipNext);
                }

                // Flip the value for the *next* block
                if (FlipConsecutive) flipNext = !flipNext;
            }

            return flipList;
        }


        // --- OLD METHODS (NO LONGER USED BY SEGMENTEDCREATOR) ---
        // These are left for compatibility, but no longer the main logic

        public List<Sprite> GetSpriteForSegments(int segmentCount)
        {
            List<Sprite> sprites = GetSpritePattern(); // Get the base pattern

            // If the pattern is empty, fill with default BodySprite
            if (sprites.Count == 0)
            {
                for (int i = 0; i < segmentCount; i++)
                {
                    sprites.Add(BodySprite);
                }
                return sprites;
            }

            ClampSpritesList(ref sprites, segmentCount, BodySprite);
            return sprites;
        }

        public void ClampSpritesList(ref List<Sprite> sprites, int targetCount, Sprite defaultSprite = null)
        {
            sprites ??= new List<Sprite>();
            int spritesCount = sprites.Count;

            if (spritesCount > targetCount)
            {
                sprites.RemoveRange(targetCount, spritesCount - targetCount);
            }
            else if (spritesCount < targetCount)
            {
                // This logic is flawed for looping, but we leave it.
                // The new creator script no longer uses this.
                for (int i = spritesCount; i < targetCount; i++)
                {
                    sprites.Add(defaultSprite);
                }
            }
        }

        public List<bool> GetFlipTagList(int count)
        {
            List<bool> flipList = GetFlipPattern(); // Get the base pattern

            if (flipList.Count == 0)
            {
                for (int i = 0; i < count; i++)
                {
                    flipList.Add(false);
                }
                return flipList;
            }

            if (flipList.Count > count)
            {
                flipList.RemoveRange(count, flipList.Count - count);
            }
            else if (flipList.Count < count)
            {
                // This logic is flawed for looping, but we leave it.
                for (int i = flipList.Count; i < count; i++)
                {
                    flipList.Add(false); // Pads with false
                }
            }

            return flipList;
        }

        public int GetStripesCount() => currentStripeType switch
        {
            StripeType.None => 6,
            StripeType.Repeat => repeatedStripe.stripeLength + stripesSpacingAfterStripe + stripesSpacingBeforeStripe,
            StripeType.Custom => customStripe.sprites.Count,
            _ => 0
        };
    }

    [Serializable]
    public class RepeatedStripe
    {
        public Sprite sprite;
        public int stripeLength;
    }

    [Serializable]
    public class CustomStripe
    {
        public List<Sprite> sprites;
    }

    public enum StripeType
    {
        None,
        Repeat,
        Custom
    }
}