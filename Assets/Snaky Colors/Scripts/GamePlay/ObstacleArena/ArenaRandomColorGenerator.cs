using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SnakyColors
{
    public class ArenaRandomColorGenerator : MonoBehaviour
    {
        //This script is attached on each game arena object and it is used to set the random color on each obstacle  
        void Start()
        {
            StartCoroutine(FindAllObstacles());   
        }

        private IEnumerator FindAllObstacles()
        {
            // Wait a frame for all spawns to complete
            yield return null;

            // If they are inside another container, specify it
            var container = GameObject.Find("ObstaclesContainer");
            if (container == null) container = this.gameObject;

            // Include inactive ones
            SpriteRenderer[] obstacles = container.GetComponentsInChildren<SpriteRenderer>(true);

           // Debug.Log($"Found {obstacles.Length} obstacles"); 

            foreach (SpriteRenderer obstacle in obstacles)
            {
                if (obstacle.gameObject.name.Contains("Star")) continue;

                int randColor = Random.Range(0, ColorLib.Colors.Length);
                obstacle.color = ColorLib.Colors[randColor]; 
            }
            if (this.gameObject.name.Equals("Arena2"))
            {
                int rand = Random.Range(1, 6);
                obstacles[rand].color = obstacles[0].color;//To make sure that at least one of these obstacles in a Random.Range have the same color as the player
            }
            else if (this.gameObject.name.Equals("Arena4"))
            {
                int rand = Random.Range(1, 4);
                obstacles[rand].color = obstacles[0].color;//To make sure that at least one of these obstacles in a Random.Range have the same color as the player
            }
            else if (this.gameObject.name.Equals("Arena5"))
            {
                int rand = Random.Range(1, 6);
                obstacles[rand].color = obstacles[0].color;//To make sure that at least one of these obstacles in a Random.Range have the same color as the player
                rand = Random.Range(6, 11);
                obstacles[rand].color = obstacles[0].color;
            }
            else if (this.gameObject.name.Equals("Arena6"))
            {
                int rand = Random.Range(1, 4);
                obstacles[rand].color = obstacles[0].color;//To make sure that at least one of these obstacles in a Random.Range have the same color as the player
                rand = Random.Range(4, 6);
                obstacles[rand].color = obstacles[0].color;
            }
            else if (this.gameObject.name.Equals("Arena7"))
            {
                int rand = Random.Range(1, 3);
                obstacles[rand].color = obstacles[0].color;//To make sure that at least one of these obstacles in a Random.Range have the same color as the player
                rand = Random.Range(3, 5);
                obstacles[rand].color = obstacles[0].color;
                rand = Random.Range(5, 7);
                obstacles[rand].color = obstacles[0].color;
            }
            else if (this.gameObject.name.Equals("Arena8"))
            {
                int rand = Random.Range(1, 3);
                obstacles[rand].color = obstacles[0].color;//To make sure that at least one of these obstacles in a Random.Range have the same color as the player
            }
            else if (this.gameObject.name.Equals("Arena9"))
            {
                int rand = Random.Range(1, 3);
                obstacles[rand].color = obstacles[0].color;//To make sure that at least one of these obstacles in a Random.Range have the same color as the player
                rand = Random.Range(3, 8);
                obstacles[rand].color = obstacles[0].color;
            }
            else if (this.gameObject.name.Equals("Arena10"))
            {
                int rand = Random.Range(1, 3);
                obstacles[rand].color = obstacles[0].color;//To make sure that at least one of these obstacles in a Random.Range have the same color as the player
                rand = Random.Range(3, 5);
                obstacles[rand].color = obstacles[0].color;
                rand = Random.Range(5, 7);
                obstacles[rand].color = obstacles[0].color;
                rand = Random.Range(7, 9);
                obstacles[rand].color = obstacles[0].color;
                rand = Random.Range(9, 11);
                obstacles[rand].color = obstacles[0].color;
            }
        }
    }
}
