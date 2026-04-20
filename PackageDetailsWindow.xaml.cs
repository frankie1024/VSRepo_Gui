using System.Diagnostics;
using System.Windows;
using VSRepo_Gui.Models;
using Wpf.Ui.Controls;
using WpfButton = Wpf.Ui.Controls.Button;
using WpfFluentWindow = Wpf.Ui.Controls.FluentWindow;

namespace VSRepo_Gui;

public partial class PackageDetailsWindow : WpfFluentWindow
{
    private readonly PackageItem _package;
    public bool ShouldRunPrimaryAction { get; private set; }

    public PackageDetailsWindow(PackageItem package)
    {
        _package = package;
        InitializeComponent();
        (Application.Current as App)?.ApplyThemeToWindow(this);
        LoadPackage();
    }

    private void LoadPackage()
    {
        PackageNameTextBlock.Text = _package.Name;
        PackageNamespaceTextBlock.Text = _package.NamespaceOrModule;
        PackageDescriptionTextBlock.Text = _package.Description;
        PackageIdentifierTextBlock.Text = _package.Identifier;
        PackageCategoryTextBlock.Text = _package.Category;
        PackageInstalledTextBlock.Text = string.IsNullOrWhiteSpace(_package.InstalledVersion) ? "Not installed" : _package.InstalledVersion;
        PackageLatestTextBlock.Text = _package.LatestVersion;
        PackagePublishedTextBlock.Text = _package.LatestPublishedText;
        PackageTypeTextBlock.Text = _package.Type;
        DependenciesItemsControl.ItemsSource = _package.Dependencies.Count > 0 ? _package.Dependencies : new[] { "No dependencies" };

        ApplyActionButton(_package.State);
        ApplyLinkButtonState(OpenWebsiteButton, _package.HasWebsite, "Open website");
        ApplyLinkButtonState(OpenGitHubButton, _package.HasGithub, "Open GitHub");
        ApplyLinkButtonState(OpenDoom9Button, _package.HasDoom9, "Open Doom9");
        LinksCard.Visibility = _package.HasWebsite || _package.HasGithub || _package.HasDoom9
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void ApplyActionButton(PackageInstallState state)
    {
        var appearance = ControlAppearance.Primary;
        var label = state switch
        {
            PackageInstallState.NotInstalled => "Install",
            PackageInstallState.UpdateAvailable => "Update",
            PackageInstallState.InstalledUnknown => "Update",
            _ => "Uninstall",
        };

        switch (state)
        {
            case PackageInstallState.Installed:
                appearance = ControlAppearance.Secondary;
                break;
            case PackageInstallState.UpdateAvailable:
                appearance = ControlAppearance.Caution;
                break;
            case PackageInstallState.InstalledUnknown:
                appearance = ControlAppearance.Primary;
                break;
            default:
                appearance = ControlAppearance.Primary;
                break;
        }

        PackageActionButton.Appearance = appearance;
        PackageActionTextBlock.Text = label;
    }

    private static void ApplyLinkButtonState(WpfButton button, bool enabled, string tooltip)
    {
        button.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        button.ToolTip = tooltip;
    }

    private static void OpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private void OpenWebsiteButton_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl(_package.Website);
    }

    private void OpenGitHubButton_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl(_package.Github);
    }

    private void OpenDoom9Button_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl(_package.Doom9);
    }

    private void PackageActionButton_Click(object sender, RoutedEventArgs e)
    {
        ShouldRunPrimaryAction = true;
        DialogResult = true;
        Close();
    }
}

