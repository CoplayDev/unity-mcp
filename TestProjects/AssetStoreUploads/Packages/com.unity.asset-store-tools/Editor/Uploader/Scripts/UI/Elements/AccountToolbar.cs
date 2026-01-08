using AssetStoreTools.Api.Models;
using System;
using System.Threading.Tasks;
using UnityEngine.UIElements;

namespace AssetStoreTools.Uploader.UI.Elements
{
    internal class AccountToolbar : VisualElement
    {
        private Image _accountImage;
        private Label _accountEmailLabel;
        private Button _refreshButton;

        public event Func<Task> OnRefresh;
        public event Action OnLogout;

        public AccountToolbar()
        {
            Create();
        }

        private void Create()
        {
            AddToClassList("account-toolbar");

            // Left side of the toolbar
            VisualElement leftSideContainer = new VisualElement { name = "LeftSideContainer" };
            leftSideContainer.AddToClassList("account-toolbar-left-side-container");

            _accountImage = new Image();
            _accountImage.AddToClassList("account-toolbar-user-image");

            _accountEmailLabel = new Label() { name = "AccountEmail" };
            _accountEmailLabel.AddToClassList("account-toolbar-email-label");

            leftSideContainer.Add(_accountImage);
            leftSideContainer.Add(_accountEmailLabel);

            // Right side of the toolbar
            VisualElement rightSideContainer = new VisualElement { name = "RightSideContainer" };
            rightSideContainer.AddToClassList("account-toolbar-right-side-container");

            // Refresh button
            _refreshButton = new Button(Refresh) { name = "RefreshButton", text = "Refresh" };
            _refreshButton.AddToClassList("account-toolbar-button-refresh");

            // Logout button
            var logoutButton = new Button(Logout) { name = "LogoutButton", text = "Log out" };
            logoutButton.AddToClassList("account-toolbar-button-logout");

            rightSideContainer.Add(_refreshButton);
            rightSideContainer.Add(logoutButton);

            // Constructing the final toolbar
            Add(leftSideContainer);
            Add(rightSideContainer);
        }

        private async void Refresh()
        {
            _refreshButton.SetEnabled(false);
            await OnRefresh?.Invoke();
            _refreshButton.SetEnabled(true);
        }

        private void Logout()
        {
            OnLogout?.Invoke();
        }

        public void SetUser(User user)
        {
            if (user == null)
            {
                _accountEmailLabel.text = string.Empty;
                _accountImage.tooltip = string.Empty;
                return;
            }

            var userEmail = !string.IsNullOrEmpty(user.Username) ? user.Username : "Unknown";
            var publisherName = !string.IsNullOrEmpty(user.Name) ? user.Name : "Unknown";
            var publisherId = !string.IsNullOrEmpty(user.PublisherId) ? user.PublisherId : "Unknown";
            var userInfo =
                $"Username: {userEmail}\n" +
                $"Publisher Name: {publisherName}\n" +
                $"Publisher ID: {publisherId}";

            _accountEmailLabel.text = userEmail;
            _accountImage.tooltip = userInfo;
        }

        public void EnableButtons()
        {
            _refreshButton.SetEnabled(true);
        }

        public void DisableButtons()
        {
            _refreshButton.SetEnabled(false);
        }
    }
}