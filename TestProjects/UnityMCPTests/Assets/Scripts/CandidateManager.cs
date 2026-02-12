using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Manages the active candidate set for content selection.
/// Applies candidate generation filters based on range and constraints.
/// </summary>
public class CandidateManager : MonoBehaviour
{
    [Header("Candidate Settings")]
    public float candidateRadius = 7.5f;
    public float outsideDimAlpha = 0.25f;
    public float highlightAlpha = 0.9f;
    
    [Header("References")]
    public GameObject pollenCircle;
    
    private List<GameObject> allFlowers = new List<GameObject>();
    private List<GameObject> currentCandidates = new List<GameObject>();
    private Vector3 filterCenter = Vector3.zero;

    void Start()
    {
        // Find pollen circle
        if (pollenCircle == null)
        {
            pollenCircle = GameObject.Find("PollenCircle");
        }

        // Subscribe to GameManager events
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnProfileUpdated.AddListener(HandleProfileUpdate);
        }

        // Find all flowers in the scene
        FindAllFlowers();
        
        // Initial candidate update
        UpdateCandidates();
    }

    void Update()
    {
        // Continuous candidate filtering
        UpdateCandidates();
    }

    void FindAllFlowers()
    {
        allFlowers.Clear();
        
        // Find all objects with "Flower" in their name
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        foreach (GameObject obj in allObjects)
        {
            if (obj.name.StartsWith("Flower_"))
            {
                allFlowers.Add(obj);
            }
        }
        
        Debug.Log($"CandidateManager: Found {allFlowers.Count} flowers");
    }

    void HandleProfileUpdate()
    {
        // Update filter center when profile changes
        if (GameManager.Instance != null)
        {
            filterCenter = GameManager.Instance.profilePosition;
        }
        
        UpdateCandidates();
    }

    void UpdateCandidates()
    {
        if (GameManager.Instance != null)
        {
            filterCenter = GameManager.Instance.profilePosition;
        }

        // Update pollen circle position
        if (pollenCircle != null)
        {
            Vector3 newPos = filterCenter;
            newPos.y = 0.02f; // Keep at ground level
            pollenCircle.transform.position = newPos;
        }

        // Filter flowers by distance
        List<GameObject> newCandidates = new List<GameObject>();
        
        foreach (GameObject flower in allFlowers)
        {
            if (flower == null) continue;
            
            float distance = Vector3.Distance(flower.transform.position, filterCenter);
            bool isCandidate = distance <= candidateRadius;
            
            if (isCandidate)
            {
                newCandidates.Add(flower);
                HighlightFlower(flower, true);
            }
            else
            {
                DimFlower(flower);
            }
        }

        // Update candidate list if changed
        if (!ListsAreEqual(currentCandidates, newCandidates))
        {
            currentCandidates = newCandidates;
            
            // Notify GameManager
            if (GameManager.Instance != null)
            {
                GameManager.Instance.UpdateCandidates(currentCandidates);
            }
            
            Debug.Log($"CandidateManager: Updated candidates - {currentCandidates.Count} flowers in range");
        }
    }

    bool ListsAreEqual(List<GameObject> list1, List<GameObject> list2)
    {
        if (list1.Count != list2.Count) return false;
        
        var set1 = new HashSet<GameObject>(list1);
        return list2.All(item => set1.Contains(item));
    }

    void HighlightFlower(GameObject flower, bool isCandidate)
    {
        // In a full implementation, this would modify the flower's material/shader
        // to show it's a valid candidate
        
        Renderer renderer = flower.GetComponent<Renderer>();
        if (renderer != null && renderer.material != null)
        {
            Color color = renderer.material.color;
            color.a = highlightAlpha;
            renderer.material.color = color;
        }
    }

    void DimFlower(GameObject flower)
    {
        // Dim flowers outside the candidate range
        
        Renderer renderer = flower.GetComponent<Renderer>();
        if (renderer != null && renderer.material != null)
        {
            Color color = renderer.material.color;
            color.a = outsideDimAlpha;
            renderer.material.color = color;
        }
    }

    public bool IsCandidate(GameObject flower)
    {
        return currentCandidates.Contains(flower);
    }

    public List<GameObject> GetCandidates()
    {
        return new List<GameObject>(currentCandidates);
    }

    public int GetCandidateCount()
    {
        return currentCandidates.Count;
    }

    void OnDestroy()
    {
        // Unsubscribe from events
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnProfileUpdated.RemoveListener(HandleProfileUpdate);
        }
    }

    void OnDrawGizmos()
    {
        // Visualize the candidate radius in the editor
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(filterCenter, candidateRadius);
    }
}
