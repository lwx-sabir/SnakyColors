using Khela.Game.Models.States;
using System.Collections.Generic;
using System.Numerics;

namespace Khela.Game.Models
{
    public static class LocalDataProvider
    {
        public static List<FoodState> GetFoodDefinitions()
        {
            return new List<FoodState>
            {
                new FoodState(1, new Vector2(0, 0), "RedApple")
                {
                    ScoreValue = 10,
                    ItemType = ItemCategory.Collectible,
                    CollectibleType = CollectibleType.Basic,
                    SpawnWeight = 40
                },

                new FoodState(2, new Vector2(0, 0), "Watermelon")
                {
                    ScoreValue = 20,
                    ItemType = ItemCategory.Collectible,
                    CollectibleType = CollectibleType.Basic,
                    SpawnWeight = 20
                },

                new FoodState(3, new Vector2(0, 0), "Banana")
                {
                    ScoreValue = 10,
                    ItemType = ItemCategory.Collectible,
                    CollectibleType = CollectibleType.Basic,
                    SpawnWeight = 25
                },

                new FoodState(4, new Vector2(0, 0), "GreenApple")
                {
                    ScoreValue = 10,
                    ItemType = ItemCategory.Collectible,
                    CollectibleType = CollectibleType.Basic,
                    SpawnWeight = 30
                },

                new FoodState(5, new Vector2(0, 0), "Kiwi")
                {
                    ScoreValue = 15,
                    ItemType = ItemCategory.Collectible,
                    CollectibleType = CollectibleType.Basic,
                    SpawnWeight = 25
                },

                new FoodState(6, new Vector2(0, 0), "Mangosteen")
                {
                    ScoreValue = 17,
                    ItemType = ItemCategory.Collectible,
                    CollectibleType = CollectibleType.Basic,
                    SpawnWeight = 20,
                },

                new FoodState(6, new Vector2(0, 0), "Pomegranate")
                {
                    ScoreValue = 25,
                    ItemType = ItemCategory.Collectible,
                    CollectibleType = CollectibleType.Basic,
                    SpawnWeight = 17,
                },

                new FoodState(7, new Vector2(0, 0), "DashChargeRefiller")
                {
                    ScoreValue = 0,
                    ItemType = ItemCategory.Collectible,
                    CollectibleType = CollectibleType.DashCharge, 
                    SpawnWeight = 4,
                    MaxInWorld = 5
                },

                //new FoodState(8, new Vector2(0, 0), "powerup_shield")
                //{
                //    ScoreValue = 0,
                //    ItemType = ItemCategory.PowerUp,
                //    PowerupType = PowerupType.Shield,
                //    PowerUpDurationInSec = 5,
                //    SpawnWeight = 3,
                //    MaxInWorld = 5
                //},

                //new FoodState(9, new Vector2(0, 0), "powerup_x2")
                //{
                //    ScoreValue = 0,
                //    ItemType = ItemCategory.PowerUp,
                //    PowerupType = PowerupType.X2ScoreMultiplier,
                //    PowerUpDurationInSec = 10,
                //    SpawnWeight = 2,
                //    MaxInWorld = 3
                //},

                //new FoodState(10, new Vector2(0, 0), "hazard_spike")
                //{
                //    ScoreValue = 0,
                //    ItemType = ItemCategory.Hazard,
                //    SpawnWeight = 5,
                //    MaxInWorld = 5
                //}
            };
        }
    }
}
