using System.Collections.Generic; 
using UnityEngine;


namespace SnakyColors
{
    [AddComponentMenu("Procedural Creatures/Rigged Animator")]
    [ExecuteInEditMode]
    public class RigAnimator : MonoBehaviour
    {
        public Transform startBone;
        public Transform endBone;

        public bool is2D = true;

        public List<Transform> Transforms = new();

        [HideInInspector]
        public List<Vector3> lastPositions = new();

        public List<Vector3> lastLocalPositions = new();
        public List<Quaternion> OffsetRotations = new();

        public MoveToTarget moveToTarget = new();


        public float maxAngle = 30f;

        private void OnEnable()
        {
            OffsetRotations = GetRotationsOffsets();

            lastLocalPositions = GetLocalPositionsOfTransform(Transforms);
        }

        public void Update()
        {
            UpdateRig();
        }


        public void UpdateRig()
        {
            if (startBone != null && endBone != null)
            {
                if (Transforms.Count == 0)
                    Transforms = GetBoneChain(startBone, endBone);


                if (OffsetRotations.Count != Transforms.Count - 1)
                    OffsetRotations = GetRotationsOffsets();

                if (moveToTarget.enableMoving && moveToTarget.Target != null)
                {
                    // moving to target and wobbling
                    moveToTarget.MoveTransformToTarget(transform, (Transforms[0].position - Transforms[1].position).normalized);

                    if (moveToTarget.enableWobble && Transforms[0] != null) Transforms[0].position = moveToTarget.wobbleHeadPos;
                }

                if (lastPositions.Count == Transforms.Count)
                {
                    for (int i = 0; i < Transforms.Count - 1; i++)
                    {
                        LookToPositionByOffsetApplied(Transforms[i], lastPositions[i + 1], OffsetRotations[i]);

                        if (is2D)
                        {
                            Vector3 rot = Transforms[i].rotation.eulerAngles;
                            Quaternion newRot = Quaternion.Euler(0f, 0f, rot.z);
                            Transforms[i].rotation = newRot;
                        }

                        if (i != 0 && i < lastLocalPositions.Count)
                            Transforms[i].localPosition = lastLocalPositions[i];
                    }
                }

                lastPositions = GetPositionsOfTransform(Transforms);
                lastLocalPositions = GetLocalPositionsOfTransform(Transforms);
            }

        }

        public void LookToPositionByOffsetApplied(Transform currTransform, Vector3 targetPosition, Quaternion m_rotOffset)
        {
            Vector3 direction = targetPosition - currTransform.position;
            if (direction.sqrMagnitude > 0.0001f) // Avoid zero-length vectors
            {
                currTransform.rotation = Quaternion.LookRotation(direction, currTransform.up) * m_rotOffset;
            }
        }


        // temp reusable lists
        readonly List<Vector3> tempPosArr = new();
        readonly List<Vector3> tempLocalPosArr = new();

        readonly List<Transform> tempBoneChain = new();
        readonly List<Quaternion> tempRots = new();

        public List<Vector3> GetPositionsOfTransform(List<Transform> bones)
        {
            tempPosArr.Clear();

            for (int i = 0; i < bones.Count; i++)
            {
                tempPosArr.Add(bones[i].position);
            }

            return tempPosArr;
        }

        public List<Vector3> GetLocalPositionsOfTransform(List<Transform> bones)
        {
            tempLocalPosArr.Clear();

            for (int i = 0; i < bones.Count; i++)
            {
                tempLocalPosArr.Add(bones[i].localPosition);
            }

            return tempLocalPosArr;
        }


        public List<Transform> GetBoneChain(Transform firstBone, Transform lastBone)
        {
            tempBoneChain.Clear();

            if (firstBone == null || lastBone == null)
            {
                return tempBoneChain;
            }

            Transform currentBone = firstBone;

            while (currentBone != null)
            {
                tempBoneChain.Add(currentBone);

                if (currentBone == lastBone)
                    break;

                if (currentBone.childCount > 0)
                    currentBone = currentBone.GetChild(0);
                else
                {
                    break;
                }
            }

            return tempBoneChain;
        }

        public List<Quaternion> GetRotationsOffsets()
        {
            tempRots.Clear();

            for (int i = 0; i < Transforms.Count - 1; i++)
            {
                Quaternion targetRot = Quaternion.LookRotation(Transforms[i + 1].position - Transforms[i].position, Transforms[i].up);

                Quaternion rotOffset = Quaternion.Inverse(targetRot) * Transforms[i].rotation;

                tempRots.Add(rotOffset);
            }

            return tempRots;
        }


    }
}
