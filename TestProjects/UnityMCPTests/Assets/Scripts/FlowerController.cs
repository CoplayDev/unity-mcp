using UnityEngine;
using System.Collections;

/// <summary>
/// Controls flower interaction behavior - proximity-based feature reveal.
/// When bee gets close, reveals flower attributes via floating tag.
/// </summary>
public class FlowerController : MonoBehaviour
{
    [Header("Feature Reveal Settings")]
    public float revealDistance = 1.3f;
    public float tagDuration = 1.5f;
    
    [Header("Flower Attributes")]
    public string flowerColor = "Red";
    public string flowerShape = "Round";
    public string flowerSize = "Medium";
    
    private GameObject bee;
    private GameObject attributeTag;
    private bool isShowingTag = false;
    private Coroutine hideTagCoroutine;

    void Start()
    {
        // Find the bee
        bee = GameObject.Find("Bee");
        
        // Randomize attributes for variety
        RandomizeAttributes();
    }

    void RandomizeAttributes()
    {
        string[] colors = { "Red", "Yellow", "Blue", "Purple" };
        string[] shapes = { "Round", "Spiky", "Tulip" };
        string[] sizes = { "Small", "Medium", "Large" };
        
        flowerColor = colors[Random.Range(0, colors.Length)];
        flowerShape = shapes[Random.Range(0, shapes.Length)];
        flowerSize = sizes[Random.Range(0, sizes.Length)];
    }

    void Update()
    {
        if (bee == null) return;

        float distance = Vector3.Distance(transform.position, bee.transform.position);
        
        if (distance <= revealDistance)
        {
            if (!isShowingTag)
            {
                ShowAttributeTag();
                
                // Notify GameManager that player approached a flower
                if (GameManager.Instance != null)
                {
                    GameManager.Instance.NotifyFlowerApproached();
                }
            }
        }
        else
        {
            if (isShowingTag)
            {
                HideAttributeTag();
            }
        }
    }

    void ShowAttributeTag()
    {
        isShowingTag = true;
        
        // In a full implementation, this would create a 3D UI element
        // For now, we'll just log it
        Debug.Log($"{gameObject.name} attributes: Color={flowerColor}, Shape={flowerShape}, Size={flowerSize}");
        
        // Create a simple 3D text object above the flower
        CreateFloatingTag();
        
        // Cancel any existing hide coroutine
        if (hideTagCoroutine != null)
        {
            StopCoroutine(hideTagCoroutine);
        }
    }

    void HideAttributeTag()
    {
        isShowingTag = false;
        
        if (attributeTag != null)
        {
            Destroy(attributeTag);
        }
    }

    void CreateFloatingTag()
    {
        if (attributeTag != null)
        {
            return; // Tag already exists
        }
        
        // Create a simple sphere as a visual indicator
        attributeTag = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        attributeTag.name = $"{gameObject.name}_Tag";
        attributeTag.transform.position = transform.position + Vector3.up * 1.5f;
        attributeTag.transform.localScale = Vector3.one * 0.2f;
        
        // Color based on attribute
        Renderer renderer = attributeTag.GetComponent<Renderer>();
        if (renderer != null)
        {
            Material mat = new Material(Shader.Find("Standard"));
            
            switch (flowerColor)
            {
                case "Red":
                    mat.color = Color.red;
                    break;
                case "Yellow":
                    mat.color = Color.yellow;
                    break;
                case "Blue":
                    mat.color = Color.blue;
                    break;
                case "Purple":
                    mat.color = new Color(0.5f, 0f, 0.5f);
                    break;
            }
            
            renderer.material = mat;
        }
        
        // Remove collider
        Collider col = attributeTag.GetComponent<Collider>();
        if (col != null)
        {
            Destroy(col);
        }
        
        // Add bobbing animation
        FloatBob bob = attributeTag.AddComponent<FloatBob>();
        bob.amplitude = 0.1f;
        bob.frequency = 2f;
    }

    public string GetAttributeString()
    {
        return $"{flowerColor}/{flowerShape}/{flowerSize}";
    }

    void OnDrawGizmosSelected()
    {
        // Visualize reveal distance
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, revealDistance);
    }
}

/// <summary>
/// Simple script to make objects bob up and down
/// </summary>
public class FloatBob : MonoBehaviour
{
    public float amplitude = 0.1f;
    public float frequency = 2f;
    private Vector3 startPosition;

    void Start()
    {
        startPosition = transform.position;
    }

    void Update()
    {
        Vector3 pos = startPosition;
        pos.y += Mathf.Sin(Time.time * frequency) * amplitude;
        transform.position = pos;
    }
}
