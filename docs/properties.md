# Properties, events and actions

Everything the plugin offers in SimHub, under **PaceLapsPlugin**. Use them in Dash Studio, in LED and formula
effects, and in SimHub's event bindings. Texts are never null: empty means nothing to show.

The plugin page's **Design your overlay** buttons show a test call for 8 seconds on your PC only, so you can build
without a race.

## Connection

| Property | Type | Meaning |
| --- | --- | --- |
| `Status` | text | `not_set_up`, `connecting`, `connected`, `offline` or `signed_out` (the overlay URL no longer works) |
| `Connected` | yes/no | Receiving race control's calls now |
| `Offline` | yes/no | The connection dropped: calls may be missing until it is back. Show a warning. |
| `Problem` | text | What is wrong, in a few words, or empty |
| `Car` | text | Your car number |
| `Session` | text | The session's name, empty when none is open |

## The call on screen

A one-off call (a blue flag, an info) shows over a lasting state (a safety car, your penalty) for its 8 seconds.

| Property | Type | Meaning |
| --- | --- | --- |
| `Call.Active` | yes/no | A call is showing |
| `Call.Text` | text | The banner text |
| `Call.Detail` | text | A second line, for example what a penalty means |
| `Call.Kind` | text | `flag`, `safety_car`, `penalty`, `wave_around`, `info` or `system` |
| `Call.Color` | text | Its colour, `#RRGGBB`: the league's own colour if its procedure chose one, else by kind (safety car yellow, penalty red, wave around blue, a flag call the race's flag colour) |
| `Call.TextColor` | text | A readable text colour on `Call.Color` |
| `Call.AgeSeconds` | number | Seconds since it was shown |
| `Call.New` | yes/no | Shown less than 5 seconds ago |
| `Call.Sticky` | yes/no | A lasting state rather than a one-off |
| `Call.Mine` | yes/no | It names your car |

## The state and the notice

For layouts with two lines: the lasting state on top, the one-off beside or below it.

| Property | Type | Meaning |
| --- | --- | --- |
| `Banner.Active` | yes/no | A lasting state is up (a safety car, Code 60, your penalty) |
| `Banner.Text`, `Banner.Detail` | text | Its text and second line |
| `Banner.Kind`, `Banner.Color`, `Banner.TextColor` | text | As for `Call.*` |
| `Banner.Mine` | yes/no | It names your car |
| `Banner.AgeSeconds` | number | Seconds since it came up |
| `Notice.Active` | yes/no | A one-off call is showing (for its 8 seconds) |
| `Notice.Text`, `Notice.Detail`, `Notice.Kind`, `Notice.Color`, `Notice.TextColor` | text | As above |
| `Notice.Mine` | yes/no | It names your car |
| `Notice.MyClass` | yes/no | It concerns your class (a class wave around) |
| `Notice.AgeSeconds` | number | Seconds since it was shown |

## Race

| Property | Type | Meaning |
| --- | --- | --- |
| `Flag.State` | text | `green`, `green_held`, `yellow`, `red`, `white` or `grey` (empty without telemetry) |
| `Flag.Label` | text | As race control's header shows it |
| `Flag.Color` | text | `#RRGGBB` |
| `Procedure.Name` | text | The running procedure (for example the safety car), empty when none |
| `Procedure.Phase` | text | Its phase |
| `Procedure.Running` | yes/no | A procedure is running |
| `Sorting.Active` | yes/no | The field is being sorted and you have a target |
| `Sorting.Position` | number | Your target place in class |
| `Sorting.Follow` | text | The car number to follow |
| `Penalty.Active` | yes/no | You have a penalty on screen |
| `Penalty.Text`, `Penalty.Detail` | text | Your penalty |
| `Personal.Text` | text | The newest call that named your car |
| `Race.Position`, `Race.ClassPosition` | number | Your place overall and in class (race control's order) |
| `Race.OnPitRoad` | yes/no | |
| `Race.Lap`, `Race.Laps` | number | The leader's lap and the race's laps (0 when unknown) |
| `Race.TimeLeftSeconds` | number | For timed races, counting down |

## LEDs

| Property | Type | Meaning |
| --- | --- | --- |
| `Led.Color` | text | The colour your LEDs should show now, empty for none: your penalty, else the call on screen, else a yellow or red flag. Empty while offline. |
| `Led.Flash` | yes/no | The colour comes from a new call: flash |

## Clock

| Property | Type | Meaning |
| --- | --- | --- |
| `Clock.OffsetMs` | number | Server clock minus this PC's clock |
| `Clock.RttMs` | number | The round trip of that measurement; the offset is accurate to about half of it |

## Events

`Call` (every call), `SafetyCar`, `Penalty`, `Flag`, `WaveAround`, `Info` (each when such a call is shown), `Offline`
and `Online` (the connection dropped or came back). Bind them in SimHub to what it can do, for example a haptic pulse.
