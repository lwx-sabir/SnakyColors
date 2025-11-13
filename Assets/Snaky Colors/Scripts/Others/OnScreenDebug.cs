using UnityEngine;
using System.Collections.Generic;

public class OnScreenDebug : MonoBehaviour
{
    private static readonly Queue<string> messages = new Queue<string>();
    private static readonly object locker = new object();
    private const int maxMessages = 100; // keep a bit more history

    [Header("Appearance")]
    [SerializeField] private int fontSize = 18;
    [SerializeField] private Color textColor = Color.yellow;
    [SerializeField] private float lineHeight = 22f;

    [Header("Layout")]
    [SerializeField] private Vector2 startPos = new Vector2(10, 10);
    [SerializeField] private Vector2 windowSize = new Vector2(600, 300);

    private Vector2 scrollPos = Vector2.zero;

    public static void Log(string msg)
    {
        lock (locker)
        {
            if (messages.Count > maxMessages)
                messages.Dequeue();
            messages.Enqueue($"[{System.DateTime.Now:HH:mm:ss}] {msg}");
        }
    }

    private void OnGUI()
    {
        GUIStyle style = new GUIStyle(GUI.skin.label)
        {
            fontSize = fontSize,
            normal = { textColor = textColor }
        };

        lock (locker)
        {
            // calculate total height
            float totalHeight = messages.Count * lineHeight;
            Rect viewRect = new Rect(0, 0, windowSize.x - 20, totalHeight + 5);

            // draw scrollable window
            GUILayout.BeginArea(new Rect(startPos.x, startPos.y, windowSize.x, windowSize.y), GUI.skin.box);
            scrollPos = GUI.BeginScrollView(
                new Rect(0, 0, windowSize.x, windowSize.y),
                scrollPos,
                viewRect,
                false,
                true
            );

            int i = 0;
            foreach (var m in messages)
            {
                GUI.Label(new Rect(5, i * lineHeight, viewRect.width - 10, lineHeight + 4f), m, style);
                i++;
            }

            GUI.EndScrollView();
            GUILayout.EndArea();

            // Auto-scroll to bottom if already near bottom
            if (Event.current.type == EventType.Repaint)
            {
                float maxScroll = Mathf.Max(0, totalHeight - windowSize.y);
                if (scrollPos.y >= maxScroll - lineHeight * 1.5f)
                    scrollPos.y = maxScroll;
            }
        }
    }
}
