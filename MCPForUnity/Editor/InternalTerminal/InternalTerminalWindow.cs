using System;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace WTL.InternalTerminal.Editor
{
    public sealed class InternalTerminalWindow : EditorWindow
    {
        private readonly TerminalScreenBuffer buffer = new TerminalScreenBuffer();
        private InternalTerminalBackend backend;
        private TerminalWebSocketClient client;
        private GUIStyle cellStyle;
        private GUIStyle boldCellStyle;
        private Font terminalFont;
        private Font terminalBoldFont;
        private string loadedFontName;
        private string loadedFontSource;
        private int loadedFontSize;
        private string pendingContextPaste;
        private string lastDragPasteText;
        private double lastDragPasteTime;
        private Vector2 cellSize = new Vector2(8f, 18f);
        private Vector2 terminalPadding = new Vector2(2f, 2f);
        private bool terminalFocused;
        private string backendStatus = "Backend stopped";
        private string socketStatus = "WS stopped";
        private string lastLog = string.Empty;
        private int lastCols;
        private int lastRows;
        private float scrollBarValue;
        private double nextCursorRepaint;
        private bool selecting;
        private Vector2Int selectionAnchor = new Vector2Int(-1, -1);
        private Vector2Int selectionFocus = new Vector2Int(-1, -1);
        private string imeText = string.Empty;
        private string imeControlName = "WTL_InternalTerminal_IME";
        private bool imeModeCaptured;
        private IMECompositionMode previousImeMode;

        [MenuItem("Window/WTL/Internal Terminal", false, 0)]
        public static void Open()
        {
            var window = GetWindow<InternalTerminalWindow>();
            window.titleContent = new GUIContent("Terminal");
            window.minSize = new Vector2(720, 420);
            window.Show();
        }

        [MenuItem("Window/WTL/Internal Terminal/Open", false, 0)]
        private static void OpenFromSubmenu()
        {
            Open();
        }

        public static void PasteToActiveTerminal(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            var window = GetWindow<InternalTerminalWindow>();
            window.titleContent = new GUIContent("Terminal");
            window.minSize = new Vector2(720, 420);
            window.Show();
            window.PasteContext(text);
        }

        [MenuItem("Window/WTL/Internal Terminal/List OS Fonts")]
        public static void ListOsFonts()
        {
            Debug.Log("WTL Internal Terminal OS fonts:\n" + string.Join("\n", Font.GetOSInstalledFontNames()));
        }

        [MenuItem("Window/WTL/Internal Terminal/Use Bundled Font")]
        public static void UseBundledFont()
        {
            InternalTerminalSettings.instance.ResetTerminalFont();
            foreach (var window in Resources.FindObjectsOfTypeAll<InternalTerminalWindow>())
            {
                window.InvalidateFontStyle();
                window.Repaint();
            }

            Debug.Log("WTL Internal Terminal font reset to bundled Sarasa Mono SC.");
        }

        [MenuItem("Window/WTL/Internal Terminal/Diagnose Font Loading")]
        public static void DiagnoseFontLoading()
        {
            Debug.Log(InternalTerminalFontResolver.BuildDiagnosticReport(InternalTerminalSettings.instance.TerminalFontName));
        }

        private void OnEnable()
        {
            backend = InternalTerminalSession.SharedBackend;
            backend.LogReceived += OnLogReceived;
            backend.Exited += OnBackendExited;

            client = new TerminalWebSocketClient();

            EditorApplication.update += DrainClientData;
            EditorApplication.delayCall += EnsureTerminalConnected;
            wantsMouseMove = true;
        }

        private void OnDisable()
        {
            EditorApplication.update -= DrainClientData;
            RestoreImeMode();

            if (client != null)
            {
                client.Dispose();
                client = null;
            }

            if (backend != null)
            {
                backend.LogReceived -= OnLogReceived;
                backend.Exited -= OnBackendExited;
                backend = null;
            }
        }

        private void OnLostFocus()
        {
            SetTerminalFocused(false);
        }

        private void OnGUI()
        {
            EnsureStyle();
            DrawToolbar();

            var terminalRect = new Rect(0, EditorGUIUtility.singleLineHeight + 6, position.width, position.height - EditorGUIUtility.singleLineHeight - 6);
            var contentRect = NeedsScrollBar ? new Rect(terminalRect.x, terminalRect.y, terminalRect.width - 14f, terminalRect.height) : terminalRect;
            HandleKeyboard(contentRect);
            DrawTerminal(contentRect);
            DrawImeBridge(contentRect);
            DrawScrollBar(terminalRect, contentRect);
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Restart", EditorStyles.toolbarButton, GUILayout.Width(72)))
                {
                    RestartTerminal();
                }

                GUILayout.FlexibleSpace();
                GUILayout.Label(backendStatus + " | " + socketStatus + " | Font: " + (loadedFontName ?? "auto"), EditorStyles.miniLabel);
            }
        }

        private void DrawTerminal(Rect rect)
        {
            EditorGUI.DrawRect(rect, TerminalPalette.Background);

            var cols = Mathf.Max(2, Mathf.FloorToInt((rect.width - terminalPadding.x * 2f) / cellSize.x));
            var rows = Mathf.Max(2, Mathf.FloorToInt((rect.height - terminalPadding.y * 2f) / cellSize.y));
            if (cols != lastCols || rows != lastRows)
            {
                lastCols = cols;
                lastRows = rows;
                buffer.Resize(cols, rows);
                client?.Resize(cols, rows);
            }

            GUI.BeginClip(rect);
            DrawBackgroundRuns(cols, rows);
            DrawSelection(cols, rows);

            DrawTextRuns(cols, rows);

            if (buffer.CursorVisible && terminalFocused && EditorWindow.focusedWindow == this && (DateTime.Now.Millisecond / 500) % 2 == 0)
            {
                var cursorRect = CellRect(buffer.CursorX, buffer.CursorY, 1);
                var cell = GetCellAtCursor();
                EditorGUI.DrawRect(cursorRect, cell.Foreground);
                if (!string.IsNullOrEmpty(cell.Text) && cell.Text != " ")
                {
                    cellStyle.normal.textColor = cell.Background;
                    GUI.Label(cursorRect, cell.Text, cellStyle);
                }
            }

            GUI.EndClip();
        }

        private void DrawScrollBar(Rect terminalRect, Rect contentRect)
        {
            if (!NeedsScrollBar)
            {
                return;
            }

            var scrollRect = new Rect(contentRect.xMax, terminalRect.y, terminalRect.xMax - contentRect.xMax, terminalRect.height);
            var max = Mathf.Max(1, buffer.BaseY);
            var currentValue = Mathf.Clamp(buffer.ViewportY, 0, max);
            EditorGUI.BeginChangeCheck();
            scrollBarValue = GUI.VerticalScrollbar(scrollRect, currentValue, Mathf.Max(1, buffer.Rows), 0, max + buffer.Rows);
            if (EditorGUI.EndChangeCheck())
            {
                var target = Mathf.Clamp(Mathf.RoundToInt(scrollBarValue), 0, max);
                if (target != currentValue)
                {
                    client?.SendScrollTo(target);
                }
            }
        }

        private void HandleKeyboard(Rect terminalRect)
        {
            var current = Event.current;
            if (HandleDragAndDrop(current, terminalRect))
            {
                return;
            }

            if (current.type == EventType.MouseDown && terminalRect.Contains(current.mousePosition))
            {
                if (current.button == 1)
                {
                    ShowTerminalContextMenu();
                    current.Use();
                    return;
                }

                SetTerminalFocused(true);
                Focus();
                GUI.FocusControl(imeControlName);
                if (current.button == 0)
                {
                    selectionAnchor = MouseToCell(current.mousePosition, terminalRect);
                    selectionFocus = selectionAnchor;
                    selecting = true;
                }

                current.Use();
                return;
            }

            if (current.type == EventType.MouseDrag && selecting)
            {
                selectionFocus = MouseToCell(current.mousePosition, terminalRect);
                current.Use();
                Repaint();
                return;
            }

            if (current.type == EventType.MouseUp && selecting)
            {
                if (terminalRect.Contains(current.mousePosition))
                {
                    selectionFocus = MouseToCell(current.mousePosition, terminalRect);
                }

                selecting = false;
                if (!HasSelection())
                {
                    ClearSelection();
                }

                current.Use();
                Repaint();
                return;
            }

            if (current.type == EventType.ScrollWheel && terminalRect.Contains(current.mousePosition))
            {
                SetTerminalFocused(true);
                Focus();
                var lines = Mathf.Clamp(Mathf.RoundToInt(current.delta.y * 3f), -24, 24);
                if (lines != 0)
                {
                    var cell = MouseToCell(current.mousePosition, terminalRect);
                    client?.SendMouseWheel(cell.x, cell.y, lines, current.shift, current.control || current.command, current.alt);
                }

                current.Use();
                return;
            }

            if (current.type == EventType.MouseDown && !terminalRect.Contains(current.mousePosition))
            {
                SetTerminalFocused(false);
                return;
            }

            if (current.type == EventType.ValidateCommand && terminalFocused && current.commandName == "Copy")
            {
                if (HasSelection())
                {
                    current.Use();
                }

                return;
            }

            if (current.type == EventType.ExecuteCommand && terminalFocused && current.commandName == "Copy")
            {
                if (CopySelectionToClipboard())
                {
                    current.Use();
                }

                return;
            }

            if (current.type == EventType.ValidateCommand && terminalFocused && current.commandName == "Paste")
            {
                current.Use();
                return;
            }

            if (current.type == EventType.ExecuteCommand && terminalFocused && current.commandName == "Paste")
            {
                PasteFromClipboard();
                current.Use();
                return;
            }

            if (current.type != EventType.KeyDown || EditorWindow.focusedWindow != this || !terminalFocused)
            {
                return;
            }

            if (TrySendCommittedText(current, false))
            {
                current.Use();
                return;
            }

            if (!string.IsNullOrEmpty(Input.compositionString))
            {
                return;
            }

            if (TrySendCommittedText(current, true))
            {
                current.Use();
                return;
            }

            if (TryHandleKeyboardPaste(current))
            {
                current.Use();
                return;
            }

            if (TryHandleKeyboardCopy(current))
            {
                current.Use();
                return;
            }

            if (ShouldPassThroughToUnityInput(current))
            {
                return;
            }

            if (TrySendTerminalKey(current))
            {
                current.Use();
            }
        }

        private bool HandleDragAndDrop(Event current, Rect terminalRect)
        {
            if ((current.type != EventType.DragUpdated && current.type != EventType.DragPerform)
                || !terminalRect.Contains(current.mousePosition))
            {
                return false;
            }

            var text = BuildDragContextText();
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            SetTerminalFocused(true);
            Focus();
            GUI.FocusControl(imeControlName);

            if (current.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                if (!IsDuplicateDragPaste(text))
                {
                    PasteContext(text);
                }
            }

            current.Use();
            return true;
        }

        private static string BuildDragContextText()
        {
            var builder = new StringBuilder();
            var seen = new System.Collections.Generic.HashSet<string>();
            var paths = DragAndDrop.paths;
            if (paths != null)
            {
                foreach (var path in paths)
                {
                    AppendLine(builder, seen, InternalTerminalContextFormatter.FormatAssetPath(path));
                }
            }

            foreach (var draggedObject in DragAndDrop.objectReferences)
            {
                if (draggedObject == null)
                {
                    continue;
                }

                var assetPath = AssetDatabase.GetAssetPath(draggedObject);
                if (!string.IsNullOrEmpty(assetPath))
                {
                    AppendLine(builder, seen, InternalTerminalContextFormatter.FormatAssetPath(assetPath));
                    continue;
                }

                AppendLine(builder, seen, InternalTerminalContextFormatter.FormatObject(draggedObject));
            }

            return builder.ToString();
        }

        private bool IsDuplicateDragPaste(string text)
        {
            var now = EditorApplication.timeSinceStartup;
            if (text == lastDragPasteText && now - lastDragPasteTime < 0.5d)
            {
                lastDragPasteTime = now;
                return true;
            }

            lastDragPasteText = text;
            lastDragPasteTime = now;
            return false;
        }

        private static void AppendLine(StringBuilder builder, System.Collections.Generic.HashSet<string> seen, string value)
        {
            if (string.IsNullOrEmpty(value) || !seen.Add(value))
            {
                return;
            }

            builder.AppendLine(value);
        }

        private void StartTerminal()
        {
            try
            {
                if (!InternalTerminalSession.IsRunning)
                {
                    if (!InternalTerminalSession.TryReconnectKnownBackend())
                    {
                        buffer.Clear();
                        InternalTerminalSession.Start();
                    }
                }

                backendStatus = "Backend running";
                socketStatus = "WS connecting";
                client.Connect(InternalTerminalSession.Url);
                SetTerminalFocused(true);
                Focus();
            }
            catch (Exception exception)
            {
                backendStatus = "Error";
                lastLog = exception.Message;
                Debug.LogException(exception);
            }

            Repaint();
        }

        private void StopTerminal()
        {
            client?.Dispose();
            InternalTerminalSession.Stop();
            buffer.Clear();
            SetTerminalFocused(false);
            backendStatus = "Backend stopped";
            socketStatus = "WS stopped";
            Repaint();
        }

        private void RestartTerminal()
        {
            StopTerminal();
            StartTerminal();
        }

        private void EnsureTerminalConnected()
        {
            if (client == null)
            {
                return;
            }

            try
            {
                if (!InternalTerminalSession.IsRunning && !InternalTerminalSession.TryReconnectKnownBackend())
                {
                    StartTerminal();
                    return;
                }

                backendStatus = "Backend running";
                socketStatus = "WS connecting";
                client.Connect(InternalTerminalSession.Url);
            }
            catch (Exception exception)
            {
                backendStatus = "Error";
                lastLog = exception.Message;
                Debug.LogException(exception);
            }

            Repaint();
        }

        private void PasteContext(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            pendingContextPaste = text;
            StartOrReconnectForPaste();
            SetTerminalFocused(true);
            Focus();
            GUI.FocusControl(imeControlName);

            if (client != null && client.IsConnected)
            {
                client.SendPaste(pendingContextPaste);
                pendingContextPaste = null;
                return;
            }

            EditorGUIUtility.systemCopyBuffer = text;
            Debug.LogWarning("WTL Internal Terminal is not connected yet. Agent context was copied to the system clipboard.");
        }

        private void StartOrReconnectForPaste()
        {
            if (client == null)
            {
                return;
            }

            if (client.IsConnected)
            {
                return;
            }

            try
            {
                if (!InternalTerminalSession.IsRunning && !InternalTerminalSession.TryReconnectKnownBackend())
                {
                    if (!InternalTerminalSession.IsRunning)
                    {
                        buffer.Clear();
                        InternalTerminalSession.Start();
                    }
                }

                if (InternalTerminalSession.IsRunning)
                {
                    backendStatus = "Backend running";
                    socketStatus = "WS connecting";
                    client.Connect(InternalTerminalSession.Url);
                }
            }
            catch (Exception exception)
            {
                backendStatus = "Error";
                lastLog = exception.Message;
                Debug.LogException(exception);
            }
        }

        private void ShowTerminalContextMenu()
        {
            SetTerminalFocused(true);
            Focus();

            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Add to Agent"), false, () =>
            {
                InternalTerminalAgentContext.AddSelectionToAgent();
            });
            menu.AddItem(new GUIContent("Add Console Entry to Agent"), false, () =>
            {
                InternalTerminalAgentContext.AddConsoleEntryToAgent();
            });
            menu.AddItem(new GUIContent("Add All Console to Agent"), false, () =>
            {
                InternalTerminalAgentContext.AddConsoleToAgent();
            });
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Paste"), false, () =>
            {
                PasteFromClipboard();
            });
            menu.AddItem(new GUIContent("Copy"), false, () =>
            {
                CopySelectionToClipboard();
            });
            menu.ShowAsContext();
        }

        private void DrainClientData()
        {
            var changed = false;
            while (client != null && client.TryDequeueStatus(out var statusMessage))
            {
                socketStatus = statusMessage;
                changed = true;
            }

            while (client != null && client.TryDequeueScreen(out var screenJson))
            {
                buffer.ApplyScreenJson(screenJson);
                socketStatus = "WS active";
                changed = true;
            }

            if (!string.IsNullOrEmpty(pendingContextPaste) && client != null && client.IsConnected)
            {
                client.SendPaste(pendingContextPaste);
                pendingContextPaste = null;
                changed = true;
            }

            if (changed)
            {
                Repaint();
            }
            else if (terminalFocused && EditorWindow.focusedWindow == this && EditorApplication.timeSinceStartup >= nextCursorRepaint)
            {
                nextCursorRepaint = EditorApplication.timeSinceStartup + 0.25d;
                Repaint();
            }
        }

        private void EnsureStyle()
        {
            var settings = InternalTerminalSettings.instance;
            var resolvedFont = InternalTerminalFontResolver.Resolve(settings.TerminalFontName);
            if (cellStyle != null && loadedFontName == resolvedFont.DisplayName && loadedFontSize == settings.TerminalFontSize)
            {
                return;
            }

            loadedFontName = resolvedFont.DisplayName;
            loadedFontSource = resolvedFont.Source;
            loadedFontSize = settings.TerminalFontSize;
            terminalFont = resolvedFont.Regular;
            terminalBoldFont = resolvedFont.Bold != null ? resolvedFont.Bold : resolvedFont.Regular;

            if (terminalFont == null)
            {
                Debug.LogWarning("WTL Internal Terminal could not load the bundled terminal font. Falling back to Unity's default editor font. Run Window/WTL/Internal Terminal/Diagnose Font Loading for details.");
            }
            else
            {
                Debug.Log("WTL Internal Terminal loaded font: " + loadedFontName + " from " + loadedFontSource);
            }

            cellStyle = new GUIStyle(GUIStyle.none)
            {
                font = terminalFont,
                fontSize = loadedFontSize,
                fontStyle = FontStyle.Normal,
                alignment = TextAnchor.UpperLeft,
                clipping = TextClipping.Overflow,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0)
            };
            boldCellStyle = new GUIStyle(cellStyle)
            {
                font = terminalBoldFont,
                fontStyle = FontStyle.Bold
            };

            var measured = cellStyle.CalcSize(new GUIContent("00000000000000000000")).x / 20f;
            cellSize.x = Mathf.Clamp(measured, 6f, 14f);
            cellSize.y = Mathf.Clamp(cellStyle.lineHeight + 3f, 12f, 36f);
        }

        private void DrawImeBridge(Rect terminalRect)
        {
            if (!terminalFocused)
            {
                return;
            }

            EnableImeMode();

            var imeRect = new Rect(
                terminalRect.x + terminalPadding.x + Mathf.Clamp(buffer.CursorX, 0, Mathf.Max(0, buffer.Cols - 1)) * cellSize.x,
                terminalRect.y + terminalPadding.y + Mathf.Clamp(buffer.CursorY, 0, Mathf.Max(0, buffer.Rows - 1)) * cellSize.y,
                Mathf.Max(cellSize.x, 2f),
                Mathf.Max(cellSize.y, 2f));
            Input.compositionCursorPos = GUIUtility.GUIToScreenPoint(new Vector2(imeRect.x, imeRect.yMax));

            GUI.SetNextControlName(imeControlName);
            var previousColor = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.01f);
            var nextText = GUI.TextField(imeRect, imeText, GUIStyle.none);
            GUI.color = previousColor;

            if (GUI.GetNameOfFocusedControl() != imeControlName)
            {
                GUI.FocusControl(imeControlName);
            }

            if (nextText != imeText)
            {
                if (!string.IsNullOrEmpty(Input.compositionString))
                {
                    imeText = nextText;
                }
                else
                {
                    var committed = nextText;
                    imeText = string.Empty;
                    if (!string.IsNullOrEmpty(committed))
                    {
                        client?.SendText(committed);
                    }
                }
            }

            DrawCompositionPreview(imeRect);
        }

        private void InvalidateFontStyle()
        {
            cellStyle = null;
            boldCellStyle = null;
            terminalFont = null;
            terminalBoldFont = null;
            loadedFontName = null;
            loadedFontSource = null;
            loadedFontSize = 0;
        }

        private void DrawSelection(int cols, int rows)
        {
            if (!HasSelection())
            {
                return;
            }

            var start = selectionAnchor;
            var end = selectionFocus;
            NormalizeSelection(ref start, ref end);
            var selectionColor = new Color(0.2f, 0.45f, 0.8f, 0.55f);
            for (var y = Mathf.Max(0, start.y); y <= Mathf.Min(rows - 1, end.y); y++)
            {
                var startX = y == start.y ? start.x : 0;
                var endX = y == end.y ? end.x : cols - 1;
                if (endX < startX)
                {
                    continue;
                }

                EditorGUI.DrawRect(CellRect(startX, y, endX - startX + 1), selectionColor);
            }
        }

        private void DrawBackgroundRuns(int cols, int rows)
        {
            for (var y = 0; y < buffer.Lines.Count && y < rows; y++)
            {
                var line = buffer.Lines[y];
                var x = 0;
                while (x < line.Length && x < cols)
                {
                    var cell = line[x];
                    if (cell.Background == TerminalPalette.Background)
                    {
                        x++;
                        continue;
                    }

                    var runStart = x;
                    var background = cell.Background;
                    while (x < line.Length && x < cols && line[x].Background == background)
                    {
                        x++;
                    }

                    EditorGUI.DrawRect(CellRect(runStart, y, x - runStart), background);
                }
            }
        }

        private void DrawTextRuns(int cols, int rows)
        {
            for (var y = 0; y < buffer.Lines.Count && y < rows; y++)
            {
                var line = buffer.Lines[y];
                var x = 0;
                while (x < line.Length && x < cols)
                {
                    var cell = line[x];
                    if (!CanDrawAsText(cell))
                    {
                        x++;
                        continue;
                    }

                    var runStyle = cell.Bold ? boldCellStyle : cellStyle;
                    var foreground = cell.Foreground;
                    var bold = cell.Bold;
                    var underline = cell.Underline;
                    while (x < line.Length && x < cols)
                    {
                        cell = line[x];
                        if (!CanDrawAsText(cell)
                            || cell.Bold != bold
                            || cell.Underline != underline
                            || cell.Foreground != foreground)
                        {
                            break;
                        }

                        runStyle.normal.textColor = foreground;
                        var cellWidth = Mathf.Max(1, cell.Width);
                        var textRect = CellRect(x, y, cellWidth);
                        textRect.width += 1f;
                        GUI.Label(textRect, cell.Text, runStyle);
                        x++;

                        if (underline)
                        {
                            EditorGUI.DrawRect(new Rect(textRect.x, textRect.yMax - 2f, cellWidth * cellSize.x, 1f), foreground);
                        }
                    }
                }
            }
        }

        private static bool CanDrawAsText(TerminalScreenBuffer.Cell cell)
        {
            return cell.Width != 0
                && !cell.Invisible
                && !string.IsNullOrEmpty(cell.Text)
                && cell.Text != " ";
        }

        private Rect CellRect(int x, int y, int widthInCells)
        {
            return new Rect(
                terminalPadding.x + x * cellSize.x,
                terminalPadding.y + y * cellSize.y,
                widthInCells * cellSize.x,
                cellSize.y);
        }

        private bool NeedsScrollBar => !buffer.Alternate && buffer.BaseY > 0;

        private void OnLogReceived(string message)
        {
            lastLog = string.IsNullOrEmpty(lastLog) ? message : lastLog + "\n" + message;
            backendStatus = message;
            Repaint();
        }

        private void OnBackendExited()
        {
            backendStatus = "Backend exited";
            Repaint();
        }

        private TerminalScreenBuffer.Cell GetCellAtCursor()
        {
            if (buffer.CursorY >= 0 && buffer.CursorY < buffer.Lines.Count)
            {
                var line = buffer.Lines[buffer.CursorY];
                if (buffer.CursorX >= 0 && buffer.CursorX < line.Length)
                {
                    return line[buffer.CursorX];
                }
            }

            return new TerminalScreenBuffer.Cell
            {
                Text = " ",
                Foreground = TerminalPalette.Foreground,
                Background = TerminalPalette.Background,
                Width = 1
            };
        }

        private static string KeyName(KeyCode keyCode)
        {
            return keyCode == KeyCode.None ? string.Empty : keyCode.ToString();
        }

        private bool TryHandleKeyboardPaste(Event current)
        {
            if ((current.control || current.command) && current.keyCode == KeyCode.V)
            {
                PasteFromClipboard();
                return true;
            }

            if (current.shift && current.keyCode == KeyCode.Insert)
            {
                PasteFromClipboard();
                return true;
            }

            return false;
        }

        private void PasteFromClipboard()
        {
            if (InternalTerminalClipboardImage.TrySavePng(out var imagePath))
            {
                PasteContext(InternalTerminalClipboardImage.FormatPasteText(imagePath));
                return;
            }

            PasteContext(EditorGUIUtility.systemCopyBuffer);
        }

        private bool TryHandleKeyboardCopy(Event current)
        {
            if (current.shift && (current.control || current.command) && current.keyCode == KeyCode.C)
            {
                return CopySelectionToClipboard();
            }

            return false;
        }

        private bool CopySelectionToClipboard()
        {
            if (!HasSelection())
            {
                return false;
            }

            var start = selectionAnchor;
            var end = selectionFocus;
            NormalizeSelection(ref start, ref end);
            var builder = new StringBuilder();
            for (var y = start.y; y <= end.y && y < buffer.Lines.Count; y++)
            {
                if (y > start.y)
                {
                    builder.AppendLine();
                }

                var line = buffer.Lines[y];
                var startX = Mathf.Clamp(y == start.y ? start.x : 0, 0, Mathf.Max(0, line.Length - 1));
                var endX = Mathf.Clamp(y == end.y ? end.x : line.Length - 1, 0, Mathf.Max(0, line.Length - 1));
                var lineBuilder = new StringBuilder();
                for (var x = startX; x <= endX; x++)
                {
                    var cell = line[x];
                    if (cell.Width == 0)
                    {
                        continue;
                    }

                    lineBuilder.Append(string.IsNullOrEmpty(cell.Text) ? " " : cell.Text);
                }

                builder.Append(lineBuilder.ToString().TrimEnd());
            }

            EditorGUIUtility.systemCopyBuffer = builder.ToString();
            return true;
        }

        private Vector2Int MouseToCell(Vector2 mousePosition, Rect terminalRect)
        {
            var x = Mathf.Clamp(Mathf.FloorToInt((mousePosition.x - terminalRect.x - terminalPadding.x) / cellSize.x), 0, Mathf.Max(0, buffer.Cols - 1));
            var y = Mathf.Clamp(Mathf.FloorToInt((mousePosition.y - terminalRect.y - terminalPadding.y) / cellSize.y), 0, Mathf.Max(0, buffer.Rows - 1));
            return new Vector2Int(x, y);
        }

        private bool HasSelection()
        {
            return selectionAnchor.x >= 0
                && selectionFocus.x >= 0
                && (selectionAnchor.x != selectionFocus.x || selectionAnchor.y != selectionFocus.y);
        }

        private void ClearSelection()
        {
            selectionAnchor = new Vector2Int(-1, -1);
            selectionFocus = new Vector2Int(-1, -1);
        }

        private static void NormalizeSelection(ref Vector2Int start, ref Vector2Int end)
        {
            if (start.y > end.y || (start.y == end.y && start.x > end.x))
            {
                var temp = start;
                start = end;
                end = temp;
            }
        }

        private static bool ShouldPassThroughToUnityInput(Event current)
        {
            if (current.keyCode == KeyCode.None && current.character == '\0')
            {
                return true;
            }

            if (IsModifierOnly(current.keyCode))
            {
                return true;
            }

            if (IsInputMethodShortcut(current))
            {
                return true;
            }

            return false;
        }

        private bool TrySendCommittedText(Event current, bool allowAscii)
        {
            if (current.type != EventType.KeyDown
                || current.control
                || current.command
                || current.alt
                || current.character == '\0'
                || char.IsControl(current.character))
            {
                return false;
            }

            if (!allowAscii && current.character <= 0x7f)
            {
                return false;
            }

            client?.SendText(current.character.ToString());
            imeText = string.Empty;
            return true;
        }

        private void DrawCompositionPreview(Rect imeRect)
        {
            var composition = Input.compositionString;
            if (string.IsNullOrEmpty(composition))
            {
                return;
            }

            cellStyle.normal.textColor = TerminalPalette.Foreground;
            GUI.Label(new Rect(imeRect.x, imeRect.y, Mathf.Max(cellSize.x * composition.Length, cellSize.x), cellSize.y), composition, cellStyle);
        }

        private void SetTerminalFocused(bool focused)
        {
            if (terminalFocused == focused)
            {
                return;
            }

            terminalFocused = focused;
            imeText = string.Empty;
            if (focused)
            {
                EnableImeMode();
            }
            else
            {
                RestoreImeMode();
            }
        }

        private void EnableImeMode()
        {
            if (!imeModeCaptured)
            {
                previousImeMode = Input.imeCompositionMode;
                imeModeCaptured = true;
            }

            Input.imeCompositionMode = IMECompositionMode.On;
        }

        private void RestoreImeMode()
        {
            if (!imeModeCaptured)
            {
                return;
            }

            Input.imeCompositionMode = previousImeMode;
            imeModeCaptured = false;
        }

        private bool TrySendTerminalKey(Event current)
        {
            var keyName = KeyName(current.keyCode);
            var text = current.character == '\0' ? string.Empty : current.character.ToString();
            if (string.IsNullOrEmpty(keyName) && string.IsNullOrEmpty(text))
            {
                return false;
            }

            client?.SendKey(keyName, text, current.shift, current.control || current.command, current.alt);
            return true;
        }

        private static bool IsModifierOnly(KeyCode keyCode)
        {
            return keyCode == KeyCode.LeftShift
                || keyCode == KeyCode.RightShift
                || keyCode == KeyCode.LeftControl
                || keyCode == KeyCode.RightControl
                || keyCode == KeyCode.LeftAlt
                || keyCode == KeyCode.RightAlt
                || keyCode == KeyCode.LeftCommand
                || keyCode == KeyCode.RightCommand
                || keyCode == KeyCode.LeftWindows
                || keyCode == KeyCode.RightWindows
                || keyCode == KeyCode.CapsLock;
        }

        private static bool IsInputMethodShortcut(Event current)
        {
            if (current.alt && current.shift)
            {
                return true;
            }

            if (current.control && current.keyCode == KeyCode.Space)
            {
                return true;
            }

            if (current.control && current.shift && current.character == '\0')
            {
                return true;
            }

            if (current.keyCode == KeyCode.Space && (current.control || current.command))
            {
                return true;
            }

            return false;
        }
    }
}
