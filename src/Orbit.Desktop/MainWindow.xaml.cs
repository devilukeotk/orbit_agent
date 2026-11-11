using System.Net.Http;
using System.Net.Http.Json;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Orbit.Contracts;
using Orbit.Core;
using Orbit.Infrastructure;
using Orbit.WindowsAgent;
using Forms = System.Windows.Forms;

namespace Orbit.Desktop;

public partial class MainWindow : Window
{
    private readonly HttpClient _client = new() { BaseAddress = new Uri("http://localhost:5080") };
    private readonly Forms.NotifyIcon _trayIcon;
    private readonly Voice _voice = new();
    private PetWindow? _desktopPet;
    private string? _configurationDirectory;
    private TaskCompletionSource? _release;
    private CancellationTokenSource? _developerCancellation;
    private Task? _developerTask;
    private bool _busy;
    private bool _isExiting;
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Orbit", "desktop.json");

    public MainWindow()
    {
        InitializeComponent();
        Conversation.Text = "Orbit is ready. Try: open Notepad\n";
        Input.KeyDown += Input_KeyDown;
        PreviewKeyDown += Window_PreviewKeyDown;
        PreviewKeyUp += Window_PreviewKeyUp;
        Deactivated += (_, _) => _release?.TrySetResult();
        Closing += MainWindow_Closing;
        _trayIcon = CreateTrayIcon();
        LoadSettings();
        VoiceLanguage.SelectionChanged += (_, _) => SaveSettings();
        SpeakReplies.Checked += (_, _) => SaveSettings();
        SpeakReplies.Unchecked += (_, _) => SaveSettings();
        SetState("Idle");
    }

    private async void Send_Click(object sender, RoutedEventArgs e) => await SendAsync();

    private async void BuildAndTest_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;

        var picker = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select a .NET solution",
            Filter = ".NET solutions (*.sln;*.slnx)|*.sln;*.slnx",
            CheckFileExists = true
        };
        if (picker.ShowDialog(this) != true)
            return;

        var solution = picker.FileName;
        var workspace = Path.GetDirectoryName(solution);
        var approval = System.Windows.MessageBox.Show(this,
            $"Open this workspace in VS Code and run dotnet build and dotnet test for {Path.GetFileName(solution)}?\n\n{workspace}\n\nBuilds and tests can execute code from the solution.",
            "Approve workspace run", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (approval != MessageBoxResult.Yes)
            return;

        _developerTask = RunDeveloperWorkflowAsync(solution);
        await _developerTask;
        _developerTask = null;
    }

    private async Task RunDeveloperWorkflowAsync(string solution)
    {
        using var cancellation = new CancellationTokenSource();
        _developerCancellation = cancellation;
        _busy = true;
        BuildAndTest.IsEnabled = false;
        StopDeveloperWorkflow.IsEnabled = true;
        SetState("Thinking");
        try
        {
            var workflow = new DeveloperWorkflow(GetApplicationCatalog());
            var succeeded = await workflow.RunAsync(solution, new Progress<string>(Append), cancellation.Token);
            Append(succeeded ? "Orbit: Build and test completed." : "Orbit: The workflow stopped after a failed command.");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Append("Orbit: Developer workflow stopped.");
        }
        catch (Exception error) when (error is ApplicationCatalogException or IOException or UnauthorizedAccessException
            or ArgumentException or TimeoutException or InvalidOperationException or System.ComponentModel.Win32Exception
            or AggregateException)
        {
            Append($"Orbit: Developer workflow failed. {error.Message}");
        }
        finally
        {
            _developerCancellation = null;
            StopDeveloperWorkflow.IsEnabled = false;
            BuildAndTest.IsEnabled = true;
            _busy = false;
            SetState("Idle");
        }
    }

    private void StopDeveloperWorkflow_Click(object sender, RoutedEventArgs e)
        => RequestDeveloperStop();

    private void RequestDeveloperStop()
    {
        StopDeveloperWorkflow.IsEnabled = false;
        _developerCancellation?.Cancel();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select the Orbit Server configuration folder",
            InitialDirectory = _configurationDirectory ?? FindConfigurationDirectory()
        };
        if (picker.ShowDialog(this) != true)
            return;

        _configurationDirectory = picker.FolderName;
        try
        {
            new ConfigurationWindow(picker.FolderName) { Owner = this }.ShowDialog();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            System.Windows.MessageBox.Show(this, error.Message, "Orbit settings");
        }
    }

    private static string FindConfigurationDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var server = Path.Combine(directory.FullName, "Orbit.Server");
            if (File.Exists(Path.Combine(server, "Orbit.Server.csproj")))
                return server;
        }

        return AppContext.BaseDirectory;
    }

    private string ConfigurationDirectory => _configurationDirectory ?? FindConfigurationDirectory();

    private AuthorizedApplicationCatalog GetApplicationCatalog() =>
        new(Path.Combine(ConfigurationDirectory, "authorized-applications.json"));

    private async void Input_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            await SendAsync();
        }
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.F8)
            return;
        e.Handled = true;
        if (!e.IsRepeat)
            _ = TalkAsync();
    }

    private void Window_PreviewKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.F8)
        {
            e.Handled = true;
            _release?.TrySetResult();
        }
    }

    private void TalkDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (!_busy && PushToTalk.CaptureMouse())
            _ = TalkAsync();
    }

    private void TalkUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        PushToTalk.ReleaseMouseCapture();
        _release?.TrySetResult();
    }

    private void TalkCaptureLost(object sender, System.Windows.Input.MouseEventArgs e) => _release?.TrySetResult();

    private async Task TalkAsync()
    {
        if (_busy)
            return;
        if (!_voice.IsConfigured)
        {
            Append("Orbit: Install whisper.cpp and a multilingual model to enable voice. See the README.");
            PushToTalk.ReleaseMouseCapture();
            return;
        }

        _busy = true;
        _release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var language = (string)((System.Windows.Controls.ComboBoxItem)VoiceLanguage.SelectedItem).Tag;
        try
        {
            SetState("Listening");
            var message = await _voice.ListenAsync(_release.Task, language, () => SetState("Thinking"));
            if (string.IsNullOrWhiteSpace(message))
            {
                Append("Orbit: No speech recognized.");
                return;
            }

            var reply = await SubmitAsync(message);
            if (SpeakReplies.IsChecked == true)
            {
                SetState("Speaking");
                try
                {
                    await _voice.SpeakAsync(reply, language);
                }
                catch (Exception error) when (error is InvalidOperationException or System.Runtime.InteropServices.COMException)
                {
                    Append($"Orbit: Speech playback unavailable. Check the installed Windows voice for {language}.");
                }
            }
        }
        catch (HttpRequestException)
        {
            Append("Orbit: The local Orbit Server is unavailable.");
        }
        catch (Exception error) when (error is InvalidOperationException or TimeoutException
            or ArgumentException or DllNotFoundException or System.Text.Json.JsonException or OperationCanceledException
            or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or NAudio.MmException)
        {
            Append("Orbit: Voice interaction failed. Check your microphone and speech configuration.");
        }
        finally
        {
            _release = null;
            PushToTalk.ReleaseMouseCapture();
            _busy = false;
            SetState("Idle");
        }
    }

    private async Task SendAsync()
    {
        var message = Input.Text.Trim();
        if (_busy || message.Length == 0)
            return;

        Input.Clear();
        _busy = true;
        try
        {
            await SubmitAsync(message);
        }
        catch (HttpRequestException)
        {
            Append("Orbit: The local Orbit Server is unavailable.");
        }
        catch (Exception error) when (error is System.Text.Json.JsonException or OperationCanceledException)
        {
            Append("Orbit: No usable response from the local Orbit Server.");
        }
        finally
        {
            _busy = false;
            SetState("Idle");
        }
    }

    private async Task<string> SubmitAsync(string message)
    {
        Append($"You: {message}");
        SetState("Thinking");
        if (ParseWindowSwitch(message) is { } windowCommand)
        {
            var windowReply = await SwitchWindowAsync(windowCommand.Query, windowCommand.Thai);
            Append($"Orbit: {windowReply}");
            return windowReply;
        }

        using var taskResponse = await _client.PostAsJsonAsync("/api/agent/run", new { input = message });
        string reply;
        if (taskResponse.StatusCode == System.Net.HttpStatusCode.NoContent)
        {
            using var response = await _client.PostAsJsonAsync("/api/chat", new { message });
            reply = response.IsSuccessStatusCode
                ? (await response.Content.ReadFromJsonAsync<ChatReply>())?.Text ?? "No chat reply."
                : "Chat request failed.";
        }
        else
        {
            var task = taskResponse.IsSuccessStatusCode
                ? await taskResponse.Content.ReadFromJsonAsync<AgentTask>()
                : null;
            reply = task is { Failure: AgentTaskFailure.NoAuthorizedApplication } &&
                    AppLaunchIntent.TryParse(task.Input) is { } query
                ? await ApproveDiscoveredApplicationAsync(task.Id, query, message.StartsWith("เปิด", StringComparison.Ordinal))
                : task?.Message ?? "Application request failed.";
        }

        Append($"Orbit: {reply}");
        return reply;
    }

    private async Task<string> ApproveDiscoveredApplicationAsync(Guid taskId, string query, bool thai)
    {
        try
        {
            var apps = await Task.Run(() => StartMenuApplications.Find(query));
            if (apps.Count == 0)
                return thai ? $"ไม่พบแอปที่เปิดได้จาก Start Menu สำหรับ {query}" : $"No launchable Start Menu app matched {query}.";

            var approval = new ApplicationApproval(apps, thai) { Owner = this };
            if (approval.ShowDialog() != true || approval.Selection is not { } selected)
                return thai ? "ยกเลิกการเปิดแอปแล้ว" : "Application launch cancelled.";

            var catalog = GetApplicationCatalog();
            var remember = approval.RememberPermission;
            if (remember)
                await Task.Run(() => catalog.Authorize(selected));

            var result = WindowsApplicationLauncher.LaunchApproved(selected, CancellationToken.None);
            var message = thai
                ? result.Succeeded ? $"เปิด {selected.DisplayName} แล้ว" : $"เปิด {selected.DisplayName} ไม่สำเร็จ"
                : result.Message;
            if (remember)
                message += thai ? " บันทึกสิทธิ์แล้ว" : " Permission saved.";

            try
            {
                var connection = new SqliteConnectionStringBuilder
                {
                    DataSource = Path.Combine(ConfigurationDirectory, "data", "orbit.db"),
                    Mode = SqliteOpenMode.ReadWrite,
                    DefaultTimeout = 2
                };
                var options = new DbContextOptionsBuilder<OrbitDbContext>().UseSqlite(connection.ToString()).Options;
                var audit = new SqliteAuditLog(new PooledDbContextFactory<OrbitDbContext>(options));
                await audit.WriteAsync("windows.app.launch", result.Succeeded ? "completed" : "failed",
                    $"{(remember ? "Persistent approval" : "One-time approval")} for {selected.DisplayName}.",
                    taskId, CancellationToken.None);
            }
            catch (Exception error) when (error is SqliteException or DbUpdateException)
            {
                message += thai ? " บันทึกผลการเปิดแอปใน audit log ไม่สำเร็จ" : " The launch could not be written to the audit log.";
            }

            return message;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException
            or ApplicationCatalogException or System.Runtime.InteropServices.COMException or System.ComponentModel.Win32Exception)
        {
            return thai ? "ค้นหาหรือบันทึกสิทธิ์เปิดแอปไม่สำเร็จ" : $"Could not discover or authorize the application. {error.Message}";
        }
    }

    private static (string Query, bool Thai)? ParseWindowSwitch(string message)
    {
        var command = message.Trim().TrimEnd('.', '!', '?', '。').TrimEnd();
        var thai = command.StartsWith("สลับ", StringComparison.Ordinal);
        var suffixes = thai ? new[] { "ครับ", "ค่ะ", "ให้หน่อย", "หน่อย" } : new[] { " please" };
        foreach (var suffix in suffixes)
        {
            if (command.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                command = command[..^suffix.Length].TrimEnd();
        }

        foreach (var prefix in new[] { "switch to ", "focus ", "สลับไปที่", "สลับไป" })
        {
            if (command.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return (command[prefix.Length..].Trim(), thai);
        }
        return null;
    }

    private async Task<string> SwitchWindowAsync(string query, bool thai)
    {
        if (query.Length == 0)
            return thai ? "ระบุชื่อแอปหรือหน้าต่างที่ต้องการสลับไป" : "Enter an app or window name to switch to.";

        IReadOnlyCollection<AuthorizedApplication> aliases;
        try
        {
            aliases = await GetApplicationCatalog().LoadAsync(CancellationToken.None);
        }
        catch (ApplicationCatalogException)
        {
            aliases = [];
        }

        var windows = await Task.Run(() => ApplicationWindows.Search(query, aliases));
        if (windows.Count == 0)
            return thai ? $"ไม่พบหน้าต่างที่เปิดอยู่สำหรับ {query}" : $"No open window matched {query}.";

        var target = windows[0];
        if (windows.Count > 1)
        {
            var picker = new WindowPicker(windows, thai) { Owner = this };
            if (picker.ShowDialog() != true || picker.Selection is not { } selection)
                return thai ? "ยกเลิกการสลับหน้าต่างแล้ว" : "Window switch cancelled.";
            target = selection;
        }

        return await ApplicationWindows.FocusAsync(target) switch
        {
            WindowFocusResult.Focused => thai ? $"สลับไปที่ {target.Title} แล้ว" : $"Switched to {target.Title}.",
            WindowFocusResult.Closed => thai ? "หน้าต่างที่เลือกปิดไปแล้ว" : "The selected window closed.",
            _ => thai ? "Windows ไม่ได้ย้ายโฟกัสไปยังหน้าต่างที่เลือก" : "Windows did not move focus to the selected window."
        };
    }

    private void SetState(string state)
    {
        AvatarState.Text = state;
        _desktopPet?.SetState(state);
        var animation = new System.Windows.Media.Animation.DoubleAnimation(
            1, state == "Idle" ? 1.05 : 1.18, new Duration(TimeSpan.FromMilliseconds(500)))
        {
            AutoReverse = true,
            RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever
        };
        PetScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, animation);
    }

    private void ShowPet_Click(object sender, RoutedEventArgs e)
    {
        if (_desktopPet is not null)
        {
            _desktopPet.Close();
            return;
        }

        _desktopPet = new PetWindow(ShowFromTray);
        _desktopPet.Closed += (_, _) => _desktopPet = null;
        _desktopPet.SetState(AvatarState.Text);
        _desktopPet.Show();
    }

    private void LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsPath) &&
                JsonSerializer.Deserialize<DesktopSettings>(File.ReadAllText(SettingsPath)) is { } settings)
            {
                VoiceLanguage.SelectedIndex = settings.UseEnglish ? 1 : 0;
                SpeakReplies.IsChecked = settings.SpeakReplies;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            Append("Orbit: Desktop settings could not be loaded.");
        }
    }

    private void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            var temporary = SettingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(
                new DesktopSettings(VoiceLanguage.SelectedIndex == 1, SpeakReplies.IsChecked == true)));
            File.Move(temporary, SettingsPath, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Append("Orbit: Desktop settings could not be saved.");
        }
    }

    private void Append(string line)
    {
        Conversation.AppendText(line + Environment.NewLine);
        Conversation.ScrollToEnd();
    }

    private Forms.NotifyIcon CreateTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Show Orbit", null, (_, _) => ShowFromTray());
        var stop = menu.Items.Add("Emergency Stop", null, (_, _) => RequestDeveloperStop());
        menu.Opening += (_, _) => stop.Enabled = _developerCancellation is { IsCancellationRequested: false };
        menu.Items.Add("Exit", null, async (_, _) =>
        {
            if (_isExiting)
                return;
            _isExiting = true;
            RequestDeveloperStop();
            if (_developerTask is { } workflow)
                await workflow;
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            System.Windows.Application.Current.Shutdown();
        });

        var trayIcon = new Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "Orbit",
            ContextMenuStrip = menu,
            Visible = true
        };
        trayIcon.DoubleClick += (_, _) => ShowFromTray();
        return trayIcon;
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_isExiting)
            return;

        e.Cancel = true;
        Hide();
        _trayIcon.ShowBalloonTip(1500, "Orbit", "Orbit is still running in the system tray.", Forms.ToolTipIcon.Info);
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }
}

internal sealed record ChatReply(string Text);
internal sealed record DesktopSettings(bool UseEnglish, bool SpeakReplies);
