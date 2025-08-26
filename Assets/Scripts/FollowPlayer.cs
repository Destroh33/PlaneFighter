using UnityEngine;

public class FollowTransform : MonoBehaviour
{
    public Transform t;

    void LateUpdate()
    {
        if (t != null)
        {
            transform.position = t.position;
        }
    }
}