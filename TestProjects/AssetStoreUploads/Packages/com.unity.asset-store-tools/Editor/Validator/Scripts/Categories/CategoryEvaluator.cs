using AssetStoreTools.Validator.Data;
using AssetStoreTools.Validator.TestDefinitions;

namespace AssetStoreTools.Validator.Categories
{
    internal class CategoryEvaluator
    {
        private string _category;

        public CategoryEvaluator(string category)
        {
            if (string.IsNullOrEmpty(category))
                _category = string.Empty;
            else
                _category = category;
        }

        public void SetCategory(string category)
        {
            if (category == null)
                _category = string.Empty;
            else
                _category = category;
        }

        public string GetCategory()
        {
            return _category;
        }

        public TestResultStatus Evaluate(ValidationTest validation, bool slugify = false)
        {
            var result = validation.Result.Status;
            if (result != TestResultStatus.VariableSeverityIssue)
                return result;

            var category = _category;

            if (slugify)
                category = validation.Slugify(category);

            return validation.CategoryInfo.EvaluateByFilter(category);
        }

#if AB_BUILDER
        public TestResultStatus EvaluateAndSlugify(ValidationTest validation)
        {
            return Evaluate(validation, true);
        }
#endif
    }
}