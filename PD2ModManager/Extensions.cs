using System;
using System.ComponentModel;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PD2ModManager {
    public static class Extensions {

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool DeleteObject(IntPtr hObject);

        public static ImageSource ToImageSource(this Icon icon) {
            IntPtr hBitmap = icon.ToBitmap().GetHbitmap();

            ImageSource wpfBitmap = Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions()
            );

            if (!DeleteObject(hBitmap)) {
                throw new Win32Exception();
            }

            return wpfBitmap;
        }

        public static bool IsNumeric(this string input) {
            decimal number;
            return decimal.TryParse(input, out number);
        }

        public static string GetDescription(this Enum value) {
            Type type = value.GetType();
            string name = Enum.GetName(type, value);
            if (name != null) {
                FieldInfo field = type.GetField(name);
                if (field != null) {
                    DescriptionAttribute attr =
                           Attribute.GetCustomAttribute(field,
                             typeof(DescriptionAttribute)) as DescriptionAttribute;
                    if (attr != null) {
                        return attr.Description;
                    }
                }
            }
            return null;
        }

    }
}
