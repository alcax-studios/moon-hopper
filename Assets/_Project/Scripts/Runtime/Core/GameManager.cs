using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Alcax.Core
{
    public enum GameState { Title, Playing, Paused, Ended, Dialogue }

    /// <summary>Owns the game state machine (Title, Playing, Paused, Ended) and the run stats.</summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        /// <summary>Set before a scene reload to go straight into play (Restart).</summary>
        public static bool SkipTitleOnLoad;

        [SerializeField] GameObject titleCamera;

        public GameState State { get; private set; } = GameState.Title;
        public int Kills { get; private set; }
        public float TimeSurvived { get; private set; }

        public event Action<GameState, GameState> StateChanged;
        public event Action<int> KillsChanged;

        /// <summary>Set by ShellUI so scene changes cross a fade.</summary>
        public Action<Action> FadeOutThen;

        public static bool IsPlaying => Instance == null || Instance.State == GameState.Playing;

        /// <summary>Gameplay input is ignored until this time (so the click that closes a menu doesn't also swing).</summary>
        public static float InputResumeTime;
        public static bool InputLive => IsPlaying && Time.unscaledTime >= InputResumeTime;

        void Awake()
        {
            Instance = this;
            Time.timeScale = 1f;
        }

        void Start()
        {
            // No title screen: every map starts in play at its start point
            SkipTitleOnLoad = false;
            SetState(GameState.Playing, force: true);
        }

        void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;

            switch (State)
            {
                case GameState.Title:
                    bool anyKey = kb != null && kb.anyKey.wasPressedThisFrame && !kb.escapeKey.wasPressedThisFrame;
                    bool click = mouse != null && mouse.leftButton.wasPressedThisFrame;
                    if (anyKey || click) SetState(GameState.Playing);
                    break;
                case GameState.Playing:
                    TimeSurvived += Time.deltaTime;
                    if (kb != null && kb.escapeKey.wasPressedThisFrame) SetState(GameState.Paused);
                    break;
                case GameState.Paused:
                    if (kb != null && kb.escapeKey.wasPressedThisFrame) SetState(GameState.Playing);
                    break;
                case GameState.Dialogue:
                    if (kb != null && kb.escapeKey.wasPressedThisFrame) SetState(GameState.Playing);
                    break;
                case GameState.Ended:
                    if (kb != null && kb.rKey.wasPressedThisFrame) Restart();
                    break;
            }

            // Re-lock the cursor if the window lost and regained focus mid-play.
            if (State == GameState.Playing && mouse != null && mouse.leftButton.wasPressedThisFrame)
                ApplyCursor();
        }

        public void SetState(GameState next, bool force = false)
        {
            if (!force && next == State) return;
            var prev = State;
            State = next;
            if (next == GameState.Playing && prev != GameState.Playing) InputResumeTime = Time.unscaledTime + 0.2f;

            Time.timeScale = next == GameState.Paused ? 0f : 1f;
            if (titleCamera != null) titleCamera.SetActive(next == GameState.Title);
            ApplyCursor();

            StateChanged?.Invoke(prev, next);
        }

        void ApplyCursor()
        {
            bool locked = State == GameState.Playing;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        public void Resume() => SetState(GameState.Playing);

        public void AddKill()
        {
            Kills++;
            KillsChanged?.Invoke(Kills);
        }

        public void EndRun()
        {
            if (State == GameState.Ended) return;
            SetState(GameState.Ended);
        }

        /// <summary>The playable maps (they must be in the build settings).</summary>
        public static readonly (string name, string path)[] Maps =
        {
            ("Atrium", "Assets/_Project/Scenes/Atrium.unity"),
            ("AI Playground", "Assets/_Project/Scenes/Playground.unity"),
        };

        /// <summary>Load a map and drop straight into play (no title screen).</summary>
        public void LoadMap(string scenePath)
        {
            void DoLoad()
            {
                SkipTitleOnLoad = true;
                Time.timeScale = 1f;
                SceneManager.LoadScene(scenePath);
            }
            if (FadeOutThen != null) FadeOutThen(DoLoad);
            else DoLoad();
        }

        public void Restart() => Reload(skipTitle: true);
        public void ToTitle() => Reload(skipTitle: false);

        void Reload(bool skipTitle)
        {
            void DoLoad()
            {
                SkipTitleOnLoad = skipTitle;
                Time.timeScale = 1f;
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            }

            if (FadeOutThen != null) FadeOutThen(DoLoad);
            else DoLoad();
        }

        public void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
