# SimHub plugin backlog

Spec section: "SimHub plugin specification" (the specification is not public).

## Step 6: proof of concept
Exit: one beta driver uses it in a league race, and the VR question is answered.
- [x] SimHub plugin SDK found and the solution set up (2026-11-12: the SDK ships with SimHub in
      `C:\Program Files (x86)\SimHub\PluginSdk`; `docs/simhub-notes.md`)
- [x] Core: realtime connection (Reverb, Pusher protocol), reconnect and resync from the cursor, clock offset,
      `showAt` scheduling, shown reports, a heartbeat ping a minute; unit tests with a fake clock, server and socket
      (2026-11-12)
- [x] SimHub adapter (net48): properties, events, settings page (paste an overlay URL, kept encrypted for the Windows
      user), tones through SimHub's NAudio, test calls for designing (2026-11-12; `docs/properties.md`)
- [ ] Texts of the settings page in a `.resx` (SimHub's translation editor), once they settle
- [x] Against the development server, without iRacing (2026-11-12, SimHub 9.13.3): the plugin loads and connects,
      four calls (safety car, own penalty, wave around, info) reached SimHub's properties with their tones, test calls
      and test sound work; shown 11 to 14 ms after showAt, none late (the browser and the console client: 6 to 13 ms)
- [ ] Dash Studio overlays: three designs built (2026-11-12: Card, the default; Solid; Strip; `tools/PaceLaps.Overlays`,
      `Banner.*`/`Notice.*` properties); checked in SimHub by the user (all three liked; two fixes: text fitted to
      its box, flag calls neutral); league colours checked against test.pacelaps.com. Still to do: screenshots for
      the guides (all three are named in them)
- [ ] A PaceLaps layer for existing dashboards
- [ ] LED effects profile, tested on a real wheel
- [ ] VR: an overlay URL in OpenKneeboard first, then the plugin's overlay, with a VR driver
- [ ] Phone and tablet dashboards: do plugin properties reach them (low priority)

## Step 7: full plugin
- [ ] All properties from the spec (safety car phase, pit status, sorting target, last personal message, audit view)
- [ ] Distinct tone per state
- [ ] Install guide for drivers

## Always
- [ ] Keep a compatibility list of the SimHub versions the plugin was tested on (README; so far 9.13.3).

## Findings and decisions
- 2026-11-12: code namespaces are `PaceLaps.Plugin` and `PaceLaps.Plugin.Core` (the DLLs keep their names): inside a
  `PaceLaps.SimHub` namespace, the XAML compiler's references to SimHub's own types (`SimHub.Plugins.Styles`) resolve
  to ours and fail.
- 2026-11-12: property names say Color, as SimHub does.
- 2026-11-12: sign-in by pasting one of the driver's overlay URLs (not a join link or QR code): the device API
  already accepts the overlay token for everything a device does, so the server needs no change. New overlay URLs or
  Turn off on the driver page disconnect the plugin.
- 2026-11-12: shown reports count under the pasted URL's view (banner, target, audit or full): the server takes the
  view from the overlay token. A view of its own (`simhub`) would need a server change; later, if the lateness
  statistics should tell the plugin apart.
- 2026-11-12: the heartbeat is a clock ping a minute (any device request marks the device as seen; race control counts
  devices seen within 2 minutes as connected); a full measurement every 5 minutes as the web views do.
