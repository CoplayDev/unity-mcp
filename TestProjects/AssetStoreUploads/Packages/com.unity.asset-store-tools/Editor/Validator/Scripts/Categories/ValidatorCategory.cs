using AssetStoreTools.Validator.Data;
using System;
using System.Linq;

namespace AssetStoreTools.Validator.Categories
{
    [System.Serializable]
    internal class ValidatorCategory
    {
        public bool IsFailFilter = false;
        public bool IsInclusiveFilter = true;
        public bool AppliesToSubCategories = true;
        public string[] Filter = { "Tools", "Art" };

        public TestResultStatus EvaluateByFilter(string category)
        {
            if (AppliesToSubCategories)
                category = category.Split('/')[0];

            var isCategoryInFilter = Filter.Any(x => String.Compare(x, category, StringComparison.OrdinalIgnoreCase) == 0);

            if (IsInclusiveFilter)
            {
                if (isCategoryInFilter)
                    return IsFailFilter ? TestResultStatus.Fail : TestResultStatus.Warning;
                else
                    return IsFailFilter ? TestResultStatus.Warning : TestResultStatus.Fail;
            }
            else
            {
                if (isCategoryInFilter)
                    return IsFailFilter ? TestResultStatus.Warning : TestResultStatus.Fail;
                else
                    return IsFailFilter ? TestResultStatus.Fail : TestResultStatus.Warning;
            }
        }
    }
}