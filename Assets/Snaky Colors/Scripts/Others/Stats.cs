using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SnakyColors
{
    public class Stats : MonoBehaviour
    {
        //This script will load the stats each time player enters the stats menu
        [SerializeField]
        private Text lastScore = null;
        [SerializeField]
        private Text bestScore = null;
        [SerializeField]
        private Text itemsOwned = null;
        [SerializeField]
        private Text starsCollected = null;
        [SerializeField]
        private Text destroyedObjects = null;
        [SerializeField]
        private Text gamesPlayed = null;

        void OnEnable()
        {
            lastScore.text = "LAST SCORE: " + PlayerPrefs.GetInt("LastScore");
            bestScore.text = "BEST SCORE: " + PlayerPrefs.GetInt("BestScore");
            itemsOwned.text = "ITEMS OWNED: " + (PlayerPrefs.GetInt("ItemsOwned") + 1) + "/25";
            starsCollected.text = "STARS COLLECTED: " + PlayerPrefs.GetInt("TotalNumberOfStars");
            destroyedObjects.text = "DESTROYED OBJECTS: " + PlayerPrefs.GetInt("DestroyedObjects");
            gamesPlayed.text = "GAMES PLAYED: " + PlayerPrefs.GetInt("GamesPlayed");
        }
    }
}
