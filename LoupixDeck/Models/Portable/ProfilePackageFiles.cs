namespace LoupixDeck.Models.Portable;

/// <summary>
/// Fixed names inside a <c>.loupixprofile</c> package (a plain zip):
/// <code>
/// manifest.json          required, read first and on its own
/// payload.json           required, the exported subtree
/// macros.json            optional, only the referenced macros
/// assets/                optional, mirrors the asset store's relative layout verbatim
///   &lt;sha&gt;.png
///   wallpapers/&lt;sha&gt;.png
/// </code>
/// The assets folder deliberately uses exactly the relative paths stored in the payload, so an
/// import is a straight "extract, then hand the file to <c>IAssetService.Import</c>" — and because
/// the store is content-addressed, re-importing an asset the machine already has is a free no-op
/// that yields the same relative path back.
/// </summary>
public static class ProfilePackageFiles
{
    /// <summary>File extension of a profile package, without the leading dot.</summary>
    public const string Extension = "loupixprofile";

    /// <summary>Manifest file name inside the package.</summary>
    public const string Manifest = "manifest.json";

    /// <summary>Default payload file name inside the package.</summary>
    public const string Payload = "payload.json";

    /// <summary>Macro document file name inside the package.</summary>
    public const string Macros = "macros.json";

    /// <summary>Asset folder name inside the package (also the stored path prefix).</summary>
    public const string AssetsFolder = "assets";
}
