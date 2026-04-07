using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using vsrepo_Gui.Models;
using vsrepo_Gui.Services;
using Wpf.Ui.Appearance;
using WpfButton = Wpf.Ui.Controls.Button;
using WpfFluentWindow = Wpf.Ui.Controls.FluentWindow;
using WpfNavigationViewItem = Wpf.Ui.Controls.NavigationViewItem;
using WpfWindowBackdropType = Wpf.Ui.Controls.WindowBackdropType;

namespace vsrepo_Gui;

public partial class MainWindow : WpfFluentWindow
{
    private readonly VsrepoService _service = new();
    private readonly AppStateService _appStateService = new();
    private readonly ObservableCollection<PackageItem> _visiblePackages = [];
    private readonly CancellationTokenSource _shutdown = new();
    private readonly DispatcherTimer _searchDebounceTimer = new() { Interval = TimeSpan.FromMilliseconds(220) };
    private CancellationTokenSource? _detailsCts;

    private AppStateService.AppState _appState = new();
    private List<PackageItem> _allPackages = [];
    private VsPackageRoot? _definitionsRoot;
    private Dictionary<string, VsrepoService.InstalledPackageInfo> _installedPackages = new(StringComparer.OrdinalIgnoreCase);
    private VsrepoService.ProbeResult? _lastProbe;
    private VsrepoService.VsrepoPaths? _lastPaths;
    private PackageItem? _selectedPackage;
    private bool _isBusy;

    public MainWindow()
    {
        AppLog.Write("MainWindow constructor start");
        InitializeComponent();
        SystemThemeWatcher.Watch(this, WpfWindowBackdropType.Mica, true);

        PackagesGrid.ItemsSource = _visiblePackages;
        TargetComboBox.ItemsSource = new[] { "win64", "win32" };
        TargetComboBox.SelectedItem = Environment.Is64BitOperatingSystem ? "win64" : "win32";
        StatusFilterComboBox.ItemsSource = new[] { "All", "Updates", "Installed", "Not Installed", "Unknown Version" };
        StatusFilterComboBox.SelectedIndex = 0;
        CategoryFilterComboBox.ItemsSource = new[] { "All Categories" };
        CategoryFilterComboBox.SelectedIndex = 0;
        PluginsNavigationItem.IsActive = true;
        SetCurrentView("plugins");
        UpdateDetailsPanel(null);
        _searchDebounceTimer.Tick += SearchDebounceTimer_Tick;

        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
        AppLog.Write("MainWindow constructor end");
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            AppLog.Write("MainWindow loaded");
            RestoreAppState();
            await AutoDetectPythonAsync();
            if (!string.IsNullOrWhiteSpace(PythonPathTextBox.Text))
            {
                await RefreshPackagesAsync(updateDefinitions: false, reprobe: true, reloadDefinitions: true);
            }
            AppLog.Write("MainWindow loaded completed");
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "MainWindow_Loaded");
            MessageBox.Show(ex.ToString(), "vsrepo_Gui", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
        }
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _detailsCts?.Cancel();
        SaveAppState();
        SystemThemeWatcher.UnWatch(this);
        _shutdown.Cancel();
    }

    private async Task AutoDetectPythonAsync()
    {
        if (!string.IsNullOrWhiteSpace(PythonPathTextBox.Text) && File.Exists(PythonPathTextBox.Text.Trim()))
        {
            return;
        }

        var candidates = await _service.DetectPythonCandidatesAsync(_shutdown.Token);
        var python = candidates.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(python))
        {
            PythonPathTextBox.Text = python;
            AppendLog($"Detected Python: {python}");
        }
        else
        {
            StatusTextBlock.Text = "No Python interpreter detected. Browse to python.exe manually.";
        }
    }

    private async Task<bool> EnsureEnvironmentAsync(bool reprobe)
    {
        var python = PythonPathTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(python))
        {
            StatusTextBlock.Text = "Select a Python interpreter first.";
            return false;
        }

        if (!reprobe && _lastProbe?.Success == true && string.Equals(_lastProbe.ResolvedPython, python, StringComparison.OrdinalIgnoreCase) && _lastPaths is not null)
        {
            return true;
        }

        _lastProbe = await _service.ProbeAsync(python, _shutdown.Token);
        PythonPathTextBox.Text = _lastProbe.ResolvedPython;
        StatusTextBlock.Text = _lastProbe.Message;

        if (_lastProbe.Success)
        {
            AppendLog($"Probe OK: {_lastProbe.ResolvedPython}");
            if (!string.IsNullOrWhiteSpace(_lastProbe.VapourSynthPath))
            {
                AppendLog($"VapourSynth: {_lastProbe.VapourSynthPath}");
            }
            if (!string.IsNullOrWhiteSpace(_lastProbe.VsrepoPath))
            {
                AppendLog($"VSRepo: {_lastProbe.VsrepoPath}");
            }
            return true;
        }

        AppendLog($"Probe failed: {_lastProbe.Message}");
        return false;
    }

    private async Task RefreshPackagesAsync(bool updateDefinitions, bool reprobe, bool reloadDefinitions)
    {
        if (_isBusy)
        {
            return;
        }

        var selectedIdentifier = _selectedPackage?.Identifier ?? (PackagesGrid.SelectedItem as PackageItem)?.Identifier;

        SetBusy(true);
        try
        {
            if (!await EnsureEnvironmentAsync(reprobe))
            {
                return;
            }

            var python = PythonPathTextBox.Text.Trim();
            var target = GetSelectedTarget();

            _lastPaths = await _service.GetPathsAsync(python, target, _shutdown.Token);
            ApplyPathsToUi(_lastPaths);

            if (updateDefinitions || !File.Exists(_lastPaths.Definitions))
            {
                var updateResult = await _service.RunVsrepoAsync(python, target, "update", cancellationToken: _shutdown.Token);
                AppendCommandResult("update", updateResult);
                if (updateResult.ExitCode != 0)
                {
                    throw new InvalidOperationException(updateResult.CombinedOutput);
                }
                reloadDefinitions = true;
            }

            if (_definitionsRoot is null || reloadDefinitions)
            {
                _definitionsRoot = _service.LoadDefinitions(_lastPaths.Definitions);
            }

            _installedPackages = await _service.GetInstalledAsync(python, target, _shutdown.Token);
            _allPackages = BuildPackageItems(_definitionsRoot, _installedPackages, target)
                .OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            UpdateCategoryFilterOptions();
            ApplyFilters(selectedIdentifier);
            UpdateCounters();
            AppendLog($"Loaded {_allPackages.Count} packages for {target}.");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "RefreshPackagesAsync");
            AppendLog(ex.ToString());
            MessageBox.Show(ex.Message, "vsrepo_Gui", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private List<PackageItem> BuildPackageItems(VsPackageRoot root, Dictionary<string, VsrepoService.InstalledPackageInfo> installed, string target)
    {
        var items = new List<PackageItem>();

        foreach (var package in root.Packages)
        {
            var latestRelease = GetLatestRelevantRelease(package, target);
            if (latestRelease is null)
            {
                continue;
            }

            installed.TryGetValue(package.Identifier, out var installedInfo);
            items.Add(new PackageItem
            {
                Name = package.Name,
                NamespaceOrModule = package.Namespace ?? package.ModuleName ?? package.Identifier,
                Identifier = package.Identifier,
                Type = package.Type,
                Category = string.IsNullOrWhiteSpace(package.Category) ? "Uncategorized" : package.Category,
                Description = package.Description,
                Website = package.Website,
                Github = package.Github,
                Doom9 = package.Doom9,
                Dependencies = package.Dependencies ?? [],
                InstalledVersion = installedInfo?.InstalledVersion ?? string.Empty,
                LatestVersion = latestRelease.Version,
                LatestPublishedText = FormatDateDisplay(latestRelease.Published),
                State = installedInfo?.State ?? PackageInstallState.NotInstalled,
            });
        }

        return items;
    }

    private static VsRelease? GetLatestRelevantRelease(VsPackageDefinition package, string target)
    {
        foreach (var release in package.Releases)
        {
            if (package.Type.Equals("VSPlugin", StringComparison.OrdinalIgnoreCase))
            {
                if (target == "win64" && release.Win64 is not null)
                {
                    return release;
                }

                if (target == "win32" && release.Win32 is not null)
                {
                    return release;
                }
            }
            else if (package.Type.Equals("PyScript", StringComparison.OrdinalIgnoreCase) && release.Script is not null)
            {
                return release;
            }
            else if (package.Type.Equals("PyWheel", StringComparison.OrdinalIgnoreCase) && release.Wheel is not null)
            {
                return release;
            }
        }

        return package.Releases.FirstOrDefault();
    }

    private void UpdateCategoryFilterOptions()
    {
        var currentSelection = CategoryFilterComboBox.SelectedItem as string;
        var categories = _allPackages
            .Select(static item => item.Category)
            .Where(static category => !string.IsNullOrWhiteSpace(category))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static category => category, StringComparer.OrdinalIgnoreCase)
            .ToList();
        categories.Insert(0, "All Categories");

        CategoryFilterComboBox.ItemsSource = categories;
        var preferred = !string.IsNullOrWhiteSpace(currentSelection) && !string.Equals(currentSelection, "All Categories", StringComparison.OrdinalIgnoreCase)
            ? currentSelection
            : _appState.CategoryFilter;

        CategoryFilterComboBox.SelectedItem = categories.Contains(preferred ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            ? preferred
            : "All Categories";
    }

    private void ApplyFilters(string? preserveIdentifier = null)
    {
        var search = SearchTextBox.Text.Trim();
        var statusFilter = (StatusFilterComboBox.SelectedItem as string) ?? "All";
        var categoryFilter = (CategoryFilterComboBox.SelectedItem as string) ?? "All Categories";

        IEnumerable<PackageItem> filtered = _allPackages;
        filtered = statusFilter switch
        {
            "Updates" => filtered.Where(static x => x.State == PackageInstallState.UpdateAvailable),
            "Installed" => filtered.Where(static x => x.State == PackageInstallState.Installed),
            "Not Installed" => filtered.Where(static x => x.State == PackageInstallState.NotInstalled),
            "Unknown Version" => filtered.Where(static x => x.State == PackageInstallState.InstalledUnknown),
            _ => filtered,
        };

        if (!string.Equals(categoryFilter, "All Categories", StringComparison.OrdinalIgnoreCase))
        {
            filtered = filtered.Where(x => string.Equals(x.Category, categoryFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            filtered = filtered.Where(x =>
                x.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                x.NamespaceOrModule.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                x.Identifier.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                x.Description.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        _visiblePackages.Clear();
        foreach (var item in filtered)
        {
            _visiblePackages.Add(item);
        }

        RestoreSelection(preserveIdentifier);
    }

    private void RestoreSelection(string? identifier)
    {
        PackageItem? restored = null;
        if (!string.IsNullOrWhiteSpace(identifier))
        {
            restored = _visiblePackages.FirstOrDefault(x => string.Equals(x.Identifier, identifier, StringComparison.OrdinalIgnoreCase));
        }

        restored ??= _visiblePackages.FirstOrDefault();
        PackagesGrid.SelectedItem = restored;
        if (restored is not null)
        {
            PackagesGrid.ScrollIntoView(restored);
        }
        else
        {
            UpdateDetailsPanel(null);
        }
    }

    private void RestoreAppState()
    {
        _appState = _appStateService.Load();

        if (!string.IsNullOrWhiteSpace(_appState.PythonPath))
        {
            PythonPathTextBox.Text = _appState.PythonPath;
        }

        if (!string.IsNullOrWhiteSpace(_appState.Target))
        {
            TargetComboBox.SelectedItem = _appState.Target;
        }

        if (!string.IsNullOrWhiteSpace(_appState.StatusFilter))
        {
            StatusFilterComboBox.SelectedItem = _appState.StatusFilter;
        }

        SearchTextBox.Text = _appState.SearchText ?? string.Empty;

        if (_appState.Width > 0)
        {
            Width = _appState.Width;
        }

        if (_appState.Height > 0)
        {
            Height = _appState.Height;
        }

        if (!double.IsNaN(_appState.Left))
        {
            Left = _appState.Left;
        }

        if (!double.IsNaN(_appState.Top))
        {
            Top = _appState.Top;
        }

        if (_appState.Maximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    private void SaveAppState()
    {
        _appState.PythonPath = PythonPathTextBox.Text.Trim();
        _appState.Target = GetSelectedTarget();
        _appState.StatusFilter = (StatusFilterComboBox.SelectedItem as string) ?? "All";
        _appState.CategoryFilter = (CategoryFilterComboBox.SelectedItem as string) ?? "All Categories";
        _appState.SearchText = SearchTextBox.Text;
        _appState.SelectedIdentifier = _selectedPackage?.Identifier ?? string.Empty;
        _appState.Maximized = WindowState == WindowState.Maximized;
        if (WindowState == WindowState.Normal)
        {
            _appState.Width = Width;
            _appState.Height = Height;
            _appState.Left = Left;
            _appState.Top = Top;
        }

        _appStateService.Save(_appState);
    }

    private void UpdateCounters()
    {
        UpdatesCountTextBlock.Text = _allPackages.Count(x => x.State == PackageInstallState.UpdateAvailable).ToString();
        InstalledCountTextBlock.Text = _allPackages.Count(x => x.State == PackageInstallState.Installed).ToString();
        NotInstalledCountTextBlock.Text = _allPackages.Count(x => x.State == PackageInstallState.NotInstalled).ToString();
        UnknownCountTextBlock.Text = _allPackages.Count(x => x.State == PackageInstallState.InstalledUnknown).ToString();
    }

    private void ApplyPathsToUi(VsrepoService.VsrepoPaths paths)
    {
        DefinitionsPathTextBox.Text = paths.Definitions;
        BinariesPathTextBox.Text = paths.Binaries;
        ScriptsPathTextBox.Text = paths.Scripts;
    }

    private void SetBusy(bool busy)
    {
        _isBusy = busy;
        RootNavigationView.IsEnabled = !busy;
        Mouse.OverrideCursor = busy ? Cursors.Wait : null;
    }

    private string GetSelectedTarget()
    {
        return (TargetComboBox.SelectedItem as string) ?? "win64";
    }

    private void AppendLog(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        LogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        LogTextBox.ScrollToEnd();
    }

    private void AppendCommandResult(string command, VsrepoService.CommandResult result)
    {
        AppendLog($"> {command}");
        if (!string.IsNullOrWhiteSpace(result.StdOut))
        {
            AppendLog(result.StdOut.TrimEnd());
        }
        if (!string.IsNullOrWhiteSpace(result.StdErr))
        {
            AppendLog(result.StdErr.TrimEnd());
        }
        AppendLog($"ExitCode: {result.ExitCode}");
    }

    private bool MutationRequiresElevation(PackageItem item)
    {
        if (_service.IsAdministrator())
        {
            return false;
        }

        var targetPath = item.Type.Equals("VSPlugin", StringComparison.OrdinalIgnoreCase)
            ? _lastPaths?.Binaries
            : _lastPaths?.Scripts;

        return !_service.CanWriteToPath(targetPath);
    }

    private async Task ExecutePackageActionAsync(PackageItem item)
    {
        var python = PythonPathTextBox.Text.Trim();
        var target = GetSelectedTarget();

        string operation;
        bool force = false;
        switch (item.State)
        {
            case PackageInstallState.Installed:
                if (MessageBox.Show($"Uninstall {item.Name}?", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                {
                    return;
                }
                operation = "uninstall";
                break;
            case PackageInstallState.InstalledUnknown:
                if (MessageBox.Show($"Force-upgrade {item.Name}? Local unknown files may be overwritten.", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                {
                    return;
                }
                operation = "upgrade";
                force = true;
                break;
            case PackageInstallState.UpdateAvailable:
                operation = "upgrade";
                break;
            default:
                operation = "install";
                break;
        }

        var requiresElevation = MutationRequiresElevation(item);
        if (requiresElevation)
        {
            AppendLog($"Elevation required for {operation} {item.Identifier}.");
        }

        var result = requiresElevation
            ? await _service.RunVsrepoElevatedAsync(python, target, operation, [item.Identifier], force, _shutdown.Token)
            : await _service.RunVsrepoAsync(python, target, operation, [item.Identifier], force, _shutdown.Token);

        if (!requiresElevation && result.ExitCode != 0 && _service.IsPermissionDenied(result))
        {
            if (MessageBox.Show(
                    "This operation needs administrator rights because VSRepo is writing into a protected directory. Approve a UAC prompt and retry elevated?",
                    "Administrator Rights Required",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                result = await _service.RunVsrepoElevatedAsync(python, target, operation, [item.Identifier], force, _shutdown.Token);
            }
        }

        AppendCommandResult($"{operation} {item.Identifier}", result);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(result.CombinedOutput);
        }
    }

    private void UpdateDetailsPanel(PackageItem? item)
    {
        _selectedPackage = item;

        if (item is null)
        {
            DetailNameTextBlock.Text = "Select a package";
            DetailNamespaceTextBlock.Text = "Namespace / module";
            DetailDescriptionTextBlock.Text = "Package details will appear here.";
            DetailIdentifierTextBlock.Text = "-";
            DetailCategoryTextBlock.Text = "-";
            DetailInstalledTextBlock.Text = "-";
            DetailLatestTextBlock.Text = "-";
            DetailTypeTextBlock.Text = "-";
            DetailPublishedTextBlock.Text = "-";
            DetailGithubStatusTextBlock.Text = "No GitHub metadata loaded.";
            DetailGithubTagTextBlock.Text = "Latest release: -";
            DetailGithubUpdatedTextBlock.Text = "Updated: -";
            DetailStateTextBlock.Text = "Idle";
            DetailStateTextBlock.Foreground = (Brush)Application.Current.Resources["TextBrush"];
            DetailStateBorder.Background = (Brush)Application.Current.Resources["SurfaceSoftBrush"];
            DependenciesItemsControl.ItemsSource = Array.Empty<string>();
            DetailActionButton.IsEnabled = false;
            CopyIdentifierButton.IsEnabled = false;
            OpenWebsiteButton.IsEnabled = false;
            OpenGitHubButton.IsEnabled = false;
            OpenDoom9Button.IsEnabled = false;
            RefreshGitHubButton.IsEnabled = false;
            return;
        }

        DetailNameTextBlock.Text = item.Name;
        DetailNamespaceTextBlock.Text = item.NamespaceOrModule;
        DetailDescriptionTextBlock.Text = item.Description;
        DetailIdentifierTextBlock.Text = item.Identifier;
        DetailCategoryTextBlock.Text = item.Category;
        DetailInstalledTextBlock.Text = string.IsNullOrWhiteSpace(item.InstalledVersion) ? "Not installed" : item.InstalledVersion;
        DetailLatestTextBlock.Text = item.LatestVersion;
        DetailTypeTextBlock.Text = item.Type;
        DetailPublishedTextBlock.Text = item.LatestPublishedText;
        DependenciesItemsControl.ItemsSource = item.Dependencies.Count > 0 ? item.Dependencies : new[] { "No dependencies" };
        DetailActionButton.Content = item.ActionText;
        DetailActionButton.IsEnabled = true;
        CopyIdentifierButton.IsEnabled = true;
        OpenWebsiteButton.IsEnabled = item.HasWebsite;
        OpenGitHubButton.IsEnabled = item.HasGithub;
        OpenDoom9Button.IsEnabled = item.HasDoom9;
        RefreshGitHubButton.IsEnabled = item.HasGithub || (!string.IsNullOrWhiteSpace(item.Website) && item.Website.Contains("github.com", StringComparison.OrdinalIgnoreCase));

        ApplyStateBadge(item.State);
        DetailGithubStatusTextBlock.Text = item.HasGithub ? "Loading GitHub metadata..." : "No GitHub URL available.";
        DetailGithubTagTextBlock.Text = "Latest release: -";
        DetailGithubUpdatedTextBlock.Text = "Updated: -";
    }

    private void ApplyStateBadge(PackageInstallState state)
    {
        var backgroundKey = "SurfaceSoftBrush";
        var foregroundKey = "TextBrush";
        var label = state switch
        {
            PackageInstallState.NotInstalled => "Not Installed",
            PackageInstallState.UpdateAvailable => "Update Available",
            PackageInstallState.InstalledUnknown => "Unknown Version",
            _ => "Installed",
        };

        switch (state)
        {
            case PackageInstallState.Installed:
                backgroundKey = "SuccessSoftBrush";
                foregroundKey = "SuccessBrush";
                break;
            case PackageInstallState.UpdateAvailable:
                backgroundKey = "WarningSoftBrush";
                foregroundKey = "WarningBrush";
                break;
            case PackageInstallState.InstalledUnknown:
                backgroundKey = "DangerSoftBrush";
                foregroundKey = "DangerBrush";
                break;
            case PackageInstallState.NotInstalled:
                backgroundKey = "AccentSoftBrush";
                foregroundKey = "AccentBrush";
                break;
        }

        DetailStateTextBlock.Text = label;
        DetailStateBorder.Background = (Brush)Application.Current.Resources[backgroundKey];
        DetailStateTextBlock.Foreground = (Brush)Application.Current.Resources[foregroundKey];
    }

    private async Task LoadGitHubDetailsAsync(PackageItem? item, bool forceRefresh = false)
    {
        _detailsCts?.Cancel();
        if (item is null)
        {
            return;
        }

        _detailsCts = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        var token = _detailsCts.Token;

        try
        {
            var githubUrl = item.Github;
            if (string.IsNullOrWhiteSpace(githubUrl) && item.Website?.Contains("github.com", StringComparison.OrdinalIgnoreCase) == true)
            {
                githubUrl = item.Website;
            }

            if (string.IsNullOrWhiteSpace(githubUrl))
            {
                DetailGithubStatusTextBlock.Text = "No GitHub repository configured for this package.";
                return;
            }

            if (forceRefresh)
            {
                DetailGithubStatusTextBlock.Text = "Refreshing GitHub metadata...";
            }

            var info = await _service.GetGitHubReleaseInfoAsync(githubUrl, forceRefresh, token);
            if (token.IsCancellationRequested || !ReferenceEquals(item, _selectedPackage))
            {
                return;
            }

            if (info is null)
            {
                DetailGithubStatusTextBlock.Text = "No GitHub metadata available.";
                return;
            }

            DetailGithubStatusTextBlock.Text = info.Error is null
                ? info.Source
                : $"{info.Source}: {info.Error}";
            DetailGithubTagTextBlock.Text = $"Latest release: {info.LatestTag ?? "No formal release"}";
            DetailGithubUpdatedTextBlock.Text = $"Updated: {FormatDateDisplay(info.ReleasePublishedAt?.ToString("O") ?? info.RepositoryUpdatedAt?.ToString("O"))}";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "LoadGitHubDetailsAsync");
            if (ReferenceEquals(item, _selectedPackage))
            {
                DetailGithubStatusTextBlock.Text = $"GitHub metadata failed: {ex.Message}";
            }
        }
    }

    private static string FormatDateDisplay(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "Unknown";
        }

        return DateTimeOffset.TryParse(raw, out var value)
            ? value.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            : raw;
    }

    private async void PackageAction_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy || sender is not WpfButton { Tag: PackageItem item })
        {
            return;
        }

        SetBusy(true);
        try
        {
            await ExecutePackageActionAsync(item);
            await RefreshPackagesAsync(updateDefinitions: false, reprobe: false, reloadDefinitions: false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "PackageAction_Click");
            AppendLog(ex.ToString());
            MessageBox.Show(ex.Message, "vsrepo_Gui", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshPackagesAsync(updateDefinitions: false, reprobe: false, reloadDefinitions: false);
    }

    private async void UpdateDefinitionsButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshPackagesAsync(updateDefinitions: true, reprobe: false, reloadDefinitions: true);
    }

    private async void UpgradeAllButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        SetBusy(true);
        try
        {
            var python = PythonPathTextBox.Text.Trim();
            var target = GetSelectedTarget();
            var requiresElevation = !_service.IsAdministrator()
                                    && (_service.CanWriteToPath(_lastPaths?.Binaries) == false
                                        || _service.CanWriteToPath(_lastPaths?.Scripts) == false);
            if (requiresElevation)
            {
                AppendLog("Elevation required for upgrade-all.");
            }

            var result = requiresElevation
                ? await _service.RunVsrepoElevatedAsync(python, target, "upgrade-all", cancellationToken: _shutdown.Token)
                : await _service.RunVsrepoAsync(python, target, "upgrade-all", cancellationToken: _shutdown.Token);

            if (!requiresElevation && result.ExitCode != 0 && _service.IsPermissionDenied(result))
            {
                if (MessageBox.Show(
                        "upgrade-all needs administrator rights because VSRepo is writing into a protected directory. Approve a UAC prompt and retry elevated?",
                        "Administrator Rights Required",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    result = await _service.RunVsrepoElevatedAsync(python, target, "upgrade-all", cancellationToken: _shutdown.Token);
                }
            }

            AppendCommandResult("upgrade-all", result);
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(result.CombinedOutput);
            }

            await RefreshPackagesAsync(updateDefinitions: false, reprobe: false, reloadDefinitions: false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "UpgradeAllButton_Click");
            AppendLog(ex.ToString());
            MessageBox.Show(ex.Message, "vsrepo_Gui", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void ProbeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        SetBusy(true);
        try
        {
            await RefreshPackagesAsync(updateDefinitions: false, reprobe: true, reloadDefinitions: false);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void DetectPythonButton_Click(object sender, RoutedEventArgs e)
    {
        await AutoDetectPythonAsync();
    }

    private void BrowsePythonButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Python (python.exe)|python.exe|Executables (*.exe)|*.exe",
            CheckFileExists = true,
            Multiselect = false,
            Title = "Select python.exe",
        };

        if (dialog.ShowDialog(this) == true)
        {
            PythonPathTextBox.Text = dialog.FileName;
        }
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchDebounceTimer.Stop();
        _searchDebounceTimer.Start();
    }

    private void FilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
        {
            ApplyFilters(_selectedPackage?.Identifier);
            SaveAppState();
        }
    }

    private async void TargetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded && !string.IsNullOrWhiteSpace(PythonPathTextBox.Text) && !_isBusy)
        {
            _lastPaths = null;
            await RefreshPackagesAsync(updateDefinitions: false, reprobe: false, reloadDefinitions: false);
        }
    }

    private async void PackagesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var item = PackagesGrid.SelectedItem as PackageItem;
        UpdateDetailsPanel(item);
        await LoadGitHubDetailsAsync(item);
        SaveAppState();
    }

    private async void RefreshGitHubButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadGitHubDetailsAsync(_selectedPackage, forceRefresh: true);
    }

    private void ClearLogButton_Click(object sender, RoutedEventArgs e)
    {
        LogTextBox.Clear();
    }

    private void OpenDefinitionsButton_Click(object sender, RoutedEventArgs e)
    {
        OpenLocation(_lastPaths?.Definitions);
    }

    private void OpenBinariesButton_Click(object sender, RoutedEventArgs e)
    {
        OpenLocation(_lastPaths?.Binaries);
    }

    private void OpenScriptsButton_Click(object sender, RoutedEventArgs e)
    {
        OpenLocation(_lastPaths?.Scripts);
    }

    private void OpenWebsiteButton_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl(_selectedPackage?.Website);
    }

    private void OpenGitHubButton_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl(_selectedPackage?.Github);
    }

    private void OpenDoom9Button_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl(_selectedPackage?.Doom9);
    }

    private async void DetailActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPackage is null || _isBusy)
        {
            return;
        }

        SetBusy(true);
        try
        {
            await ExecutePackageActionAsync(_selectedPackage);
            await RefreshPackagesAsync(updateDefinitions: false, reprobe: false, reloadDefinitions: false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "DetailActionButton_Click");
            AppendLog(ex.ToString());
            MessageBox.Show(ex.Message, "vsrepo_Gui", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void CopyIdentifierButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPackage is null)
        {
            return;
        }

        Clipboard.SetText(_selectedPackage.Identifier);
        AppendLog($"Copied identifier: {_selectedPackage.Identifier}");
    }

    private void OpenLogFolderButton_Click(object sender, RoutedEventArgs e)
    {
        OpenLocation(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "vsrepo_Gui", "logs"));
    }

    private void SearchDebounceTimer_Tick(object? sender, EventArgs e)
    {
        _searchDebounceTimer.Stop();
        ApplyFilters(_selectedPackage?.Identifier);
        SaveAppState();
    }

    private static void OpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private static void OpenLocation(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (File.Exists(path))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            return;
        }

        if (Directory.Exists(path))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
    }

    private void RootNavigationView_SelectionChanged(object sender, RoutedEventArgs e)
    {
        if (RootNavigationView.SelectedItem is WpfNavigationViewItem item)
        {
            var tag = item.Tag as string ?? "plugins";
            SetCurrentView(tag);
        }
    }

    private void SetCurrentView(string tag)
    {
        var showSettings = string.Equals(tag, "settings", StringComparison.OrdinalIgnoreCase);
        PluginsView.Visibility = showSettings ? Visibility.Collapsed : Visibility.Visible;
        SettingsView.Visibility = showSettings ? Visibility.Visible : Visibility.Collapsed;
    }
}
