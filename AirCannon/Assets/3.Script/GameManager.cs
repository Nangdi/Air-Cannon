using System;
using UnityEngine;

// 게임 시작/종료 신호를 한곳에서 관리하는 공용 매니저.
// 실제 시작·종료 조건(버튼, 센서, 통신 등)은 프로젝트별로 StartGame()/EndGame()을 호출해 연결한다.
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }
    public event Action OnGameStart; // 게임 시작 이벤트
    public event Action OnGameEnd;   // 게임 종료 이벤트

    [Tooltip("게임 시간의 흐름 배속. 값을 바꾸면 Time.timeScale에 바로 반영된다.")]
    [SerializeField] private float gameTimeScale = 1f;

    public bool IsPlaying { get; private set; }

    public float GameTimeScale
    {
        get { return gameTimeScale; }
        set
        {
            gameTimeScale = value;
            Time.timeScale = gameTimeScale;
        }
    }

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
        if (Instance == this)
            Instance = null;
    }
    private void OnDisable()
    {
        // 도메인 리로드/종료 시점엔 GameTimer가 먼저 파괴돼 Instance가 null일 수 있다.
        if (GameTimer.Instance != null)
            GameTimer.Instance.OnTimeOver -= GameManager_OnTimeOver;
    }
    private void Start()
    {
        Time.timeScale = gameTimeScale; // 게임 시작 시 시간 흐름을 설정

        if (GameTimer.Instance != null)
            GameTimer.Instance.OnTimeOver += GameManager_OnTimeOver;
    }

    // 게임 시작 알림. 중복 호출은 무시한다.
    public void StartGame()
    {
        if (IsPlaying) return;

        IsPlaying = true;
        OnGameStart?.Invoke();
    }

    // 게임 종료 알림. 시작하지 않은 상태에서의 호출은 무시한다.
    public void EndGame()
    {
        if (!IsPlaying) return;

        IsPlaying = false;
        OnGameEnd?.Invoke();
    }

    // 제한 시간 종료 시 게임 종료로 이어준다.
    private void GameManager_OnTimeOver()
    {
        EndGame();
    }

#if UNITY_EDITOR
    // 인스펙터에서 배속을 바꿨을 때 플레이 중에도 바로 반영되도록 한다.
    private void OnValidate()
    {
        if (Application.isPlaying)
            Time.timeScale = gameTimeScale;
    }
#endif
}
