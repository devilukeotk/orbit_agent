using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Orbit.WindowsAgent;

namespace Orbit.Desktop;

public partial class ApplicationApproval : Window
{
    private static readonly JsonSerializerOptions DisplayJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private readonly bool _thai;

    public ApplicationApproval(IReadOnlyList<AuthorizedApplication> candidates, bool thai)
    {
        _thai = thai;
        InitializeComponent();
        if (thai)
        {
            Title = "อนุญาตให้เปิดแอป";
            Instructions.Text = "เลือกแอปจาก Start Menu แล้วตรวจโปรแกรมและอาร์กิวเมนต์ก่อนอนุญาต";
            CancelButton.Content = "ยกเลิก";
            OpenOnce.Content = "เปิดครั้งนี้";
            Remember.Content = "อนุญาตไว้และเปิด";
        }

        Candidates.ItemsSource = candidates;
        if (candidates.Count == 1)
            Candidates.SelectedIndex = 0;
        Loaded += (_, _) => Candidates.Focus();
    }

    public AuthorizedApplication? Selection => Candidates.SelectedItem as AuthorizedApplication;
    public bool RememberPermission { get; private set; }

    private void Candidates_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var app = Selection;
        OpenOnce.IsEnabled = Remember.IsEnabled = app is not null;
        Details.Text = app is null ? "" : _thai
            ? $"โปรแกรม: {app.Executable}\nโฟลเดอร์ทำงาน: {app.WorkingDirectory}\nอาร์กิวเมนต์: {JsonSerializer.Serialize(app.Arguments, DisplayJson)}"
            : $"Executable: {app.Executable}\nWorking folder: {app.WorkingDirectory}\nArguments: {JsonSerializer.Serialize(app.Arguments, DisplayJson)}";
    }

    private void OpenOnce_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Remember_Click(object sender, RoutedEventArgs e)
    {
        RememberPermission = true;
        DialogResult = true;
    }
}
