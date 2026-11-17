using UnityEngine;

namespace TestNamespace
{
    public class PlayModeStartupTestComponent : MonoBehaviour
    {
        public int StartCalls;
        public int LateUpdates;
        public int ReadyCalls;

        private void Start() => StartCalls++;
        private void LateUpdate() => LateUpdates++;
    }
}
