using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Controls the pollen circle visual and candidate filtering.
/// Manages which flowers are highlighted as candidates vs dimmed.
/// </summary>
public class PollenCircleController : MonoBehaviour
{
    [Header("Candidate Filter Settings")]
    public float radius = 7.5f;
    public float outsideDimAlpha = 0.25f;
    public float highlightAlpha = 0.9f;
    
    [Header("Visual Settings")]
    public bool animateCircle = true;
    public float pulseSpeed = 1f;
    public float pulseAmount = 0.1f;
    
    private List<GameObject> flowers = new List<GameObject>();
    private Vector3 baseScale;
    private Material circleMaterial;

    void Start()
    {
        // Store base scale
        baseScale = transform.localScale;
        
        // Setup material
        Renderer renderer = GetComponent<Renderer>();
        if (renderer != null)
        {
            circleMaterial = renderer.material;
        }
        
        // Find all flowers
        FindFlowers();
    }

    void Update()
    {
        // Animate the circle
        if (animateCircle)
        {
            AnimatePulse();
        }
        
        // Continuous filtering
        ApplyCandidateFilter();
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
        
        Debug.Log($"PollenCircleController: Found {flowers.Count} flowers to filter");
    }

    void AnimatePulse()
    {
        float pulse = Mathf.Sin(Time.time * pulseSpeed) * pulseAmount;
        transform.localScale = baseScale * (1f + pulse);
        
        // Also pulse the alpha
        if (circleMaterial != null)
        {
            Color color = circleMaterial.color;
            color.a = 0.18f + (pulse * 0.05f);
            circleMaterial.color = color;
        }
    }

    void ApplyCandidateFilter()
    {
        Vector3 centerPos = transform.position;
        int candidateCount = 0;
        
        foreach (GameObject flower in flowers)
        {
            if (flower == null) continue;
            
            // Calculate distance (2D, ignore Y)
            Vector3 flowerPos = flower.transform.position;
            flowerPos.y = centerPos.y;
            
            float distance = Vector3.Distance(flowerPos, centerPos);
            bool isInside = distance <= radius;
            
            // Apply visual feedback
            if (isInside)
            {
                HighlightFlower(flower);
                candidateCount++;
            }
            else
            {
                DimFlower(flower);
            }
        }
        
        // Optional: Update debug info
        // Debug.Log($"PollenCircle: {candidateCount} candidates in range");
    }

    void HighlightFlower(GameObject flower)
    {
        // Make candidate flowers more visible
        Renderer renderer = flower.GetComponent<Renderer>();
        if (renderer != null)
        {
            // Use property block to avoid creating material instances
            MaterialPropertyBlock props = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(props);
            
            Color color = props.GetColor("_Color");
            if (color == Color.clear)
            {
                color = Color.white;
            }
            color.a = highlightAlpha;
            props.SetColor("_Color", color);
            
            renderer.SetPropertyBlock(props);
        }
    }

    void DimFlower(GameObject flower)
    {
        // Dim flowers outside candidate range
        Renderer renderer = flower.GetComponent<Renderer>();
        if (renderer != null)
        {
            MaterialPropertyBlock props = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(props);
            
            Color color = props.GetColor("_Color");
            if (color == Color.clear)
            {
                color = Color.white;
            }
            color.a = outsideDimAlpha;
            props.SetColor("_Color", color);
            
            renderer.SetPropertyBlock(props);
        }
    }

    public bool IsFlowerInRange(GameObject flower)
    {
        if (flower == null) return false;
        
        Vector3 centerPos = transform.position;
        Vector3 flowerPos = flower.transform.position;
        flowerPos.y = centerPos.y;
        
        float distance = Vector3.Distance(flowerPos, centerPos);
        return distance <= radius;
    }

    void OnDrawGizmos()
    {
        // Visualize the filter radius
        Gizmos.color = new Color(0.85f, 0.95f, 0.55f, 0.3f);
        Vector3 center = transform.position;
        
        // Draw circle on XZ plane
        for (int i = 0; i < 32; i++)
        {
            float angle1 = (i / 32f) * Mathf.PI * 2f;
            float angle2 = ((i + 1) / 32f) * Mathf.PI * 2f;
            
            Vector3 p1 = center + new Vector3(Mathf.Cos(angle1) * radius, 0, Mathf.Sin(angle1) * radius);
            Vector3 p2 = center + new Vector3(Mathf.Cos(angle2) * radius, 0, Mathf.Sin(angle2) * radius);
            
            Gizmos.DrawLine(p1, p2);
        }
    }
}
