using UnityEngine;

namespace MCPForUnityTests.Editor.Tools.Fixtures
{
    // NOTE: File name matches class name so Unity can resolve a MonoScript asset for this ScriptableObject type.
    public class ManageScriptableObjectTestDefinitionBase : ScriptableObject
    {
        [SerializeField] private int baseNumber = 1;
        public int BaseNumber => baseNumber;
    }
}



