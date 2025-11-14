using System.Collections.Generic;
using UnityEngine;

namespace SnakyColors
{ 
    public class RemoteSnake : MonoBehaviour
    {
        private struct Snapshot
        {
            public float t;
            public Vector2 pos;
            public Vector2 dir;
            public float speed;
        }

        [SerializeField] private float interpolationDelay = 0.06f; // buffer to hide jitter
        [SerializeField] private float maxExtrapolate = 0.03f;      // seconds to predict when late
        [SerializeField] private float dirLerp = 0.15f;              // orient smoothing
        [SerializeField] private float easeRate = 10f;              // position easing rate (per second) 

        private readonly List<Snapshot> _snaps = new List<Snapshot>(16);
        private SegmentedCreator _snake;
        [SerializeField] private bool debugTiming = false;
        private float _nextDbg; 
        private float _serverOffsetSec = 0f; 
        public float ServerOffset
        {
            set { _serverOffsetSec = value; }
        }

        private void Awake()
        {
            _snake = GetComponent<SegmentedCreator>();
            if (_snake != null && _snake.moveToTarget != null)
            {
                _snake.moveToTarget.enableMoving = false;
            }
        }

        public void SetTargetLength(int len)
        {
            if (_snake == null) return;
            _snake.SetRibCountNoClear(Mathf.Max(2, len));
        } 

        // Feed by NetworkClient on each server update (server seconds)
        public void OnServerUpdate(Vector2 headPos, float serverSeconds, float? serverSpeed = null)
        {
            float serverTime = serverSeconds;
            if (_snaps.Count > 0 && serverTime <= _snaps[_snaps.Count - 1].t)
            {
                serverTime = _snaps[_snaps.Count - 1].t + 0.0001f;
            }

            Vector2 pos = headPos;

            // derive dir/speed from last sample if not provided
            Vector2 dir = Vector2.up;
            float spd = serverSpeed.HasValue ? Mathf.Max(0.01f, serverSpeed.Value) : 0f;
            if (_snaps.Count > 0)
            {
                var last = _snaps[_snaps.Count - 1];
                Vector2 d = pos - last.pos;
                float dt = Mathf.Max(0.02f, serverTime - last.t);
                if (d.sqrMagnitude > 0.000001f)
                {
                    dir = d.normalized;
                    if (!serverSpeed.HasValue)
                        spd = d.magnitude / dt;
                }
                else
                {
                    dir = last.dir;
                    if (!serverSpeed.HasValue) spd = last.speed;
                }
            }
            else
            {
                if (!serverSpeed.HasValue) spd = 0f;
            }
            if (_snaps.Count > 0)
            {
                var last = _snaps[_snaps.Count - 1];
                float blend = Mathf.Lerp(0.55f, 0.75f, Mathf.InverseLerp(0f, 6f, spd));
                pos = Vector2.Lerp(last.pos, pos, blend);
                //pos = Vector2.Lerp(last.pos, pos, 0.7f);   // soften noisy packets
            }
            _snaps.Add(new Snapshot { t = serverTime, pos = pos, dir = dir, speed = spd });
            float minKeep = serverTime - 1.0f;
            while (_snaps.Count > 2 && _snaps[0].t < minKeep) _snaps.RemoveAt(0);
        } 

        private void Update()
        {
            if (_snaps.Count == 0) return;
            float renderTime = (Time.time + _serverOffsetSec) - interpolationDelay;
            if (debugTiming && Time.time >= _nextDbg)
            {
                float latest = _snaps[_snaps.Count - 1].t;
                Debug.Log($"[REMOTE] renderTime={(Time.time + _serverOffsetSec - interpolationDelay):F3}, latestSnapshot={latest:F3}, count={_snaps.Count}");
                _nextDbg = Time.time + 1f;
            }

            // ensure we have at least 2 snapshots bracketing renderTime, else handle edges
            // drop older ones while the second snapshot is still <= render time
            while (_snaps.Count >= 3 && _snaps[1].t < renderTime)
                _snaps.RemoveAt(0);

            Vector3 newPos;
            Vector3 newUp = transform.up;

            if (_snaps.Count >= 2 && _snaps[0].t <= renderTime && _snaps[1].t >= renderTime)
            {
                var a = _snaps[0];
                var b = _snaps[1];
                float t = Mathf.InverseLerp(a.t, b.t, renderTime);
                Vector2 p = Vector2.Lerp(a.pos, b.pos, t);
                // Smooth direction interpolation (angle lerp)
                float angA = Mathf.Atan2(a.dir.y, a.dir.x) * Mathf.Rad2Deg;
                float angB = Mathf.Atan2(b.dir.y, b.dir.x) * Mathf.Rad2Deg;
                float lerpAng = Mathf.LerpAngle(angA, angB, t);
                Vector2 d = new Vector2(Mathf.Cos(lerpAng * Mathf.Deg2Rad), Mathf.Sin(lerpAng * Mathf.Deg2Rad));
                if (d.sqrMagnitude < 0.000001f) d = (b.pos - a.pos).normalized;
                newPos = new Vector3(p.x, p.y, 0f);
                newUp = Vector3.Lerp(transform.up, new Vector3(d.x, d.y, 0f), Mathf.Clamp01(dirLerp));
            }
            else
            {
                // Extrapolate slightly when no future sample yet
                var last = _snaps[_snaps.Count - 1];
                float dt = Mathf.Clamp(renderTime - last.t, 0f, maxExtrapolate);
                if (last.speed < 0.01f) dt = 0f; // clamp jitter at rest
                Vector2 p = last.pos + last.dir * last.speed * dt;
                newPos = new Vector3(p.x, p.y, 0f);
                newUp = Vector3.Lerp(transform.up, new Vector3(last.dir.x, last.dir.y, 0f), Mathf.Clamp01(dirLerp));
            } 

            transform.position = Vector3.Lerp(transform.position, newPos, Mathf.Clamp01(Time.deltaTime * easeRate));
            transform.up = newUp;
        }
    }
}
