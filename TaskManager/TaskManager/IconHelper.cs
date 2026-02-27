using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;

namespace TaskManager
{
    public static class IconHelper
    {
        // This method now returns the raw SoftwareBitmap so the UI thread can wrap it
        public static SoftwareBitmap GetSoftwareBitmapFromIcon(Icon icon)
        {
            using var bitmap = icon.ToBitmap();
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