using System.Text;
using UnityEngine;

namespace Project.Samples.PrisonYard
{
    /// <summary>
    /// An on-screen summary so the prison can be followed without reading the console: the time of
    /// day, what is locked, what is going wrong, and the last few radio messages, which is where
    /// the decision makers' conversation with each other actually shows up.
    ///
    /// Deliberately OnGUI: no canvas, no prefabs, no art, so the sample stays buildable from one
    /// menu item.
    /// </summary>
    [AddComponentMenu("PrisonYard/Prison Hud")]
    public class PrisonHud : MonoBehaviour
    {
        [Tooltip("Show the panel. Turn it off for a clean screenshot.")]
        public bool show = true;

        [Tooltip("How many recent radio messages to list.")]
        public int radioLines = 5;

        private PrisonClock clock;
        private PrisonStatusBoard board;
        private PrisonRadio radio;
        private GUIStyle panelStyle;
        private GUIStyle textStyle;

        private void Awake()
        {
            clock = FindFirstObjectByType<PrisonClock>();
            board = FindFirstObjectByType<PrisonStatusBoard>();
            radio = FindFirstObjectByType<PrisonRadio>();
        }

        private void OnGUI()
        {
            if (!show) return;
            EnsureStyles();

            GUILayout.BeginArea(new Rect(12, 12, 430, 460), panelStyle);
            GUILayout.Label(BuildStatus(), textStyle);
            GUILayout.Space(6);
            GUILayout.Label(BuildRadio(), textStyle);
            GUILayout.Space(6);
            GUILayout.Label(
                "DIRECTOR KEYS\n" +
                "1  start a fight        2  plant contraband\n" +
                "3  injure a guard       4  cut the lights\n" +
                "5  open all gates       right-drag orbit, scroll zoom",
                textStyle);
            GUILayout.EndArea();
        }

        private string BuildStatus()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("PRISON YARD\n");

            if (clock != null)
            {
                sb.Append($"Phase: {clock.CurrentPhase}  ({Mathf.RoundToInt(clock.SecondsLeftInPhase)}s left)   ")
                  .Append($"Prisoners should be in: {clock.ScheduledSection.Id()}\n");
            }

            if (board == null) return sb.ToString();

            sb.Append("\n");
            for (int i = 0; i < PrisonSections.All.Length; i++)
            {
                PrisonSection s = PrisonSections.All[i];
                sb.Append(board.IsLocked(s) ? "[LOCKED] " : "         ")
                  .Append(s.Id().PadRight(12))
                  .Append($"{board.PrisonersIn(s)}p {board.GuardsIn(s)}g");
                if (board.HasIncidentIn(s)) sb.Append("   << INCIDENT");
                sb.Append('\n');
            }

            sb.Append($"\nIncidents opened {board.IncidentsOpened}, resolved {board.IncidentsResolved}");
            return sb.ToString();
        }

        private string BuildRadio()
        {
            if (radio == null) return "RADIO\n(offline)";

            StringBuilder sb = new StringBuilder("RADIO\n");
            var messages = radio.Messages;
            int from = Mathf.Max(0, messages.Count - Mathf.Max(1, radioLines));
            if (messages.Count == 0) sb.Append("(silent)");

            for (int i = from; i < messages.Count; i++)
            {
                sb.Append($"{messages[i].Sender}: {messages[i].Text}\n");
            }
            return sb.ToString().TrimEnd();
        }

        private void EnsureStyles()
        {
            if (panelStyle == null)
            {
                panelStyle = new GUIStyle(GUI.skin.box) { padding = new RectOffset(10, 10, 10, 10) };
            }
            if (textStyle == null)
            {
                textStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 12,
                    richText = false,
                    wordWrap = true
                };
                textStyle.normal.textColor = Color.white;
            }
        }
    }
}
