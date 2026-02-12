using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Computes ordered ranking over active candidates.
/// Applies ranking effects based on proximity to profile.
/// </summary>
public class RankingManager : MonoBehaviour
{
    [Header("Ranking Settings")]
    public float growthRateNear = 1.0f;
    public float growthRateFar = 0.25f;
    public float maxRankDistance = 7.5f;
    
    private List<GameObject> rankedCandidates = new List<GameObject>();
    private Dictionary<GameObject, float> growthProgress = new Dictionary<GameObject, float>();
    private Vector3 rankingCenter = Vector3.zero;

    void Start()
    {
        // Subscribe to GameManager events
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnCandidatesUpdated.AddListener(HandleCandidatesUpdated);
            GameManager.Instance.OnProfileUpdated.AddListener(HandleProfileUpdated);
        }
    }

    void Update()
    {
        // Continuous ranking update
        UpdateRanking();
        ApplyGrowthEffects();
    }

    void HandleCandidatesUpdated()
    {
        UpdateRanking();
    }

    void HandleProfileUpdated()
    {
        if (GameManager.Instance != null)
        {
            rankingCenter = GameManager.Instance.profilePosition;
        }
        UpdateRanking();
    }

    void UpdateRanking()
    {
        if (GameManager.Instance == null) return;

        rankingCenter = GameManager.Instance.profilePosition;
        List<GameObject> candidates = GameManager.Instance.candidateFlowers;

        if (candidates == null || candidates.Count == 0)
        {
            rankedCandidates.Clear();
            return;
        }

        // Sort candidates by distance from ranking center (profile position)
        // Closer flowers rank higher
        var sorted = candidates
            .Where(f => f != null)
            .OrderBy(f => Vector3.Distance(f.transform.position, rankingCenter))
            .ToList();

        rankedCandidates = sorted;

        // Initialize growth progress for new candidates
        foreach (GameObject flower in rankedCandidates)
        {
            if (!growthProgress.ContainsKey(flower))
            {
                growthProgress[flower] = 0f;
            }
        }

        // Notify GameManager of ranking update
        if (GameManager.Instance != null)
        {
            GameManager.Instance.UpdateRanking(rankedCandidates);
        }
    }

    void ApplyGrowthEffects()
    {
        // Apply growth animation based on ranking
        // Flowers closer to the profile (higher ranked) grow faster
        
        foreach (GameObject flower in rankedCandidates)
        {
            if (flower == null) continue;

            float distance = Vector3.Distance(flower.transform.position, rankingCenter);
            float normalizedDistance = Mathf.Clamp01(distance / maxRankDistance);
            
            // Interpolate growth rate based on distance
            float growthRate = Mathf.Lerp(growthRateNear, growthRateFar, normalizedDistance);
            
            // Update growth progress
            if (growthProgress.ContainsKey(flower))
            {
                growthProgress[flower] += growthRate * Time.deltaTime;
                growthProgress[flower] = Mathf.Clamp01(growthProgress[flower]);
                
                // Apply visual growth effect
                ApplyVisualGrowth(flower, growthProgress[flower]);
            }
        }
    }

    void ApplyVisualGrowth(GameObject flower, float progress)
    {
        // In a full implementation, this would animate the flower from bud to bloom
        // For now, we'll use scale as a proxy for growth
        
        Animator animator = flower.GetComponent<Animator>();
        if (animator != null)
        {
            // Control animation speed or blend based on progress
            animator.speed = progress;
        }
        
        // Alternative: Scale the flower based on growth progress
        // Vector3 targetScale = Vector3.one * (0.5f + progress * 0.5f);
        // flower.transform.localScale = Vector3.Lerp(flower.transform.localScale, targetScale, Time.deltaTime * 2f);
    }

    public int GetRank(GameObject flower)
    {
        return rankedCandidates.IndexOf(flower);
    }

    public List<GameObject> GetTopRanked(int count)
    {
        return rankedCandidates.Take(count).ToList();
    }

    public float GetGrowthProgress(GameObject flower)
    {
        return growthProgress.ContainsKey(flower) ? growthProgress[flower] : 0f;
    }

    public void ResetGrowth(GameObject flower)
    {
        if (growthProgress.ContainsKey(flower))
        {
            growthProgress[flower] = 0f;
        }
    }

    void OnDestroy()
    {
        // Unsubscribe from events
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnCandidatesUpdated.RemoveListener(HandleCandidatesUpdated);
            GameManager.Instance.OnProfileUpdated.RemoveListener(HandleProfileUpdated);
        }
    }

    void OnDrawGizmos()
    {
        // Visualize ranking order in the editor
        Gizmos.color = Color.green;
        
        for (int i = 0; i < rankedCandidates.Count && i < 5; i++)
        {
            if (rankedCandidates[i] != null)
            {
                Vector3 pos = rankedCandidates[i].transform.position;
                Gizmos.DrawLine(rankingCenter, pos);
            }
        }
    }
}
