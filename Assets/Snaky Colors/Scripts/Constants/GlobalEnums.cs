using System;

namespace SnakyColors
{
    public enum SoundType
    {
        Button,
        Button2,
        ObstacleDestroy,
        ArenaComplete,
        Shop,
        Denied,
        Explosion,
        StarCollect,
        MenuOpen1,
        Whoosh1,
        BGM
    }

    public enum ParticleType
    {
        None,
        FruitCollect,
        GemBurst,
        StarPickup,
        DashTrail
    }

    [Serializable]
    public struct Vec2Dto
    {
        public float X { get; set; }
        public float Y { get; set; }

        // Optional helper to convert to UnityEngine.Vector2
        public UnityEngine.Vector2 ToUnityVector2() => new UnityEngine.Vector2(X, Y);
    }
}

