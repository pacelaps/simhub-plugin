using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using PaceLaps.Plugin.Core;

namespace PaceLaps.Plugin
{
    /// <summary>The plugin's page in SimHub: connect with an overlay URL, sound, and test calls.</summary>
    public partial class SettingsControl : UserControl
    {
        private readonly PaceLapsPlugin _plugin;
        private bool _loading;
        private UIElement? _disconnect;

        public SettingsControl(PaceLapsPlugin plugin)
        {
            _plugin = plugin;
            InitializeComponent();

            _loading = true;
            VolumeSlider.Value = plugin.Settings.Volume;
            _loading = false;
            VersionText.Text = "PaceLaps plugin " + Host.Version;

            Loaded += (_, _) =>
            {
                Host.Changed += OnChanged;
                Show(Host.State);
            };
            Unloaded += (_, _) => Host.Changed -= OnChanged;
        }

        private void OnChanged(DriverState state) => Dispatcher.BeginInvoke(new System.Action(() => Show(state)));

        private void Show(DriverState s)
        {
            var site = Host.Site;
            var car = s.Device.Car == null ? "" : ", car #" + s.Device.Car;
            var session = s.SessionName == null ? "no session open" : s.SessionName;
            (StatusText.Text, DetailText.Text) = s.Status switch
            {
                ConnectionStatus.NotSetUp => ("Not connected", "Paste an overlay URL to get race control's calls."),
                ConnectionStatus.Connecting => ("Connecting…", s.Problem ?? site),
                ConnectionStatus.Connected => ("Connected", $"{site}{car}: {session}"),
                ConnectionStatus.Offline => ("Offline", "Calls may be missing until it reconnects. It retries on its own."),
                _ => ("Not connected", s.Problem ?? "This overlay URL no longer works. Paste a current one from your driver page."),
            };
            if (_disconnect != null)
            {
                _disconnect.IsEnabled = s.Status != ConnectionStatus.NotSetUp;
            }
        }

        // SimHub's controls get no x:Name: the generated code would look for them under this namespace.
        private void DisconnectButton_Loaded(object sender, RoutedEventArgs e)
        {
            _disconnect = (UIElement)sender;
            Show(Host.State);
        }

        private void SoundBox_Loaded(object sender, RoutedEventArgs e)
        {
            _loading = true;
            ((ToggleButton)sender).IsChecked = _plugin.Settings.Sound;
            _loading = false;
        }

        private void Connect_Click(object sender, RoutedEventArgs e)
        {
            var url = UrlBox.Password;
            var problem = OverlayLink.Parse(url, out var why) == null ? why : Host.Use(url);
            UrlProblem.Text = problem ?? "";
            UrlProblem.Visibility = problem == null ? Visibility.Collapsed : Visibility.Visible;
            if (problem == null)
            {
                _plugin.Settings.SetOverlayUrl(url);
                _plugin.Save();
                UrlBox.Clear();
            }
        }

        private void Disconnect_Click(object sender, RoutedEventArgs e)
        {
            Host.Stop();
            _plugin.Settings.SetOverlayUrl(null);
            _plugin.Save();
        }

        private void Sound_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading)
            {
                return;
            }

            _plugin.Settings.Sound = ((ToggleButton)sender).IsChecked == true;
            _plugin.Save();
        }

        private void Volume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading)
            {
                return;
            }

            _plugin.Settings.Volume = (int)VolumeSlider.Value;
            _plugin.Save();
        }

        private void TestSound_Click(object sender, RoutedEventArgs e) =>
            TonePlayer.Play(Tones.For("safety_car", "safety_car"), _plugin.Settings.Volume);

        private void Preview_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: string kind })
            {
                _plugin.ShowPreview(kind);
            }
        }
    }
}
