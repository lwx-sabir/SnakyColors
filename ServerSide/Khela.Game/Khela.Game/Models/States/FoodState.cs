using System.Collections.Generic;
using System.Numerics;
using System.Text.Json.Serialization; // Required for [JsonIgnore]

namespace Khela.Game.Models.States
{
    public class FoodState
    {
        // Properties must be settable for deserialization
        public int Id { get; set; }

        public SerializableVector2 Position { get; set; }

        public string ItemKey { get; set; }

        // Parameterless constructor for deserializer
        public FoodState() { }

        // Your logic constructor (used by GameEngine)
        public FoodState(int id, Vector2 position, string itemKey)
        {
            Id = id;
            Position = position;
            ItemKey = itemKey;
        }
    } 
}