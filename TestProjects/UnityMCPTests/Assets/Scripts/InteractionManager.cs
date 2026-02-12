using UnityEngine;

/// <summary>
/// Normalizes user triggers and dispatches to GameManager pipeline.
/// Coordinates trigger guards and cooldowns across interaction mappings.
/// </summary>
public class InteractionManager : MonoBehaviour
{
    [Header("Interaction Settings")]
    public float aimConeRadius = 8.0f;
    public float maxDistance = 6.0f;
    public float likestWeight = 1.0f;
    public KeyCode pollinationKey = KeyCode.Space;
    
    [Header("References")]
    public Camera playerCamera;
    public GameObject bee;
    
    [Header("Cooldowns")]
    public float pollinationCooldown = 0.5f;
    private float lastPollinationTime = -999f;

    private GameObject currentTargetFlower = null;
    private bool isAiming = false;

    void Start()
    {
        // Find references if not assigned
        if (playerCamera == null)
        {
            playerCamera = Camera.main;
        }
        
        if (bee == null)
        {
            bee = GameObject.Find("Bee");
        }
    }

    void Update()
    {
        // Update targeting
        UpdateTargeting();
        
        // Check for pollination input
        if (Input.GetKeyDown(pollinationKey))
        {
            TriggerPollination();
        }
    }

    void UpdateTargeting()
    {
        if (playerCamera == null) return;

        // Raycast from camera to find targeted flower
        Ray ray = playerCamera.ScreenPointToRay(new Vector3(Screen.width / 2, Screen.height / 2, 0));
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, maxDistance))
        {
            GameObject hitObject = hit.collider.gameObject;
            
            // Check if it's a flower
            if (hitObject.name.StartsWith("Flower_"))
            {
                // Check if flower is a valid candidate
                CandidateManager candidateManager = FindObjectOfType<CandidateManager>();
                
                if (candidateManager != null && candidateManager.IsCandidate(hitObject))
                {
                    currentTargetFlower = hitObject;
                    isAiming = true;
                    
                    // Notify GameManager that player targeted an in-circle flower
                    if (GameManager.Instance != null)
                    {
                        GameManager.Instance.NotifyInCircleTargeted();
                    }
                }
                else
                {
                    // Flower is out of circle
                    currentTargetFlower = null;
                    isAiming = false;
                }
            }
            else
            {
                currentTargetFlower = null;
                isAiming = false;
            }
        }
        else
        {
            currentTargetFlower = null;
            isAiming = false;
        }
    }

    void TriggerPollination()
    {
        // Check cooldown
        if (Time.time - lastPollinationTime < pollinationCooldown)
        {
            return;
        }

        if (currentTargetFlower == null || !isAiming)
        {
            Debug.Log("InteractionManager: No valid target for pollination");
            
            // Check if player tried to pollinate an out-of-circle flower
            Ray ray = playerCamera.ScreenPointToRay(new Vector3(Screen.width / 2, Screen.height / 2, 0));
            RaycastHit hit;
            
            if (Physics.Raycast(ray, out hit, maxDistance))
            {
                if (hit.collider.gameObject.name.StartsWith("Flower_"))
                {
                    // Player tried to pollinate a flower outside the circle
                    if (GameManager.Instance != null)
                    {
                        GameManager.Instance.NotifyOutOfCircleAttempt();
                    }
                    Debug.Log("InteractionManager: Attempted to pollinate out-of-circle flower");
                }
            }
            
            return;
        }

        // Valid pollination
        lastPollinationTime = Time.time;
        
        Debug.Log($"InteractionManager: Pollination triggered on {currentTargetFlower.name}");
        
        // Create visual/audio feedback
        CreatePollinationEffect(currentTargetFlower);
        
        // Notify managers
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnPollinationTriggered(currentTargetFlower);
        }

        ProfileManager profileManager = FindObjectOfType<ProfileManager>();
        if (profileManager != null)
        {
            profileManager.AddLikedFlower(currentTargetFlower);
        }
    }

    void CreatePollinationEffect(GameObject flower)
    {
        // Create particle burst effect
        GameObject particleObj = new GameObject("PollenBurst");
        particleObj.transform.position = flower.transform.position;
        
        ParticleSystem particles = particleObj.AddComponent<ParticleSystem>();
        var main = particles.main;
        main.duration = 0.5f;
        main.startLifetime = 1f;
        main.startSpeed = 2f;
        main.startSize = 0.2f;
        main.startColor = new Color(1f, 0.9f, 0.2f, 1f);
        
        var emission = particles.emission;
        emission.rateOverTime = 0;
        emission.SetBursts(new ParticleSystem.Burst[] {
            new ParticleSystem.Burst(0f, 20)
        });
        
        // Auto-destroy
        Destroy(particleObj, 2f);
        
        // Play audio (if audio source exists)
        AudioSource audioSource = GetComponent<AudioSource>();
        if (audioSource != null)
        {
            audioSource.Play();
        }
    }

    public GameObject GetCurrentTarget()
    {
        return currentTargetFlower;
    }

    public bool IsAiming()
    {
        return isAiming;
    }

    void OnGUI()
    {
        // Draw simple crosshair
        if (isAiming && currentTargetFlower != null)
        {
            GUI.color = Color.green;
        }
        else
        {
            GUI.color = Color.white;
        }
        
        float size = 10f;
        float x = Screen.width / 2 - size / 2;
        float y = Screen.height / 2 - size / 2;
        
        GUI.Box(new Rect(x - 20, y, 15, 2), "");
        GUI.Box(new Rect(x + size + 5, y, 15, 2), "");
        GUI.Box(new Rect(x, y - 20, 2, 15), "");
        GUI.Box(new Rect(x, y + size + 5, 2, 15), "");
    }
}
