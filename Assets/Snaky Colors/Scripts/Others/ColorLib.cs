using UnityEngine;

namespace SnakyColors
{
    public static class ColorLib
    {
        public static readonly Color Red;
        public static readonly Color Purple;
        public static readonly Color Blue;
        public static readonly Color Yellow;
        public static readonly Color Green;
        public static readonly Color Carrot;
        public static readonly Color Alizarin;
        public static readonly Color MidnightBlue;
        public static readonly Color Pumpkin;
        public static readonly Color Orange;
        public static readonly Color GreenSea;
        public static readonly Color GreenSeaLight;

        public static readonly Color[] Colors;

        static ColorLib()
        {
            Red = ParseColor("#c0392b", Color.red);
            Purple = ParseColor("#8e44ad", new Color(0.5f, 0, 0.5f));
            Blue = ParseColor("#3498db", Color.blue);
            Yellow = ParseColor("#f1c40f", Color.yellow);
            Green = ParseColor("#2ecc71", Color.green);
            Carrot = ParseColor("#e67e22", new Color(1f, 0.5f, 0f));
            Alizarin = ParseColor("#e74c3c", Color.red);
            MidnightBlue = ParseColor("#2c3e50", Color.blue);
            Pumpkin = ParseColor("#d35400", new Color(0.83f, 0.33f, 0f));
            Orange = ParseColor("#f39c12", new Color(1f, 0.61f, 0.07f));
            GreenSea = ParseColor("#16a085", new Color(0f, 0.66f, 0.52f));
            GreenSeaLight = ParseColor("#1abc9c", new Color(0.1f, 0.72f, 0.7f));

            Colors = new Color[]
            {
            Red, Purple, Blue, Yellow, Green, Carrot,
            MidnightBlue, GreenSea,
            };
        }

        private static Color ParseColor(string hex, Color fallback)
        {
            if (ColorUtility.TryParseHtmlString(hex, out Color col))
            {
              //  Debug.Log($"Parsed color {hex} successfully: {col}");
                return col;
            }
            else
            {
                Debug.LogError($"Failed to parse color {hex}, using fallback {fallback}");
                return fallback;
            }
        }
    }
}

