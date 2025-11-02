using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace SnakyColors
{
    [Serializable]
    public class SpriteOverride
    {
        public Transform prefab;
        public int Position;
    
        public List<IntegerField> layerOrders = new();
    }
}
