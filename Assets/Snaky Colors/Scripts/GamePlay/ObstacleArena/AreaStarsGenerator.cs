using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SnakyColors
{
    public class AreaStarsGenerator : MonoBehaviour
    {
        //There are stars placed in each game arena and this script is used to show only 5 of these stars
        void Start()
        {
            for(int i = 0; i < transform.childCount; i++){
                transform.GetChild(i).gameObject.SetActive(false);//Deactivate all stars
            }

            int enabledStars = 0;
            while(enabledStars < 5)//Activate 5 stars
            {
                GameObject randomStar = transform.GetChild(Random.Range(0, transform.childCount)).gameObject;
                if(!randomStar.activeSelf)
                {
                    randomStar.SetActive(true);
                    enabledStars++;
                }
            }
        }
    }
}
