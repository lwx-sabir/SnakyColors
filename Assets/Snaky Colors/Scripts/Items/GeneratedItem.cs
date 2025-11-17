using UnityEngine;
using System.Collections;
// using UnityEditor; // This should not be in a runtime script.

namespace SnakyColors
{
    public class GeneratedItem : MonoBehaviour
    {
        [HideInInspector] public ItemData data;
         
        [Header("Particles")]
        [SerializeField] private ParticleSystem collectParticle;
        [HideInInspector] public int Id;

        // References set at runtime 
        private FruitCollectEffect collectEffect; 
        private Vector3 originalScale;  

        private void Awake()
        {
            collectEffect = GetComponent<FruitCollectEffect>(); 
            originalScale = transform.localScale;
            if (collectParticle == null) collectParticle = GetComponent<ParticleSystem>();
        }

        private void OnEnable()
        {
            transform.localScale = originalScale;  
            if (collectEffect != null) collectEffect.enabled = true; 
            data = null;

            if (collectParticle != null)
            {
                collectParticle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                collectParticle.gameObject.SetActive(false);
            }
        }

        public void SetData(ItemData newItemData, Transform player = null)
        {
            data = newItemData; 
        }  
      
        // Remote collection: play VFX into a given head without applying local PlayerStats
        public void PlayRemoteCollect(Transform collectorHead, bool isPlayer)
        { 
            if (collectorHead != null && TryGetComponent<FruitCollectEffect>(out var effect))
            {
                effect.playerHead = collectorHead;

                if (isPlayer && data.collectSound != null && AudioManager.Instance != null)
                {
                    AudioManager.Instance.PlayClip(data.collectSound, Random.Range(0.92f, 1.0f));
                } 
                var txt = data != null ? data.scoreText : "0";
                var color = data != null ? data.itemColor : Color.white;
                var type = data != null ? data.collectibleType : CollectibleType.Basic;
                var icon = data != null ? data.icon : null;
                float dur = effect.PlayCollectAnimation(txt, color, type, icon);
                StartCoroutine(ReturnToPoolAfterDelay(dur * 1.07f));
            }
            else
            {
                ReturnToPool();
            }
        } 
         
        private IEnumerator CollectAndReturnToPool()
        {
            if (collectEffect != null)
            {
                float animationDuration = collectEffect.PlayCollectAnimation(
                    data.scoreText,
                    data.itemColor,
                    data.collectibleType,
                    data.icon
                );

                yield return new WaitForSeconds(animationDuration);
            }
            ReturnToPool();
        }

        private IEnumerator ReturnToPoolAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);
            ReturnToPool();
        }

        private void PlayCollectParticle()
        {
            if (collectParticle == null) return;

            collectParticle.gameObject.SetActive(true);

            var main = collectParticle.main;
            // main.stopAction = ParticleSystemStopAction.Callback; // Callback is complex, disable/delay is safer

            collectParticle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            collectParticle.Clear(true);
            collectParticle.Simulate(0f, true, true);
            collectParticle.Play(true);
        }

        public void ReturnToPool()
        {
            if (!gameObject.activeSelf) return; // Prevent double calls

            ResetItemState();
            gameObject.SetActive(false);
        }

        private void ResetItemState()
        {
            transform.localScale = originalScale; 
            if (collectParticle != null)
            {
                collectParticle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                collectParticle.gameObject.SetActive(false);
            }
             
            data = null; 
        } 
    }
}
