using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Orchestrates the feedback loop - spawns similar flowers after pollination cycles.
/// Reinforces the recommendation system's feedback mechanism visually.
/// </summary>
public class GardenDynamicsController : MonoBehaviour
{
    [Header("Feedback Loop Settings")]
    public float spawnDelay = 2.5f;
    public int spawnCount = 4;
    public float similarityBias = 0.75f;
    public float despawnOldFraction = 0.15f;
    
    [Header("Spawn Settings")]
    public float minSpawnDistance = 2f;
    public float maxSpawnDistance = 6f;
    public bool spawnWithinCandidateCircle = true;
    
    [Header("References")]
    public GameObject beehive;
    public GameObject flowerPrefab;
    
    private List<GameObject> pollinatedFlowers = new List<GameObject>();
    private Dictionary<GameObject, string> flowerAttributes = new Dictionary<GameObject, string>();
    private List<GameObject> spawnedFlowers = new List<GameObject>();

    void Start()
    {
        // Find beehive
        if (beehive == null)
        {
            beehive = GameObject.Find("Beehive");
        }
        
        // Subscribe to GameManager events
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnFeedbackLoopTick.AddListener(OnFeedbackLoopTick);
        }
        
        // Catalog existing flowers
        CatalogExistingFlowers();
    }

    void CatalogExistingFlowers()
    {
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        foreach (GameObject obj in allObjects)
        {
            if (obj.name.StartsWith("Flower_"))
            {
                FlowerController controller = obj.GetComponent<FlowerController>();
                if (controller != null)
                {
                    flowerAttributes[obj] = controller.GetAttributeString();
                }
                else
                {
                    // Assign random attributes
                    flowerAttributes[obj] = GetRandomAttributes();
                }
            }
        }
        
        Debug.Log($"GardenDynamics: Cataloged {flowerAttributes.Count} existing flowers");
    }

    public void OnFlowerPollinated(GameObject flower)
    {
        if (!pollinatedFlowers.Contains(flower))
        {
            pollinatedFlowers.Add(flower);
            Debug.Log($"GardenDynamics: Flower {flower.name} added to pollination history");
        }
    }

    void OnFeedbackLoopTick()
    {
        Debug.Log("GardenDynamics: Feedback loop tick - spawning similar flowers");
        StartCoroutine(ExecuteFeedbackLoop());
    }

    IEnumerator ExecuteFeedbackLoop()
    {
        // Wait for spawn delay
        yield return new WaitForSeconds(spawnDelay);
        
        // Despawn some old flowers if needed
        DespawnOldFlowers();
        
        // Spawn new similar flowers
        SpawnSimilarFlowers();
        
        Debug.Log("GardenDynamics: Feedback loop complete");
    }

    void DespawnOldFlowers()
    {
        if (spawnedFlowers.Count == 0) return;
        
        int despawnCount = Mathf.CeilToInt(spawnedFlowers.Count * despawnOldFraction);
        
        for (int i = 0; i < despawnCount && spawnedFlowers.Count > 0; i++)
        {
            // Remove oldest spawned flowers
            GameObject oldFlower = spawnedFlowers[0];
            spawnedFlowers.RemoveAt(0);
            
            if (oldFlower != null)
            {
                // Fade out effect
                CreateDespawnEffect(oldFlower);
                Destroy(oldFlower, 0.5f);
            }
        }
        
        Debug.Log($"GardenDynamics: Despawned {despawnCount} old flowers");
    }

    void SpawnSimilarFlowers()
    {
        if (pollinatedFlowers.Count == 0)
        {
            Debug.Log("GardenDynamics: No pollinated flowers to base spawning on");
            return;
        }
        
        // Get the most recent pollinated flower as template
        GameObject template = pollinatedFlowers[pollinatedFlowers.Count - 1];
        string templateAttributes = flowerAttributes.ContainsKey(template) 
            ? flowerAttributes[template] 
            : GetRandomAttributes();
        
        // Get spawn center (beehive position or candidate circle center)
        Vector3 spawnCenter = beehive != null ? beehive.transform.position : Vector3.zero;
        
        // Get candidate manager for radius
        CandidateManager candidateManager = FindObjectOfType<CandidateManager>();
        float candidateRadius = candidateManager != null ? candidateManager.candidateRadius : maxSpawnDistance;
        
        int spawned = 0;
        
        for (int i = 0; i < spawnCount; i++)
        {
            // Generate similar attributes
            string newAttributes = GenerateSimilarAttributes(templateAttributes);
            
            // Find spawn position within candidate circle
            Vector3 spawnPos = FindSpawnPosition(spawnCenter, candidateRadius);
            
            if (spawnPos != Vector3.zero)
            {
                GameObject newFlower = CreateFlower(spawnPos, newAttributes);
                
                if (newFlower != null)
                {
                    spawnedFlowers.Add(newFlower);
                    spawned++;
                }
            }
        }
        
        Debug.Log($"GardenDynamics: Spawned {spawned} similar flowers near {template.name}");
    }

    Vector3 FindSpawnPosition(Vector3 center, float maxRadius)
    {
        // Try to find a valid spawn position
        for (int attempt = 0; attempt < 10; attempt++)
        {
            float angle = Random.Range(0f, Mathf.PI * 2f);
            float distance = Random.Range(minSpawnDistance, maxRadius);
            
            Vector3 pos = center + new Vector3(
                Mathf.Cos(angle) * distance,
                0f,
                Mathf.Sin(angle) * distance
            );
            
            // Check if position is clear
            Collider[] colliders = Physics.OverlapSphere(pos, 0.5f);
            if (colliders.Length == 0)
            {
                return pos;
            }
        }
        
        return Vector3.zero; // Failed to find position
    }

    GameObject CreateFlower(Vector3 position, string attributes)
    {
        // Create a simple flower GameObject
        GameObject flower = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        flower.name = $"Flower_Spawned_{spawnedFlowers.Count}";
        flower.transform.position = position;
        flower.transform.localScale = new Vector3(0.5f, 0.8f, 0.5f);
        
        // Add FlowerController
        FlowerController controller = flower.AddComponent<FlowerController>();
        
        // Parse and set attributes
        string[] parts = attributes.Split('/');
        if (parts.Length >= 3)
        {
            controller.flowerColor = parts[0];
            controller.flowerShape = parts[1];
            controller.flowerSize = parts[2];
        }
        
        // Color based on attributes
        Renderer renderer = flower.GetComponent<Renderer>();
        if (renderer != null)
        {
            Material mat = new Material(Shader.Find("Standard"));
            mat.color = GetColorForAttribute(controller.flowerColor);
            renderer.material = mat;
        }
        
        // Store attributes
        flowerAttributes[flower] = attributes;
        
        // Spawn effect
        CreateSpawnEffect(flower);
        
        return flower;
    }

    string GenerateSimilarAttributes(string template)
    {
        // Generate attributes similar to template based on similarity bias
        string[] parts = template.Split('/');
        
        if (parts.Length < 3) return GetRandomAttributes();
        
        string color = Random.value < similarityBias ? parts[0] : GetRandomColor();
        string shape = Random.value < similarityBias ? parts[1] : GetRandomShape();
        string size = Random.value < similarityBias ? parts[2] : GetRandomSize();
        
        return $"{color}/{shape}/{size}";
    }

    string GetRandomAttributes()
    {
        return $"{GetRandomColor()}/{GetRandomShape()}/{GetRandomSize()}";
    }

    string GetRandomColor()
    {
        string[] colors = { "Red", "Yellow", "Blue", "Purple" };
        return colors[Random.Range(0, colors.Length)];
    }

    string GetRandomShape()
    {
        string[] shapes = { "Round", "Spiky", "Tulip" };
        return shapes[Random.Range(0, shapes.Length)];
    }

    string GetRandomSize()
    {
        string[] sizes = { "Small", "Medium", "Large" };
        return sizes[Random.Range(0, sizes.Length)];
    }

    Color GetColorForAttribute(string colorName)
    {
        switch (colorName)
        {
            case "Red": return Color.red;
            case "Yellow": return Color.yellow;
            case "Blue": return Color.blue;
            case "Purple": return new Color(0.5f, 0f, 0.5f);
            default: return Color.white;
        }
    }

    void CreateSpawnEffect(GameObject flower)
    {
        GameObject effectObj = new GameObject("SpawnBurst");
        effectObj.transform.position = flower.transform.position;
        
        ParticleSystem particles = effectObj.AddComponent<ParticleSystem>();
        
        var main = particles.main;
        main.duration = 0.6f;
        main.startLifetime = 1f;
        main.startSpeed = 2f;
        main.startSize = 0.2f;
        main.startColor = new Color(0.5f, 1f, 0.5f, 1f); // Green growth
        
        var emission = particles.emission;
        emission.rateOverTime = 0;
        emission.SetBursts(new ParticleSystem.Burst[] {
            new ParticleSystem.Burst(0f, 20)
        });
        
        particles.Play();
        Destroy(effectObj, 2f);
    }

    void CreateDespawnEffect(GameObject flower)
    {
        GameObject effectObj = new GameObject("DespawnEffect");
        effectObj.transform.position = flower.transform.position;
        
        ParticleSystem particles = effectObj.AddComponent<ParticleSystem>();
        
        var main = particles.main;
        main.duration = 0.5f;
        main.startLifetime = 0.8f;
        main.startSpeed = 1f;
        main.startSize = 0.15f;
        main.startColor = new Color(0.8f, 0.8f, 0.8f, 0.5f);
        
        particles.Play();
        Destroy(effectObj, 1.5f);
    }

    void OnDestroy()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnFeedbackLoopTick.RemoveListener(OnFeedbackLoopTick);
        }
    }
}
