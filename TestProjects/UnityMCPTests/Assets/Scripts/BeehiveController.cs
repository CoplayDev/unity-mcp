using UnityEngine;

/// <summary>
/// Controls beehive initialization behavior.
/// On start, emits visible candidate boundary (PollenCircle) centered on itself.
/// </summary>
public class BeehiveController : MonoBehaviour
{
    [Header("Profile Initialization")]
    public float circleRadius = 7.5f;
    public float burstSize = 0.6f;
    
    [Header("References")]
    public GameObject pollenCircle;
    public ParticleSystem burstEffect;
    
    void Start()
    {
        // Find pollen circle if not assigned
        if (pollenCircle == null)
        {
            pollenCircle = GameObject.Find("PollenCircle");
        }

        // Initialize profile
        InitializeProfile();
        
        // Notify GameManager when beehive is viewed
        Invoke(nameof(NotifyBeehiveViewed), 1f);
    }

    void InitializeProfile()
    {
        // Position the pollen circle centered on the beehive
        if (pollenCircle != null)
        {
            Vector3 circlePos = transform.position;
            circlePos.y = 0.02f; // Ground level
            pollenCircle.transform.position = circlePos;
            
            // Scale the circle to match radius
            float scale = circleRadius * 2f / 10f; // Assuming Quad default size of 10
            pollenCircle.transform.localScale = new Vector3(scale, scale, scale);
            
            Debug.Log($"BeehiveController: Pollen circle initialized at {circlePos} with radius {circleRadius}");
        }

        // Create burst effect  
        CreateBurstEffect();
        
        // Initialize profile manager
        if (GameManager.Instance != null)
        {
            GameManager.Instance.UpdateProfilePosition(transform.position);
        }
    }

    void CreateBurstEffect()
    {
        // Create a particle burst to show profile initialization
        GameObject particleObj = new GameObject("ProfileBurst");
        particleObj.transform.position = transform.position;
        ParticleSystem particles = particleObj.AddComponent<ParticleSystem>();
        
        var main = particles.main;
        main.duration = 1f;
        main.startLifetime = 1.5f;
        main.startSpeed = burstSize * 3f;
        main.startSize = burstSize;
        main.startColor = new Color(0.85f, 0.95f, 0.55f, 1f); // Match pollen circle color
        
        var emission = particles.emission;
        emission.rateOverTime = 0;
        emission.SetBursts(new ParticleSystem.Burst[] {
            new ParticleSystem.Burst(0f, 30)
        });
        
        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.5f;
        
        particles.Play();
        
        // Auto-destroy
        Destroy(particleObj, 3f);
    }

    void NotifyBeehiveViewed()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.NotifyBeehiveViewed();
        }
    }

    void OnDrawGizmos()
    {
        // Visualize the initial candidate boundary
        Gizmos.color = new Color(0.85f, 0.95f, 0.55f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, circleRadius);
    }
}
