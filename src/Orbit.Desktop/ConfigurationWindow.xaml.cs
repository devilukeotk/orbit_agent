using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using Orbit.Contracts;
using Orbit.WindowsAgent;

namespace Orbit.Desktop;

public partial class ConfigurationWindow : Window
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ConfigurationFile[] _files;

    public ConfigurationWindow(string directory)
    {
        InitializeComponent();
        FolderLabel.Text = directory;
        _files =
        [
            new("AI profiles", Path.Combine(directory, "ai-providers.json"),
                "Set activeProfile and edit profile details. Enter credential names in keyRef and store keys in your configured secret source.",
                ValidateProfiles),
            new("Authorized applications", Path.Combine(directory, "authorized-applications.json"),
                "Set the application ID, display name, executable, fixed arguments, and optional spoken aliases.",
                AuthorizedApplicationCatalog.ValidateConfiguration)
        ];
        Files.ItemsSource = _files;
        Files.SelectedIndex = 0;
    }

    private static void ValidateProfiles(string json)
    {
        var configuration = JsonSerializer.Deserialize<AiProfileConfiguration>(json, JsonOptions)
            ?? throw new InvalidDataException("Enter an AI profile configuration object.");
        configuration.ValidateActiveProfile();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is ConfigurationFile file)
            file.Save();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_files.Any(file => file.HasChanges) &&
            System.Windows.MessageBox.Show(this, "Close settings and discard unsaved edits?", "Orbit settings",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            e.Cancel = true;
    }

    private sealed class ConfigurationFile : INotifyPropertyChanged
    {
        private readonly string _path;
        private readonly Action<string> _validate;
        private string? _saved;
        private string _initialText;
        private string _status = "";

        public ConfigurationFile(string name, string path, string instructions, Action<string> validate)
        {
            Name = name;
            Instructions = instructions;
            _path = path;
            _validate = validate;
            _saved = LocalConfigurationFile.Read(path);
            var examplePath = Path.ChangeExtension(path, ".example.json");
            _initialText = _saved ?? LocalConfigurationFile.Read(examplePath) ?? "{}";
            Text = _initialText;
            if (_saved is null)
                _status = "Save to create " + Path.GetFileName(path) + ".";
        }

        public string Name { get; }
        public string Instructions { get; }
        public string Text { get; set; }
        public string Status => _status;
        public bool HasChanges => !string.Equals(Text, _initialText, StringComparison.Ordinal);
        public event PropertyChangedEventHandler? PropertyChanged;

        public void Save()
        {
            try
            {
                _validate(Text);
                LocalConfigurationFile.Update(_path, current =>
                {
                    if (!string.Equals(_saved, current, StringComparison.Ordinal))
                        throw new InvalidDataException("The file changed on disk. Copy your edits and reopen settings to load the latest version.");
                    return Text;
                });

                _saved = _initialText = Text;
                _status = "Saved. Orbit Server reads the updated file on its next request.";
            }
            catch (JsonException error)
            {
                _status = $"Check the JSON near line {error.LineNumber.GetValueOrDefault() + 1}.";
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException
                or InvalidDataException or ApplicationCatalogException)
            {
                _status = error.Message;
            }

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
        }

    }
}
