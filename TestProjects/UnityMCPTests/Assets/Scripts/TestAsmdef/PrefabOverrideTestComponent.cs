using System;
using UnityEngine;

namespace TestNamespace
{
    public class PrefabOverrideTestComponent : MonoBehaviour
    {
        [Serializable]
        public class Node
        {
            public float value;
            [SerializeReference] public Node next;
        }

        public float count = 1;
        public float[] values = { 1 };
        [SerializeReference] public Node node;
    }
}
