using AssetStoreTools.Uploader.Data;
using System.Collections.Generic;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace AssetStoreTools.Uploader.UI.Elements
{
    internal class PackageListToolbar : VisualElement
    {
        private List<IPackageGroup> _packageGroups;

        public PackageListToolbar()
        {
            Create();
        }

        private void Create()
        {
            AddToClassList("package-list-toolbar");

            // Search
            var searchField = new ToolbarSearchField { name = "SearchField" };
            searchField.AddToClassList("package-search-field");

            // Sorting menu button
            var sortMenu = new ToolbarMenu() { text = "Sort: Name ↓" };
            sortMenu.menu.AppendAction("Sort: Name ↓", (_) => { sortMenu.text = "Sort: Name ↓"; Sort(PackageSorting.Name); });
            sortMenu.menu.AppendAction("Sort: Updated ↓", (_) => { sortMenu.text = "Sort: Updated ↓"; Sort(PackageSorting.Date); });
            sortMenu.menu.AppendAction("Sort: Category ↓", (_) => { sortMenu.text = "Sort: Category ↓"; Sort(PackageSorting.Category); });
            sortMenu.AddToClassList("package-sort-menu");

            // Finalize the bar
            Add(searchField);
            Add(sortMenu);

            // Add Callbacks and click events
            searchField.RegisterCallback<ChangeEvent<string>>(SearchFilter);
        }

        public void SetPackageGroups(List<IPackageGroup> packageGroups)
        {
            _packageGroups = packageGroups;
        }

        private void SearchFilter(ChangeEvent<string> evt)
        {
            var searchString = evt.newValue.ToLower();
            foreach (var packageGroup in _packageGroups)
                packageGroup.Filter(searchString);
        }

        public void Sort(PackageSorting sortingType)
        {
            foreach (var packageGroup in _packageGroups)
                packageGroup.Sort(sortingType);
        }
    }
}
