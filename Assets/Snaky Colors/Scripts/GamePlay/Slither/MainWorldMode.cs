using System;
using UnityEngine;

namespace SnakyColors
{
    public class MainWorldMode : GameMode
    {
        public override string ModeName => "MainWorld";
        public override bool IsInitialized { get; set; } = false;

        [Header("Network Settings")]
        [SerializeField] private string defaultSkinId = "cobra";
        [Tooltip("Leave empty to auto-generate a GUID each session.")]
        [SerializeField] private string playerIdOverride = string.Empty;

        private NetworkClient client;

        public override void Initialize()
        {
            if (IsInitialized) return;
            // Find a NetworkClient within this mode prefab hierarchy (recommended)
            client = GetComponentInChildren<NetworkClient>(true);
            if (client == null)
            {
                // Fallback: attach one here (expects references to be assigned via inspector on this object)
                client = gameObject.AddComponent<NetworkClient>();
            }
            IsInitialized = true;
        }

        public override async void StartMode()
        {
            OnScreenDebug.Log("client started: ");
            if (client == null) Initialize();
            OnScreenDebug.Log("client init: ");
            string pid = string.IsNullOrWhiteSpace(playerIdOverride) ? Guid.NewGuid().ToString() : playerIdOverride;
            await client.ConnectAsync(pid, defaultSkinId);
        }

        public override void UpdateMode() { }

        public override async void EndMode()
        {
            if (client != null)
            {
                await client.DisconnectAsync();
            }
        }

        public override void PauseMode() { /* keep connection while paused */ }
        public override void ResumeMode() { }

        public override async void GameOverMode()
        {
            if (client != null)
            {
                await client.DisconnectAsync();
            }
        }
    }
}

