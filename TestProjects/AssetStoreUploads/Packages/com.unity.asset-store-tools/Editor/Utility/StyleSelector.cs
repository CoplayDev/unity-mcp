using System;
using UnityEditor;
using UnityEngine.UIElements;
using WindowStyles = AssetStoreTools.Constants.WindowStyles;

namespace AssetStoreTools.Utility
{
    internal static class StyleSelector
    {
        private static StyleSheet GetStylesheet(string rootPath, string filePath)
        {
            var path = $"{rootPath}/{filePath}.uss";
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
            if (sheet == null)
                throw new Exception($"Stylesheet '{path}' was not found");
            return sheet;
        }

        private static StyleSheet GetStylesheetTheme(string rootPath, string filePath)
        {
            var suffix = !EditorGUIUtility.isProSkin ? "Light" : "Dark";
            return GetStylesheet(rootPath, filePath + suffix);
        }

        public static class UploaderWindow
        {
            public static StyleSheet UploaderWindowStyle => GetStylesheet(WindowStyles.UploaderStylesPath, "Style");
            public static StyleSheet UploaderWindowTheme => GetStylesheetTheme(WindowStyles.UploaderStylesPath, "Theme");

            public static StyleSheet LoginViewStyle => GetStylesheet(WindowStyles.UploaderStylesPath, "LoginView/Style");
            public static StyleSheet LoginViewTheme => GetStylesheetTheme(WindowStyles.UploaderStylesPath, "LoginView/Theme");

            public static StyleSheet PackageListViewStyle => GetStylesheet(WindowStyles.UploaderStylesPath, "PackageListView/Style");
            public static StyleSheet PackageListViewTheme => GetStylesheetTheme(WindowStyles.UploaderStylesPath, "PackageListView/Theme");
        }

        public static class ValidatorWindow
        {
            public static StyleSheet ValidatorWindowStyle => GetStylesheet(WindowStyles.ValidatorStylesPath, "Style");
            public static StyleSheet ValidatorWindowTheme => GetStylesheetTheme(WindowStyles.ValidatorStylesPath, "Theme");
        }

        public static class PreviewGeneratorWindow
        {
            public static StyleSheet PreviewGeneratorWindowStyle => GetStylesheet(WindowStyles.PreviewGeneratorStylesPath, "Style");
            public static StyleSheet PreviewGeneratorWindowTheme => GetStylesheetTheme(WindowStyles.PreviewGeneratorStylesPath, "Theme");
        }

        public static class UpdaterWindow
        {
            public static StyleSheet UpdaterWindowStyle => GetStylesheet(WindowStyles.UpdaterStylesPath, "Style");
            public static StyleSheet UpdaterWindowTheme => GetStylesheetTheme(WindowStyles.UpdaterStylesPath, "Theme");
        }
    }
}