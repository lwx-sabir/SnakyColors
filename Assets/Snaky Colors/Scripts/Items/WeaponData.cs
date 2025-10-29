using UnityEngine;

namespace SnakyColors
{
    [CreateAssetMenu(fileName = "NewWeaponData", menuName = "Game Data/Weapon Data")]
    public class WeaponData : ScriptableObject
    {
        [Header("Setup")] 
        public string weaponName;
        [Tooltip("If any visual need to attach to the player/player head")]
        public GameObject weaponPrefab;
        public GameObject projectilePrefab;
        public Sprite icon;

        [Header("Stats")]
        [Min(0.01f)]
        public float fireRate = 0.5f; // Time between shots
        public float projectileSpeed = 20f;
        public float damage = 10f;
        public float lifetime = 2f;
        public bool autoFire = true; // Does this weapon auto-aim and fire?

        [Header("Feedback")]
        public GameObject muzzleEffect;
        public GameObject impactEffect;
        public AudioClip fireSound;
    }
} 