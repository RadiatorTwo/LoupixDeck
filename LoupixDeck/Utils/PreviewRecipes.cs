using System.Runtime.CompilerServices;
using Avalonia.Media.Imaging;

namespace LoupixDeck.Utils;

/// <summary>
/// Remembers how a device image was drawn, so the on-screen device view can draw the same image
/// again at the resolution it is actually shown at (issue #251). The images sent to the hardware
/// are only device-sized (90 px keys); scaled up on screen they turn soft, while their text,
/// symbols and source pictures could be drawn sharp at any size.
/// </summary>
/// <remarks>
/// Only images whose renderer registers a recipe get a sharp preview. Anything else - a swipe
/// composite, a plugin-drawn strip - simply keeps showing the device image, so the preview can
/// never show content the device does not.
/// </remarks>
public static class PreviewRecipes
{
    // Weak on the image: a recipe lives exactly as long as the image it belongs to.
    private static readonly ConditionalWeakTable<object, Func<double, Bitmap>> Recipes = new();

    /// <summary>
    /// Registers how to draw <paramref name="image"/> again, <paramref name="recipe"/> taking the
    /// scale relative to the image's own pixel size. A recipe runs off the UI thread.
    /// </summary>
    public static void Register(object image, Func<double, Bitmap> recipe)
    {
        if (image != null)
            Recipes.AddOrUpdate(image, recipe);
    }

    /// <summary>The recipe registered for <paramref name="image"/>, or null.</summary>
    public static Func<double, Bitmap> Find(object image)
    {
        return image != null && Recipes.TryGetValue(image, out Func<double, Bitmap> recipe) ? recipe : null;
    }
}
