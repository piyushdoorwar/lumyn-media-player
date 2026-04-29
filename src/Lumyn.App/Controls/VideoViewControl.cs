using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using LibVLCSharp.Avalonia;
using LibVLCSharp.Shared;

namespace Lumyn.App.Controls;

public partial class VideoViewControl : UserControl
{
    public static readonly StyledProperty<MediaPlayer?> MediaPlayerProperty =
        AvaloniaProperty.Register<VideoViewControl, MediaPlayer?>(nameof(MediaPlayer));

    private readonly VideoView _videoView = new()
    {
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch
    };

    public VideoViewControl()
    {
        InitializeComponent();
        this.FindControl<Grid>("VideoHost")?.Children.Add(_videoView);
    }

    public MediaPlayer? MediaPlayer
    {
        get => GetValue(MediaPlayerProperty);
        set => SetValue(MediaPlayerProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MediaPlayerProperty)
        {
            _videoView.MediaPlayer = MediaPlayer;
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
