using AssetStoreTools.Uploader.Data;
using AssetStoreTools.Validator.Data;
using System.Linq;
using UnityEditor;
using UnityEngine.UIElements;

namespace AssetStoreTools.Uploader.UI.Elements
{
    internal abstract class ValidationElementBase : VisualElement
    {
        // Data
        protected IWorkflow Workflow;

        // UI
        protected VisualElement ResultsBox;
        protected Image ResultsBoxImage;
        protected Label ResultsBoxLabel;

        protected ValidationElementBase(IWorkflow workflow)
        {
            Workflow = workflow;
            Create();
        }

        private void Create()
        {
            CreateInfoRow();
            CreateResultsBox();
        }

        private void CreateInfoRow()
        {
            VisualElement validatorButtonRow = new VisualElement();
            validatorButtonRow.AddToClassList("package-content-option-box");

            VisualElement validatorLabelHelpRow = new VisualElement();
            validatorLabelHelpRow.AddToClassList("package-content-option-label-help-row");

            Label validatorLabel = new Label { text = "Validation" };
            Image validatorLabelTooltip = new Image
            {
                tooltip = "You can use the Asset Store Validator to check your package for common publishing issues"
            };

            var validateButton = new Button(Validate) { name = "ValidateButton", text = "Validate" };

            validatorLabelHelpRow.Add(validatorLabel);
            validatorLabelHelpRow.Add(validatorLabelTooltip);

            validatorButtonRow.Add(validatorLabelHelpRow);
            validatorButtonRow.Add(validateButton);

            Add(validatorButtonRow);
        }

        private void CreateResultsBox()
        {
            ResultsBox = new Box { name = "InfoBox" };
            ResultsBox.style.display = DisplayStyle.None;
            ResultsBox.AddToClassList("validation-result-box");

            ResultsBoxImage = new Image();
            ResultsBoxLabel = new Label { name = "ValidationLabel" };

            ResultsBox.Add(ResultsBoxImage);
            ResultsBox.Add(ResultsBoxLabel);

            Add(ResultsBox);
        }

        protected virtual bool ConfirmValidation()
        {
            // Child classes can implement pre-validation prompts
            return true;
        }

        private void Validate()
        {
            if (!ConfirmValidation())
                return;

            var validationResult = Workflow.Validate();

            if (validationResult.Status == ValidationStatus.Cancelled)
                return;

            if (validationResult.Status != ValidationStatus.RanToCompletion)
            {
                EditorUtility.DisplayDialog("Validation failed", $"Package validation failed: {validationResult.Exception.Message}", "OK");
                return;
            }

            DisplayResult(validationResult);
        }

        private void DisplayResult(ValidationResult result)
        {
            ResultsBox.style.display = DisplayStyle.Flex;
            UpdateValidationResultImage(result);
            UpdateValidationResultLabel(result);
        }

        public void HideResult()
        {
            ResultsBox.style.display = DisplayStyle.None;
        }

        protected void UpdateValidationResultImage(ValidationResult result)
        {
            switch (GetValidationSummaryStatus(result))
            {
                case TestResultStatus.Pass:
                    ResultsBoxImage.image = EditorGUIUtility.IconContent("console.infoicon@2x").image;
                    break;
                case TestResultStatus.Warning:
                    ResultsBoxImage.image = EditorGUIUtility.IconContent("console.warnicon@2x").image;
                    break;
                case TestResultStatus.Fail:
                    ResultsBoxImage.image = EditorGUIUtility.IconContent("console.erroricon@2x").image;
                    break;
                default:
                    ResultsBoxImage.image = EditorGUIUtility.IconContent("_Help@2x").image;
                    break;
            }
        }

        private void UpdateValidationResultLabel(ValidationResult result)
        {
            var errorCount = result.Tests.Where(x => x.Result.Status == TestResultStatus.Fail).Count();
            var warningCount = result.Tests.Where(x => x.Result.Status == TestResultStatus.Warning).Count();

            string text = string.Empty;
            if (result.HadCompilationErrors)
            {
                text += "- Package caused compilation errors\n";
            }
            if (errorCount > 0)
            {
                text += $"- Validation reported {errorCount} error(s)\n";
            }
            if (warningCount > 0)
            {
                text += $"- Validation reported {warningCount} warning(s)\n";
            }

            if (string.IsNullOrEmpty(text))
            {
                text = "No issues were found!";
            }
            else
            {
                text = text.Substring(0, text.Length - "\n".Length);
            }

            ResultsBoxLabel.text = text;
        }

        private TestResultStatus GetValidationSummaryStatus(ValidationResult result)
        {
            if (result.HadCompilationErrors ||
                result.Tests.Any(x => x.Result.Status == TestResultStatus.Fail))
                return TestResultStatus.Fail;

            if (result.Tests.Any(x => x.Result.Status == TestResultStatus.Warning))
                return TestResultStatus.Warning;

            return TestResultStatus.Pass;
        }
    }
}