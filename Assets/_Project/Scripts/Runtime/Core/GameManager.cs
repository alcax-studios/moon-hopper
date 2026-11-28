using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Alcax.Core
{
    public enum GameState { Title, Playing, Paused, Ended, Dialogue, Menu, Transfer, Prompt }

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
        int relockFrames;
        public static bool InputLive => IsPlaying && Time.unscaledTime >= InputResumeTime && !AdminSettings.ConsoleOpen;

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
                case GameState.Menu:
                case GameState.Transfer:
                case GameState.Prompt:
                    if (kb != null && kb.escapeKey.wasPressedThisFrame) { SetState(GameState.Playing); relockFrames = 6; }
                    break;
                case GameState.Ended:
                    if (kb != null && kb.rKey.wasPressedThisFrame) Restart();
                    break;
            }

            // Keep the cursor matching the state: locked in play (after closing a menu, a refocus, or anything that
            // freed it), free in menus. The Editor can drop the lock on its own, so this runs every frame.
bool wantLocked = State == GameState.Playing && !AdminSettings.ConsoleOpen;
            // The Editor frees the mouse on Esc itself, after we've locked it, while the lock state still reads Locked.
            // After closing something with Esc, drop and re-take the lock for a few frames to win it back.
            if (relockFrames > 0)
            {
                relockFrames--;
                if (wantLocked) { Cursor.lockState = CursorLockMode.None; Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
            }
            if (Application.isFocused && (wantLocked ? Cursor.lockState != CursorLockMode.Locked || Cursor.visible : Cursor.lockState != CursorLockMode.None || !Cursor.visible))
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

        /// <summary>Re-apply the cursor lock (after the admin console opens or closes).</summary>
        public static void RefreshCursor() { if (Instance != null) Instance.ApplyCursor(); }

        void ApplyCursor()
        {
            bool locked = State == GameState.Playing && !AdminSettings.ConsoleOpen;
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
            ("Kelxor Exterior", "Assets/_Project/Scenes/Kelxor.unity"),
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
