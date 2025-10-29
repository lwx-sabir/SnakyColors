using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SnakyColors
{
    public class DestroyExplosionParticle : MonoBehaviour
    {
        [SerializeField] private float time = 1f;

        public void Init(float t)
        {
            time = t;
            Destroy(gameObject, time);
        }

        public float GetTime() => time;
    } 
}
