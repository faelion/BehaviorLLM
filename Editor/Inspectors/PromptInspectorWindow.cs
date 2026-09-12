using System.Collections.Generic;
using System.Text;
using BehaviorLLM.Core.Decisions;
using UnityEditor;
using UnityEngine;

namespace BehaviorLLM.Editor.Inspectors
{
    /// <summary>
    /// Live view of every decision as it happens: what was sent, what came back, and what the game
    /// did with it.
    ///
    /// The package already records all of this to a CSV, but a run report answers "how did the
    /// hundred decisions go" and this answers "what happened in *that* one". Before it existed, the
    /// way to see a prompt was to turn on <c>logPrompts</c>, replay the scene and scroll the
    /// console, which does not survive eight decision makers talking at once.
    ///
    /// It listens only while it is open (<see cref="DecisionMaker.CaptureExchanges"/>), so a project
    /// that never opens it pays nothing for it.
    /// </summary>
    public class PromptInspectorWindow : EditorWindow
    {
        private const int DefaultCapacity = 200;

        /// <summary>One decision, with the text that produced it. Copied at capture time, because
        /// the decision maker keeps only its most recent exchange.</summary>
        private class Entry
        {
            public int Index;
            public string Source;
            public float Time;
            public string Action;
            public string Argument;
            public DecisionResultType Result;
            public bool UsedFallback;
            public float LatencyMs;
            public int PromptTokens, CompletionTokens, CachedTokens;
            public bool StructuredOutput;
            public string SystemPrompt, StatePrompt, Schema, ResponseText, ReasoningText;
            public string Reason, FailureReason;

            /// <summary>Anything that is not "the model chose a bound action and the game ran it".</summary>
            public bool Failed =>
                Result != DecisionResultType.ModelAction || UsedFallback || !string.IsNullOrEmpty(FailureReason);
        }

        private readonly List<Entry> entries = new List<Entry>();
        private readonly HashSet<string> sources = new HashSet<string>();

        private Vector2 listScroll, detailScroll;
        private int selected = -1;
        private bool paused;
        private bool failuresOnly;
        private bool clearOnPlay = true;
        private string sourceFilter = "";
        private int capacity = DefaultCapacity;
        private float split = 0.42f;

        private GUIStyle rowStyle, selectedRowStyle, monoStyle, headerStyle;

        [MenuItem("Tools/BehaviorLLM/Prompt Inspector")]
        public static void Open()
        {
            PromptInspectorWindow window = GetWindow<PromptInspectorWindow>();
            window.titleContent = new GUIContent("Prompt Inspector");
            window.minSize = new Vector2(680f, 420f);
            window.Show();
        }

        private void OnEnable()
        {
            DecisionMaker.CaptureExchanges = true;
            DecisionMaker.DecisionInspected += OnDecision;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private void OnDisable()
        {
            DecisionMaker.DecisionInspected -= OnDecision;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            // Only the last window out turns the lights off; two open inspectors are unusual but
            // one closing must not blind the other.
            if (!HasOpenInstances<PromptInspectorWindow>()) DecisionMaker.CaptureExchanges = false;
        }

        private void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode && clearOnPlay) Clear();
        }

        // ---------------------------------------------------------------- capture

        private void OnDecision(DecisionMaker source, DecisionTelemetry telemetry)
        {
            if (paused || source == null || telemetry == null) return;

            entries.Add(new Entry
            {
                Index = telemetry.decisionIndex,
                Source = string.IsNullOrEmpty(telemetry.sourceId) ? source.name : telemetry.sourceId,
                Time = telemetry.timestampSec,
                Action = telemetry.actionName,
                Argument = telemetry.argument,
                Result = telemetry.resultType,
                UsedFallback = telemetry.usedFallback,
                LatencyMs = telemetry.latencyMs,
                PromptTokens = telemetry.promptTokens,
                CompletionTokens = telemetry.completionTokens,
                CachedTokens = telemetry.cachedTokens,
                StructuredOutput = telemetry.structuredOutput,
                SystemPrompt = source.LastSystemPrompt,
                StatePrompt = source.LastStatePrompt,
                Schema = source.LastSchema,
                ResponseText = source.LastResponseText,
                ReasoningText = source.LastReasoningText,
                Reason = telemetry.reason,
                FailureReason = FirstNonEmpty(telemetry.parseFailureReason, telemetry.backendError,
                                              telemetry.fallbackReason, telemetry.argumentPolicyReason)
            });

            sources.Add(entries[entries.Count - 1].Source);

            // A ring buffer, so a long run cannot grow the window without bound.
            while (entries.Count > Mathf.Max(10, capacity))
            {
                entries.RemoveAt(0);
                if (selected >= 0) selected--;
            }

            // Follow the tail unless the user has picked a row to look at.
            if (selected < 0) listScroll.y = float.MaxValue;
            Repaint();
        }

        private static string FirstNonEmpty(params string[] candidates)
        {
            for (int i = 0; i < candidates.Length; i++)
                if (!string.IsNullOrWhiteSpace(candidates[i])) return candidates[i];
            return "";
        }

        private void Clear()
        {
            entries.Clear();
            sources.Clear();
            selected = -1;
        }

        // ---------------------------------------------------------------- drawing

        private void OnGUI()
        {
            EnsureStyles();
            DrawToolbar();

            List<Entry> shown = Filtered();

            float listHeight = Mathf.Clamp(position.height * split, 90f, position.height - 140f);
            DrawList(shown, listHeight);
            DrawSplitter(listHeight);
            DrawDetail(shown);
        }

        private void EnsureStyles()
        {
            if (rowStyle != null) return;

            rowStyle = new GUIStyle(EditorStyles.label) { padding = new RectOffset(6, 6, 2, 2), richText = true };
            selectedRowStyle = new GUIStyle(rowStyle) { normal = { background = Texture2D.linearGrayTexture } };
            monoStyle = new GUIStyle(EditorStyles.textArea)
            {
                font = Font.CreateDynamicFontFromOSFont(new[] { "Consolas", "Menlo", "Courier New" }, 11),
                wordWrap = true,
                richText = false
            };
            headerStyle = new GUIStyle(EditorStyles.miniBoldLabel) { padding = new RectOffset(6, 6, 2, 2) };
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                paused = GUILayout.Toggle(paused, paused ? "Paused" : "Recording",
                                          EditorStyles.toolbarButton, GUILayout.Width(80f));

                if (GUILayout.Button("Clear", EditorStyles.toolbarButton, GUILayout.Width(50f))) Clear();

                failuresOnly = GUILayout.Toggle(failuresOnly, "Failures only",
                                                EditorStyles.toolbarButton, GUILayout.Width(90f));

                GUILayout.Space(8f);
                GUILayout.Label("Source", EditorStyles.miniLabel, GUILayout.Width(45f));
                List<string> options = new List<string> { "All" };
                options.AddRange(sources);
                int current = Mathf.Max(0, options.IndexOf(string.IsNullOrEmpty(sourceFilter) ? "All" : sourceFilter));
                int picked = EditorGUILayout.Popup(current, options.ToArray(), EditorStyles.toolbarPopup, GUILayout.Width(130f));
                sourceFilter = picked <= 0 ? "" : options[picked];

                GUILayout.FlexibleSpace();

                clearOnPlay = GUILayout.Toggle(clearOnPlay, "Clear on play", EditorStyles.toolbarButton, GUILayout.Width(90f));
                GUILayout.Label("Keep", EditorStyles.miniLabel, GUILayout.Width(32f));
                capacity = EditorGUILayout.IntField(capacity, EditorStyles.toolbarTextField, GUILayout.Width(50f));

                using (new EditorGUI.DisabledScope(selected < 0))
                {
                    if (GUILayout.Button("Copy", EditorStyles.toolbarButton, GUILayout.Width(45f))) CopySelected();
                }
            }

            if (entries.Count != 0 || Application.isPlaying) return;
            EditorGUILayout.HelpBox(
                "Press Play. Every decision made while this window is open is captured here, with the " +
                "exact prompt sent and the exact answer received.", MessageType.Info);
        }

        private List<Entry> Filtered()
        {
            List<Entry> shown = new List<Entry>(entries.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                if (failuresOnly && !entries[i].Failed) continue;
                if (!string.IsNullOrEmpty(sourceFilter) && entries[i].Source != sourceFilter) continue;
                shown.Add(entries[i]);
            }
            return shown;
        }

        private void DrawList(List<Entry> shown, float height)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("#", headerStyle, GUILayout.Width(38f));
                GUILayout.Label("Time", headerStyle, GUILayout.Width(52f));
                GUILayout.Label("Source", headerStyle, GUILayout.Width(110f));
                GUILayout.Label("Action", headerStyle, GUILayout.Width(190f));
                GUILayout.Label("Latency", headerStyle, GUILayout.Width(60f));
                GUILayout.Label("Tokens (cached)", headerStyle, GUILayout.Width(110f));
                GUILayout.Label("Result", headerStyle);
            }

            using (EditorGUILayout.ScrollViewScope scroll =
                   new EditorGUILayout.ScrollViewScope(listScroll, GUILayout.Height(height)))
            {
                listScroll = scroll.scrollPosition;

                for (int i = 0; i < shown.Count; i++)
                {
                    Entry e = shown[i];
                    bool isSelected = selected >= 0 && selected < entries.Count && entries[selected] == e;

                    using (new EditorGUILayout.HorizontalScope(isSelected ? selectedRowStyle : rowStyle))
                    {
                        GUILayout.Label(e.Index.ToString(), rowStyle, GUILayout.Width(38f));
                        GUILayout.Label(e.Time.ToString("0.0") + "s", rowStyle, GUILayout.Width(52f));
                        GUILayout.Label(e.Source, rowStyle, GUILayout.Width(110f));
                        GUILayout.Label(Describe(e), rowStyle, GUILayout.Width(190f));
                        GUILayout.Label(e.LatencyMs.ToString("0") + " ms", rowStyle, GUILayout.Width(60f));
                        GUILayout.Label($"{e.PromptTokens}+{e.CompletionTokens} ({e.CachedTokens})", rowStyle, GUILayout.Width(110f));
                        GUILayout.Label(ResultLabel(e), rowStyle);
                    }

                    Rect row = GUILayoutUtility.GetLastRect();
                    if (Event.current.type == EventType.MouseDown && row.Contains(Event.current.mousePosition))
                    {
                        selected = entries.IndexOf(e);
                        detailScroll = Vector2.zero;
                        Event.current.Use();
                        Repaint();
                    }
                }

                if (shown.Count == 0 && entries.Count > 0)
                    EditorGUILayout.LabelField("Nothing matches the current filter.", EditorStyles.centeredGreyMiniLabel);
            }
        }

        private static string Describe(Entry e)
        {
            if (string.IsNullOrEmpty(e.Action)) return "-";
            return string.IsNullOrEmpty(e.Argument) ? e.Action : $"{e.Action}({e.Argument})";
        }

        private static string ResultLabel(Entry e)
        {
            if (!string.IsNullOrEmpty(e.FailureReason)) return $"{e.Result} - {e.FailureReason}";
            return e.UsedFallback ? e.Result + " (fallback)" : e.Result.ToString();
        }

        private void DrawSplitter(float listHeight)
        {
            Rect handle = GUILayoutUtility.GetRect(position.width, 5f);
            EditorGUI.DrawRect(handle, new Color(0f, 0f, 0f, 0.25f));
            EditorGUIUtility.AddCursorRect(handle, MouseCursor.ResizeVertical);

            if (Event.current.type == EventType.MouseDrag && handle.Contains(Event.current.mousePosition))
            {
                split = Mathf.Clamp(Event.current.mousePosition.y / position.height, 0.15f, 0.8f);
                Event.current.Use();
                Repaint();
            }
        }

        private void DrawDetail(List<Entry> shown)
        {
            if (selected < 0 || selected >= entries.Count)
            {
                EditorGUILayout.LabelField("Select a decision to see its prompt and answer.",
                                           EditorStyles.centeredGreyMiniLabel);
                return;
            }

            Entry e = entries[selected];

            using (EditorGUILayout.ScrollViewScope scroll = new EditorGUILayout.ScrollViewScope(detailScroll))
            {
                detailScroll = scroll.scrollPosition;

                EditorGUILayout.LabelField(
                    $"#{e.Index}  {e.Source}  -  {Describe(e)}  -  {e.LatencyMs:0} ms  -  " +
                    $"structured output {(e.StructuredOutput ? "on" : "off")}",
                    EditorStyles.boldLabel);

                if (!string.IsNullOrEmpty(e.FailureReason))
                    EditorGUILayout.HelpBox(e.FailureReason, MessageType.Warning);

                if (!string.IsNullOrWhiteSpace(e.Reason)) Section("Model's stated reason", e.Reason, 40f);
                if (!string.IsNullOrWhiteSpace(e.ReasoningText)) Section("Reasoning channel", e.ReasoningText, 90f);

                Section("Answer (raw)", e.ResponseText, 60f);
                Section("STATE (this decision)", e.StatePrompt, 150f);
                Section("System prompt (cached prefix)", e.SystemPrompt, 200f);
                if (!string.IsNullOrWhiteSpace(e.Schema)) Section("Applied JSON Schema", e.Schema, 150f);
            }
        }

        private void Section(string title, string body, float height)
        {
            EditorGUILayout.LabelField(title, EditorStyles.miniBoldLabel);
            // Selectable rather than read-only text: the whole point is to copy a prompt out and
            // paste it at llama-cli, and a disabled field cannot be selected.
            EditorGUILayout.SelectableLabel(string.IsNullOrEmpty(body) ? "(empty)" : body,
                                            monoStyle, GUILayout.Height(height));
            EditorGUILayout.Space(2f);
        }

        private void CopySelected()
        {
            if (selected < 0 || selected >= entries.Count) return;
            Entry e = entries[selected];

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"# decision {e.Index} - {e.Source} - {Describe(e)}");
            sb.AppendLine($"# {e.LatencyMs:0} ms, {e.PromptTokens}+{e.CompletionTokens} tokens ({e.CachedTokens} cached), " +
                          $"structured output {(e.StructuredOutput ? "on" : "off")}, result {e.Result}");
            if (!string.IsNullOrEmpty(e.FailureReason)) sb.AppendLine($"# failure: {e.FailureReason}");
            sb.AppendLine().AppendLine("--- SYSTEM ---").AppendLine(e.SystemPrompt);
            sb.AppendLine().AppendLine("--- STATE ---").AppendLine(e.StatePrompt);
            sb.AppendLine().AppendLine("--- ANSWER ---").AppendLine(e.ResponseText);
            if (!string.IsNullOrWhiteSpace(e.Schema)) sb.AppendLine().AppendLine("--- SCHEMA ---").AppendLine(e.Schema);

            EditorGUIUtility.systemCopyBuffer = sb.ToString();
            ShowNotification(new GUIContent("Decision copied"));
        }
    }
}
