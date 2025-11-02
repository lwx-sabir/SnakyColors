using System;
using UnityEngine;

namespace SnakyColors
{
    [Serializable]
    public class MoveToTarget
    {
        public Transform Target;

        [HideInInspector]
        public Vector3 wobbleHeadPos;


        public bool enableMoving = false;

        public bool moveTowardMouse = false;
        public float arrowDist = 2f;

        public bool enableWobble = false;

        public bool moveThroughTarget;
        public float maxTurningAngle = 10f;

        public float movingSpeed = 3f;
        public float wobbleAmplitude = 3f;
        public float wobbleFreq = 3f;

        private bool keepWobbling = true;


        public void MoveTransformToTarget(Transform mainTransform, Vector3 headDir)
        {
            if (Target != null && Application.isPlaying && Camera.main != null)
            {
                if (moveTowardMouse)
                {
                    Vector3 mousePosition = Camera.main.ScreenToWorldPoint(Input.mousePosition);
                    mousePosition.z = mainTransform.position.z;

                    float angle = Mathf.Atan2(mousePosition.y - mainTransform.position.y, mousePosition.x - mainTransform.position.x) * Mathf.Rad2Deg;

                    // Rotate the arrow around the parent
                    Target.SetPositionAndRotation(mainTransform.position + Quaternion.Euler(0, 0, angle) * Vector3.right * arrowDist, Quaternion.Euler(0, 0, angle - 90));
                }

                Vector3 dirToTarget = (Target.position - mainTransform.position).normalized;

                float angleInRadians = maxTurningAngle * Mathf.Deg2Rad;
                if (Vector3.Cross(headDir, dirToTarget).z < 0)
                {
                    angleInRadians *= -1f;
                }

                Vector2 rotatedDirection = new(
                    headDir.x * Mathf.Cos(angleInRadians) - headDir.y * Mathf.Sin(angleInRadians),
                    headDir.x * Mathf.Sin(angleInRadians) + headDir.y * Mathf.Cos(angleInRadians)
                );

                if (((Vector2)Target.position - (Vector2)mainTransform.position).magnitude > 0.5f || moveThroughTarget)
                {
                    // actual moving
                    dirToTarget = Vector3.Angle(headDir, dirToTarget) < maxTurningAngle ? dirToTarget : rotatedDirection;

                    mainTransform.position = Vector3.MoveTowards(mainTransform.position, mainTransform.position + dirToTarget, movingSpeed * Time.deltaTime);

                    if (enableWobble && !moveThroughTarget) SetHeadPosAfterWobble(mainTransform);
                    else wobbleHeadPos = mainTransform.position;
                }

                keepWobbling = !moveThroughTarget;
                if (((Vector2)Target.position - (Vector2)mainTransform.position).magnitude < 0.5f) keepWobbling = false;

            }
            else wobbleHeadPos = mainTransform.position;

        }

        private float _wobblePhase = 0f;

        public void SetHeadPosAfterWobble(Transform mainTransform)
        {
            // wobbling movement
            if (enableWobble && keepWobbling && Application.isPlaying)
            {
                Vector3 targetDirection = (Target.position - mainTransform.position).normalized;

                if (((Vector2)Target.position - (Vector2)mainTransform.position).magnitude > 0.5f)
                {
                    _wobblePhase += Time.deltaTime * wobbleFreq * movingSpeed;
                    _wobblePhase %= Mathf.PI * 2f;

                    float sineOffset = Mathf.Sin(_wobblePhase) * wobbleAmplitude * mainTransform.localScale.x;

                    Vector3 perpendicularDirection = Vector3.Cross(targetDirection, Vector3.forward).normalized;


                    wobbleHeadPos = mainTransform.position + perpendicularDirection * sineOffset;
                }
            } 
        } 
    }

}