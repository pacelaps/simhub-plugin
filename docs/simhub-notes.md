# SimHub notes

Findings from the proof of concept: SDK setup, VR result, sound, tablet dashboards. Date every entry.

## 2026-11-12: the SDK

- The plugin SDK ships with SimHub: `C:\Program Files (x86)\SimHub\PluginSdk` holds a demo plugin
  (`User.PluginSdkDemo`), a device extension demo and an LED effect demo. Copy, don't edit: SimHub updates overwrite
  the folder.
- Plugins are .NET Framework 4.8 class libraries. They reference SimHub's DLLs through the `SIMHUB_INSTALL_PATH`
  environment variable (`SimHub.Plugins.dll`, `GameReaderCommon.dll`, `SimHub.Logging.dll`, MahApps.Metro for the
  settings page) and are copied into the SimHub folder after the build.
- A plugin implements `IPlugin`, `IDataPlugin` (called on every game data update: keep it fast, never throw) and
  `IWPFSettingsV2` (its settings page and left menu entry). Properties come from `AttachDelegate(name, () => value)`,
  evaluated when shown or used in a formula; events from `AddEvent`/`TriggerEvent`; actions from `AddAction`.
  Settings persist with `ReadCommonSettings`/`SaveCommonSettings`.
- SimHub localises plugin texts through `.resx` files (`SLoc` in XAML, `Loc.GetValue` in code).
- SimHub ships Newtonsoft.Json 13 (assembly version 13.0.0.0) and NAudio 2.2: the plugin uses those copies and ships
  none of its own, so nothing clashes.
- Dependencies of the plugin's own must not clash with SimHub's and must be added to the post-build copy.
- SimHub's free version refreshes dashboards at about 10 fps (a licence raises it); enough for calls.
