# Loupedeck CT: Wheel Screen Appearance

The Loupedeck CT's centre wheel has its own round 240 × 240 screen. By default LoupixDeck
draws an unsplit wheel automatically: the mode's label and, for adjustment commands such as
volume, an arc filled to the current value with text in the middle. You can replace that look
with an editable layer canvas or split the screen into independently pressable segments.

## Opening the editor

Either:

- right-click the wheel in the main window and choose **Edit appearance…**, or
- click the brush button in the wheel-modes bar under the device.

The first time an unsplit wheel mode is opened its canvas is created from the automatic layout, so the
editor opens on what the wheel already shows: a black background, an **Indicator** layer for
the arc, a **Label** text layer and a **Value** text layer. Every wheel mode has its own canvas;
modes you never open keep the automatic layout, and so does a mode whose editor you close
without changing anything.

The editor is the touch-button editor without the parts a wheel screen does not have: there are
no states, no vibration, and no command sequence. The wheel's rotate-left, rotate-right and
press or segment commands stay in the rotary editor (double-click the wheel, or **Advanced settings…**).
The preview dims the corners the round glass hides and shows a sample value (65 %) so the arc
and value text are visible while you style them. On the device the live value is drawn.

Edits are pushed to the wheel screen as you make them.

## Split wheel surfaces

Use the **Split screen** zone icons to select a horizontal split (top/bottom), vertical split
(left/right), diagonal X split (top/right/bottom/left sectors), or cross + split (four quadrants).
Each wheel mode has its own split. **Not split** keeps the whole screen as one press target.

Each segment has a press command sequence in the rotary editor and an appearance canvas in this
editor. The rotary editor puts the turn commands on the top row and the press/segments below,
two per row. Turning, workspace swipes, and the wheel-mode menu remain shared by the wheel.

Surface tabs above the preview choose a segment or **All segments**. You can also click a segment
in the preview; hovering shows the area that will be selected. Segment canvases use the whole
wheel's coordinates and are clipped to the selected zone. Their text, images, animations, and
live command layers update like those on an unsplit wheel. A segment with no artwork starts blank.

**All segments** is a shared surface for a background, ring, logo, or other full-wheel layers.
Move a layer across the **Segments** marker in the layer list to put it behind or in front of
the segment canvases. This surface has no press command. Use the toggle next to the split icons
to show or hide divider lines.

Switching from four segments to two and back preserves the hidden segments. Copy/paste carries
the split, commands, and artwork; clearing the wheel mode removes them. Older files with no split
continue to use the unsplit wheel.

## Layers

Text, image, animated image and symbol layers work as on a touch button. An animated image plays
at the clip's own frame rate, up to 30 frames per second, and pauses
while the wheel menu is open, the device is off or the screensaver runs. Live text such as the
time comes from a text layer's *Dynamic text command* source (below). Two things are specific to
the wheel:

**Indicator layer.** Draws the adjustment arc. You can set the track and fill colours, the
stroke thickness (as a fraction of the layer size), the start angle and the sweep (360 for a full
ring), and whether the ends are rounded. Move and resize it like a symbol layer. While the bound
command reports no value — a plain command, or a plugin that only reports text — the layer draws
nothing, so the label and other layers show on their own. You can add more than one, hide it, or
delete it.

**Text source.** A text layer on the wheel canvas has a *Text source* setting:

| Source      | Draws                                                           |
|-------------|-----------------------------------------------------------------|
| Own text    | Whatever you type in the Text box (the normal behaviour).       |
| Wheel label | The wheel mode's label from the wheel-modes bar.                |
| Value       | The value text the adjustment command reports, e.g. `-32.5 dB`.  |
| Value detail | The value's secondary text, when the command reports one.     |
| Dynamic text command | The text of a dynamic-text command, refreshed on its own interval. |

**Dynamic text command.** Choosing this source shows a *Dynamic text command* box: enter the
command string a touch key would use, for example `DynamicText.Clock(HH:mm)`, or a display command
from a plugin. The wheel repaints whenever the text changes, honouring the command's update interval,
and immediately when a plugin pushes new data. Only the wheel mode that is showing is polled;
switching mode, page or workspace stops the others. The layer's own Text is kept as the fallback
shown until the command first reports, and by a LoupixDeck release that predates this source.

A dynamic text layer keeps all the usual styling — size, colour, bold, outline, box and
position — so the label and value can be placed and coloured independently. When there is no
label or value to show, the layer draws nothing.

## Reset and automatic layout

- On an unsplit wheel, **Reset** in Properties restores the default indicator, label, and value layers.
- On a segment, Reset clears that segment's layers and leaves its background transparent. On
  **All segments**, it clears the layers and restores the black shared background. Other surfaces
  and the wheel's commands are kept.
- **Use automatic layout** is available only while the wheel is unsplit. It removes that mode's
  custom canvas and returns to the host's automatic label/value display. Select **Not split**
  first if you want to return a split mode to this appearance.

Copy, cut, paste and clear on the wheel include its canvas. Profiles exported as portable
packages carry it too; a LoupixDeck release that predates this feature ignores it and shows the
automatic layout.
