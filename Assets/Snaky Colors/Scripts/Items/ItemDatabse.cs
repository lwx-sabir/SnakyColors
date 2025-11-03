using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace SnakyColors
{
    [CreateAssetMenu(fileName = "ItemDatabase", menuName = "Game Data/Item Database")]
    public class ItemDatabase : ScriptableObject
    {
        [Tooltip("Assign ALL ItemData assets (food, powerups, etc.) here.")]
        public List<ItemData> allItems;

        private Dictionary<string, ItemData> itemDictionary;

        public void Initialize()
        {
            itemDictionary = new Dictionary<string, ItemData>();
            foreach (ItemData item in allItems)
            {
                if (item == null) continue;

                // Use the ScriptableObject's name as its unique ID
                if (!itemDictionary.ContainsKey(item.name))
                {
                    itemDictionary.Add(item.name, item);
                }
            }
        }

        /// <summary>
        /// Gets an ItemData asset from the database by its string key (its filename).
        /// </summary>
        public ItemData GetItemByKey(string itemKey)
        {
            if (itemDictionary == null) Initialize();

            if (string.IsNullOrEmpty(itemKey) || !itemDictionary.TryGetValue(itemKey, out ItemData item))
            {
                Debug.LogWarning($"Could not find ItemData with key: {itemKey}. Returning default.");
                // Return the first item as a failsafe
                return (allItems != null && allItems.Count > 0) ? allItems[0] : null;
            }
            return item;
        }
    }
}