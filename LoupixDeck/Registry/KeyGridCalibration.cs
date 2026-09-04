namespace LoupixDeck.Registry;

/// <summary>
/// Where the touch keys actually sit on a device's panel, as numbers rather than as an
/// assumption baked into the renderer.
///
/// Every Loupedeck-family device so far tiles its grid gaplessly from the corner of the
/// visible area, so a single key size described it completely. The Razer Stream Controller X
/// does not: its keys are smaller than their pitch, and the first one is not half a pitch
/// from the edge. Measured on hardware, a gapless 96px grid puts the outer columns about
/// 5px too far towards the middle (mirrored left and right, which is what a wrong pitch
/// looks like) and every row several pixels too low.
///
/// A panel's real numbers cannot be derived, only measured, and they can differ between
/// units of the same model. So they are data: <see cref="DeviceGeometry.DefaultKeyCalibration"/>
/// carries what we measured, and the user can nudge it from the settings if their unit sits
/// differently.
/// </summary>
public sealed record KeyGridCalibration
{
    /// <summary>Edge length of one key's drawn tile, in panel pixels. Keys are square.</summary>
    public int KeySize { get; init; } = DeviceGeometry.Default.KeySize;

    /// <summary>
    /// Horizontal distance between the centres of two neighbouring keys. Equal to
    /// <see cref="KeySize"/> on a gapless grid, larger when there is a gap between keys.
    /// </summary>
    public int SpacingX { get; init; } = DeviceGeometry.Default.KeySize;

    /// <summary>Vertical distance between the centres of two neighbouring keys.</summary>
    public int SpacingY { get; init; } = DeviceGeometry.Default.KeySize;

    /// <summary>
    /// Centre of the top-left key, measured from the origin of the grid area (so from
    /// <c>VisibleX[0]</c>/<c>VisibleY[0]</c> on the panel, and from the grid's x-offset in
    /// the wallpaper). Half a key on a gapless grid; less when the outer keys are clipped
    /// by the edge of the glass.
    /// </summary>
    public int FirstCenterX { get; init; } = DeviceGeometry.Default.KeySize / 2;

    /// <inheritdoc cref="FirstCenterX"/>
    public int FirstCenterY { get; init; } = DeviceGeometry.Default.KeySize / 2;

    /// <summary>
    /// The classic layout: keys of <paramref name="keySize"/> tiled edge to edge from the
    /// corner. Produces exactly the positions the renderer computed before calibration
    /// existed, which is what keeps every other device unchanged.
    /// </summary>
    public static KeyGridCalibration Gapless(int keySize) => new()
    {
        KeySize = keySize,
        SpacingX = keySize,
        SpacingY = keySize,
        FirstCenterX = keySize / 2,
        FirstCenterY = keySize / 2
    };

    /// <summary>
    /// True when the keys cover the whole grid area between them, which every
    /// Loupedeck-family device does and the Stream Controller X does not. It decides
    /// whether repainting key by key is enough: where there are gaps, no per-key write ever
    /// touches the pixels between the keys, and whatever was drawn there last stays.
    /// </summary>
    public bool TilesGaplessly =>
        SpacingX <= KeySize && SpacingY <= KeySize &&
        FirstCenterX * 2 <= KeySize && FirstCenterY * 2 <= KeySize;

    /// <summary>
    /// Top-left corner of the key at <paramref name="column"/>/<paramref name="row"/>,
    /// relative to the grid origin. The tile is <see cref="KeySize"/> square from there.
    /// </summary>
    public (int X, int Y) GetKeyOrigin(int column, int row) =>
        (FirstCenterX + (column * SpacingX) - (KeySize / 2),
            FirstCenterY + (row * SpacingY) - (KeySize / 2));

    /// <summary>
    /// The column whose centre is nearest to <paramref name="x"/> (measured from the grid
    /// origin), clamped to the grid. Nearest-centre rather than a division because with a
    /// gap there are coordinates that belong to no key at all, and the nearest key is the
    /// only sensible answer for them.
    /// </summary>
    public int NearestColumn(int x, int columns) => NearestIndex(x, FirstCenterX, SpacingX, columns);

    /// <inheritdoc cref="NearestColumn"/>
    public int NearestRow(int y, int rows) => NearestIndex(y, FirstCenterY, SpacingY, rows);

    private static int NearestIndex(int position, int firstCentre, int spacing, int count)
    {
        if (count <= 1 || spacing <= 0) return 0;

        // Round to the nearest multiple of the pitch rather than truncating, so the
        // half-way point between two keys is the boundary.
        int index = (int)Math.Round((position - firstCentre) / (double)spacing,
            MidpointRounding.AwayFromZero);
        return Math.Clamp(index, 0, count - 1);
    }
}
