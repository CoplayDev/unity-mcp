using System.Collections.Generic;
using System.Linq;

namespace AssetStoreTools.Validator.Data
{
    internal class CurrentProjectValidationSettings : ValidationSettings
    {
        public List<string> ValidationPaths;
        public ValidationType ValidationType;

        public CurrentProjectValidationSettings()
        {
            Category = string.Empty;
            ValidationPaths = new List<string>();
        }

        public override int GetHashCode()
        {
            return base.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if (obj == null || obj.GetType() != typeof(CurrentProjectValidationSettings))
                return false;

            var other = (CurrentProjectValidationSettings)obj;
            return Category == other.Category
                && ValidationType == other.ValidationType
                && ValidationPaths.OrderBy(x => x).SequenceEqual(other.ValidationPaths.OrderBy(x => x));
        }
    }
}