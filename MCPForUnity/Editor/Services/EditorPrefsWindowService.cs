using System;
using MCPForUnity.Editor.Windows;
using UnityEditor;

namespace MCPForUnity.Editor.Services
{
    /// <summary>
    /// Service for managing the EditorPrefs window
    /// Follows the Class-level Singleton pattern
    /// </summary>
    public class EditorPrefsWindowService
    {
        private static EditorPrefsWindowService _instance;
        
        /// <summary>
        /// Get the singleton instance
        /// </summary>
        public static EditorPrefsWindowService Instance
        {
            get
            {
                if (_instance == null)
                {
                    throw new Exception("EditorPrefsWindowService not initialized");
                }
                return _instance;
            }
        }
        
        /// <summary>
        /// Initialize the service
        /// </summary>
        public static void Initialize()
        {
            if (_instance == null)
            {
                _instance = new EditorPrefsWindowService();
            }
        }
        
        private EditorPrefsWindowService()
        {
            // Private constructor for singleton
        }
        
        /// <summary>
        /// Show the EditorPrefs window
        /// </summary>
        public void ShowWindow()
        {
            EditorPrefsWindow.ShowWindow();
        }
    }
}
