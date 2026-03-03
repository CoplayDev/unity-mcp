using UnityEngine;
using UnityEngine.Events;

namespace TestNamespace
{
    public class UnityEventTestComponent : MonoBehaviour
    {
        public UnityEvent onSimpleEvent;
        public UnityEvent<float> onFloatEvent;

        [SerializeField]
        private UnityEvent _onPrivateEvent;
    }
}
