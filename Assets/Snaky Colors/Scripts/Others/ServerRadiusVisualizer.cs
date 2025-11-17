using UnityEngine;

/// <summary>
/// Visualizes authoritative server radiuses so you can tune collision / food eat zones.
/// Attach this to the player snake (or any GameObject) on the Unity client.
/// </summary>
public class ServerRadiusVisualizer : MonoBehaviour
{
    [Header("Server authoritative values")]
    public float baseCollisionRadius = 0.25f;
    public float collisionPerMass = 0.01f;
    public float minCollisionRadius = 0.15f;
    public float maxCollisionRadius = 1.0f;

    public float baseHeadHeadRadius = 0.25f;
    public float headHeadPerMass = 0.01f;
    public float minHeadHeadRadius = 0.15f;
    public float maxHeadHeadRadius = 1.0f;

    public float baseFoodEatRadius = 0.35f;
    public float foodPerMass = 0.04f;
    public float minFoodEat = 0.35f;
    public float maxFoodEat = 1.20f;

    [Header("Latency Padding")]
    public float collisionLatencyPadding = 0.35f;
    public float headHeadLatencyPadding = 0.25f;

    [Header("Dynamic snake values (runtime)")]
    public float snakeMass = 0f;
    public Vector2 headPosition;

    [Header("Visualization Options")]
    public bool showCollision = true;
    public bool showHeadHead = true;
    public bool showFoodEat = true;
    public bool runtimeDebugDraw = false;

    private Transform _cachedTransform;

    private void Awake()
    {
        _cachedTransform = transform;
    }

    private void LateUpdate()
    {
        headPosition = _cachedTransform != null ? (Vector2)_cachedTransform.position : headPosition;

        var net = NetworkClient.Instance;
        if (net != null && net.localPlayerState != null)
        {
            snakeMass = net.localPlayerState.Mass;
        }
        else if (_cachedTransform != null)
        {
            // fallback: approximate via current scale (used for editor preview)
            snakeMass = Mathf.Max(0f, (_cachedTransform.localScale.x * 2f - baseFoodEatRadius) / Mathf.Max(0.0001f, foodPerMass));
        }

        if (runtimeDebugDraw)
        {
            DrawCircleRuntime(headPosition, ComputeCollisionRadius(), Color.red);
            DrawCircleRuntime(headPosition, ComputeHeadHeadRadius(), Color.yellow);
            DrawCircleRuntime(headPosition, ComputeFoodEatRadius(), Color.green);
        }
    }

    private float ComputeFoodEatRadius()
    {
        float r = baseFoodEatRadius + snakeMass * foodPerMass;
        return Mathf.Clamp(r, minFoodEat, maxFoodEat);
    }

    private float ComputeCollisionRadius()
    {
        float r = baseCollisionRadius + snakeMass * collisionPerMass;
        r = Mathf.Clamp(r, minCollisionRadius, maxCollisionRadius);
        return r + Mathf.Max(0f, collisionLatencyPadding);
    }

    private float ComputeHeadHeadRadius()
    {
        float r = baseHeadHeadRadius + snakeMass * headHeadPerMass;
        r = Mathf.Clamp(r, minHeadHeadRadius, maxHeadHeadRadius);
        return r + Mathf.Max(0f, headHeadLatencyPadding);
    }

    private void OnDrawGizmos()
    {
        Vector3 hp = new Vector3(headPosition.x, headPosition.y, 0f);

        if (showCollision)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(hp, ComputeCollisionRadius());
        }

        if (showHeadHead)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(hp, ComputeHeadHeadRadius());
        }

        if (showFoodEat)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(hp, ComputeFoodEatRadius());
        }
    }

    private void DrawCircleRuntime(Vector2 center, float radius, Color c)
    {
        const int steps = 45;
        float step = 360f / steps;

        Vector3 prev = Vector3.zero;

        for (int i = 0; i <= steps; i++)
        {
            float angle = Mathf.Deg2Rad * (i * step);
            Vector3 next = new Vector3(
                center.x + Mathf.Cos(angle) * radius,
                center.y + Mathf.Sin(angle) * radius,
                0f
            );

            if (i > 0)
            {
                Debug.DrawLine(prev, next, c, 0f);
            }

            prev = next;
        }
    }
}
