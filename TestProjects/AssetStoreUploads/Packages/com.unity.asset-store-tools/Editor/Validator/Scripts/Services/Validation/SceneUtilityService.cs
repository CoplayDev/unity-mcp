using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AssetStoreTools.Validator.Services.Validation
{
    internal class SceneUtilityService : ISceneUtilityService
    {
        public string CurrentScenePath => SceneManager.GetActiveScene().path;

        public Scene OpenScene(string scenePath)
        {
            EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
            if (string.IsNullOrEmpty(scenePath) || AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null)
                return EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects);
            else
                return EditorSceneManager.OpenScene(scenePath);
        }

        public GameObject[] GetRootGameObjects()
        {
            return SceneManager.GetSceneByPath(CurrentScenePath).GetRootGameObjects();
        }
    }
}