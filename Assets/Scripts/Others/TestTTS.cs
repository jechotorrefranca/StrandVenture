using UnityEngine;

public class TestTTS : MonoBehaviour
{
    public PiperTTS piperTTS;

    void Start()
    {
        // Speak when the game starts
        piperTTS.Speak("Hello, this is a test of the Piper text to speech system.");
    }

    void Update()
    {
        // Press Space to speak
        if (Input.GetKeyDown(KeyCode.Space))
        {
            piperTTS.Speak("You pressed the space bar!");
        }
    }

    // Call this from a button or anywhere else
    public void SpeakCustomText(string text)
    {
        piperTTS.Speak(text);
    }
}