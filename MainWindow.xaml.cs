using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using VSRepo_Gui.Models;
using VSRepo_Gui.Services;
using Wpf.Ui.Appearance;
using WpfButton = Wpf.Ui.Controls.Button;
using WpfControlAppearance = Wpf.Ui.Controls.ControlAppearance;
using WpfFluentWindow = Wpf.Ui.Controls.FluentWindow;
using WpfSymbolRegular = Wpf.Ui.Controls.SymbolRegular;

namespace VSRepo_Gui;

public partial class MainWindow : WpfFluentWindow
{
    private const double NavigationPaneCollapsedWidth = 72;
    private const double NavigationPaneExpandedWidth = 184;
    private const string AllCategoriesLabel = "All Categories";

    private static readonly string[] SupportedTargets = ["win64", "win32"];
    private static readonly string[] StatusFilterOptions = ["All", "Updates", "Installed", "Not Installed", "Unknown Version"];

    private readonly VsrepoService _service = new();
    private readonly AppStateService _appStateService = new();
    private readonly ObservableCollection<PackageItem> _visiblePackages = [];
    private readonly CancellationTokenSource _shutdown = new();
    private readonly DispatcherTimer _searchDebounceTimer = new() { Interval = TimeSpan.FromMilliseconds(220) };

    private AppStateService.AppState _appState = new();
    private List<PackageItem> _allPackages = [];
    private VsPackageRoot? _definitionsRoot;
    private Dictionary<string, VsrepoService.InstalledPackageInfo> _installedPackages = new(StringComparer.OrdinalIgnoreCase);
    private VsrepoService.ProbeResult? _lastProbe;
    private VsrepoService.VsrepoPaths? _lastPaths;
    private PackageItem? _selectedPackage;
    private bool _isBusy;
    private bool _isNavigationExpanded;
    private bool _isUpdatingThemeSelection;

    private sealed record PackageCommand(string Operation, bool Force);

    public MainWindow()
    {
        InitializeComponent();
        (Application.Current as App)?.ApplyThemeToWindow(this);
        ApplicationThemeManager.Changed += ApplicationThemeManager_Changed;

        PackagesGrid.ItemsSource = _visiblePackages;
        TargetComboBox.ItemsSource = SupportedTargets;
        TargetComboBox.SelectedItem = Environment.Is64BitOperatingSystem ? "win64" : "win32";
        StatusFilterComboBox.ItemsSource = StatusFilterOptions;
        StatusFilterComboBox.SelectedIndex = 0;
        CategoryFilterComboBox.ItemsSource = new[] { AllCategoriesLabel };
        CategoryFilterComboBox.SelectedIndex = 0;
        PluginsNavButton.Appearance = WpfControlAppearance.Secondary;
        SetCurrentView("plugins");
        SidebarHost.Width = NavigationPaneCollapsedWidth;
        UpdateNavigationPaneLayout();
        ApplyThemeSensitiveStyles();
        _searchDebounceTimer.Tick += SearchDebounceTimer_Tick;

        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            RestoreAppState();
            await AutoDetectPythonAsync();
            if (!string.IsNullOrWhiteSpace(PythonPathTextBox.Text))
            {
                await RefreshPackagesAsync(updateDefinitions: false, reprobe: true, reloadDefinitions: true);
            }
        }
        catch (Exception ex)
        {
            HandleException(ex, nameof(MainWindow_Loaded));
            Close();
        }
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        SaveAppState();
        try
        {
            ApplicationThemeManager.Changed -= ApplicationThemeManager_Changed;
        }
        finally
        {
            _shutdown.Cancel();
        }
    }

    private void ApplicationThemeManager_Changed(ApplicationTheme currentApplicationTheme, Color systemAccent)
    {
        Dispatcher.InvokeAsync(ApplyThemeSensitiveStyles);
    }

    private void ApplyThemeSensitiveStyles()
    {
        var isDarkTheme = (Application.Current as App)?.GetEffectiveTheme() == ApplicationTheme.Dark
                          || ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;

        var comboStyle = (Style)FindResource(isDarkTheme ? "FluentDarkComboBoxStyle" : "FluentComboBoxStyle");
        var comboItemStyle = (Style)FindResource(isDarkTheme ? "FluentDarkComboBoxItemStyle" : "FluentComboBoxItemStyle");
        var textBoxStyle = (Style)FindResource(isDarkTheme ? "FluentDarkTextBoxStyle" : "FluentTextBoxStyle");
        var readOnlyTextBoxStyle = (Style)FindResource(isDarkTheme ? "FluentDarkReadOnlyTextBoxStyle" : "FluentReadOnlyTextBoxStyle");

        foreach (var comboBox in new[] { TargetComboBox, StatusFilterComboBox, CategoryFilterComboBox })
        {
            comboBox.Style = comboStyle;
            comboBox.ItemContainerStyle = comboItemStyle;
        }

        foreach (var textBox in new[] { SearchTextBox, PythonPathTextBox })
        {
            textBox.Style = textBoxStyle;
        }

        foreach (var textBox in new[] { DefinitionsPathTextBox, BinariesPathTextBox, ScriptsPathTextBox })
        {
            textBox.Style = readOnlyTextBoxStyle;
        }
    }

    private void ThemeModeRadioButton_Checked(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingThemeSelection || sender is not RadioButton radioButton || radioButton.Tag is not string rawMode)
        {
            return;
        }

        if (!Enum.TryParse<AppThemeMode>(rawMode, true, out var themeMode))
        {
            return;
        }

        _appState.ThemeMode = themeMode;
        (Application.Current as App)?.SetThemeMode(themeMode);
        ApplyThemeSensitiveStyles();

        if (IsLoaded)
        {
            SaveAppState();
        }
    }

    private void FilterComboBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ComboBox comboBox || comboBox.IsDropDownOpen)
        {
            return;
        }

        comboBox.Focus();
        comboBox.IsDropDownOpen = true;
        e.Handled = true;
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

    private async Task RefreshPackagesAsync(bool updateDefinitions, bool reprobe, bool reloadDefinitions, bool allowBusyRefresh = false)
    {
        if (_isBusy && !allowBusyRefresh)
        {
            return;
        }

        var selectedIdentifier = _selectedPackage?.Identifier ?? (PackagesGrid.SelectedItem as PackageItem)?.Identifier;
        var ownsBusyState = !_isBusy;

        if (ownsBusyState)
        {
            SetBusy(true);
        }
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
            HandleException(ex, nameof(RefreshPackagesAsync));
        }
        finally
        {
            if (ownsBusyState)
            {
                SetBusy(false);
            }
        }
    }

    private List<PackageItem> BuildPackageItems(VsPackageRoot root, Dictionary<string, VsrepoService.InstalledPackageInfo> installed, string target)
    {
        var items = new List<PackageItem>(root.Packages.Count);

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
        categories.Insert(0, AllCategoriesLabel);

        CategoryFilterComboBox.ItemsSource = categories;
        var preferred = !string.IsNullOrWhiteSpace(currentSelection) && !string.Equals(currentSelection, AllCategoriesLabel, StringComparison.OrdinalIgnoreCase)
            ? currentSelection
            : _appState.CategoryFilter;

        CategoryFilterComboBox.SelectedItem = categories.Contains(preferred ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            ? preferred
            : AllCategoriesLabel;
    }

    private void ApplyFilters(string? preserveIdentifier = null)
    {
        var search = SearchTextBox.Text.Trim();
        var statusFilter = (StatusFilterComboBox.SelectedItem as string) ?? "All";
        var categoryFilter = (CategoryFilterComboBox.SelectedItem as string) ?? AllCategoriesLabel;

        IEnumerable<PackageItem> filtered = _allPackages;
        filtered = statusFilter switch
        {
            "Updates" => filtered.Where(static x => x.State == PackageInstallState.UpdateAvailable),
            "Installed" => filtered.Where(static x => x.State == PackageInstallState.Installed),
            "Not Installed" => filtered.Where(static x => x.State == PackageInstallState.NotInstalled),
            "Unknown Version" => filtered.Where(static x => x.State == PackageInstallState.InstalledUnknown),
            _ => filtered,
        };

        if (!string.Equals(categoryFilter, AllCategoriesLabel, StringComparison.OrdinalIgnoreCase))
        {
            filtered = filtered.Where(x => string.Equals(x.Category, categoryFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            // Weighted search: name (5), namespace/module (3), category (2)
            var tokens = search.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            var scored = filtered
                .Select(x =>
                {
                    var score = 0;
                    foreach (var tok in tokens)
                    {
                        if (!string.IsNullOrWhiteSpace(x.Name) && x.Name.StartsWith(tok, StringComparison.OrdinalIgnoreCase))
                        {
                            score += 8;
                        }
                        else if (!string.IsNullOrWhiteSpace(x.Name) && x.Name.IndexOf(tok, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            score += 5;
                        }

                        if (!string.IsNullOrWhiteSpace(x.NamespaceOrModule) && x.NamespaceOrModule.StartsWith(tok, StringComparison.OrdinalIgnoreCase))
                        {
                            score += 5;
                        }
                        else if (!string.IsNullOrWhiteSpace(x.NamespaceOrModule) && x.NamespaceOrModule.IndexOf(tok, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            score += 3;
                        }

                        if (!string.IsNullOrWhiteSpace(x.Category) && x.Category.StartsWith(tok, StringComparison.OrdinalIgnoreCase))
                        {
                            score += 3;
                        }
                        else if (!string.IsNullOrWhiteSpace(x.Category) && x.Category.IndexOf(tok, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            score += 2;
                        }
                    }

                    return new { Item = x, Score = score };
                })
                .Where(s => s.Score > 0)
                .OrderByDescending(s => s.Score)
                .ThenBy(s => s.Item.Name, StringComparer.OrdinalIgnoreCase)
                .Select(s => s.Item)
                .ToList();

            filtered = scored;
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
    }

    private void RestoreAppState()
    {
        _appState = _appStateService.Load();
        ApplyThemeSelection();

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
        _appState.ThemeMode = GetSelectedThemeMode();
        _appState.PythonPath = PythonPathTextBox.Text.Trim();
        _appState.Target = GetSelectedTarget();
        _appState.StatusFilter = (StatusFilterComboBox.SelectedItem as string) ?? "All";
        _appState.CategoryFilter = (CategoryFilterComboBox.SelectedItem as string) ?? AllCategoriesLabel;
        _appState.SearchText = SearchTextBox.Text;
        _appState.SelectedIdentifier = (PackagesGrid.SelectedItem as PackageItem)?.Identifier ?? string.Empty;
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

    private void ApplyThemeSelection()
    {
        _isUpdatingThemeSelection = true;
        try
        {
            LightThemeRadioButton.IsChecked = _appState.ThemeMode == AppThemeMode.Light;
            DarkThemeRadioButton.IsChecked = _appState.ThemeMode == AppThemeMode.Dark;
            SystemThemeRadioButton.IsChecked = _appState.ThemeMode == AppThemeMode.System;
        }
        finally
        {
            _isUpdatingThemeSelection = false;
        }
    }

    private AppThemeMode GetSelectedThemeMode()
    {
        if (LightThemeRadioButton.IsChecked == true)
        {
            return AppThemeMode.Light;
        }

        if (DarkThemeRadioButton.IsChecked == true)
        {
            return AppThemeMode.Dark;
        }

        return AppThemeMode.System;
    }

    private void UpdateCounters()
    {
        var updates = 0;
        var installed = 0;
        var notInstalled = 0;
        var unknown = 0;

        foreach (var item in _allPackages)
        {
            switch (item.State)
            {
                case PackageInstallState.UpdateAvailable:
                    updates++;
                    break;
                case PackageInstallState.Installed:
                    installed++;
                    break;
                case PackageInstallState.InstalledUnknown:
                    unknown++;
                    break;
                default:
                    notInstalled++;
                    break;
            }
        }

        UpdatesCountTextBlock.Text = updates.ToString();
        InstalledCountTextBlock.Text = installed.ToString();
        NotInstalledCountTextBlock.Text = notInstalled.ToString();
        UnknownCountTextBlock.Text = unknown.ToString();
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
        SidebarHost.IsEnabled = !busy;
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

    private void HandleException(Exception ex, string context)
    {
        AppLog.Write(ex, context);
        AppendLog($"{context}: {ex.Message}");
        MessageBox.Show(ex.Message, "VSRepo_Gui", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private async Task RunBusyOperationAsync(Func<Task> action, string errorContext)
    {
        if (_isBusy)
        {
            return;
        }

        SetBusy(true);
        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            HandleException(ex, errorContext);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task RefreshAfterMutationAsync()
    {
        AppendLog("Refreshing package status...");
        await RefreshPackagesAsync(updateDefinitions: false, reprobe: false, reloadDefinitions: false, allowBusyRefresh: true);
    }

    private async Task RunPackageActionWorkflowAsync(PackageItem item, string errorContext)
    {
        await RunBusyOperationAsync(async () =>
        {
            if (await ExecutePackageActionAsync(item))
            {
                await RefreshAfterMutationAsync();
            }
        }, errorContext);
    }

    private async Task<VsrepoService.CommandResult> RunVsrepoCommandAsync(
        string python,
        string target,
        string operation,
        bool requiresElevation,
        string elevationLogMessage,
        string elevationPrompt,
        IEnumerable<string>? packages = null,
        bool force = false)
    {
        if (requiresElevation)
        {
            AppendLog(elevationLogMessage);
        }

        var result = requiresElevation
            ? await _service.RunVsrepoElevatedAsync(python, target, operation, packages, force, _shutdown.Token)
            : await _service.RunVsrepoAsync(python, target, operation, packages, force, _shutdown.Token);

        if (!requiresElevation && result.ExitCode != 0 && _service.IsPermissionDenied(result))
        {
            if (MessageBox.Show(
                    elevationPrompt,
                    "Administrator Rights Required",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                result = await _service.RunVsrepoElevatedAsync(python, target, operation, packages, force, _shutdown.Token);
            }
        }

        return result;
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

    private PackageCommand? GetPackageCommand(PackageItem item)
    {
        return item.State switch
        {
            PackageInstallState.Installed when MessageBox.Show(
                $"Uninstall {item.Name}?",
                "Confirm",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) == MessageBoxResult.Yes
                => new PackageCommand("uninstall", false),
            PackageInstallState.InstalledUnknown when MessageBox.Show(
                $"Force-upgrade {item.Name}? Local unknown files may be overwritten.",
                "Confirm",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) == MessageBoxResult.Yes
                => new PackageCommand("upgrade", true),
            PackageInstallState.UpdateAvailable => new PackageCommand("upgrade", false),
            PackageInstallState.NotInstalled => new PackageCommand("install", false),
            _ => null
        };
    }

    private async Task<bool> ExecutePackageActionAsync(PackageItem item)
    {
        var command = GetPackageCommand(item);
        if (command is null)
        {
            return false;
        }

        var python = PythonPathTextBox.Text.Trim();
        var target = GetSelectedTarget();
        var result = await RunVsrepoCommandAsync(
            python,
            target,
            command.Operation,
            MutationRequiresElevation(item),
            $"Elevation required for {command.Operation} {item.Identifier}.",
            "This operation needs administrator rights because VSRepo is writing into a protected directory. Approve a UAC prompt and retry elevated?",
            [item.Identifier],
            command.Force);

        AppendCommandResult($"{command.Operation} {item.Identifier}", result);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(result.CombinedOutput);
        }

        return true;
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
        if (sender is not WpfButton { Tag: PackageItem item })
        {
            return;
        }

        await RunPackageActionWorkflowAsync(item, nameof(PackageAction_Click));
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
        await RunBusyOperationAsync(async () =>
        {
            var python = PythonPathTextBox.Text.Trim();
            var target = GetSelectedTarget();
            var requiresElevation = !_service.IsAdministrator()
                                    && (_service.CanWriteToPath(_lastPaths?.Binaries) == false
                                        || _service.CanWriteToPath(_lastPaths?.Scripts) == false);
            var result = await RunVsrepoCommandAsync(
                python,
                target,
                "upgrade-all",
                requiresElevation,
                "Elevation required for upgrade-all.",
                "upgrade-all needs administrator rights because VSRepo is writing into a protected directory. Approve a UAC prompt and retry elevated?");

            AppendCommandResult("upgrade-all", result);
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(result.CombinedOutput);
            }

            await RefreshAfterMutationAsync();
        }, nameof(UpgradeAllButton_Click));
    }

    private async void ProbeButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshPackagesAsync(updateDefinitions: false, reprobe: true, reloadDefinitions: false);
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
        _selectedPackage = PackagesGrid.SelectedItem as PackageItem;
        SaveAppState();
    }

    private async void PackagesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (PackagesGrid.SelectedItem is PackageItem item)
        {
            var dialog = new PackageDetailsWindow(item)
            {
                Owner = this
            };
            _ = dialog.ShowDialog();

            if (dialog.ShouldRunPrimaryAction)
            {
                await RunPackageActionWorkflowAsync(item, nameof(PackagesGrid_MouseDoubleClick));
            }
        }
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

    private void OpenLogFolderButton_Click(object sender, RoutedEventArgs e)
    {
        OpenLocation(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VSRepo_Gui", "logs"));
    }

    private void SearchDebounceTimer_Tick(object? sender, EventArgs e)
    {
        _searchDebounceTimer.Stop();
        ApplyFilters(_selectedPackage?.Identifier);
        SaveAppState();
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

    private void ToggleNavigationButton_Click(object sender, RoutedEventArgs e)
    {
        _isNavigationExpanded = !_isNavigationExpanded;
        UpdateNavigationPaneLayout();
    }

    private void UpdateNavigationToggleIcon()
    {
        ToggleNavigationIcon.Symbol = _isNavigationExpanded
            ? WpfSymbolRegular.PanelLeftContract24
            : WpfSymbolRegular.PanelLeftExpand24;
    }

    private void UpdateNavigationPaneLayout()
    {
        var targetWidth = _isNavigationExpanded ? NavigationPaneExpandedWidth : NavigationPaneCollapsedWidth;
        var animation = new DoubleAnimation
        {
            To = targetWidth,
            Duration = TimeSpan.FromMilliseconds(180),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };

        SidebarHost.BeginAnimation(WidthProperty, animation);
        SidebarTitleText.Visibility = _isNavigationExpanded ? Visibility.Visible : Visibility.Collapsed;
        PluginsNavText.Visibility = _isNavigationExpanded ? Visibility.Visible : Visibility.Collapsed;
        SettingsNavText.Visibility = _isNavigationExpanded ? Visibility.Visible : Visibility.Collapsed;
        UpdateNavigationToggleIcon();
    }

    private void SetCurrentView(string tag)
    {
        var showSettings = string.Equals(tag, "settings", StringComparison.OrdinalIgnoreCase);
        PluginsNavButton.Appearance = showSettings ? WpfControlAppearance.Transparent : WpfControlAppearance.Secondary;
        SettingsNavButton.Appearance = showSettings ? WpfControlAppearance.Secondary : WpfControlAppearance.Transparent;
        PluginsView.Visibility = showSettings ? Visibility.Collapsed : Visibility.Visible;
        SettingsView.Visibility = showSettings ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PluginsNavButton_Click(object sender, RoutedEventArgs e)
    {
        SetCurrentView("plugins");
    }

    private void SettingsNavButton_Click(object sender, RoutedEventArgs e)
    {
        SetCurrentView("settings");
    }
}

