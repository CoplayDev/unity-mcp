using UnityEngine;

/// <summary>
/// Handles pollination trigger logic and effects.
/// Works with InteractionManager to create pollination events.
/// </summary>
public class PollinationTrigger : MonoBehaviour
{
    [Header("Pollination Settings")]
    public float aimConeDegrees = 8.0f;
    public float maxDistance = 6.0f;
    public float likeWeight = 1.0f;
    
    [Header("Audio")]
    public AudioClip pollinationSound;
    private AudioSource audioSource;

    void Start()
    {
        // Setup audio
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.volume = 0.7f;
        
        // Subscribe to GameManager events
        if (GameManager.Instance != null)
        {
            // The actual trigger is handled by InteractionManager
            // This script provides additional effects and orchestration
        }
    }

    public void OnPollinationTriggered(GameObject flower, GameObject beehive, GameObject gardenDynamics)
    {
        // Mark flower as liked/engaged with pollen burst
        CreatePollenBurst(flower);
        
        // Play audio feedback
        PlayPollinationSound();
        
        // Queue beehive drift update
        BeehiveMovementController movement = FindObjectOfType<BeehiveMovementController>();
        if (movement != null)
        {
            movement.QueueDrift(flower);
        }
        
        // Nudge garden to spawn more similar flowers over time
        GardenDynamicsController garden = FindObjectOfType<GardenDynamicsController>();
        if (garden != null)
        {
            garden.OnFlowerPollinated(flower);
        }
        
        Debug.Log($"PollinationTrigger: Pollination complete on {flower.name}");
    }

    void CreatePollenBurst(GameObject flower)
    {
        GameObject burstObj = new GameObject("PollenBurst");
        burstObj.transform.position = flower.transform.position;
        
        ParticleSystem particles = burstObj.AddComponent<ParticleSystem>();
        
        var main = particles.main;
        main.duration = 0.5f;
        main.startLifetime = 1f;
        main.startSpeed = 3f;
        main.startSize = 0.3f;
        main.startColor = new Color(1f, 0.9f, 0.2f, 1f); // Golden pollen color
        
        var emission = particles.emission;
        emission.rateOverTime = 0;
        emission.SetBursts(new ParticleSystem.Burst[] {
            new ParticleSystem.Burst(0f, 25)
        });
        
        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.5f;
        
        particles.Play();
        Destroy(burstObj, 2f);
    }

    void PlayPollinationSound()
    {
        if (audioSource != null && pollinationSound != null)
        {
            audioSource.PlayOneShot(pollinationSound);
        }
        else
        {
            // Synthesize a simple "pop" sound if no clip assigned
            Debug.Log("SOUND: Pollination Pop!");
        }
    }
}
