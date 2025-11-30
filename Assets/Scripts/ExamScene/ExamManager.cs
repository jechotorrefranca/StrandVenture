using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine.Networking;
using System.Text;

[System.Serializable]
public class QuestionData
{
    // Kept for compatibility if you want to store local variants later (not used when pulling from API)
    public string strand;
    public string text;
    public string[] options;
    public int answerIndex;
}

public class ExamManager : MonoBehaviour
{
    [Header("UI Elements")]
    public TMP_Text questionText;
    public Button[] answerButtons;        // MUST be length 5 (buttons labeled 1..5)
    public Slider progressBar;
    public Button startButton;
    public GameObject startPanel;
    public GameObject questionPanel;

    // O*NET API / config
    private const string BaseUrl = "https://api-v2.onetcenter.org/";
    private string apiKey = "";

    // API-fetched questions
    private List<ApiQuestion> apiQuestions = new List<ApiQuestion>();
    private StringBuilder answersBuilder = new StringBuilder();
    private string lastAnswersString = "";

    // Flow
    private int currentIndex = 0;
    private float questionStartTime;
    private bool examStarted = false;

    void Start()
    {
        LoadOnetApiKey();

        // Prefetch immediately so UI doesn't show template text
        StartCoroutine(PrefetchQuestions());

        // Initially hide question panel
        if (questionPanel != null) questionPanel.SetActive(false);
        if (startPanel != null) startPanel.SetActive(true);

        if (startButton != null)
        {
            startButton.onClick.RemoveAllListeners();
            startButton.onClick.AddListener(() => StartCoroutine(StartExamSequence()));
        }
        else
        {
            Debug.LogWarning("⚠️ Start Button not assigned! Starting automatically.");
            StartCoroutine(StartExamSequence());
        }

        SetupAnswerButtonsAsScale();
    }

    // ---------------------------
    // Config (API key loading)
    // ---------------------------
    private void LoadOnetApiKey()
    {
        string path;
#if UNITY_EDITOR
        path = Path.Combine(Application.dataPath, "../onet_config.json");
#else
        path = Path.Combine(Application.streamingAssetsPath, "onet_config.json");
#endif
        Debug.Log("Looking for O*NET config at: " + path);

        if (File.Exists(path))
        {
            try
            {
                string json = File.ReadAllText(path);
                var config = JsonUtility.FromJson<OnetConfig>(json);
                apiKey = config.api_key;
                Debug.Log("O*NET API Key loaded successfully!");
            }
            catch (System.Exception ex)
            {
                Debug.LogError("Failed to parse onet_config.json: " + ex.Message);
            }
        }
        else
        {
            Debug.LogWarning("onet_config.json not found at: " + path + " — continuing without API key (requests will likely fail).");
        }
    }

    [System.Serializable]
    private class OnetConfig { public string api_key; }

    // ---------------------------
    // Prefetch questions so UI is ready immediately
    // ---------------------------
    private IEnumerator PrefetchQuestions()
    {
        // Attempt to fetch questions now
        yield return StartCoroutine(FetchQuestionsCoroutine(1, 60));
    }

    // ---------------------------
    // UI / Buttons Setup
    // ---------------------------
    void SetupAnswerButtonsAsScale()
    {
        if (answerButtons == null || answerButtons.Length < 5)
        {
            Debug.LogError("AnswerButtons must contain 5 buttons (scale 1..5).");
            return;
        }

        for (int i = 0; i < answerButtons.Length; i++)
        {
            int scoreValue = i + 1; // 1..5
            var textComp = answerButtons[i].GetComponentInChildren<TMP_Text>();
            if (textComp != null) textComp.text = scoreValue.ToString();

            answerButtons[i].onClick.RemoveAllListeners();
            answerButtons[i].onClick.AddListener(() => OnScaleSelected(scoreValue));
        }
    }

    // ---------------------------
    // Flow: Fetch questions -> show -> collect -> submit results
    // ---------------------------
    private IEnumerator StartExamSequence()
    {
        examStarted = true;

        if (startPanel != null) startPanel.SetActive(false);
        if (questionPanel != null) questionPanel.SetActive(true);
        if (startButton != null) startButton.interactable = false;

        // reset state
        answersBuilder.Clear();
        currentIndex = 0;

        // If prefetch didn't get questions, try again
        if (apiQuestions == null || apiQuestions.Count == 0)
        {
            yield return StartCoroutine(FetchQuestionsCoroutine(1, 60));
        }

        if (apiQuestions == null || apiQuestions.Count == 0)
        {
            Debug.LogError("No questions received from API. Aborting exam.");
            if (startPanel != null) startPanel.SetActive(true);
            yield break;
        }

        ShowQuestion();
    }

    private IEnumerator FetchQuestionsCoroutine(int start, int end)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            Debug.LogWarning("API key not set. Attempting to fetch anyway (likely to fail).");
        }

        string endpoint = $"mnm/interestprofiler/questions?start={start}&end={end}";
        string url = BaseUrl.TrimEnd('/') + "/" + endpoint;

        using var req = UnityWebRequest.Get(url);
        req.SetRequestHeader("Accept", "application/json");
        if (!string.IsNullOrEmpty(apiKey)) req.SetRequestHeader("X-API-Key", apiKey);

        Debug.Log("Requesting questions: " + url);
        yield return req.SendWebRequest();

#if UNITY_2020_1_OR_NEWER
        if (req.result == UnityWebRequest.Result.ConnectionError || req.result == UnityWebRequest.Result.ProtocolError)
#else
        if (req.isNetworkError || req.isHttpError)
#endif
        {
            Debug.LogError($"Failed to fetch questions: {req.error} (HTTP {(int)req.responseCode})");
            yield break;
        }

        string json = req.downloadHandler.text;
        if (string.IsNullOrEmpty(json))
        {
            Debug.LogError("Empty response when fetching questions.");
            yield break;
        }

        var qResp = JsonUtility.FromJson<ApiQuestionResponse>(json);
        if (qResp == null || qResp.question == null || qResp.question.Length == 0)
        {
            Debug.LogError("Failed to parse question response or no questions found.");
            Debug.Log("Raw JSON: " + json);
            yield break;
        }

        apiQuestions = qResp.question.ToList();
        Debug.Log($"Fetched {apiQuestions.Count} questions from API.");
    }

    void ShowQuestion()
    {
        if (apiQuestions == null || apiQuestions.Count == 0)
        {
            Debug.LogError("No questions available to show.");
            EndExamWithError();
            return;
        }

        if (currentIndex >= apiQuestions.Count)
        {
            // finished
            StartCoroutine(SubmitResultsCoroutine());
            return;
        }

        var q = apiQuestions[currentIndex];
        // Display as requested: "1/60 question text" (no brackets)
        questionText.text = $"{currentIndex + 1}/{apiQuestions.Count} {q.text}";

        progressBar.value = (float)currentIndex / (float)apiQuestions.Count;

        questionStartTime = Time.time;

        foreach (var b in answerButtons) b.interactable = true;
    }

    void EndExamWithError()
    {
        Debug.LogWarning("Ending exam due to an error or missing data.");
        // try to trigger bot sequence so user sees something
        AptitudeBotController bot = FindObjectOfType<AptitudeBotController>();
        if (bot != null)
            bot.StartCoroutine(bot.PlayExamCompleteSequence());
    }

    // Called when user presses a 1..5 rating button
    void OnScaleSelected(int rating)
    {
        // prevent double hits
        foreach (var b in answerButtons) b.interactable = false;

        if (apiQuestions == null || currentIndex >= apiQuestions.Count) return;

        // Append rating to answers builder (the O*NET console client appended digits without separators)
        answersBuilder.Append(rating);

        currentIndex++;

        // short delay to avoid accidental double-tap and allow UI update
        StartCoroutine(NextQuestionDelay());
    }

    private IEnumerator NextQuestionDelay()
    {
        yield return new WaitForSeconds(0.12f);
        ShowQuestion();
    }

    // ---------------------------
    // Submit results to O*NET and process RIASEC response
    // ---------------------------
    private IEnumerator SubmitResultsCoroutine()
    {
        lastAnswersString = answersBuilder.ToString();
        if (string.IsNullOrEmpty(lastAnswersString))
        {
            Debug.LogError("No answers to submit.");
            yield break;
        }

        string escapedAnswers = UnityWebRequest.EscapeURL(lastAnswersString);
        string endpoint = $"mnm/interestprofiler/results?answers={escapedAnswers}";
        string url = BaseUrl.TrimEnd('/') + "/" + endpoint;

        using var req = UnityWebRequest.Get(url);
        req.SetRequestHeader("Accept", "application/json");
        if (!string.IsNullOrEmpty(apiKey)) req.SetRequestHeader("X-API-Key", apiKey);

        Debug.Log("Submitting answers to O*NET: " + url);
        yield return req.SendWebRequest();

#if UNITY_2020_1_OR_NEWER
        if (req.result == UnityWebRequest.Result.ConnectionError || req.result == UnityWebRequest.Result.ProtocolError)
#else
        if (req.isNetworkError || req.isHttpError)
#endif
        {
            Debug.LogError($"Failed to fetch RIASEC results: {req.error} (HTTP {(int)req.responseCode})");
            Debug.Log("Raw response: " + req.downloadHandler.text);
            yield break;
        }

        string json = req.downloadHandler.text;
        if (string.IsNullOrEmpty(json))
        {
            Debug.LogError("Empty RIASEC response.");
            yield break;
        }

        var rResp = JsonUtility.FromJson<ApiRiasecResponse>(json);
        if (rResp == null || rResp.result == null || rResp.result.Length == 0)
        {
            Debug.LogError("Failed to parse RIASEC response. Raw JSON: " + json);
            yield break;
        }

        // Map to canonical names same as reference
        string[] correctOrder = { "Realistic", "Investigative", "Artistic", "Social", "Enterprising", "Conventional" };
        if (rResp.result.Length >= 6)
        {
            for (int i = 0; i < 6 && i < rResp.result.Length; i++)
            {
                rResp.result[i].area = correctOrder[i];
            }
        }

        ProcessRiasecResults(rResp.result.ToList());

        // After processing RIASEC, fetch careers and then trigger completion sequence
        yield return StartCoroutine(FetchCareersCoroutine(lastAnswersString));
    }

    // ---------------------------
    // Processing / PlayerPrefs saving, compatibility calculation
    // ---------------------------
    private void ProcessRiasecResults(List<ApiRiasecScore> scores)
    {
        // Convert to dictionary of area -> points
        var pointsByArea = new Dictionary<string, int>();
        foreach (var s in scores)
        {
            string area = s.area ?? "Unknown";
            pointsByArea[area] = s.score;
        }

        // Ensure canonical order present
        string[] riasecOrder = new[] { "Realistic", "Investigative", "Artistic", "Social", "Enterprising", "Conventional" };
        foreach (var a in riasecOrder)
        {
            if (!pointsByArea.ContainsKey(a)) pointsByArea[a] = 0;
        }

        // Convert Points (assumed max 40) -> percent and save each individual strand percent
        var percentByArea = new Dictionary<string, float>();
        foreach (var a in riasecOrder)
        {
            int pts = pointsByArea[a];
            float pct = (pts / 40f) * 100f;
            percentByArea[a] = pct;

            // Save to PlayerPrefs (consistent naming)
            PlayerPrefs.SetFloat($"RIASEC_{a}_Percent", pct);
            PlayerPrefs.SetInt($"RIASEC_{a}_Points", pts);
        }

        // Save total questions count and answers string
        PlayerPrefs.SetInt("RIASEC_TotalQuestions", apiQuestions.Count);
        PlayerPrefs.SetString("RIASEC_AnswersString", lastAnswersString);

        // Sorted profile (percent)
        var sorted = percentByArea.OrderByDescending(k => k.Value).ToList();

        // Holland code (top 3 letters)
        var top3 = sorted.Take(3).Select(k => (!string.IsNullOrEmpty(k.Key) ? k.Key[0].ToString() : "?"));
        string hollandCode = string.Join("", top3);
        PlayerPrefs.SetString("RIASEC_HollandCode", hollandCode);

        // Best areas
        float bestPercent = sorted.First().Value;
        var bestAreas = sorted.Where(s => Mathf.Approximately(s.Value, bestPercent) || s.Value == bestPercent).Select(s => s.Key).ToArray();
        string bestCombined = string.Join(",", bestAreas);
        PlayerPrefs.SetString("RIASEC_BestAreas", bestCombined);
        PlayerPrefs.SetFloat("RIASEC_BestPercent", bestPercent);

        // Compute SHS Strand compatibility using **points** exactly like your reference code
        float GetPoints(string area) => pointsByArea.ContainsKey(area) ? pointsByArea[area] : 0f;

        var strandResults = new List<(string Name, float Percent, string Formula, string Description)>
        {
            ("STEM", (GetPoints("Investigative") + GetPoints("Realistic")) / 80f * 100f, "Investigative + Realistic", "Science, Technology, Engineering, Mathematics"),
            ("ABM", (GetPoints("Enterprising") + GetPoints("Conventional")) / 80f * 100f, "Enterprising + Conventional", "Accountancy, Business, Management"),
            ("HUMSS", (GetPoints("Social") + GetPoints("Artistic")) / 80f * 100f, "Social + Artistic", "Humanities, Social Sciences"),
            ("ICT", (GetPoints("Investigative") + GetPoints("Conventional") + GetPoints("Realistic")) / 120f * 100f, "Investigative + Conventional + Realistic", "Information & Communications Technology"),
            ("MARITIME", (GetPoints("Realistic") + GetPoints("Enterprising") + GetPoints("Investigative")) / 120f * 100f, "Realistic + Enterprising + Investigative", "Maritime and Seafaring Studies"),
            ("TVL", (GetPoints("Realistic") + GetPoints("Conventional")) / 80f * 100f, "Realistic + Conventional", "Technical-Vocational-Livelihood")
        };

        var sortedStrands = strandResults.OrderByDescending(s => s.Percent).ToList();

        // Save top strand recommendation and each strand percent (save both name & percent)
        if (sortedStrands.Count > 0)
        {
            PlayerPrefs.SetString("RIASEC_TopStrand", sortedStrands[0].Name);
            PlayerPrefs.SetFloat("RIASEC_TopStrandPercent", sortedStrands[0].Percent);
        }

        // Save each strand percent as well (so front-end UI can read them easily)
        foreach (var s in sortedStrands)
        {
            PlayerPrefs.SetFloat($"Strand_{s.Name}_Percent", s.Percent);
            Debug.Log($"Strand please work {s.Name}: {s.Percent:F1}% ({s.Formula})");
        }

        PlayerPrefs.Save();

        // Debug log summary
        Debug.Log("──────────────────────────────");
        Debug.Log("RIASEC PROFILE (top -> bottom):");
        for (int i = 0; i < sorted.Count; i++)
        {
            Debug.Log($"{i + 1}. {sorted[i].Key.PadRight(12)} {sorted[i].Value,6:F1}%  (Points: {pointsByArea[sorted[i].Key]})");
        }
        Debug.Log($"Holland Code: {hollandCode}");
        Debug.Log("──────────────────────────────");

        Debug.Log("STRAND COMPATIBILITY:");
        for (int i = 0; i < sortedStrands.Count; i++)
        {
            Debug.Log($"{i + 1}. {sortedStrands[i].Name.PadRight(10)} {sortedStrands[i].Percent,6:F1}%  ({sortedStrands[i].Formula})");
        }
        Debug.Log("──────────────────────────────");
        Debug.Log($"🏆 Best RIASEC Area(s): {bestCombined} ({bestPercent:F1}%)");
    }

    // ---------------------------
    // Fetch career recommendations (job_zone 3..5), log and save in one PlayerPref
    // ---------------------------
    private IEnumerator FetchCareersCoroutine(string answersString)
    {
        if (string.IsNullOrEmpty(answersString))
        {
            Debug.LogWarning("No answers string provided for career lookup.");
            TriggerCompletionSequence();
            yield break;
        }

        string escapedAnswers = UnityWebRequest.EscapeURL(answersString);
        var allCareers = new List<ApiCareer>();

        for (int zone = 3; zone <= 5; zone++)
        {
            string endpoint = $"mnm/interestprofiler/careers?answers={escapedAnswers}&job_zone={zone}";
            string url = BaseUrl.TrimEnd('/') + "/" + endpoint;

            using var req = UnityWebRequest.Get(url);
            req.SetRequestHeader("Accept", "application/json");
            if (!string.IsNullOrEmpty(apiKey)) req.SetRequestHeader("X-API-Key", apiKey);

            Debug.Log($"Requesting careers (zone {zone}): {url}");
            yield return req.SendWebRequest();

#if UNITY_2020_1_OR_NEWER
            if (req.result == UnityWebRequest.Result.ConnectionError || req.result == UnityWebRequest.Result.ProtocolError)
#else
            if (req.isNetworkError || req.isHttpError)
#endif
            {
                Debug.LogWarning($"Career request (zone {zone}) failed: {req.error} (HTTP {(int)req.responseCode})");
                continue;
            }

            string json = req.downloadHandler.text;
            if (string.IsNullOrEmpty(json)) continue;

            var cResp = JsonUtility.FromJson<ApiCareerResponse>(json);
            if (cResp?.career != null && cResp.career.Length > 0)
            {
                allCareers.AddRange(cResp.career);
                Debug.Log($"  -> Received {cResp.career.Length} careers for zone {zone}");
            }
        }

        // Combine and deduplicate by title
        var distinct = allCareers
            .Where(c => !string.IsNullOrEmpty(c.title))
            .GroupBy(c => c.title)
            .Select(g => g.First())
            .ToList();

        // Log top careers (up to 15)
        if (distinct.Count > 0)
        {
            Debug.Log("Sample career paths (top results):");
            int count = 0;
            foreach (var career in distinct.Take(15))
            {
                count++;
                Debug.Log($"{count,2}. {career.title}");
            }

            // Save joined titles in one PlayerPref (joined by "||")
            string joinedTitles = string.Join("||", distinct.Select(c => c.title).Take(50));
            PlayerPrefs.SetString("RIASEC_CareerRecommendations", joinedTitles);
            Debug.Log($"RIASEC_CareerRecommendations, {joinedTitles}");

            // Save top career separately
            PlayerPrefs.SetString("RIASEC_TopCareer", distinct[0].title ?? "");
            Debug.Log($"RIASEC_TopCareer, {distinct[0].title} ?? '' ");
        }
        else
        {
            Debug.Log("No careers found from O*NET for provided answers.");
            PlayerPrefs.SetString("RIASEC_CareerRecommendations", "");
            PlayerPrefs.SetString("RIASEC_TopCareer", "");
        }

        PlayerPrefs.Save();

        // Finally trigger completion sequence
        TriggerCompletionSequence();
    }

    private void TriggerCompletionSequence()
    {
        AptitudeBotController bot = FindObjectOfType<AptitudeBotController>();
        if (bot != null)
            bot.StartCoroutine(bot.PlayExamCompleteSequence());
        else
            Debug.LogWarning("No AptitudeBotController found in scene!");
    }

    // ---------------------------
    // API JSON helper classes (must match API JSON field names)
    // ---------------------------
    [System.Serializable]
    private class ApiQuestionResponse
    {
        public ApiQuestion[] question;
    }

    [System.Serializable]
    private class ApiQuestion
    {
        public int index;
        public string text;
    }

    [System.Serializable]
    private class ApiRiasecResponse
    {
        public ApiRiasecScore[] result;
    }

    [System.Serializable]
    private class ApiRiasecScore
    {
        public string area;
        public int score;
    }

    [System.Serializable]
    private class ApiCareerResponse
    {
        public ApiCareer[] career;
    }

    [System.Serializable]
    private class ApiCareer
    {
        public string title;
        public string fit;
        public string href;
    }
}
