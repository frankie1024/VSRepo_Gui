using System.Text.Json;
using System.IO;

namespace VSRepo_Gui.Services;

public sealed class AppStateService
{
    private static readonly string StateDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VSRepo_Gui");
    private static readonly string StatePath = Path.Combine(StateDirectory, "state.json");
    private static readonly JsonSerializerOptions SaveOptions = new() { WriteIndented = true };

    public sealed class AppState
    {
        public string PythonPath { get; set; } = string.Empty;
        public string Target { get; set; } = "win64";
        public string StatusFilter { get; set; } = "All";
        public string CategoryFilter { get; set; } = "All Categories";
        public string SearchText { get; set; } = string.Empty;
        public string SelectedIdentifier { get; set; } = string.Empty;
        public double Width { get; set; } = 1520;
        public double Height { get; set; } = 940;
        public double Left { get; set; } = double.NaN;
        public double Top { get; set; } = double.NaN;
        public bool Maximized { get; set; }
    }

    public AppState Load()
    {
        try
        {
            if (!File.Exists(StatePath))
            {
                return new AppState();
            }

            var json = File.ReadAllText(StatePath);
            return JsonSerializer.Deserialize<AppState>(json) ?? new AppState();
        }
        catch
        {
            return new AppState();
        }
    }

    public void Save(AppState state)
    {
        try
        {
            Directory.CreateDirectory(StateDirectory);
            var json = JsonSerializer.Serialize(state, SaveOptions);
            File.WriteAllText(StatePath, json);
        }
        catch
        {
        }
    }
}

