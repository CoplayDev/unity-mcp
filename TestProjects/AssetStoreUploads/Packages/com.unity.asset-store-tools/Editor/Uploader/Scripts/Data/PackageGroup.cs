using System;
using System.Collections.Generic;
using System.Linq;

namespace AssetStoreTools.Uploader.Data
{
    internal class PackageGroup : IPackageGroup
    {
        private class FilteredPackage
        {
            public IPackage Package;
            public bool IsInFilter;
        }

        public string Name { get; private set; }
        public List<IPackage> Packages { get; private set; }

        private List<FilteredPackage> _filteredPackages;

        public event Action<List<IPackage>> OnPackagesSorted;
        public event Action<List<IPackage>> OnPackagesFiltered;

        public PackageGroup(string name, List<IPackage> packages)
        {
            Name = name;
            Packages = packages;

            _filteredPackages = new List<FilteredPackage>();
            foreach (var package in Packages)
                _filteredPackages.Add(new FilteredPackage() { Package = package, IsInFilter = true });
        }

        public void Sort(PackageSorting sortingType)
        {
            switch (sortingType)
            {
                case PackageSorting.Name:
                    _filteredPackages = _filteredPackages.OrderBy(x => x.Package.Name).ToList();
                    break;
                case PackageSorting.Date:
                    _filteredPackages = _filteredPackages.OrderByDescending(x => x.Package.Modified).ToList();
                    break;
                case PackageSorting.Category:
                    _filteredPackages = _filteredPackages.OrderBy(x => x.Package.Category).ThenBy(x => x.Package.Name).ToList();
                    break;
                default:
                    throw new NotImplementedException("Undefined sorting type");
            }

            OnPackagesSorted?.Invoke(_filteredPackages.Where(x => x.IsInFilter).Select(x => x.Package).ToList());
        }

        public void Filter(string filter)
        {
            foreach (var package in _filteredPackages)
            {
                bool inFilter = package.Package.Name.ToLower().Contains(filter.ToLower());
                package.IsInFilter = inFilter;
            }

            OnPackagesFiltered?.Invoke(_filteredPackages.Where(x => x.IsInFilter).Select(x => x.Package).ToList());
        }
    }
}