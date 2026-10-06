using UnityEngine;
using System.Collections;

public class BackgroundAudio : MonoBehaviour
{
    [Header("References")]
    public AudioSource audioSource;

    [Header("Settings")]
    [Range(0f, 1f)]
    public float targetVolume = 0.15f;
    public float fadeInDuration = 3f;
    public bool loop = true;
    public bool playOnStart = true;

    void Start()
    {
        if (audioSource == null) return;

        audioSource.loop = loop;
        audioSource.spatialBlend = 0f; // fully 2D/ambient, not tied to a world position
        audioSource.volume = 0f;

        if (playOnStart)
        {
            audioSource.Play();
            StartCoroutine(FadeIn());
        }
    }

    IEnumerator FadeIn()
    {
        float elapsed = 0f;
        while (elapsed < fadeInDuration)
        {
            elapsed += Time.deltaTime;
            audioSource.volume = Mathf.Lerp(0f, targetVolume, elapsed / fadeInDuration);
            yield return null;
        }
        audioSource.volume = targetVolume;
    }
}
