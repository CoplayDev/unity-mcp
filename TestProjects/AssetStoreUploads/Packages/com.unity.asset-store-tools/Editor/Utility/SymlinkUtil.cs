using System.IO;

namespace AssetStoreTools.Utility
{
    internal static class SymlinkUtil
    {
        private const FileAttributes FolderSymlinkAttributes = FileAttributes.Directory | FileAttributes.ReparsePoint;

        public static bool FindSymlinkFolderRelative(string folderPathAbsolute, out string relativePath)
        {
            // Get directory info for path outside of the project
            var absoluteInfo = new DirectoryInfo(folderPathAbsolute);

            // Get all directories within the project
            var allFolderPaths = Directory.GetDirectories("Assets", "*", SearchOption.AllDirectories);
            foreach (var path in allFolderPaths)
            {
                var fullPath = path.Replace("\\", "/");

                // Get directory info for one of the paths within the project
                var relativeInfo = new DirectoryInfo(fullPath);

                // Check if project's directory is a symlink
                if (!relativeInfo.Attributes.HasFlag(FolderSymlinkAttributes))
                    continue;

                // Compare metadata of outside directory with a directories within the project
                if (!CompareDirectories(absoluteInfo, relativeInfo))
                    continue;

                // Found symlink within the project, assign it
                relativePath = fullPath;
                return true;
            }

            relativePath = string.Empty;
            return false;
        }

        private static bool CompareDirectories(DirectoryInfo directory, DirectoryInfo directory2)
        {
            var contents = directory.EnumerateFileSystemInfos("*", SearchOption.AllDirectories).GetEnumerator();
            var contents2 = directory2.EnumerateFileSystemInfos("*", SearchOption.AllDirectories).GetEnumerator();

            while (true)
            {
                var firstNext = contents.MoveNext();
                var secondNext = contents2.MoveNext();

                if (firstNext != secondNext)
                    return false;

                if (!firstNext && !secondNext)
                    break;

                var equals = contents.Current?.Name == contents2.Current?.Name
                             && contents.Current?.LastWriteTime == contents2.Current?.LastWriteTime;

                if (!equals)
                    return false;
            }

            return true;
        }

    }
}