using LoupixDeck.Models;
using LoupixDeck.Models.Layers;

namespace LoupixDeck.Services.Actions;

/// <summary>
/// "Prevent Dial Labels to obstruct Touch Buttons": moves the content of a Live S key next to a dial
/// label (<see cref="RotaryButton.DialLabel"/>) out from under the label. Every layer goes right by
/// <see cref="DialLabelGeometry.ObstructionShift"/> and no text box stays wider than
/// <see cref="DialLabelGeometry.ObstructionTextMaxWidth"/>. The user can move and resize them freely
/// afterwards; nothing keeps them there.
/// </summary>
public static class DialLabelLayout
{
    /// <summary>Adjusts <paramref name="layers"/> on a key of <paramref name="keySize"/> pixels.
    /// Returns true when there was a layer to adjust.</summary>
    public static bool Adjust(IEnumerable<LayerBase> layers, int keySize)
    {
        if (layers == null) return false;

        int shift = DialLabelGeometry.ObstructionShift(keySize);
        int maxTextWidth = DialLabelGeometry.ObstructionTextMaxWidth(keySize);
        bool any = false;

        foreach (LayerBase layer in layers.ToList())
        {
            if (layer == null) continue;

            layer.PositionX += shift;
            // 0 means "as wide as the key", so it is capped too.
            if (layer is TextLayer text && (text.BoxWidth <= 0 || text.BoxWidth > maxTextWidth))
                text.BoxWidth = maxTextWidth;
            any = true;
        }

        return any;
    }

    /// <summary>Adjusts the layers of every state of <paramref name="key"/>.</summary>
    public static bool AdjustAllStates(TouchButton key, int keySize)
    {
        if (key?.States == null) return false;

        bool any = false;
        foreach (ButtonState state in key.States.ToList())
            any |= Adjust(state?.Layers, keySize);

        if (any)
            key.Refresh();
        return any;
    }
}
