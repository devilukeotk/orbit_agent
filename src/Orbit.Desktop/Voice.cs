using System.Diagnostics;
using System.IO;
using System.Media;
using NAudio.Wave;
using Windows.Media.SpeechSynthesis;

namespace Orbit.Desktop;

internal sealed class Voice
{
    private static readonly string InstallDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Orbit", "whisper");
    private readonly string _executable = Environment.GetEnvironmentVariable("ORBIT_WHISPER_CLI")
        ?? Path.Combine(InstallDirectory, "Release", "whisper-cli.exe");
    private readonly string _model = Environment.GetEnvironmentVariable("ORBIT_WHISPER_MODEL")
        ?? Path.Combine(InstallDirectory, "ggml-base.bin");

    public bool IsConfigured => Path.IsPathFullyQualified(_executable) && Path.IsPathFullyQualified(_model)
        && File.Exists(_executable) && File.Exists(_model);

    public async Task<string> ListenAsync(Task released, string language, Action onTranscribing)
    {
        var workingDirectory = Directory.CreateTempSubdirectory("orbit-voice-");
        var recording = Path.Combine(workingDirectory.FullName, "input.wav");
        var transcript = Path.Combine(workingDirectory.FullName, "transcript");
        try
        {
            using (var microphone = new WaveInEvent { WaveFormat = new WaveFormat(16_000, 16, 1) })
            using (var wave = new WaveFileWriter(recording, microphone.WaveFormat))
            {
                var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                microphone.DataAvailable += (_, args) => wave.Write(args.Buffer, 0, args.BytesRecorded);
                microphone.RecordingStopped += (_, args) =>
                {
                    if (args.Exception is { } error)
                        stopped.TrySetException(error);
                    else
                        stopped.TrySetResult();
                };
                microphone.StartRecording();
                using var limit = new CancellationTokenSource();
                await Task.WhenAny(released, stopped.Task, Task.Delay(TimeSpan.FromSeconds(25), limit.Token));
                limit.Cancel();
                microphone.StopRecording();
                await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));
                if (wave.Length < 8_000)
                    return "";
            }

            onTranscribing();
            var startInfo = new ProcessStartInfo(_executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = workingDirectory.FullName
            };
            foreach (var argument in new[]
            {
                "-m", _model, "-f", recording, "-l", language == "th-TH" ? "th" : "en",
                "-ng", "-otxt", "-of", transcript
            })
                startInfo.ArgumentList.Add(argument);

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Whisper could not start.");
            var output = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null);
            var errors = process.StandardError.BaseStream.CopyToAsync(Stream.Null);
            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(90));
            }
            catch (TimeoutException)
            {
                if (!process.HasExited)
                    process.Kill();
                await process.WaitForExitAsync();
                throw;
            }
            finally
            {
                await Task.WhenAll(output, errors);
            }
            if (process.ExitCode != 0 || !File.Exists(transcript + ".txt"))
                throw new InvalidOperationException("Whisper could not transcribe the recording.");

            return (await File.ReadAllTextAsync(transcript + ".txt")).Trim();
        }
        finally
        {
            File.Delete(recording);
            File.Delete(transcript + ".txt");
            workingDirectory.Delete();
        }
    }

    public async Task SpeakAsync(string text, string language)
    {
        var voice = SpeechSynthesizer.AllVoices.FirstOrDefault(voice =>
            voice.Language.Equals(language, StringComparison.OrdinalIgnoreCase));
        if (voice is null)
            throw new InvalidOperationException($"Install a Windows text-to-speech voice for {language}.");

        using var synthesizer = new SpeechSynthesizer { Voice = voice };
        using var speech = await synthesizer.SynthesizeTextToStreamAsync(text);
        using var stream = speech.AsStreamForRead();
        using var player = new SoundPlayer(stream);
        await Task.Run(player.PlaySync);
    }
}
