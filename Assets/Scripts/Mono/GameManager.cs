using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Networking;

public class GameManager : MonoBehaviour
{

    #region Variables
    private string playerId = "1"; // ID mặc định của người chơi
    private Data data = new Data();

    [SerializeField] GameEvents events = null;

    [SerializeField] Animator timerAnimtor = null;
    [SerializeField] TextMeshProUGUI timerText = null;
    [SerializeField] Color timerHalfWayOutColor = Color.yellow;
    [SerializeField] Color timerAlmostOutColor = Color.red;
    private Color timerDefaultColor = Color.white;

    private List<AnswerData> PickedAnswers = new List<AnswerData>();
    private List<int> FinishedQuestions = new List<int>();
    private int currentQuestion = 0;

    private int timerStateParaHash = 0;

    private IEnumerator IE_WaitTillNextRound = null;
    private IEnumerator IE_StartTimer = null;

    private bool IsFinished
    {
        get
        {
            return (FinishedQuestions.Count < 5) ? false : true;
        }
    }

    private UIManager uiManager;

    #endregion

    #region Default Unity methods

    private void OnEnable()
    {
        events.UpdateQuestionAnswer += UpdateAnswers;
    }

    private void OnDisable()
    {
        events.UpdateQuestionAnswer -= UpdateAnswers;
    }

    private void Awake()
    {
        if (events.level == 1) { events.CurrentFinalScore = 0; }
    }

    private void Start()
    {
        events.StartupHighscore = PlayerPrefs.GetInt(GameUtility.SavePrefKey);

        timerDefaultColor = timerText.color;
        LoadData();

        timerStateParaHash = Animator.StringToHash("TimerState");

        var seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
        UnityEngine.Random.InitState(seed);

        Display();

        uiManager = FindObjectOfType<UIManager>();
    }

    #endregion

    public void UpdateAnswers(AnswerData newAnswer)
{
    // Kiểm tra xem currentQuestion có nằm trong phạm vi của mảng data.Questions hay không
    if (currentQuestion < 0 || currentQuestion >= data.Questions.Length)
    {
        Debug.LogError("Current question index is out of bounds.");
        return;
    }

    // Kiểm tra xem AnswerData có tồn tại trong danh sách PickedAnswers hay không
    if (PickedAnswers.Contains(newAnswer))
    {
        Debug.LogWarning("New answer is already in the PickedAnswers list.");
        return;
    }

    if (data.Questions[currentQuestion].Type == AnswerType.Single)
    {
        foreach (var answer in PickedAnswers)
        {
            if (answer != newAnswer)
            {
                answer.Reset();
            }
        }
        PickedAnswers.Clear();
        PickedAnswers.Add(newAnswer);
    }
    else
    {
        bool alreadyPicked = PickedAnswers.Exists(x => x == newAnswer);
        if (alreadyPicked)
        {
            PickedAnswers.Remove(newAnswer);
        }
        else
        {
            PickedAnswers.Add(newAnswer);
        }
    }
}


    public void EraseAnswers()
    {
        PickedAnswers = new List<AnswerData>();
    }

    void Display()
    {
        EraseAnswers();
        var question = GetRandomQuestion();

        if (events.UpdateQuestionUI != null)
        {
            events.UpdateQuestionUI(question);
        }
        else { Debug.LogWarning("Ups! Something went wrong while trying to display new Question UI Data. GameEvents.UpdateQuestionUI is null. Issue occured in GameManager.Display() method."); }

        if (question.UseTimer)
        {
            UpdateTimer(question.UseTimer);
        }
    }

    public void Accept()
    {
        UpdateTimer(false);
        bool isCorrect = CheckAnswers();
        FinishedQuestions.Add(currentQuestion);

        if (isCorrect)
        {
            UpdateScore(data.Questions[currentQuestion].AddScore);
        }


        if (IsFinished)
        {
            events.level++;
            if (events.level > GameEvents.maxLevel)
            {
                events.level = 1;
            }
            SetHighscore();
        }

        var type
            = (IsFinished)
            ? UIManager.ResolutionScreenType.Finish
            : (isCorrect) ? UIManager.ResolutionScreenType.Correct
            : UIManager.ResolutionScreenType.Incorrect;

        events.DisplayResolutionScreen?.Invoke(type, data.Questions[currentQuestion].AddScore);

        AudioManager.Instance.PlaySound((isCorrect) ? "CorrectSFX" : "IncorrectSFX");

        if (type != UIManager.ResolutionScreenType.Finish)
        {
            if (IE_WaitTillNextRound != null)
            {
                StopCoroutine(IE_WaitTillNextRound);
            }
            IE_WaitTillNextRound = WaitTillNextRound();
            StartCoroutine(IE_WaitTillNextRound);
        }
    }

    #region Timer Methods

    void UpdateTimer(bool state)
    {
        switch (state)
        {
            case true:
                IE_StartTimer = StartTimer();
                StartCoroutine(IE_StartTimer);

                timerAnimtor.SetInteger(timerStateParaHash, 2);
                break;
            case false:
                if (IE_StartTimer != null)
                {
                    StopCoroutine(IE_StartTimer);
                }

                timerAnimtor.SetInteger(timerStateParaHash, 1);
                break;
        }
    }

    IEnumerator StartTimer()
    {
        var totalTime = data.Questions[currentQuestion].Timer;
        var timeLeft = totalTime;

        timerText.color = timerDefaultColor;
        while (timeLeft > 0)
        {
            timeLeft--;

            AudioManager.Instance.PlaySound("CountdownSFX");

            if (timeLeft < totalTime / 2 && timeLeft > totalTime / 4)
            {
                timerText.color = timerHalfWayOutColor;
            }
            if (timeLeft < totalTime / 4)
            {
                timerText.color = timerAlmostOutColor;
            }

            timerText.text = timeLeft.ToString();
            yield return new WaitForSeconds(1.0f);
        }
        Accept();
    }

    IEnumerator WaitTillNextRound()
    {
        yield return new WaitForSeconds(GameUtility.ResolutionDelayTime);
        Display();
    }

    #endregion

    bool CheckAnswers()
    {
        return CompareAnswers();
    }

    bool CompareAnswers()
    {
        if (PickedAnswers.Count > 0)
        {
            List<int> c = data.Questions[currentQuestion].GetCorrectAnswers();
            List<int> p = PickedAnswers.Select(x => x.AnswerIndex).ToList();

            // Số lượng đáp án đúng đã chọn
            int correctPickedCount = p.Count(x => c.Contains(x));

            // Số lượng đáp án đúng tổng cộng
            int totalCorrectCount = c.Count;

            // Trả về true nếu số lượng đáp án đúng đã chọn bằng số lượng đáp án đúng tổng cộng
            return correctPickedCount == totalCorrectCount;
        }
        return false;
    }


    void LoadData()
    {
        var path = Path.Combine(GameUtility.FileDir, GameUtility.FileName + events.level + ".xml");
        data = Data.Fetch(path);
    }

    public void RestartGame()
    {
        if (events.level == 1) { events.CurrentFinalScore = 0; }

        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public void QuitGame()
    {
        events.level = 1;

        Application.Quit();
    }

    private void SetHighscore()
    {
        var highscore = PlayerPrefs.GetInt(GameUtility.SavePrefKey);
        if (highscore < events.CurrentFinalScore)
        {
            PlayerPrefs.SetInt(GameUtility.SavePrefKey, events.CurrentFinalScore);
        }

        // Gửi điểm về server
        if (highscore < 0)
        {
            StartCoroutine(SendScoreToServer(playerId, 0));
        }
        StartCoroutine(SendScoreToServer(playerId, events.CurrentFinalScore));
    }

    private IEnumerator SendScoreToServer(string playerId, int score)
    {
        string url = "http://localhost:3000/submit-score"; // Thay đổi URL nếu cần

        ScoreData scoreData = new ScoreData { playerId = playerId, score = score };
        string jsonData = JsonUtility.ToJson(scoreData);

        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonData);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                Debug.Log("Score submitted successfully");
            }
            else
            {
                Debug.LogError("Failed to submit score: " + request.error);
            }
        }
    }

    [System.Serializable]
    private class ScoreData
    {
        public string playerId; // Đã thêm trường này
        public int score;
    }




    private void UpdateScore(int add)
    {
        events.CurrentFinalScore += add;
        events.ScoreUpdated?.Invoke();
    }

    #region Getters

    Question GetRandomQuestion()
    {
        var randomIndex = GetRandomQuestionIndex();
        currentQuestion = randomIndex;

        return data.Questions[currentQuestion];
    }

    int GetRandomQuestionIndex()
    {
        var random = 0;
        if (FinishedQuestions.Count < data.Questions.Length)
        {
            do
            {
                random = UnityEngine.Random.Range(0, data.Questions.Length);
            } while (FinishedQuestions.Contains(random) || random == currentQuestion);
        }
        return random;
    }
    private bool hasUsedFiftyFifty = false;

    public void UseFiftyFifty()
    {
        if (hasUsedFiftyFifty)
        {
            return;
        }

        var question = data.Questions[currentQuestion];
        var correctAnswerIndex = question.Answers.ToList().FindIndex(a => a.IsCorrect);

        var wrongAnswers = new List<int>();
        for (int i = 0; i < question.Answers.Length; i++)
        {
            if (i != correctAnswerIndex)
            {
                wrongAnswers.Add(i);
            }
        }

        var answersToRemove = wrongAnswers.OrderBy(x => UnityEngine.Random.value).Take(2).ToList();

        if (uiManager != null)
        {
            uiManager.HideAnswers(answersToRemove);
        }

        hasUsedFiftyFifty = true;
        uiManager.DisableFiftyFiftyButton();
    }
    #endregion
}
