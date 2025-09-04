using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;
using MCPForUnity.Editor.Models;

namespace MCPForUnity.Editor.Windows
{
    public class VSCodeManualSetupWindow : ManualConfigEditorWindow
    {
        public static void ShowWindow(string configPath, string configJson)
        {
            var window = GetWindow<VSCodeManualSetupWindow>("VSCode GitHub Copilot Setup");
            window.configPath = configPath;
            window.configJson = configJson;
            window.minSize = new Vector2(550, 500);
            
            // Create a McpClient for VSCode
            window.mcpClient = new McpClient
            {
                name = "VSCode GitHub Copilot",
                mcpType = McpTypes.VSCode
            };
            
            window.Show();
        }

        protected override void OnGUI()
        {
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

            // Header with improved styling
            EditorGUILayout.Space(10);
            Rect titleRect = EditorGUILayout.GetControlRect(false, 30);
            EditorGUI.DrawRect(
                new Rect(titleRect.x, titleRect.y, titleRect.width, titleRect.height),
                new Color(0.2f, 0.2f, 0.2f, 0.1f)
            );
            GUI.Label(
                new Rect(titleRect.x + 10, titleRect.y + 6, titleRect.width - 20, titleRect.height),
                "VSCode GitHub Copilot MCP Setup",
                EditorStyles.boldLabel
            );
            EditorGUILayout.Space(10);

            // Instructions with improved styling
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            Rect headerRect = EditorGUILayout.GetControlRect(false, 24);
            EditorGUI.DrawRect(
                new Rect(headerRect.x, headerRect.y, headerRect.width, headerRect.height),
                new Color(0.1f, 0.1f, 0.1f, 0.2f)
            );
            GUI.Label(
                new Rect(
                    headerRect.x + 8,
                    headerRect.y + 4,
                    headerRect.width - 16,
                    headerRect.height
                ),
                "Setting up GitHub Copilot in VSCode with MCP for Unity",
                EditorStyles.boldLabel
            );
            EditorGUILayout.Space(10);

            GUIStyle instructionStyle = new(EditorStyles.wordWrappedLabel)
            {
                margin = new RectOffset(10, 10, 5, 5),
            };

            EditorGUILayout.LabelField(
                "1. Prerequisites",
                EditorStyles.boldLabel
            );
            EditorGUILayout.LabelField(
                "• Ensure you have VSCode installed",
                instructionStyle
            );
            EditorGUILayout.LabelField(
                "• Ensure you have GitHub Copilot extension installed in VSCode",
                instructionStyle
            );
            EditorGUILayout.LabelField(
                "• Ensure you have a valid GitHub Copilot subscription",
                instructionStyle
            );
            EditorGUILayout.Space(5);
            
            EditorGUILayout.LabelField(
                "2. Steps to Configure",
                EditorStyles.boldLabel
            );
            EditorGUILayout.LabelField(
                "a) Open or create your VSCode MCP config file (mcp.json) at the path below",
                instructionStyle
            );
            EditorGUILayout.LabelField(
                "b) Paste the JSON shown below into mcp.json",
                instructionStyle
            );
            EditorGUILayout.LabelField(
                "c) Save the file and restart VSCode",
                instructionStyle
            );
            EditorGUILayout.Space(5);
            
            EditorGUILayout.LabelField(
                "3. VSCode mcp.json location:",
                EditorStyles.boldLabel
            );

            // Path section with improved styling
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            string displayPath;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                displayPath = System.IO.Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
                    "Code",
                    "User",
                    "mcp.json"
                );
            }
            else 
            {
                displayPath = System.IO.Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile),
                    "Library",
                    "Application Support",
                    "Code",
                    "User",
                    "mcp.json"
                );
            }

            // Store the path in the base class config path
            if (string.IsNullOrEmpty(configPath))
            {
                configPath = displayPath;
            }

            // Prevent text overflow by allowing the text field to wrap
            GUIStyle pathStyle = new(EditorStyles.textField) { wordWrap = true };

            EditorGUILayout.TextField(
                displayPath,
                pathStyle,
                GUILayout.Height(EditorGUIUtility.singleLineHeight)
            );

            // Copy button with improved styling
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUIStyle copyButtonStyle = new(GUI.skin.button)
            {
                padding = new RectOffset(15, 15, 5, 5),
                margin = new RectOffset(10, 10, 5, 5),
            };

            if (
                GUILayout.Button(
                    "Copy Path",
                    copyButtonStyle,
                    GUILayout.Height(25),
                    GUILayout.Width(100)
                )
            )
            {
                EditorGUIUtility.systemCopyBuffer = displayPath;
                pathCopied = true;
                copyFeedbackTimer = 2f;
            }

            if (
                GUILayout.Button(
                    "Open File",
                    copyButtonStyle,
                    GUILayout.Height(25),
                    GUILayout.Width(100)
                )
            )
            {
                // Open the file using the system's default application
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = displayPath,
                        UseShellExecute = true,
                    }
                );
            }

            if (pathCopied)
            {
                GUIStyle feedbackStyle = new(EditorStyles.label);
                feedbackStyle.normal.textColor = Color.green;
                EditorGUILayout.LabelField("Copied!", feedbackStyle, GUILayout.Width(60));
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(10);

            EditorGUILayout.LabelField(
                "4. Add this configuration to your mcp.json:",
                EditorStyles.boldLabel
            );

            // JSON section with improved styling
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // Improved text area for JSON with syntax highlighting colors
            GUIStyle jsonStyle = new(EditorStyles.textArea)
            {
                font = EditorStyles.boldFont,
                wordWrap = true,
            };
            jsonStyle.normal.textColor = new Color(0.3f, 0.6f, 0.9f); // Syntax highlighting blue

            // Draw the JSON in a text area with a taller height for better readability
            EditorGUILayout.TextArea(configJson, jsonStyle, GUILayout.Height(200));

            // Copy JSON button with improved styling
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            if (
                GUILayout.Button(
                    "Copy JSON",
                    copyButtonStyle,
                    GUILayout.Height(25),
                    GUILayout.Width(100)
                )
            )
            {
                EditorGUIUtility.systemCopyBuffer = configJson;
                jsonCopied = true;
                copyFeedbackTimer = 2f;
            }

            if (jsonCopied)
            {
                GUIStyle feedbackStyle = new(EditorStyles.label);
                feedbackStyle.normal.textColor = Color.green;
                EditorGUILayout.LabelField("Copied!", feedbackStyle, GUILayout.Width(60));
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField(
                "5. After configuration:",
                EditorStyles.boldLabel
            );
            EditorGUILayout.LabelField(
                "• Restart VSCode",
                instructionStyle
            );
            EditorGUILayout.LabelField(
                "• GitHub Copilot will now be able to interact with your Unity project through the MCP protocol",
                instructionStyle
            );
            EditorGUILayout.LabelField(
                "• Remember to have the MCP for Unity Bridge running in Unity Editor",
                instructionStyle
            );

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);

            // Close button at the bottom
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Close", GUILayout.Height(30), GUILayout.Width(100)))
            {
                Close();
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndScrollView();
        }

        protected override void Update()
        {
            // Call the base implementation which handles the copy feedback timer
            base.Update();
        }
    }
}
