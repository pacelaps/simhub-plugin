using System;
using System.Windows.Controls;
using System.Windows.Media;
using GameReaderCommon;
using PaceLaps.Plugin.Core;
using SimHub.Plugins;

namespace PaceLaps.Plugin
{
    /// <summary>
    /// The SimHub side: binds the core's <see cref="PluginView"/> to properties (PaceLapsPlugin.*), raises events on
    /// calls, plays their tones and shows the settings page. No race logic: what is shown comes from race control.
    /// </summary>
    [PluginDescription("PaceLaps race control calls on your overlays, dashboards and LEDs, with a tone for each call.")]
    [PluginAuthor("PaceLaps")]
    [PluginName("PaceLaps")]
    public class PaceLapsPlugin : IPlugin, IDataPlugin, IWPFSettingsV2
    {
        /// <summary>A view this old is computed again (ages and the time left move).</summary>
        private const long ViewMaxAgeMs = 100;

        private const long PreviewMs = 8_000;

        private readonly object _gate = new object();
        private PluginView _view = PluginView.NotSetUp;
        private DriverState? _viewState;
        private long _viewAt;
        private DriverState? _preview;
        private long _previewUntil;
        private ConnectionStatus _lastStatus = ConnectionStatus.NotSetUp;

        public PluginSettings Settings { get; private set; } = new PluginSettings();

        public PluginManager PluginManager { get; set; } = null!;

        public ImageSource PictureIcon => Mark.Icon;

        public string LeftMenuTitle => "PaceLaps";

        public void Init(PluginManager pluginManager)
        {
            Settings = this.ReadCommonSettings("Settings", () => new PluginSettings());
            AttachProperties();
            foreach (var name in new[] { "Call", "SafetyCar", "Penalty", "Flag", "WaveAround", "Info", "Offline", "Online" })
            {
                this.AddEvent(name);
            }

            Host.Changed += OnChanged;
            Host.CallShown += OnCallShown;
            var problem = Host.Use(Settings.OverlayUrl());
            if (problem != null)
            {
                Log.Info("saved overlay URL not used: " + problem);
            }
        }

        public void End(PluginManager pluginManager)
        {
            // SimHub rebuilds plugins on a game change: the connection stays (Host), only this instance goes.
            Host.Changed -= OnChanged;
            Host.CallShown -= OnCallShown;
            Save();
        }

        public void DataUpdate(PluginManager pluginManager, ref GameData data)
        {
            // Nothing per frame: race control's state comes from the server, not the sim.
        }

        public Control GetWPFSettingsControl(PluginManager pluginManager) => new SettingsControl(this);

        internal void Save() => this.SaveCommonSettings("Settings", Settings);

        /// <summary>Shows a test call for 8 seconds (properties, events, tone), for designing overlays and LEDs.</summary>
        internal void ShowPreview(string kind)
        {
            var now = Host.ServerNowMs;
            var preview = DriverState.Preview(kind, Host.State.Device.Car, now);
            lock (_gate)
            {
                _preview = preview;
                _previewUntil = now + PreviewMs;
            }

            if (preview.PreviewCall != null)
            {
                OnCallShown(preview.PreviewCall);
            }
        }

        internal void PlayTone(string? sound, string? type)
        {
            if (Settings.Sound)
            {
                TonePlayer.Play(Tones.For(sound, type), Settings.Volume);
            }
        }

        /// <summary>The values the properties show now; computed again when the state changes or every 100 ms.</summary>
        internal PluginView View()
        {
            var now = Host.ServerNowMs;
            lock (_gate)
            {
                var state = _preview != null && now < _previewUntil ? _preview : Host.State;
                if (!ReferenceEquals(state, _viewState) || now - _viewAt >= ViewMaxAgeMs)
                {
                    _view = PluginView.From(state, now);
                    _viewState = state;
                    _viewAt = now;
                }

                return _view;
            }
        }

        private void OnChanged(DriverState state)
        {
            var previous = _lastStatus;
            _lastStatus = state.Status;
            if (previous == ConnectionStatus.Connected && state.Status == ConnectionStatus.Offline)
            {
                this.TriggerEvent("Offline");
            }
            else if (previous != ConnectionStatus.Connected && state.Status == ConnectionStatus.Connected)
            {
                this.TriggerEvent("Online");
            }
        }

        private void OnCallShown(RaceMessage message)
        {
            this.TriggerEvent("Call");
            this.TriggerEvent(message.Type switch
            {
                "safety_car" => "SafetyCar",
                "penalty" => "Penalty",
                "flag" => "Flag",
                "wave_around" => "WaveAround",
                _ => "Info",
            });
            PlayTone(message.Sound, message.Type);
        }

        private void AttachProperties()
        {
            Attach("Status", v => v.Status);
            Attach("Connected", v => v.Connected);
            Attach("Offline", v => v.Offline);
            Attach("Problem", v => v.Problem);
            Attach("Car", v => v.Car);
            Attach("Session", v => v.Session);

            Attach("Call.Active", v => v.CallActive);
            Attach("Call.Text", v => v.CallText);
            Attach("Call.Detail", v => v.CallDetail);
            Attach("Call.Kind", v => v.CallKind);
            Attach("Call.Color", v => v.CallColour);
            Attach("Call.TextColor", v => v.CallTextColour);
            Attach("Call.AgeSeconds", v => v.CallAgeSeconds);
            Attach("Call.New", v => v.CallNew);
            Attach("Call.Sticky", v => v.CallSticky);
            Attach("Call.Mine", v => v.CallMine);

            Attach("Flag.State", v => v.FlagState);
            Attach("Flag.Label", v => v.FlagLabel);
            Attach("Flag.Color", v => v.FlagColour);

            Attach("Procedure.Name", v => v.Procedure);
            Attach("Procedure.Phase", v => v.Phase);
            Attach("Procedure.Running", v => v.ProcedureRunning);

            Attach("Sorting.Active", v => v.SortingActive);
            Attach("Sorting.Position", v => v.SortingPosition);
            Attach("Sorting.Follow", v => v.SortingFollow);

            Attach("Penalty.Active", v => v.PenaltyActive);
            Attach("Penalty.Text", v => v.PenaltyText);
            Attach("Penalty.Detail", v => v.PenaltyDetail);
            Attach("Personal.Text", v => v.PersonalText);

            Attach("Race.Position", v => v.Position);
            Attach("Race.ClassPosition", v => v.ClassPosition);
            Attach("Race.OnPitRoad", v => v.OnPitRoad);
            Attach("Race.Lap", v => v.Lap);
            Attach("Race.Laps", v => v.Laps);
            Attach("Race.TimeLeftSeconds", v => v.TimeLeftSeconds);

            Attach("Led.Color", v => v.LedColour);
            Attach("Led.Flash", v => v.LedFlash);

            Attach("Clock.OffsetMs", v => v.ClockOffsetMs);
            Attach("Clock.RttMs", v => v.ClockRttMs);
        }

        private void Attach<T>(string name, Func<PluginView, T> value) => this.AttachDelegate(name, () => value(View()));
    }
}
