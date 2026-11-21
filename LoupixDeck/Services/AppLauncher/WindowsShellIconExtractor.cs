#if WINDOWS
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;
using SkiaSharp;

namespace LoupixDeck.Services.AppLauncher
{
    /// <summary>
    /// Provides functionality for extracting application icons from Windows Shell items.
    /// </summary>
    /// <remarks>
    /// Uses the Windows Shell API to retrieve application icons as native GDI bitmaps and converts them into SkiaSharp images.
    /// 
    /// Supports executable files, shortcuts, and other Shell-supported paths.
    /// </remarks>
    [SupportedOSPlatform("windows")]
    static partial class WindowsShellIconExtractor
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapInfoHeader
        {
            public int Size;
            public int Width;
            public int Height;
            public short Planes;
            public short BitCount;
            public int Compression;
            public int SizeImage;
            public int XPelsPerMeter;
            public int YPelsPerMeter;
            public int ClrUsed;
            public int ClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapInfo
        {
            public BitmapInfoHeader Header;
            public uint Colors;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Bitmap
        {
            public int Type;
            public int Width;
            public int Height;
            public int WidthBytes;
            public ushort Planes;
            public ushort BitsPixel;
            public IntPtr Bits;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Size
        {
            public int Width;
            public int Height;
        }

        [Flags]
        internal enum SIIGBF
        {
            ResizeToFit = 0x0,
            BiggerSizeOk = 0x1,
            MemoryOnly = 0x2,
            IconOnly = 0x4,
            ThumbnailOnly = 0x8
        }

        [GeneratedComInterface]
        [Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B")]
        internal partial interface IShellItemImageFactory
        {
            [PreserveSig]
            int GetImage(Size size, SIIGBF flags, out IntPtr phbm);
        }

        [LibraryImport("gdi32.dll")]
        private static partial IntPtr CreateCompatibleDC(IntPtr hdc);

        [LibraryImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool DeleteDC(IntPtr hdc);

        [LibraryImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool DeleteObject(IntPtr obj);

        [LibraryImport("gdi32.dll")]
        private static partial int GetDIBits(IntPtr hdc, IntPtr bitmap, uint start, uint lines, IntPtr bits, ref BitmapInfo info, uint usage);

        [LibraryImport("gdi32.dll", EntryPoint = "GetObjectW")]
        private static partial int GetObject(IntPtr h, int c, out Bitmap bitmap);

        [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
        private static partial int SHCreateItemFromParsingName(string pszPath, IntPtr pbc, ref Guid riid, out IShellItemImageFactory ppv);

        /// <summary>
        /// Extracts an application icon from a Windows Shell item.
        /// </summary>
        /// <param name="shellPath">File system path or Windows Shell parsing path identifying the item</param>
        /// <param name="size">Requested icon width and height in pixels</param>
        /// <returns>A SkiaSharp image containing the extracted icon or <see langword="null"/> if extraction fails</returns>
        public static SKImage Extract(string shellPath, int size)
        {
            IShellItemImageFactory factory = null;
            IntPtr hBitmap = IntPtr.Zero;

            try
            {
                Guid id = typeof(IShellItemImageFactory).GUID;
                if (SHCreateItemFromParsingName(shellPath, IntPtr.Zero, ref id, out factory) < 0 || factory == null)
                    return null;

                // Request the item's icon as a native HBITMAP
                int hResult = factory.GetImage(new Size { Width = size, Height = size }, SIIGBF.IconOnly, out hBitmap);

                if (hResult < 0 || hBitmap == IntPtr.Zero)
                    return null;

                // Get actual bitmap dimensions
                if (GetObject(hBitmap, Marshal.SizeOf<Bitmap>(), out Bitmap nativeBitmap) == 0)
                    return null;

                int width = nativeBitmap.Width;
                int height = nativeBitmap.Height;

                if (width <= 0 || height <= 0)
                    return null;

                // Configure a 32-bit, uncompressed, top-down bitmap.
                // Negative height prevents vertically inverted pixel data.
                var info = new BitmapInfo
                {
                    Header = new BitmapInfoHeader
                    {
                        Size = Marshal.SizeOf<BitmapInfoHeader>(),
                        Width = width,
                        Height = -height,
                        Planes = 1,
                        BitCount = 32,
                        Compression = 0
                    }
                };

                using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));

                // Create a temporary memory device context for GDI operations.
                IntPtr hdc = CreateCompatibleDC(IntPtr.Zero);
                if (hdc == IntPtr.Zero)
                    return null;

                try
                {
                    int result = GetDIBits(hdc, hBitmap, 0, (uint)height, bitmap.GetPixels(), ref info, 0);
                    if (result != height)
                        return null;

                    return SKImage.FromBitmap(bitmap);
                }
                finally
                {
                    DeleteDC(hdc);
                }
            }
            finally
            {
                // Release the native bitmap and the COM reference. Exceptions are left to the
                // caller, which logs them.
                if (hBitmap != IntPtr.Zero)
                    DeleteObject(hBitmap);
                ((object)factory as ComObject)?.FinalRelease();
            }
        }
    }
}
#endif
