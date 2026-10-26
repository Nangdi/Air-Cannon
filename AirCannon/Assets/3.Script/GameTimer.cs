using System;
using UnityEngine;

// 게임 진행 시간을 재는 공용 타이머.
// GameManager의 시작/종료 이벤트에 맞춰 자동으로 켜지고 꺼진다.
// 제한 시간(timeLimit)은 프로젝트마다 다르므로 인스펙터에서 설정한다.
public class GameTimer : MonoBehaviour
{
    private static GameTimer instance;

    // 다른 스크립트가 처음 접근하는 시점에 씬에서 한 번 찾아서 보완하는 lazy singleton getter입니다.
    public static GameTimer Instance
    {
        get
        {
            if (instance == null)
                instance = FindObjectOfType<GameTimer>();

            return instance;
        }
        private set
        {
            instance = value;
        }
    }

    [Header("Time")]
    [Tooltip("제한 시간(초). 0 이하이면 시간 제한 없이 계속 잰다.")]
    [SerializeField] private float timeLimit = 0f;
    [SerializeField] private float currentTime;

    public bool IsRunning { get; private set; }
    public float TimeLimit => timeLimit;
    public float CurrentTime
    {
        get { return currentTime; }
        private set { currentTime = value; }
    }
    // 남은 시간. 제한 시간이 없으면 항상 0을 돌려준다.
    public float RemainingTime => timeLimit > 0f ? Mathf.Max(0f, timeLimit - currentTime) : 0f;

    public event Action<float> OnTimeChanged;
    public event Action OnTimeOver;

    // 제한 시간 도달 이벤트를 매 프레임 중복 발생시키지 않기 위한 플래그
    private bool timeOverFired;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }
    private void OnDestroy()
    {
        // 현재 싱글톤이 파괴될 때는 정적 참조도 같이 비워서 다음 탐색이 가능하게 합니다.
        if (Instance == this)
            Instance = null;

        if (GameManager.Instance == null) return;
        GameManager.Instance.OnGameStart -= Timer_OnGameStart;
        GameManager.Instance.OnGameEnd -= Timer_OnGameEnd;
    }
    private void Start()
    {
        if (GameManager.Instance == null) return;
        GameManager.Instance.OnGameStart += Timer_OnGameStart;
        GameManager.Instance.OnGameEnd += Timer_OnGameEnd;
    }
    private void Update()
    {
        if (!IsRunning) return;

        CurrentTime += Time.deltaTime;
        OnTimeChanged?.Invoke(currentTime);

        // 제한 시간이 설정된 경우에만 종료를 알린다. (0 이하 = 무제한)
        if (timeLimit > 0f && !timeOverFired && CurrentTime >= timeLimit)
        {
            timeOverFired = true;
            Debug.Log("Time Over!");
            OnTimeOver?.Invoke();
        }
    }
    public void StartTimer()
    {
        CurrentTime = 0f;
        timeOverFired = false;
        IsRunning = true;
    }

    public void StopTimer()
    {
        IsRunning = false;
    }

    public void ResetTimer()
    {
        CurrentTime = 0f;
        timeOverFired = false;
        OnTimeChanged?.Invoke(currentTime);
    }

    public void SetTimeLimit(float seconds)
    {
        timeLimit = seconds;
    }

    public float GetCurrentTime()
    {
        return CurrentTime;
    }
    private void Timer_OnGameStart()
    {
        StartTimer();
    }
    private void Timer_OnGameEnd()
    {
        StopTimer();
        ResetTimer();
    }
}
