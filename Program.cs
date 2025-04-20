
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using TagLib;
using System.Text;
using Spectre.Console;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using System.IO;
using System.Linq;
using SixLabors.ImageSharp.PixelFormats;
using Newtonsoft.Json.Linq;
public class AdvancedMusicPlayer

{
    private static WaveOutEvent? outputDevice;
    private static AudioFileReader? audioFile;
    private static List<string> playlist = new List<string>();
    private static int currentTrackIndex = 0;
    private static bool isPlaying = false;
    private static float volume = 1.0f;
    private static EqualizerBand[]? equalizer;
    private static HttpClient httpClient = new HttpClient();
    private static readonly string lyricsApiKey = "YOUR_API_KEY"; // Get from lyrics service
    private static readonly string defaultMusicPath = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);

    class EqualizerBand
    {
        public float Frequency { get; set; }
        public float Gain { get; set; }
        
    }

    static async Task Main(string[] args)
    {
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
            InitializeEqualizer();
            SetupConsoleSettings();

            if (args.Length == 0)
            {
                await BrowseForFiles();
            }
            else
            {
                playlist.AddRange(args.Where(IsValidAudioFile));
            }

            if (playlist.Count > 0)
            {
                await RunPlayerInterface();
            }
            else
            {
                AnsiConsole.MarkupLine("[red]No valid audio files found.[/]");
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything);
        }
    }

    static void SetupConsoleSettings()
    {
        Console.Title = "Advanced Music Player";
        Console.CursorVisible = false;
        AnsiConsole.Markup("[bold blue]Advanced Music Player[/]\n");
    }

    static void InitializeEqualizer()
    {
        equalizer = new EqualizerBand[]
        {
            new EqualizerBand { Frequency = 60, Gain = 0 },
            new EqualizerBand { Frequency = 170, Gain = 0 },
            new EqualizerBand { Frequency = 310, Gain = 0 },
            new EqualizerBand { Frequency = 600, Gain = 0 },
            new EqualizerBand { Frequency = 1000, Gain = 0 },
            new EqualizerBand { Frequency = 3000, Gain = 0 },
            new EqualizerBand { Frequency = 6000, Gain = 0 },
            new EqualizerBand { Frequency = 12000, Gain = 0 },
            new EqualizerBand { Frequency = 14000, Gain = 0 },
            new EqualizerBand { Frequency = 16000, Gain = 0 }
        };

    }

    static bool IsValidAudioFile(string filePath)
    {
        try
        {
            var validExtensions = new[] { ".mp3", ".wav", ".flac", ".m4a", ".wma", ".aac" };
            if (!validExtensions.Contains(Path.GetExtension(filePath).ToLower()))
                return false;

            // Try to read the file with TagLib# to verify it's valid
            using (var file = TagLib.File.Create(filePath))
            {
                return file.Properties != null && file.Properties.Duration.TotalSeconds > 0;
            }
        }
        catch
        {
            return false;
        }
    }

    static async Task BrowseForFiles()
    {
        try
        {
            var files = Directory.GetFiles(defaultMusicPath, "*.*", SearchOption.AllDirectories)
                .Where(IsValidAudioFile)
                .ToList();

            if (files.Count == 0)
            {
                Console.WriteLine("No valid audio files found in music directory.");
                return;
            }

            Console.WriteLine("\nAvailable audio files:");
            for (int i = 0; i < files.Count; i++)
            {
                Console.WriteLine($"{i + 1}. {Path.GetFileName(files[i])}");
            }

            Console.WriteLine("\nEnter file number to play (1-" + files.Count + "):");
            if (int.TryParse(Console.ReadLine(), out int selectedIndex) && 
                selectedIndex >= 1 && 
                selectedIndex <= files.Count)
            {
                playlist.Clear(); // Clear existing playlist
                playlist.Add(files[selectedIndex - 1]); // Add selected file
                currentTrackIndex = 0; // Reset to first track
                await PlayCurrentTrack(); // Start playing immediately
            }
            else
            {
                Console.WriteLine("\nInvalid selection.");
                await Task.Delay(1000);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error browsing files: {ex.Message}");
            await Task.Delay(2000);
        }
    }

    static async Task RunPlayerInterface()
    {
        while (true)
        {
            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("\nPlayer Controls")
                    .HighlightStyle("green")
                    .AddChoices(new[]
                    {
                        "⏯️ Play/Pause",
                        "⏭️ Next Track",
                        "⏮️ Previous Track",
                        "🔊 Adjust Volume",
                        "🎚️ Equalizer",
                        "📝 Show Playlist",
                        "➕ Add Files",
                        "❌ Exit"
                    }));

            await HandlePlayerControls(choice);
        }
    }
    static async Task HandlePlayerControls(string choice)
    {
        switch (choice)
        {
            case "⏯️ Play/Pause":
                TogglePlayPause();
                break;
            case "⏭️ Next Track":
                await PlayNextTrack();
                break;
            case "⏮️ Previous Track":
                await PlayPreviousTrack();
                break;
            case "🔊 Adjust Volume":
                AdjustVolume();
                break;
            case "🎚️ Equalizer":
                await AdjustEqualizer();
                break;
            case "📝 Show Playlist":
                ShowPlaylist();
                break;
            case "➕ Add Files":
                await BrowseForFiles();
                break;
            case "❌ Exit":
                Cleanup();
                Environment.Exit(0);
                break;
        }
    }

    static async Task PlayCurrentTrack()
    {
        if (currentTrackIndex >= playlist.Count) return;

        try
        {
            Cleanup();
            Console.Clear();

            string filePath = playlist[currentTrackIndex];
            var file = TagLib.File.Create(filePath);

            // Display track information
            DisplayTrackInfo(file);
            await DisplayAlbumArt(file);
            await DisplayLyrics(file);

            // Add space for progress bar
            Console.WriteLine("\n");
            int progressBarLine = Console.CursorTop;
            Console.WriteLine(); // Extra line for controls below progress bar

            audioFile = new AudioFileReader(filePath);
            outputDevice = new WaveOutEvent();

            if (equalizer != null)
            {
                var equalizedSample = new EqualizerSampleProvider(audioFile, equalizer);
                outputDevice.Init(equalizedSample);
            }
            else
            {
                outputDevice.Init(audioFile);
            }

            outputDevice.Volume = volume;
            outputDevice.Play();
            isPlaying = true;

            // Start monitoring playback
            await Task.Factory.StartNew(async () => await MonitorPlayback(progressBarLine), 
                TaskCreationOptions.LongRunning);
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteException(ex);
            await Task.Delay(2000);
        }
    }

    static void DisplayTrackInfo(TagLib.File file)
    {
        Console.Clear(); // Clear the console first
        Console.SetCursorPosition(0, 0);

        var panel = new Panel(new Table()
            .AddColumn("Property")
            .AddColumn("Value")
            .AddRow("Title", file.Tag.Title ?? Path.GetFileName(file.Name))
            .AddRow("Artist", file.Tag.FirstPerformer ?? "Unknown")
            .AddRow("Album", file.Tag.Album ?? "Unknown")
            .AddRow("Year", file.Tag.Year.ToString() ?? "Unknown")
            .AddRow("Genre", string.Join(", ", file.Tag.Genres) ?? "Unknown"))
        {
            Header = new PanelHeader("Track Information"),
            Border = BoxBorder.Rounded,
            Expand = false
        };

        AnsiConsole.Write(panel);
        Console.WriteLine(); // Add some space after the panel
    }

    static void DisplayImageAsAscii(Image<Rgba32> image)
    {
        const string asciiChars = " .:-=+*#%@";
        
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                var pixel = image[x, y];
                var brightness = (pixel.R + pixel.G + pixel.B) / 3;
                var charIndex = (int)(brightness / 255.0 * (asciiChars.Length - 1));
                Console.Write(asciiChars[charIndex]);
            }
            Console.WriteLine();
        }
    }

    static async Task DisplayAlbumArt(TagLib.File file)
    {
        if (file.Tag.Pictures.Length > 0)
        {
            var picture = file.Tag.Pictures[0];
            using var image = Image.Load<Rgba32>(picture.Data.Data);

            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new SixLabors.ImageSharp.Size(40, 20),
                Mode = ResizeMode.Max
            }));

            DisplayImageAsAscii(image);
        }
    }

    static async Task DisplayLyrics(TagLib.File file)
    {
        if (!string.IsNullOrEmpty(file.Tag.Lyrics))
        {
            var lyricsPanel = new Panel(file.Tag.Lyrics)
            {
                Header = new PanelHeader("Lyrics"),
                Border = BoxBorder.Rounded,
                Expand = true
            };
            AnsiConsole.Write(lyricsPanel);
        }
    }


    static async Task MonitorPlayback(int progressBarLine)
    {
        const int progressBarWidth = 50;
        Console.CursorVisible = false;

        while (isPlaying && audioFile != null && outputDevice != null)
        {
            try
            {
                var progress = (audioFile.Position * 100.0) / audioFile.Length;
                var currentTime = TimeSpan.FromSeconds(audioFile.CurrentTime.TotalSeconds);
                var totalTime = TimeSpan.FromSeconds(audioFile.TotalTime.TotalSeconds);

                // Save current cursor position
                int currentTop = Console.CursorTop;

                // Move to progress bar line and clear it
                Console.SetCursorPosition(0, progressBarLine);
                Console.Write(new string(' ', Console.WindowWidth));
                Console.SetCursorPosition(0, progressBarLine);

                // Build progress bar
                int filled = (int)((progress * progressBarWidth) / 100);
                string progressBar = new string('█', filled) + new string('─', progressBarWidth - filled);

                // Format and write the progress line
                string progressLine = $"{currentTime:mm\\:ss}/{totalTime:mm\\:ss} [{progressBar}] {progress:F1}%";
                Console.Write(progressLine);

                // Restore cursor position
                Console.SetCursorPosition(0, currentTop);

                await Task.Delay(100);

                if (outputDevice.PlaybackState == PlaybackState.Stopped)
                {
                    await PlayNextTrack();
                    break;
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error updating progress: {ex.Message}[/]");
                break;
            }
        }

        Console.CursorVisible = true;
    }

    static void TogglePlayPause()
    {
        if (outputDevice != null)
        {
            if (outputDevice.PlaybackState == PlaybackState.Playing)
            {
                outputDevice.Pause();
                isPlaying = false;
            }
            else
            {
                outputDevice.Play();
                isPlaying = true;
            }
        }
    }

    static async Task PlayNextTrack()
    {
        if (currentTrackIndex < playlist.Count - 1)
        {
            currentTrackIndex++;
            await PlayCurrentTrack();
        }
    }

    static async Task PlayPreviousTrack()
    {
        if (currentTrackIndex > 0)
        {
            currentTrackIndex--;
            await PlayCurrentTrack();
        }
    }

    static void AdjustVolume()
    {
        Console.WriteLine($"Current volume: {volume:F2}");
        Console.WriteLine("Enter new volume (0.0 - 1.0):");
        
        if (float.TryParse(Console.ReadLine(), out float newVolume))
        {
            volume = Math.Clamp(newVolume, 0f, 1f);
            if (outputDevice != null)
            {
                outputDevice.Volume = volume;
            }
        }
    }

    static async Task AdjustEqualizer()
    {
        if (equalizer == null) return;

        foreach (var band in equalizer)
        {
            Console.WriteLine($"Current gain for {band.Frequency}Hz: {band.Gain:F1}dB");
            Console.WriteLine("Enter new gain (-12.0 to +12.0):");
            
            if (float.TryParse(Console.ReadLine(), out float newGain))
            {
                band.Gain = Math.Clamp(newGain, -12f, 12f);
            }
        }

        if (audioFile != null)
        {
            await PlayCurrentTrack(); // Restart track to apply new EQ settings
        }
    }

    static void ShowPlaylist()
    {
        var table = new Table()
            .AddColumn("Index")
            .AddColumn("Title")
            .AddColumn("Artist")
            .AddColumn("Duration");

        for (int i = 0; i < playlist.Count; i++)
        {
            var file = TagLib.File.Create(playlist[i]);
            table.AddRow(
                (i == currentTrackIndex ? "▶ " : "") + i.ToString(),
                file.Tag.Title ?? Path.GetFileName(playlist[i]),
                file.Tag.FirstPerformer ?? "Unknown",
                file.Properties.Duration.ToString(@"mm\:ss"));
        }

        AnsiConsole.Write(table);
        Console.ReadKey();
    }

    static void Cleanup()
    {
        isPlaying = false;

        if (outputDevice != null)
        {
            try
            {
                outputDevice.Stop();
                outputDevice.Dispose();
            }
            catch { }
            outputDevice = null;
        }

        if (audioFile != null)
        {
            try
            {
                audioFile.Dispose();
            }
            catch { }
            audioFile = null;
        }
    }

    private class EqualizerSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider source;
        private readonly EqualizerBand[] bands;

        public EqualizerSampleProvider(ISampleProvider source, EqualizerBand[] bands)
        {
            this.source = source;
            this.bands = bands;
        }

        public WaveFormat WaveFormat => source.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            int samplesRead = source.Read(buffer, offset, count);
            
            // Apply equalizer bands
            for (int i = offset; i < offset + samplesRead; i++)
            {
                float sample = buffer[i];
                foreach (var band in bands)
                {
                    sample *= (float)Math.Pow(10, band.Gain / 20);
                }
                buffer[i] = sample;
            }
            
            return samplesRead;
        }
    }

}


