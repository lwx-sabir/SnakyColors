using System.Numerics;

namespace Khela.Game.Models
{
    /// <summary>
    /// A simple, JSON-serializable struct to replace System.Numerics.Vector2
    /// for state storage in Redis.
    /// </summary>    
    using System;
    using System.Numerics;

    [Serializable]
    public struct SerializableVector2 : IEquatable<SerializableVector2>
    {
        public float X { get; set; }
        public float Y { get; set; }

        public SerializableVector2(float x, float y)
        {
            X = x;
            Y = y;
        }

        public SerializableVector2(Vector2 v)
        {
            X = v.X;
            Y = v.Y;
        }

        // === Implicit conversions ===
        public static implicit operator Vector2(SerializableVector2 v) => new Vector2(v.X, v.Y);
        public static implicit operator SerializableVector2(Vector2 v) => new SerializableVector2(v.X, v.Y);

        // === Vector addition ===
        public static SerializableVector2 operator +(SerializableVector2 a, SerializableVector2 b)
            => new SerializableVector2(a.X + b.X, a.Y + b.Y);

        // === Vector subtraction ===
        public static SerializableVector2 operator -(SerializableVector2 a, SerializableVector2 b)
            => new SerializableVector2(a.X - b.X, a.Y - b.Y);

        // === Vector * scalar ===
        public static SerializableVector2 operator *(SerializableVector2 v, float scalar)
            => new SerializableVector2(v.X * scalar, v.Y * scalar);

        public static SerializableVector2 operator *(float scalar, SerializableVector2 v)
            => new SerializableVector2(v.X * scalar, v.Y * scalar);

        // === Vector / scalar ===
        public static SerializableVector2 operator /(SerializableVector2 v, float scalar)
            => new SerializableVector2(v.X / scalar, v.Y / scalar);

        // === Component-wise multiplication ===
        public static SerializableVector2 operator *(SerializableVector2 a, SerializableVector2 b)
            => new SerializableVector2(a.X * b.X, a.Y * b.Y);

        // === Component-wise division ===
        public static SerializableVector2 operator /(SerializableVector2 a, SerializableVector2 b)
            => new SerializableVector2(a.X / b.X, a.Y / b.Y);

        // === Unary minus ===
        public static SerializableVector2 operator -(SerializableVector2 v)
            => new SerializableVector2(-v.X, -v.Y);

        // === Magnitude helpers ===
        public float LengthSquared() => X * X + Y * Y;

        public float Length() => MathF.Sqrt(X * X + Y * Y);

        // === Normalize ===
        public SerializableVector2 Normalized()
        {
            float len = Length();
            if (len < 0.0001f) return new SerializableVector2(0, 0);
            return this / len;
        }

        // === Comparison operators ===
        public static bool operator ==(SerializableVector2 a, SerializableVector2 b)
            => MathF.Abs(a.X - b.X) < 0.0001f && MathF.Abs(a.Y - b.Y) < 0.0001f;

        public static bool operator !=(SerializableVector2 a, SerializableVector2 b)
            => !(a == b);

        // === Equality overrides ===
        public bool Equals(SerializableVector2 other)
            => this == other;

        public override bool Equals(object? obj)
            => obj is SerializableVector2 other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine(X, Y);

        // === ToString override (for debugging) ===
        public override string ToString() => $"({X:0.###}, {Y:0.###})";
    }

}