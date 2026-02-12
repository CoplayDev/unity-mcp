using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Manages learner profile state and applies profile update effects.
/// Focused manager for profile-related operations.
/// </summary>
public class ProfileManager : MonoBehaviour
{
    [Header("Profile State")]
    public Vector3 currentProfilePosition = Vector3.zero;
    public Dictionary<string, float> profileAttributes = new Dictionary<string, float>();
    
    [Header("Beehive Reference")]
    public GameObject beehive;
    
    [Header("Update Settings")]
    public float driftSpeed = 0.9f;
    public float driftDuration = 2.0f;
    public float recenterLerp = 0.65f;

    private List<GameObject> likedFlowers = new List<GameObject>();
    private bool isDrifting = false;

    void Start()
    {
        // Find beehive
        if (beehive == null)
        {
            beehive = GameObject.Find("Beehive");
        }

        if (beehive != null)
        {
            currentProfilePosition = beehive.transform.position;
        }

        // Subscribe to GameManager events
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnProfileUpdated.AddListener(HandleProfileUpdate);
        }

        InitializeProfile();
    }

    void InitializeProfile()
    {
        // Initialize default profile attributes
        profileAttributes["color"] = 0f;
        profileAttributes["shape"] = 0f;
        profileAttributes["size"] = 0f;
        
        Debug.Log("ProfileManager: Profile initialized");
    }

    void HandleProfileUpdate()
    {
        // Calculate new profile position based on liked flowers
        if (likedFlowers.Count > 0)
        {
            Vector3 targetPosition = CalculateCentroid(likedFlowers);
            StartDrift(targetPosition);
        }
    }

    Vector3 CalculateCentroid(List<GameObject> flowers)
    {
        if (flowers.Count == 0) return currentProfilePosition;

        Vector3 sum = Vector3.zero;
        foreach (GameObject flower in flowers)
        {
            if (flower != null)
            {
                sum += flower.transform.position;
            }
        }
        
        return sum / flowers.Count;
    }

    void StartDrift(Vector3 targetPosition)
    {
        if (!isDrifting && beehive != null)
        {
            isDrifting = true;
            StartCoroutine(DriftToPosition(targetPosition));
        }
    }

    System.Collections.IEnumerator DriftToPosition(Vector3 target)
    {
        float elapsed = 0f;
        Vector3 startPosition = beehive.transform.position;

        while (elapsed < driftDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / driftDuration);
            
            // Smooth drift using lerp
            beehive.transform.position = Vector3.Lerp(startPosition, target, t * recenterLerp);
            
            yield return null;
        }

        // Update profile position
        currentProfilePosition = beehive.transform.position;
        
        // Notify GameManager
        if (GameManager.Instance != null)
        {
            GameManager.Instance.UpdateProfilePosition(currentProfilePosition);
        }

        isDrifting = false;
        Debug.Log($"ProfileManager: Drift complete to {currentProfilePosition}");
    }

    public void AddLikedFlower(GameObject flower)
    {
        if (!likedFlowers.Contains(flower))
        {
            likedFlowers.Add(flower);
            UpdateProfileAttributes(flower);
            
            Debug.Log($"ProfileManager: Added liked flower {flower.name}. Total: {likedFlowers.Count}");
        }
    }

    void UpdateProfileAttributes(GameObject flower)
    {
        // In a full implementation, this would extract flower attributes
        // and update the profile's weighted preferences
        
        // For now, just log the update
        Debug.Log($"ProfileManager: Profile attributes updated based on {flower.name}");
    }

    public Vector3 GetProfilePosition()
    {
        return currentProfilePosition;
    }

    public bool IsDrifting()
    {
        return isDrifting;
    }

    void OnDestroy()
    {
        // Unsubscribe from events
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnProfileUpdated.RemoveListener(HandleProfileUpdate);
        }
    }
}
