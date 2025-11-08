using UnityEngine;

namespace SnakyColors
{
    [RequireComponent(typeof(SegmentedCreator))]
    public class AIMovement : MonoBehaviour
    {
        private SegmentedCreator snake;
        private Transform snakeHead;
        private Transform snakeTarget;
        private float snakeSpeed; 
        private float maxTurningAngle;

        // AI state
        private Vector3 currentAIDirection = Vector3.up;
        private Vector3 targetFollowVelocity = Vector3.zero;

        // AI Behavior Tunables
        private float targetDistance = 5f;
        private float boundaryCheckDist = 10f; // Start turning 10 units from wall
        private float wanderTurnAngle = 30f;

        void Start()
        {
            snake = GetComponent<SegmentedCreator>();
            snakeHead = this.transform;
            snakeTarget = snake.moveToTarget.Target;

            if (snakeTarget == null)
            {
                Debug.LogError($"AI {gameObject.name} has no target!", this);
                this.enabled = false;
            }

            currentAIDirection = new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), 0).normalized;
        }

        void Update()
        {
            if (snake == null || snakeTarget == null) return;

            float speed = snakeSpeed;
            float turnAngle = maxTurningAngle;

            snake.moveToTarget.movingSpeed = speed;
            snake.moveToTarget.maxTurningAngle = turnAngle;

            // Current head direction
            Vector3 headDir = snakeTarget.up;

            // --- 1. Boundary Avoidance ---
            float worldHalf = NetworkClient.Instance.CurrentWorldSize / 2f;
            Vector3 headPos = snakeHead.position;

            if (headPos.x > worldHalf - boundaryCheckDist && headDir.x > 0) currentAIDirection = new Vector3(-headDir.y, headDir.x, 0);
            else if (headPos.x < -worldHalf + boundaryCheckDist && headDir.x < 0) currentAIDirection = new Vector3(headDir.y, -headDir.x, 0);
            else if (headPos.y > worldHalf - boundaryCheckDist && headDir.y > 0) currentAIDirection = new Vector3(headDir.x, -Mathf.Abs(headDir.y), 0);
            else if (headPos.y < -worldHalf + boundaryCheckDist && headDir.y < 0) currentAIDirection = new Vector3(headDir.x, Mathf.Abs(headDir.y), 0);
            else
            {
                // --- 2. Wander ---
                if (UnityEngine.Random.Range(0f, 1f) < 0.01f)
                {
                    float randomAngle = UnityEngine.Random.Range(-wanderTurnAngle, wanderTurnAngle);
                    currentAIDirection = Quaternion.Euler(0, 0, randomAngle) * headDir;
                }
            }

            // --- 3. Rotate target smoothly toward AI direction ---
            Vector3 desiredDir = currentAIDirection.normalized;
            snakeTarget.up = Vector3.RotateTowards(snakeTarget.up, desiredDir, turnAngle * Mathf.Deg2Rad * Time.deltaTime, 0f);

            // --- 4. Keep the target a fixed lead ahead of the head (unified with SlitherMovement)
            snakeTarget.position = snakeHead.position + snakeTarget.up * targetDistance;

            // IMPORTANT: Do NOT call MoveTransformToTarget here; SegmentedCreator moves once per frame in LateUpdate.
        }

        public void Init(float speed, float maxTurningAngle)
        {
            this.maxTurningAngle = maxTurningAngle;
            this.snakeSpeed = speed;
        }
    }
}
