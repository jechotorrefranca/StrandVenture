using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

[System.Serializable]
public class QuestionData
{
    public string strand;
    public string text;
    public string[] options;
    public int answerIndex;
}

[System.Serializable]
public class QuestionList
{
    public List<QuestionData> questions;
}

public class ExamManager : MonoBehaviour
{
    public TMP_Text questionText;
    public Button[] answerButtons;
    public Slider progressBar;

    private List<QuestionData> questions;
    private int currentIndex = 0;
    private float questionStartTime;
    private Dictionary<string, int> strandScores = new();
    private float totalTime = 0f;

    void Start()
    {
        LoadQuestions();
        ShowQuestion();
    }

    void LoadQuestions()
    {
        TextAsset jsonFile = Resources.Load<TextAsset>("Questions/questions");
        if (jsonFile != null)
        {
            questions = JsonUtility.FromJson<QuestionList>(jsonFile.text).questions;
        }
        else
        {
            Debug.LogError("Question file not found!");
        }
    }

    void ShowQuestion()
    {
        if (currentIndex >= questions.Count)
        {
            EndExam();
            return;
        }

        QuestionData q = questions[currentIndex];
        questionText.text = q.text;

        for (int i = 0; i < answerButtons.Length; i++)
        {
            int index = i;
            answerButtons[i].GetComponentInChildren<TMP_Text>().text = q.options[i];
            answerButtons[i].onClick.RemoveAllListeners();
            answerButtons[i].onClick.AddListener(() => OnAnswerSelected(index));
        }

        progressBar.value = (float)currentIndex / questions.Count;
        questionStartTime = Time.time;
    }

    void OnAnswerSelected(int selectedIndex)
    {
        QuestionData q = questions[currentIndex];
        float answerTime = Time.time - questionStartTime;
        totalTime += answerTime;

        if (!strandScores.ContainsKey(q.strand))
            strandScores[q.strand] = 0;

        if (selectedIndex == q.answerIndex)
            strandScores[q.strand] += 1;

        currentIndex++;
        ShowQuestion();
    }

    void EndExam()
    {
        float totalQuestions = questions.Count;
        string bestStrand = strandScores.OrderByDescending(x => x.Value).First().Key;
        float bestScore = (strandScores[bestStrand] / totalQuestions) * 100f;
        float avgTime = totalTime / totalQuestions;

        PlayerPrefs.SetString("BestStrand", bestStrand);
        PlayerPrefs.SetFloat("BestScore", bestScore);
        PlayerPrefs.SetFloat("AvgAnswerTime", avgTime);
        PlayerPrefs.Save();

        Debug.Log($"Exam done! Best Strand: {bestStrand}, Score: {bestScore:F1}%, Avg Time: {avgTime:F2}s");

        // TODO: Load result scene (e.g., AptitudeResultScene)
        // SceneManager.LoadScene("AptitudeResultScene");
    }
}
