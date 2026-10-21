using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;

namespace TaskManager.Helper
{
    public static class IconHelper
    {
        public static SoftwareBitmap GetSoftwareBitmapFromIcon(Icon icon)
        {
            using var bitmap = icon.ToBitmap();
            return GetSoftwareBitmapFromBitmap(bitmap);
        }

        public static SoftwareBitmap GetSoftwareBitmapFromBitmap(Bitmap bitmap)
        {
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

        // Public method to generate the default gear emoji bitmap
        public static SoftwareBitmap GetDefaultGearBitmap()
        {
            // Create a 32x32 transparent bitmap
            using var bitmap = new Bitmap(32, 32);
            using var g = Graphics.FromImage(bitmap);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            // Halve the font size from 20 to 10
            using var font = new Font("Segoe UI Emoji", 15);
            var brush = Brushes.Gray;

            // Set up formatting to perfectly center the smaller emoji
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };

            // Draw the gear emoji perfectly centered inside the 32x32 area
            var rect = new RectangleF(0, 0, 32, 32);
            g.DrawString("⚙️", font, brush, rect, format);

            return GetSoftwareBitmapFromBitmap(bitmap);
        }
    }
}