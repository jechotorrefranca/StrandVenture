using System.Diagnostics;
using System.IO;
using System.Collections;
using UnityEngine;
using Debug = UnityEngine.Debug;

public class PiperTTS : MonoBehaviour
{
    public AudioSource audioSource;
    public string voiceName = "en_US-kusal-medium"; // Type the voice name here

    private string piperPath;
    private string voicesDir;

    private void Awake()
    {
        piperPath = Path.Combine(Application.streamingAssetsPath, "piper/piper.exe");
        voicesDir = Path.Combine(Application.streamingAssetsPath, "piper/voices");
    }

    public void Speak(string text) => StartCoroutine(GenerateAndPlay(text));

    private IEnumerator GenerateAndPlay(string text)
    {
        string outputPath = Path.Combine(Application.persistentDataPath, "piper_output.wav");
        string modelPath = Path.Combine(voicesDir, voiceName + ".onnx");

        if (!File.Exists(modelPath))
        {
            Debug.LogError($"❌ Voice not found: {voiceName}");
            yield break;
        }

        if (File.Exists(outputPath)) File.Delete(outputPath);

        var psi = new ProcessStartInfo
        {
            FileName = piperPath,
            Arguments = $"--model \"{modelPath}\" --output_file \"{outputPath}\" --output_format wav",
            RedirectStandardInput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(piperPath)
        };

        using (var process = Process.Start(psi))
        {
            process.StandardInput.WriteLine(text);
            process.StandardInput.Close();
            process.WaitForExit();

            string stderr = process.StandardError.ReadToEnd();
            if (!string.IsNullOrEmpty(stderr)) Debug.LogWarning($"[Piper] {stderr}");
        }

        float timer = 0f;
        while (!File.Exists(outputPath) && timer < 5f)
        {
            timer += Time.deltaTime;
            yield return null;
        }

        if (!File.Exists(outputPath))
        {
            Debug.LogError("❌ No WAV file generated!");
            yield break;
        }

        using (var www = new WWW("file://" + outputPath))
        {
            yield return www;
            var clip = www.GetAudioClip(false, false, AudioType.WAV);
            if (clip != null)
            {
                audioSource.clip = clip;
                audioSource.Play();
                Debug.Log($"✅ Playing: {voiceName}");
            }
            else Debug.LogError("❌ Failed to load WAV!");
        }
    }
}