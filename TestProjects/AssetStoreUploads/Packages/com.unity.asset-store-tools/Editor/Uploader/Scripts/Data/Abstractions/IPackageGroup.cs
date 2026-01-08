using System;
using System.Collections.Generic;

namespace AssetStoreTools.Uploader.Data
{
    internal interface IPackageGroup
    {
        string Name { get; }
        List<IPackage> Packages { get; }

        event Action<List<IPackage>> OnPackagesSorted;
        event Action<List<IPackage>> OnPackagesFiltered;

        void Sort(PackageSorting sortingType);
        void Filter(string filter);
    }
}