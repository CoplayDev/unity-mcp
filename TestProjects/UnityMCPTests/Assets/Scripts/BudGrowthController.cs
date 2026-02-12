using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Controls flower growth based on proximity ranking.
/// Flowers closer to beehive grow from bud to bloom first.
/// </summary>
public class BudGrowthController : MonoBehaviour
{
    [Header("Growth Settings")]
    public float growthRateNear = 1.0f;
    public float growthRateFar = 0.25f;
    public float maxRankDistance = 7.5f;
    
    [Header("Visual Settings")]
    public bool useScaleForGrowth = true;
    public bool useParticlesForBloom = true;
    public float minScale = 0.5f;
    public float maxScale = 1.0f;
    
    [Header("References")]
    public GameObject beehive;
    
    private Dictionary<GameObject, float> flowerGrowth = new Dictionary<GameObject, float>();
    private List<GameObject> flowers = new List<GameObject>();
    private Vector3 rankingCenter;

    void Start()
    {
        // Find beehive
        if (beehive == null)
        {
            beehive = GameObject.Find("Beehive");
        }
        
        if (beehive != null)
        {
            rankingCenter = beehive.transform.position;
        }
        
        // Find all flowers
        FindFlowers();
        
        // Initialize growth values
        InitializeGrowth();
        
        // Subscribe to events
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnProfileUpdated.AddListener(OnProfileUpdated);
        }
    }

    void FindFlowers()
    {
        flowers.Clear();
        
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        foreach (GameObject obj in allObjects)
        {
            if (obj.name.StartsWith("Flower_"))
            {
                flowers.Add(obj);
            }
        }
        
        Debug.Log($"BudGrowthController: Managing growth for {flowers.Count} flowers");
    }

    void InitializeGrowth()
    {
        foreach (GameObject flower in flowers)
        {
            if (flower != null)
            {
                flowerGrowth[flower] = Random.Range(0.3f, 0.5f); // Start partially grown
            }
        }
    }

    void Update()
    {
        UpdateRankingCenter();
        ApplyGrowthRates();
    }

    void UpdateRankingCenter()
    {
        if (beehive != null)
        {
            rankingCenter = beehive.transform.position;
        }
        else if (GameManager.Instance != null)
        {
            rankingCenter = GameManager.Instance.profilePosition;
        }
    }

    void ApplyGrowthRates()
    {
        foreach (GameObject flower in flowers)
        {
            if (flower == null) continue;
            
            // Calculate distance to ranking center
            float distance = Vector3.Distance(flower.transform.position, rankingCenter);
            
            // Only grow flowers within max rank distance
            if (distance > maxRankDistance)
            {
                continue;
            }
            
            // Calculate growth rate based on proximity
            float normalizedDistance = Mathf.Clamp01(distance / maxRankDistance);
            float growthRate = Mathf.Lerp(growthRateNear, growthRateFar, normalizedDistance);
            
            // Update growth value
            if (flowerGrowth.ContainsKey(flower))
            {
                flowerGrowth[flower] += growthRate * Time.deltaTime;
                flowerGrowth[flower] = Mathf.Clamp01(flowerGrowth[flower]);
                
                // Apply visual growth
                ApplyVisualGrowth(flower, flowerGrowth[flower]);
                
                // Check for bloom completion
                if (flowerGrowth[flower] >= 1.0f && useParticlesForBloom)
                {
                    OnFlowerFullyBloomed(flower);
                }
            }
        }
    }

    void ApplyVisualGrowth(GameObject flower, float growth)
    {
        if (!useScaleForGrowth) return;
        
        // Scale the flower based on growth progress
        float scale = Mathf.Lerp(minScale, maxScale, growth);
        flower.transform.localScale = Vector3.one * scale;
        
        // Optionally animate based on growth
        Animator animator = flower.GetComponent<Animator>();
        if (animator != null)
        {
            // Could set animation parameters here
            // animator.SetFloat("GrowthProgress", growth);
        }
    }

    void OnFlowerFullyBloomed(GameObject flower)
    {
        // Only show bloom effect once
        if (!flowerGrowth.ContainsKey(flower) || flowerGrowth[flower] < 0.999f)
        {
            return;
        }
        
        // Mark as shown
        flowerGrowth[flower] = 1.1f; // Slightly over to prevent repeat
        
        // Create bloom particle effect
        CreateBloomEffect(flower);
        
        Debug.Log($"BudGrowth: {flower.name} fully bloomed!");
    }

    void CreateBloomEffect(GameObject flower)
    {
        GameObject effectObj = new GameObject("BloomEffect");
        effectObj.transform.position = flower.transform.position;
        
        ParticleSystem particles = effectObj.AddComponent<ParticleSystem>();
        
        var main = particles.main;
        main.duration = 0.8f;
        main.startLifetime = 1.2f;
        main.startSpeed = 1f;
        main.startSize = 0.15f;
        main.startColor = new Color(1f, 0.8f, 0.9f, 1f); // Pink bloom
        
        var emission = particles.emission;
        emission.rateOverTime = 0;
        emission.SetBursts(new ParticleSystem.Burst[] {
            new ParticleSystem.Burst(0f, 15)
        });
        
        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.3f;
        
        particles.Play();
        Destroy(effectObj, 2f);
    }

    public void OnProfileUpdated()
    {
        Debug.Log("BudGrowth: Profile updated, recalculating growth priorities");
        
        // Profile position changed, so growth rates will naturally update
        // Could optionally reset growth values for more dramatic effect
        // ResetAllGrowth();
    }

    void ResetAllGrowth()
    {
        foreach (GameObject flower in flowers)
        {
            if (flowerGrowth.ContainsKey(flower))
            {
                flowerGrowth[flower] = 0.3f; // Reset to bud state
            }
        }
    }

    public float GetGrowthProgress(GameObject flower)
    {
        if (flowerGrowth.ContainsKey(flower))
        {
            return flowerGrowth[flower];
        }
        return 0f;
    }

    void OnDestroy()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnProfileUpdated.RemoveListener(OnProfileUpdated);
        }
    }

    void OnDrawGizmos()
    {
        // Visualize growth radius
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(rankingCenter, maxRankDistance);
        
        // Show growth lines to top 3 flowers
        if (flowers.Count > 0)
        {
            var sorted = new List<GameObject>(flowers);
            sorted.Sort((a, b) => {
                float distA = Vector3.Distance(a.transform.position, rankingCenter);
                float distB = Vector3.Distance(b.transform.position, rankingCenter);
                return distA.CompareTo(distB);
            });
            
            for (int i = 0; i < Mathf.Min(3, sorted.Count); i++)
            {
                if (sorted[i] != null)
                {
                    Gizmos.color = Color.Lerp(Color.green, Color.yellow, i / 3f);
                    Gizmos.DrawLine(rankingCenter, sorted[i].transform.position);
                }
            }
        }
    }
}
