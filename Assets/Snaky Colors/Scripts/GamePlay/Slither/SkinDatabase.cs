using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace SnakyColors // Or your 'SVassets' namespace
{
    [CreateAssetMenu(fileName = "SkinDatabase", menuName = "Game Data/Skin Database")]
    public class SkinDatabase : ScriptableObject
    {
        public List<Skin> allSkins;

        // A dictionary for fast, O(1) lookups by string ID
        private Dictionary<string, Skin> skinDictionary;

        public void Initialize()
        {
            skinDictionary = new Dictionary<string, Skin>();
            foreach (Skin skin in allSkins)
            {
                if (skin == null) continue;

                // Use the ScriptableObject's name as its unique ID
                if (!skinDictionary.ContainsKey(skin.name))
                {
                    skinDictionary.Add(skin.name, skin);
                }
            }
        }

        /// <summary>
        /// Gets a Skin asset from the database by its string ID (its filename).
        /// </summary>
        public Skin GetSkinByID(string skinID)
        {
            if (skinDictionary == null) Initialize(); // Failsafe

            if (string.IsNullOrEmpty(skinID) || !skinDictionary.TryGetValue(skinID, out Skin skin))
            {
                Debug.LogWarning($"Could not find skin with ID: {skinID}. Returning default.");
                return allSkins[0]; // Return the first skin as a default
            }
            return skin;
        }
    }
}