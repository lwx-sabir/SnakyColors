using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SnakyColors
{
    public class RotateTheObstacle : MonoBehaviour
    {
        //Used to rotate an obstacle on which this script is attached
        [SerializeField]
        private float rotationSpeed = 0;

        void Start()
        {
            if (rotationSpeed == 0)//If rotation speed is not set in the inspector than set the random rotation speed
                rotationSpeed = Random.Range(50, 100);
        }
        void Update()
        {
            transform.rotation = Quaternion.Euler(0, 0, transform.eulerAngles.z + rotationSpeed * Time.deltaTime);
        }
    }
}
