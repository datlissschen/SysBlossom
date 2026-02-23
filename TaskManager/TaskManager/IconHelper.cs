using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;

namespace TaskManager
{
    public static class IconHelper
    {
        public static async Task<SoftwareBitmapSource> GetWinUI3BitmapSourceFromIcon(Icon icon)
        {
            using var bitmap = icon.ToBitmap();
            var softwareBitmap = ConvertToSoftwareBitmap(bitmap);

            var source = new SoftwareBitmapSource();
            await source.SetBitmapAsync(softwareBitmap);
            return source;
        }

        private static SoftwareBitmap ConvertToSoftwareBitmap(Bitmap bitmap)
        {
            // Lock the bitmap's bits.
            var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            var bitmapData = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

            try
            {
                var buffer = new byte[bitmapData.Stride * bitmapData.Height];
                Marshal.Copy(bitmapData.Scan0, buffer, 0, buffer.Length);

                var softwareBitmap = new SoftwareBitmap(BitmapPixelFormat.Bgra8, bitmap.Width, bitmap.Height, BitmapAlphaMode.Premultiplied);
                softwareBitmap.CopyFromBuffer(buffer.AsBuffer());

                return softwareBitmap;
            }
            finally
            {
                bitmap.UnlockBits(bitmapData);
            }
        }
    }
}