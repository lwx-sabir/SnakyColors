using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SnakyColors
{
    public class ObstacleLeftRightMovement : MonoBehaviour
    {
        //It is used to make an obstacle move left and right on which this script is attached
        [SerializeField]
        private float startPosition;
        [SerializeField]
        private float endPosition;
        private bool shouldMoveLeft = false;
        [SerializeField]
        private float speed;
        void Start()
        {
            if (speed == 0)//If speed is not set in the inspector than set the random speed
            {
                speed = Random.Range(2, 5);
            }
        }

        void Update()
        {
            if (shouldMoveLeft)
            {
                transform.position = new Vector2(transform.position.x - speed * Time.deltaTime, transform.position.y);
                if (transform.position.x < startPosition)
                {
                    shouldMoveLeft = false;
                }
            }
            else
            {
                transform.position = new Vector2(transform.position.x + speed * Time.deltaTime, transform.position.y);
                if (transform.position.x > endPosition)
                {
                    shouldMoveLeft = true;
                }
            }

        }
    }
}
