using UnityEngine;
using UnityEngine.SceneManagement;

public static class SceneLoader
{
    private static string nextScene;

    public static void LoadSceneWithLoading(string sceneToLoad)
    {
        nextScene = sceneToLoad;
        SceneManager.LoadScene("LoadingScene");
    }

    public static string GetNextScene()
    {
        return nextScene;
    }
}
