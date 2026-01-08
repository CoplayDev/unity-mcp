using AssetStoreTools.Validator.UI.Data;
using System.Linq;
using UnityEngine.UIElements;

namespace AssetStoreTools.Validator.UI.Elements
{
    internal class ValidatorResultsElement : ScrollView
    {
        private IValidatorResults _results;

        public ValidatorResultsElement(IValidatorResults results)
        {
            AddToClassList("validator-test-list");

            _results = results;
            _results.OnResultsChanged += ResultsChanged;

            Create();
        }

        private void Create()
        {
            var groups = _results.GetSortedTestGroups().ToList();
            for (int i = 0; i < groups.Count; i++)
            {
                var groupElement = new ValidatorTestGroupElement(groups[i]);
                Add(groupElement);
                if (i != groups.Count - 1)
                    Add(CreateSeparator());
            }
        }

        private void ResultsChanged()
        {
            Clear();
            Create();
        }

        private VisualElement CreateSeparator()
        {
            var groupSeparator = new VisualElement { name = "GroupSeparator" };
            groupSeparator.AddToClassList("validator-test-list-group-separator");

            return groupSeparator;
        }
    }
}