using System.Collections;
using UnityEngine;

namespace SnakyColors
{
    public class ObstaclePlayerManager : MonoBehaviour
    {
        [HideInInspector]
        public Color playerColor;
        
        public Sprite[] sprite; 
        public float headScaleFactor = 2f;
        public float trailerWidthFactorToLose = 0.9f;

        public SpriteRenderer sr;
        private TrailRenderer tr; 
         
        private Menus menus; 

        private void Awake()
        { 
        }  

        private void Start()
        {
            menus = GameObject.Find("GameManager").GetComponent<Menus>();
            sr = GetComponent<SpriteRenderer>();
            tr = transform.Find("TrailEffect").GetComponent<TrailRenderer>();

            LoadPlayerTexture();  

            int rand = Random.Range(0, ColorLib.Colors.Length);
            playerColor = ColorLib.Colors[rand]; 
            tr.startColor = playerColor;
            tr.endColor = playerColor; 
            sr.color = playerColor; 
        }

        public void SetPlayerColor()
        { 
            tr.startColor = playerColor;
            tr.endColor = playerColor;
            sr.color = playerColor;

            // Make sure the trail gradient is solid
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new GradientColorKey[] { new GradientColorKey(playerColor, 0f), new GradientColorKey(playerColor, 1f) },
                new GradientAlphaKey[] { new GradientAlphaKey(playerColor.a, 0f), new GradientAlphaKey(playerColor.a, 1f) }
            );
            tr.colorGradient = gradient;
        }  
        void LoadPlayerTexture()
        {
            SpriteRenderer renderer = GetComponent<SpriteRenderer>();

            if (PlayerPrefs.GetInt("ChoosenItem", 0) == 0)
            {
                renderer.sprite = sprite[0];
            }
            else
            {
                int choosenItem = PlayerPrefs.GetInt("ChoosenItem", 0) - 1;
                renderer.sprite = sprite[choosenItem];
            }

            // Normalize head sprite scale
            float baseSize = 1f / renderer.sprite.bounds.size.x;
            transform.localScale = Vector3.one * baseSize * headScaleFactor;

            // Now calculate head width manually using sprite rect + scale
            float headWidth = renderer.sprite.bounds.size.x * transform.localScale.x;

            // Adjust trail width slightly less than head
            TrailRenderer tr = transform.Find("TrailEffect").GetComponent<TrailRenderer>();
            tr.startWidth = headWidth * trailerWidthFactorToLose;
            tr.endWidth = headWidth * trailerWidthFactorToLose;

            // Adjust CircleCollider2D radius to match scaled head
            CircleCollider2D collider = GetComponent<CircleCollider2D>();
            if (collider != null)
            {
                collider.radius = (renderer.sprite.bounds.size.x * 0.5f) * 0.75f;
            }
        } 
      
        public void DisablePlayerMovement()
        {
            if (IsPlayerAvailable())
            {
                GetComponent<PlayerMovement>().enabled = false;
            }
        }

        public void EnablePlayerMovement()
        {
            if (IsPlayerAvailable())
            {
                GetComponent<PlayerMovement>().enabled = true;
            }
        }

        public bool IsPlayerAvailable()
        {
            return this != null;
        }

        public void Disable()
        {
            gameObject.SetActive(false);
        }

        void OnTriggerEnter2D(Collider2D col)
        {
            if (col.gameObject.name.Equals("ChangeColor"))
            {
                Color newCol = col.gameObject.GetComponent<SpriteRenderer>().color;
                this.playerColor = new Color(newCol.r, newCol.g, newCol.b, 1f);
                this.SetPlayerColor();

                GameObject[] gameArenas = GameObject.FindGameObjectsWithTag("Arena");
                foreach (GameObject gameArena in gameArenas)
                {
                    if (gameArena.transform.position.y < transform.position.y)
                    {
                        Destroy(gameArena, 2f);
                    }
                }

                InstantiateNewGameArena();
                AudioManager.Instance.Play(SoundType.ArenaComplete);
                Destroy(col.gameObject);
                return;
            }
            if (col.gameObject.name.Equals("Star"))
            {
                PlayerPrefs.SetInt("TotalNumberOfStars", (PlayerPrefs.GetInt("TotalNumberOfStars") + 1));
                PlayerPrefs.SetInt("NumberOfStars", (PlayerPrefs.GetInt("NumberOfStars") + 1));

                GameObject starExplosionParticle = col.gameObject.transform.Find("StarParticle").gameObject;
                starExplosionParticle.transform.parent = null;
                starExplosionParticle.SetActive(true);

                AudioManager.Instance.Play(SoundType.StarCollect);
                Destroy(col.gameObject);

                Vars.starScore++; 
                return;
            }
            if (Mathf.Approximately(playerColor.r, col.gameObject.GetComponent<SpriteRenderer>().color.r))
            {
                AudioManager.Instance.Play(SoundType.ObstacleDestroy);

                GameObject obstacleExplosionParticle = col.gameObject.transform.Find("ExplosionParticle").gameObject;
                ParticleSystem.MainModule psmain = obstacleExplosionParticle.GetComponent<ParticleSystem>().main;
                psmain.startColor = col.gameObject.GetComponent<SpriteRenderer>().color;
                obstacleExplosionParticle.transform.parent = null;
                obstacleExplosionParticle.SetActive(true);

                Vars.score++; 

                PlayerPrefs.SetInt("DestroyedObjects", (PlayerPrefs.GetInt("DestroyedObjects") + 1));
                if (PlayerPrefs.GetInt("Vibration") == 1)
                {
                    Handheld.Vibrate();
                }
                Destroy(col.gameObject);
            }
            else
            {
                StartCoroutine(HandlePlayerDeath());
            }
        }

        IEnumerator HandlePlayerDeath()
        {
            // Detach trail
            GameObject trailEffect = transform.Find("TrailEffect").gameObject;
            trailEffect.transform.parent = null;

            TrailRenderer trail = trailEffect.GetComponent<TrailRenderer>();
            DestroyExplosionParticle destroyScript = trailEffect.GetComponent<DestroyExplosionParticle>();

            float trailTime = 1.5f; // or trail.time if using TrailRenderer
            if (destroyScript != null)
                destroyScript.Init(trailTime); 

            this.DisablePlayerMovement();
            //GetComponent<MeshRenderer>().enabled = false;
           // GetComponent<Collider>().enabled = false;

            // Wait for trail to finish
            yield return new WaitForSeconds(trailTime);

            Destroy(gameObject);
            Destroy(trailEffect);
            Menus.Instance.GameOver();
        }  

        private void InstantiateNewGameArena()
        {
            int arenaNumber = Random.Range(1, 16);
            GameObject newArena = Instantiate(Resources.Load("Arena" + arenaNumber)) as GameObject;
            newArena.tag = "Arena";
            newArena.name = "Arena" + arenaNumber;
            newArena.transform.position = new Vector2(0, Vars.currentArena * 15);
            Vars.currentArena++;
        }
    }
}