using UnityEngine;
using UnityEngine.SceneManagement;

public class TitleMenuController : MonoBehaviour
{
    public void StartGame()
    {
        Debug.Log("Start Game");
    }

    public void OpenSettings()
    {
        Debug.Log("Open Settings");
    }

    public void OpenAbout()
    {
        Debug.Log("Open About");
    }

    public void ExitGame()
    {
        Application.Quit();
    }
}
