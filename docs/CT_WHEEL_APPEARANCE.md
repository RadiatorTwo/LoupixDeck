# Loupedeck CT: Wheel Screen Appearance

The Loupedeck CT's centre wheel has its own round 240 × 240 screen. By default LoupixDeck
draws it automatically: the wheel mode's label, and — when the wheel is bound to a plugin
adjustment command such as a volume — an arc filled to the current value with the value text in
the middle. That look can be replaced with a layer canvas edited the same way as a touch button.

## Opening the editor

Either:

- right-click the wheel in the main window and choose **Edit appearance…**, or
- click the brush button in the wheel-modes bar under the device.

The first time a wheel mode is opened its canvas is created from the automatic layout, so the
editor opens on what the wheel already shows: a black background, an **Indicator** layer for
the arc, a **Label** text layer and a **Value** text layer. Every wheel mode has its own canvas;
modes you never open keep the automatic layout, and so does a mode whose editor you close
without changing anything.

The editor is the touch-button editor without the parts a wheel screen does not have: there are
no states, no vibration, and no command sequence — the wheel's rotate-left, rotate-right and
press commands stay in the rotary editor (double-click the wheel, or **Advanced settings…**).
The preview dims the corners the round glass hides and shows a sample value (65 %) so the arc
and value text are visible while you style them. On the device the live value is drawn.

Edits are pushed to the wheel screen as you make them.

## Layers

Text, image and symbol layers work as on a touch button. Animated image layers are not offered,
and text tokens such as the time are not refreshed: the wheel screen is repainted when its value
or its layers change, not on a timer. Two things are specific to the wheel:

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

A dynamic text layer keeps all the usual styling — size, colour, bold, outline, box and
position — so the label and value can be placed and coloured independently. When there is no
label or value to show, the layer draws nothing.

## Reset and going back to the automatic layout

- **Reset** in the Properties tab puts the three default layers back and clears any extra ones.
- **Use automatic layout** removes the canvas for this wheel mode entirely. The wheel is drawn
  by LoupixDeck again, exactly as before the canvas was created, and the profile no longer stores
  a canvas for that mode.

Copy, cut, paste and clear on the wheel include its canvas. Profiles exported as portable
packages carry it too; a LoupixDeck release that predates this feature ignores it and shows the
automatic layout.
