#if UNITY_ASTOOLS_DEVELOPMENT
using UnityEngine;
#endif

namespace AssetStoreTools.Validator.TestDefinitions
{
#if UNITY_ASTOOLS_DEVELOPMENT
    [CreateAssetMenu(fileName = "AutomatedTest", menuName = "Asset Store Validator/Automated Test")]
#endif
    internal class AutomatedTestScriptableObject : ValidationTestScriptableObject { }
}