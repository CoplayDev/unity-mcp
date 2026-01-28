using UnityEngine;

public class Bouncer : MonoBehaviour
{
    public float speed = 1f;
    public float height = 2f;
    private Vector3 startPos;

    void Start()
    {
        startPos = transform.position;
    }

    void Update()
    {
        float newY = startPos.y + Mathf.Abs(Mathf.Sin(Time.time * speed)) * height;
        transform.position = new Vector3(startPos.x, newY, startPos.z);
    }
}