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
    [Header("UI Elements")]
    public TMP_Text questionText;
    public Button[] answerButtons;
    public Slider progressBar;
    public Button startButton;
    public GameObject startPanel;
    public GameObject questionPanel;

    private List<QuestionData> questions;
    private int currentIndex = 0;
    private float questionStartTime;
    private Dictionary<string, int> strandScores = new();
    private Dictionary<string, float> strandTotalTimes = new();
    private Dictionary<string, int> strandQuestionCounts = new();
    private float totalTime = 0f;
    private bool examStarted = false;

    void Start()
    {
        LoadQuestions();

        if (questionPanel != null)
            questionPanel.SetActive(false);

        if (startPanel != null)
            startPanel.SetActive(true);

        if (startButton != null)
        {
            startButton.onClick.RemoveAllListeners();
            startButton.onClick.AddListener(OnStartExam);
        }
        else
        {
            Debug.LogWarning("⚠️ Start Button not assigned! Exam will start immediately.");
            OnStartExam();
        }
    }

    void OnStartExam()
    {
        examStarted = true;

        if (startPanel != null)
            startPanel.SetActive(false);

        if (questionPanel != null)
            questionPanel.SetActive(true);

        if (startButton != null)
            startButton.interactable = false;

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
        foreach (var btn in answerButtons)
            btn.interactable = false;

        if (currentIndex >= questions.Count) return;

        QuestionData q = questions[currentIndex];
        float answerTime = Time.time - questionStartTime;
        totalTime += answerTime;

        if (!strandScores.ContainsKey(q.strand))
        {
            strandScores[q.strand] = 0;
            strandTotalTimes[q.strand] = 0f;
            strandQuestionCounts[q.strand] = 0;
        }

        if (selectedIndex == q.answerIndex)
            strandScores[q.strand] += 1;

        strandTotalTimes[q.strand] += answerTime;
        strandQuestionCounts[q.strand]++;

        currentIndex++;

        StartCoroutine(NextQuestionDelay());
    }

    private IEnumerator NextQuestionDelay()
    {
        yield return new WaitForSeconds(0.2f);
        ShowQuestion();

        foreach (var btn in answerButtons)
            btn.interactable = true;
    }

    void EndExam()
    {
        float totalQuestions = questions.Count;

        float bestScore = 0f;
        List<string> bestStrands = new List<string>();

        foreach (var strand in strandScores.Keys)
        {
            float strandScore = ((float)strandScores[strand] / (float)strandQuestionCounts[strand]) * 100f;
            if (strandScore > bestScore)
            {
                bestScore = strandScore;
                bestStrands.Clear();
                bestStrands.Add(strand);
            }
            else if (Mathf.Approximately(strandScore, bestScore))
            {
                bestStrands.Add(strand);
            }
        }

        float avgTime = totalTime / totalQuestions;

        string bestStrandCombined = string.Join(",", bestStrands);
        PlayerPrefs.SetString("BestStrand", bestStrandCombined);
        PlayerPrefs.SetFloat("BestScore", bestScore);
        PlayerPrefs.SetFloat("AvgAnswerTime", avgTime);

        foreach (var strand in strandScores.Keys)
        {
            float strandScore = ((float)strandScores[strand] / (float)strandQuestionCounts[strand]) * 100f;
            float avgStrandTime = strandTotalTimes[strand] / (float)strandQuestionCounts[strand];
            PlayerPrefs.SetFloat($"{strand}_Score", strandScore);
            PlayerPrefs.SetString($"{strand}_Stats", $"Score={strandScore:F1}%, AvgTime={avgStrandTime:F2}s, Questions={strandQuestionCounts[strand]}");

            Debug.Log($"{strand}: Score={strandScore:F1}%, AvgTime={avgStrandTime:F2}s, Questions={strandQuestionCounts[strand]}");
        }

        PlayerPrefs.Save();

        // Summary
        Debug.Log($"Exam Complete! Best Strand(s): {bestStrandCombined}, Score: {bestScore:F1}%, Avg Time: {avgTime:F2}s");
        Debug.Log("──────────────────────────────");

        // Score Distribution
        Debug.Log("Score Distribution by Strand (Total: 100%)");
        Debug.Log("──────────────────────────────");

        float totalCorrectAnswers = 0f;
        foreach (var score in strandScores.Values)
        {
            totalCorrectAnswers += score;
        }

        foreach (var strand in strandScores.Keys)
        {
            float percentage = totalCorrectAnswers > 0 ? ((float)strandScores[strand] / totalCorrectAnswers) * 100f : 0f;
            int barLength = Mathf.RoundToInt((percentage / 100f) * 20);
            string bar = new string('█', barLength).PadRight(20, ' ');

            Debug.Log($"{strand.PadRight(6)} {bar} {percentage,6:F1}%");
        }

        Debug.Log("──────────────────────────────");

        // Performance Summary
        Debug.Log("Performance by Strand");
        Debug.Log("──────────────────────────────");

        foreach (var strand in strandScores.Keys)
        {
            float strandScore = ((float)strandScores[strand] / (float)strandQuestionCounts[strand]) * 100f;
            int barLength = Mathf.RoundToInt((strandScore / 100f) * 20);
            string bar = new string('█', barLength).PadRight(20, ' ');

            Debug.Log($"{strand.PadRight(6)} {bar} {strandScore,6:F1}% ({strandScores[strand]}/{strandQuestionCounts[strand]} correct)");
        }

        Debug.Log("──────────────────────────────");
        Debug.Log($"🏆 Best Strand(s): {bestStrandCombined} ({bestScore:F1}%)");
        Debug.Log("──────────────────────────────");

        AptitudeBotController bot = FindObjectOfType<AptitudeBotController>();
        if (bot != null)
            bot.StartCoroutine(bot.PlayExamCompleteSequence());
        else
            Debug.LogWarning("No AptitudeBotController found in scene!");
    }

}