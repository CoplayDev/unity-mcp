using System;
using UnityEngine;
using PackageModel = AssetStoreTools.Api.Models.Package;

namespace AssetStoreTools.Uploader.Data
{
    internal class Package : IPackage
    {
        private PackageModel _source;

        public string PackageId => _source.PackageId;
        public string VersionId => _source.VersionId;
        public string Name => _source.Name;
        public string Status => _source.Status;
        public string Category => _source.Category;
        public bool IsCompleteProject => _source.IsCompleteProject;
        public string RootGuid => _source.RootGuid;
        public string RootPath => _source.RootPath;
        public string ProjectPath => _source.ProjectPath;
        public string Modified => _source.Modified;
        public string Size => _source.Size;
        public string IconUrl => _source.IconUrl;
        public bool IsDraft => Status.Equals("draft", StringComparison.OrdinalIgnoreCase);
        public Texture2D Icon { get; private set; }

        public event Action OnUpdate;
        public event Action OnIconUpdate;

        public Package(PackageModel packageSource)
        {
            _source = packageSource;
        }

        public void UpdateIcon(Texture2D texture)
        {
            if (texture == null)
                return;

            Icon = texture;
            OnIconUpdate?.Invoke();
        }

        public string FormattedSize()
        {
            var defaultSize = "0.00 MB";
            if (float.TryParse(Size, out var sizeBytes))
                return $"{sizeBytes / (1024f * 1024f):0.00} MB";

            return defaultSize;
        }

        public string FormattedModified()
        {
            var defaultDate = "Unknown";
            if (DateTime.TryParse(Modified, out var dt))
                return dt.Date.ToString("yyyy-MM-dd");

            return defaultDate;
        }

        public void UpdateData(PackageModel source)
        {
            if (source == null)
                throw new ArgumentException("Provided package is null");

            _source = source;
            OnUpdate?.Invoke();
        }

        public PackageModel ToModel()
        {
            var model = new PackageModel()
            {
                PackageId = _source.PackageId,
                VersionId = _source.VersionId,
                Name = _source.Name,
                Status = _source.Status,
                Category = _source.Category,
                IsCompleteProject = _source.IsCompleteProject,
                RootGuid = _source.RootGuid,
                RootPath = _source.RootPath,
                ProjectPath = _source.ProjectPath,
                Modified = _source.Modified,
                Size = _source.Size,
                IconUrl = _source.IconUrl
            };

            return model;
        }
    }
}