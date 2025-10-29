using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SnakyColors
{
    public class PlayerSettings : MonoBehaviour
    {
        [SerializeField]
        private Slider steeringSlider;

        public static PlayerSettings Instance;
        private float steeringSpeed; 
        const float MIN_STEERING_SPEED = 5f;

        void Awake()
        { 
            if (Instance == null)
            {
                Instance = this; 
            }
            else
            {
                Destroy(gameObject);
                return;
            } 
            
            steeringSpeed = PlayerPrefs.GetFloat("SteeringSpeedKey", MIN_STEERING_SPEED);  
        }

        public void SetSteeringSpeedFromSlider()
        { 
            steeringSpeed = steeringSlider.value;
            Debug.Log(steeringSlider.value); 
            PlayerPrefs.SetFloat("SteeringSpeedKey", steeringSlider.value); 
            PlayerPrefs.Save();
        }
    }
}