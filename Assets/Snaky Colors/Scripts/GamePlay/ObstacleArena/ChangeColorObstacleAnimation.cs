using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SnakyColors
{
    public class ChangeColorObstacleAnimation : MonoBehaviour
    {
        //This script is attached on the "changeColor" game obstacle and it is used to create simple fade in - fade out animation
        [SerializeField]
        private SpriteRenderer changeColorObstacle;
        private float alpha = 1;
        private bool shouldLowerTheAlphaValue = false;

        void Update()
        {
            if (shouldLowerTheAlphaValue)
            {
                alpha -= Time.deltaTime * 2;
                if (alpha < 0.2f) shouldLowerTheAlphaValue = false;
            }
            else
            {
                alpha += Time.deltaTime * 2;
                if (alpha >= 1f) shouldLowerTheAlphaValue = true;
            }
            changeColorObstacle.color = new Color(changeColorObstacle.color.r, changeColorObstacle.color.g, changeColorObstacle.color.b, alpha);
        }
    }
}
