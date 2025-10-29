using System.Collections; 
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SnakyColors
{ 
    public enum TransitionAction { StartGame, BackToMainMenu, Replay }

    public class Menus : MonoBehaviour
    {
        [Header("UI Panels")]
        [SerializeField] private TextMeshProUGUI mainMenuBestScore;
        [SerializeField] private GameObject mainMenu;
        [SerializeField] private GameObject statsMenu;
        [SerializeField] private GameObject shopMenu;
        [SerializeField] private GameObject giftMenu;
        [SerializeField] private GameObject settingsMenu;
        [SerializeField] private GameObject gameplayMenu;  
        [SerializeField] private GameObject pauseButton;
        [SerializeField] private GameObject pauseMenu;
        [SerializeField] private GameObject gameOverMenu;

        [Header("Fade Transition")] 
        [SerializeField] private Image transitionImage;
        [SerializeField] private float fadeSpeed = 3f; // Speed of the fade

        [HideInInspector]
        public bool isGameOver;
        private string currentMode;

        [HideInInspector]
        public static Menus Instance { get; private set; }


        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this; 
            DontDestroyOnLoad(gameObject);
             
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60; 
        }

        private void Start()
        {
            mainMenuBestScore.text = "BEST: " + PlayerPrefs.GetInt("BestScore"); 
        }

        #region Menu Navigation 

        public void StartTheGameTransition(string gameMode)
        {
            currentMode = gameMode;
            StartCoroutine(FadeSequence(TransitionAction.StartGame));
            AudioManager.Instance.Play(SoundType.Button2);
        }

        public void ReplayTransition()
        {
            Time.timeScale = 1;
            StartCoroutine(FadeSequence(TransitionAction.Replay));
            AudioManager.Instance.Play(SoundType.Button);
        }

        public void BackToTheMainMenuTransition()
        {
            Time.timeScale = 1;
            StartCoroutine(FadeSequence(TransitionAction.BackToMainMenu));
            AudioManager.Instance.Play(SoundType.Button);
        } 

        public void StartTheGame(string mode)
        {
            currentMode = mode;
            mainMenu.SetActive(false);
            gameplayMenu.SetActive(true); 
            GameManager.Instance.StartMode(mode);
        }

        public void Replay()
        {
            BackToTheMainMenu();
            StartTheGame(currentMode);
        }

        public void BackToTheMainMenu()
        {
            gameplayMenu.SetActive(false);
            pauseButton.SetActive(true); 
            pauseMenu.SetActive(false);
            gameOverMenu.SetActive(false);
            mainMenu.SetActive(true);

            GameManager.Instance.EndGame();

            mainMenuBestScore.text = "BEST: " + PlayerPrefs.GetInt("BestScore");
        }

        public void ShowStatsMenu()
        {
            AudioManager.Instance.Play(SoundType.Whoosh1);
            statsMenu.SetActive(true); 
        }

        public void HideStatsMenu()
        {
            statsMenu.SetActive(false);
            AudioManager.Instance.Play(SoundType.Button);
        }

        public void ShowSettingsMenu()
        {
            AudioManager.Instance.Play(SoundType.Whoosh1);
            settingsMenu.SetActive(true);
        }

        public void HideSettingsMenu()
        {
            settingsMenu.SetActive(false);
            AudioManager.Instance.Play(SoundType.Button);
        }

        public void ShowPauseMenu()
        {
            pauseButton.SetActive(false);
            GameManager.Instance.PauseGame();
            pauseMenu.SetActive(true);
            AudioManager.Instance.Play(SoundType.Button);
        }

        public void HidePauseMenu()
        {
            pauseMenu.SetActive(false);
            pauseButton.SetActive(true);
            GameManager.Instance.ResumeGame();
            AudioManager.Instance.Play(SoundType.Button);
        }
            
        public void GameOver()
        {
            pauseButton.SetActive(false);
            Invoke("ShowGameOverMenu", 2f);
        }

        private void ShowGameOverMenu()
        {
            gameOverMenu.SetActive(true);
        }

        #endregion
         
        private IEnumerator FadeSequence(TransitionAction action)
        {
            if (transitionImage == null)
            {
                Debug.LogError("Transition Image is not assigned on Menus component.");
                yield break;
            }

            transitionImage.raycastTarget = true; // Block input during transition
            float alpha = transitionImage.color.a;
             
            while (alpha < 1f)
            {
                alpha += Time.deltaTime * fadeSpeed;
                transitionImage.color = new Color(0, 0, 0, alpha);
                yield return null;
            }
            alpha = 1f;
             
            switch (action)
            {
                case TransitionAction.StartGame:
                    StartTheGame(currentMode);
                    break;
                case TransitionAction.BackToMainMenu:
                    BackToTheMainMenu();
                    break;
                case TransitionAction.Replay:
                    // Replay calls BackToTheMainMenu then StartTheGame
                    Replay();
                    break;
            }

            yield return null; // Wait one frame for scene/game state changes to fully process
             
            while (alpha > 0f)
            {
                alpha -= Time.deltaTime * fadeSpeed;
                transitionImage.color = new Color(0, 0, 0, alpha);
                yield return null;
            }
             
            transitionImage.color = new Color(0, 0, 0, 0);
            transitionImage.raycastTarget = false; // Allow input again
        }
    }
}