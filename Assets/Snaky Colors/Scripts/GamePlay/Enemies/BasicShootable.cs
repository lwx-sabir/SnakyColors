using UnityEngine;

namespace SnakyColors
{
    public class BasicShootable : Enemy // Inherits from the base Enemy class
    {
        // override methods for specific behavior:

        protected override void Move()
        {
            base.Move();
        }

        protected override void Die()
        {
            // You could add specific explosion logic or drop items here
            Debug.Log($"BasicObstacle {gameObject.name} specific death actions.");

            // Always call the base Die() method to handle health, score, effects, and deactivation
            base.Die();
        }
         
    }
}