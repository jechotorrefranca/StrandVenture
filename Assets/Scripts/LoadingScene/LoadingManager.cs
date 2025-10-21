using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Collections.Generic;

public class LoadingManager : MonoBehaviour
{
    [Header("UI References")]
    public Image fadeOverlay;
    public Image backgroundImage;
    public TMP_Text tipText;
    public Image botImage;
    public TMP_Text loadingText;

    [Header("Loading Settings")]
    public List<Sprite> backgroundImages;
    public List<string> loadingTips;
    public float imageSwitchInterval = 4f;
    public float fadeDuration = 1f;
    private string nextSceneName;

    [Header("Bot Animation")]
    public float spinSpeed = 50f;

    private List<Sprite> shuffledImages;
    private List<string> shuffledTips;
    private int imageIndex = 0;
    private int tipIndex = 0;

    void Start()
    {
        nextSceneName = SceneLoader.GetNextScene();

        InitializeShuffledLists();

        StartCoroutine(LoadingSequence());
    }

    private void InitializeShuffledLists()
    {
        shuffledImages = new List<Sprite>(backgroundImages);
        shuffledTips = new List<string>(loadingTips);
        ShuffleList(shuffledImages);
        ShuffleList(shuffledTips);

        if (shuffledImages.Count > 0)
        {
            backgroundImage.sprite = shuffledImages[0];
            backgroundImage.color = new Color(1, 1, 1, 1);
            imageIndex = 1;
        }

        if (shuffledTips.Count > 0)
        {
            tipText.text = shuffledTips[0];
            tipText.color = new Color(tipText.color.r, tipText.color.g, tipText.color.b, 1);
            tipIndex = 1;
        }
    }

    private IEnumerator LoadingSequence()
    {
        fadeOverlay.color = new Color(0, 0, 0, 1);
        loadingText.text = "Loading";
        botImage.transform.localRotation = Quaternion.identity;

        StartCoroutine(CycleBackgroundAndTip());
        StartCoroutine(AnimateBot());
        StartCoroutine(AnimateLoadingText());

        StartCoroutine(Fade(1, 0, fadeDuration));

        yield return StartCoroutine(LoadNextScene());
    }

    private IEnumerator LoadNextScene()
    {
        AsyncOperation async = SceneManager.LoadSceneAsync(nextSceneName);
        async.allowSceneActivation = false;

        while (async.progress < 0.9f)
            yield return null;

        yield return new WaitForSeconds(2f);

        yield return StartCoroutine(Fade(0, 1, fadeDuration));
        async.allowSceneActivation = true;
    }

    private IEnumerator Fade(float start, float end, float duration)
    {
        float elapsed = 0f;
        Color c = fadeOverlay.color;

        while (elapsed < duration)
        {
            float t = Mathf.SmoothStep(start, end, elapsed / duration);
            fadeOverlay.color = new Color(c.r, c.g, c.b, t);
            elapsed += Time.deltaTime;
            yield return null;
        }

        fadeOverlay.color = new Color(c.r, c.g, c.b, end);
    }

    private IEnumerator CycleBackgroundAndTip()
    {
        if (backgroundImages.Count == 0)
            yield break;

        yield return new WaitForSeconds(imageSwitchInterval);

        Image tempImage = new GameObject("TempImage").AddComponent<Image>();
        tempImage.transform.SetParent(backgroundImage.transform.parent, false);
        tempImage.transform.SetSiblingIndex(backgroundImage.transform.GetSiblingIndex());
        tempImage.rectTransform.sizeDelta = backgroundImage.rectTransform.sizeDelta;
        tempImage.rectTransform.anchoredPosition = backgroundImage.rectTransform.anchoredPosition;
        tempImage.rectTransform.anchorMin = backgroundImage.rectTransform.anchorMin;
        tempImage.rectTransform.anchorMax = backgroundImage.rectTransform.anchorMax;
        tempImage.rectTransform.pivot = backgroundImage.rectTransform.pivot;
        tempImage.preserveAspect = true;
        tempImage.gameObject.SetActive(false);

        while (true)
        {
            if (imageIndex >= shuffledImages.Count)
            {
                ShuffleList(shuffledImages);
                imageIndex = 0;
            }
            if (tipIndex >= shuffledTips.Count)
            {
                ShuffleList(shuffledTips);
                tipIndex = 0;
            }

            Sprite newSprite = shuffledImages[imageIndex++];
            string newTip = shuffledTips.Count > 0 ? shuffledTips[tipIndex++] : "";

            tempImage.sprite = backgroundImage.sprite;
            tempImage.color = new Color(1, 1, 1, 1);
            tempImage.gameObject.SetActive(true);

            backgroundImage.sprite = newSprite;
            backgroundImage.color = new Color(1, 1, 1, 0);

            yield return StartCoroutine(FadeTextAlpha(tipText, 1, 0, fadeDuration / 2));
            tipText.text = string.IsNullOrEmpty(newTip) ? "" : newTip;

            float t = 0f;
            while (t < fadeDuration)
            {
                t += Time.deltaTime;
                float alpha = Mathf.SmoothStep(0, 1, t / fadeDuration);
                backgroundImage.color = new Color(1, 1, 1, alpha);
                tempImage.color = new Color(1, 1, 1, 1 - alpha);
                yield return null;
            }

            tempImage.gameObject.SetActive(false);

            yield return StartCoroutine(FadeTextAlpha(tipText, 0, 1, fadeDuration / 2));

            yield return new WaitForSeconds(imageSwitchInterval);
        }
    }

    private void ShuffleList<T>(List<T> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            int randomIndex = Random.Range(i, list.Count);
            (list[i], list[randomIndex]) = (list[randomIndex], list[i]);
        }
    }

    private IEnumerator FadeTextAlpha(TMP_Text text, float start, float end, float duration)
    {
        float elapsed = 0f;
        Color c = text.color;
        while (elapsed < duration)
        {
            float a = Mathf.Lerp(start, end, elapsed / duration);
            text.color = new Color(c.r, c.g, c.b, a);
            elapsed += Time.deltaTime;
            yield return null;
        }
        text.color = new Color(c.r, c.g, c.b, end);
    }

    private IEnumerator AnimateBot()
    {
        Vector3 startPos = botImage.rectTransform.anchoredPosition;
        float floatAmplitude = 10f;
        float floatSpeed = 2f;
        float animTime = 0f;

        while (true)
        {
            animTime += Time.deltaTime;

            botImage.transform.Rotate(Vector3.forward, spinSpeed * Time.deltaTime);

            float newY = startPos.y + Mathf.Sin(animTime * floatSpeed) * floatAmplitude;
            botImage.rectTransform.anchoredPosition = new Vector2(startPos.x, newY);

            yield return null;
        }
    }

    private IEnumerator AnimateLoadingText()
    {
        string baseText = "Loading";
        int dotCount = 0;

        while (true)
        {
            loadingText.text = baseText + new string('.', dotCount);
            dotCount = (dotCount + 1) % 4;
            yield return new WaitForSeconds(0.5f);
        }
    }
}