using System;

namespace MCPForUnity.Editor.Services
{
    public interface IPackageDeploymentService
    {
        string GetStoredSourcePath();
        void SetStoredSourcePath(string path);
        void ClearStoredSourcePath();

        string GetTargetPath();
        string GetTargetDisplayPath();

        string GetLastBackupPath();
        bool HasBackup();

        PackageDeploymentResult DeployFromStoredSource();
        PackageDeploymentResult RestoreLastBackup();
    }

    public class PackageDeploymentResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public string SourcePath { get; set; }
        public string TargetPath { get; set; }
        public string BackupPath { get; set; }
    }
}
