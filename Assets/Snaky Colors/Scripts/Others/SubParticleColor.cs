using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SnakyColors
{
    public class SubParticleColor : MonoBehaviour
    {
        //There are larger obstacles that contain multiple game objects with the particle system. This script is attached on sub particles to match the color of the main particle
        void Start()
        {
            ParticleSystem.MainModule psmain = GetComponent<ParticleSystem>().main;
            psmain.startColor = transform.parent.GetComponent<ParticleSystem>().main.startColor;
        }
    }
}
