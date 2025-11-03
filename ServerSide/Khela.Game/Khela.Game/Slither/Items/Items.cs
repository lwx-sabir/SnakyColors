using System.Collections.Generic;
using System.Numerics;
using System.Text.Json.Serialization; // Required for [JsonIgnore]

namespace Khela.Game.Slither.Items
{
    public class ServerFood
    {
        // Properties must be settable for deserialization
        public int Id { get; set; }
        public Vector2 Position { get; set; }

        // Parameterless constructor for deserializer
        public ServerFood() { }

        // Your logic constructor (used by GameEngine)
        public ServerFood(int id, Vector2 position)
        {
            Id = id;
            Position = position;
        }
    } 
}