using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using System.Runtime.InteropServices;

namespace SmartPlugAndroid
{
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    struct PowerUsageHistory
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 120)]
        public int[] values;
        public static PowerUsageHistory Constructor()
        {
            PowerUsageHistory str = new PowerUsageHistory();
            str.values = new int[120];

            return str;
        }
        public static PowerUsageHistory FromByteArray(byte[] bytes)
        {
            PowerUsageHistory str = Constructor();

            int size = Marshal.SizeOf(str);

            if (bytes.Length != size)
                throw new ArgumentException($"Wrong size of byte array. Expecdey: {size}, actual {bytes.Length}.");


            IntPtr ptr = Marshal.AllocHGlobal(size);

            Marshal.Copy(bytes, 0, ptr, size);

            str = (PowerUsageHistory)Marshal.PtrToStructure(ptr, str.GetType());
            Marshal.FreeHGlobal(ptr);

            return str;
        }

        public byte[] ToByteArray()
        {
            int size = Marshal.SizeOf(this);
            byte[] bytes = new byte[size];

            IntPtr ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(this, ptr, true);
            Marshal.Copy(ptr, bytes, 0, size);
            Marshal.FreeHGlobal(ptr);

            return bytes;
        }
    }
}