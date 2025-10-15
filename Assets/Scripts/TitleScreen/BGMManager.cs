using UnityEngine;
using System.Collections;


public class BGMManager : MonoBehaviour
{
    private static BGMManager instance;
    private AudioSource audioSource;

    void Awake()
    {
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject); // Keep this object alive across scenes
            audioSource = GetComponent<AudioSource>();
        }
        else
        {
            Destroy(gameObject); // Prevent duplicates if you return to this scene
        }
    }

    public void FadeOut(float duration)
    {
        StartCoroutine(FadeAudio(duration, 0f));
    }

    public void FadeIn(float duration, float targetVolume = 1f)
    {
        if (!audioSource.isPlaying)
            audioSource.Play(); // ensure it resumes

        StartCoroutine(FadeAudio(duration, targetVolume));
    }


    private IEnumerator FadeAudio(float duration, float targetVolume)
    {
        float startVolume = audioSource.volume;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            audioSource.volume = Mathf.Lerp(startVolume, targetVolume, elapsed / duration);
            elapsed += Time.deltaTime;
            yield return null;
        }
        audioSource.volume = targetVolume;
        if (targetVolume == 0f) audioSource.volume = 0f; // don’t stop, just stay silent

    }
}
