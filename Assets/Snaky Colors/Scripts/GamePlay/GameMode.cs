using UnityEngine;

namespace SnakyColors
{
    public abstract class GameMode : MonoBehaviour
    {
        public abstract string ModeName { get; }
        public abstract bool IsInitialized { get; set; }

        public virtual void Initialize()
        { 
        } 
        public virtual void StartMode() { }
        public virtual void UpdateMode() { }
        public virtual void EndMode() { }

        // Common controls
        public virtual void PauseMode() { }
        public virtual void ResumeMode() { }
        public virtual void RestartMode() { }
        public virtual void GameOverMode() { }
    } 
}