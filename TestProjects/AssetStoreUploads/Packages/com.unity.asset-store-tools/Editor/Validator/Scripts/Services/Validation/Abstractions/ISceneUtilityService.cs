using UnityEngine;
using UnityEngine.SceneManagement;

namespace AssetStoreTools.Validator.Services.Validation
{
    internal interface ISceneUtilityService : IValidatorService
    {
        string CurrentScenePath { get; }

        Scene OpenScene(string scenePath);
        GameObject[] GetRootGameObjects();
    }
}