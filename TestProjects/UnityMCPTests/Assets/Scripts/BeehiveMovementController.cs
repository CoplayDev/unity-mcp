using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Controls beehive spatial drift toward pollinated flowers.
/// Implements profile update as physical movement.
/// </summary>
public class BeehiveMovementController : MonoBehaviour
{
    [Header("Drift Settings")]
    public float driftSpeed = 0.9f;
    public float driftDuration = 2.0f;
    public float recenterLerp = 0.65f;
    
    [Header("References")]
    public GameObject beehive;
    public GameObject pollenCircle;
    
    [Header("VFX")]
    public bool showBeamEffect = true;
    private LineRenderer driftBeam;
    
    private Queue<GameObject> driftQueue = new Queue<GameObject>();
    private bool isDrifting = false;
    private List<GameObject> pollinatedFlowers = new List<GameObject>();

    void Start()
    {
        // Find beehive if not assigned
        if (beehive == null)
        {
            beehive = GameObject.Find("Beehive");
        }
        
        if (pollenCircle == null)
        {
            pollenCircle = GameObject.Find("PollenCircle");
        }

        // Setup drift beam effect
        if (showBeamEffect && beehive != null)
        {
            SetupDriftBeam();
        }
    }

    void SetupDriftBeam()
    {
        GameObject beamObj = new GameObject("DriftBeam");
        beamObj.transform.SetParent(beehive.transform);
        driftBeam = beamObj.AddComponent<LineRenderer>();
        
        driftBeam.startWidth = 0.1f;
        driftBeam.endWidth = 0.05f;
        driftBeam.material = new Material(Shader.Find("Sprites/Default"));
        driftBeam.startColor = new Color(0.85f, 0.95f, 0.55f, 0.5f);
        driftBeam.endColor = new Color(0.85f, 0.95f, 0.55f, 0f);
        driftBeam.positionCount = 2;
        driftBeam.enabled = false;
    }

    public void QueueDrift(GameObject targetFlower)
    {
        if (!pollinatedFlowers.Contains(targetFlower))
        {
            pollinatedFlowers.Add(targetFlower);
        }
        
        driftQueue.Enqueue(targetFlower);
        
        if (!isDrifting)
        {
            StartCoroutine(ProcessDriftQueue());
        }
    }

    IEnumerator ProcessDriftQueue()
    {
        // Wait for profile drift start delay
        yield return new WaitForSeconds(GameManager.Instance?.profileDriftStartDelay ?? 0.3f);
        
        while (driftQueue.Count > 0)
        {
            GameObject target = driftQueue.Dequeue();
            
            if (target != null)
            {
                yield return StartCoroutine(DriftToward(target));
            }
        }
    }

    IEnumerator DriftToward(GameObject targetFlower)
    {
        if (beehive == null) yield break;
        
        isDrifting = true;
        
        // Calculate target position (centroid of all pollinated flowers)
        Vector3 targetPosition = CalculateTargetPosition();
        Vector3 startPosition = beehive.transform.position;
        
        Debug.Log($"BeehiveMovement: Drifting from {startPosition} toward {targetPosition}");
        
        // Show drift beam
        if (driftBeam != null)
        {
            driftBeam.enabled = true;
            driftBeam.SetPosition(0, startPosition);
            driftBeam.SetPosition(1, targetPosition);
        }
        
        // Animate drift
        float elapsed = 0f;
        
        while (elapsed < driftDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / driftDuration;
            
            // Smooth interpolation
            float smoothT = Mathf.SmoothStep(0f, 1f, t);
            Vector3 newPosition = Vector3.Lerp(startPosition, targetPosition, smoothT * recenterLerp);
            
            beehive.transform.position = newPosition;
            
            // Update beam
            if (driftBeam != null)
            {
                driftBeam.SetPosition(0, beehive.transform.position);
            }
            
            yield return null;
        }
        
        // Hide beam
        if (driftBeam != null)
        {
            driftBeam.enabled = false;
        }
        
        // Update profile position in GameManager
        if (GameManager.Instance != null)
        {
            GameManager.Instance.UpdateProfilePosition(beehive.transform.position);
        }
        
        // Recenter pollen circle
        RecenterPollenCircle();
        
        // Notify bud growth to update priorities
        NotifyBudGrowth();
        
        isDrifting = false;
        
        Debug.Log($"BeehiveMovement: Drift complete. New position: {beehive.transform.position}");
    }

    Vector3 CalculateTargetPosition()
    {
        if (pollinatedFlowers.Count == 0)
        {
            return beehive.transform.position;
        }
        
        Vector3 sum = Vector3.zero;
        int validCount = 0;
        
        foreach (GameObject flower in pollinatedFlowers)
        {
            if (flower != null)
            {
                sum += flower.transform.position;
                validCount++;
            }
        }
        
        if (validCount == 0) return beehive.transform.position;
        
        Vector3 centroid = sum / validCount;
        
        // Keep Y at beehive height
        centroid.y = beehive.transform.position.y;
        
        return centroid;
    }

    void RecenterPollenCircle()
    {
        if (pollenCircle != null && beehive != null)
        {
            Vector3 newPos = beehive.transform.position;
            newPos.y = 0.02f; // Ground level
            pollenCircle.transform.position = newPos;
            
            Debug.Log("BeehiveMovement: Pollen circle recentered");
        }
    }

    void NotifyBudGrowth()
    {
        BudGrowthController budGrowth = FindObjectOfType<BudGrowthController>();
        if (budGrowth != null)
        {
            budGrowth.OnProfileUpdated();
        }
    }

    public bool IsDrifting()
    {
        return isDrifting;
    }

    void OnDrawGizmos()
    {
        if (beehive != null && pollinatedFlowers.Count > 0)
        {
            Gizmos.color = Color.yellow;
            Vector3 target = CalculateTargetPosition();
            Gizmos.DrawLine(beehive.transform.position, target);
            Gizmos.DrawWireSphere(target, 0.3f);
        }
    }
}
