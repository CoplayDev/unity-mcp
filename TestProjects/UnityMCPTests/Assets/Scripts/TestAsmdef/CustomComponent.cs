using UnityEngine;

namespace TestNamespace
{
    public class CustomComponent : MonoBehaviour
    {
        [SerializeField]
        private string customText = "Hello from custom asmdef!";
        
        [SerializeField]
        private float customFloat = 42.0f;

        void Start()
        {
            Debug.Log($"CustomComponent started: {customText}, value: {customFloat}");
        }
    }
}