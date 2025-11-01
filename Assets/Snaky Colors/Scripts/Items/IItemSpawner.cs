using UnityEngine;

namespace SnakyColors
{
    // This interface ensures any spawner class has the OnItemDespawned method.
    public interface IItemSpawner
    {
        void OnItemDespawned(GameObject obj, ItemData item);
    }
}