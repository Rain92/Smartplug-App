using System;
using System.Text;
using System.Runtime.InteropServices;
using Java.Util.Functions;

namespace SmartPlugAndroid
{
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    struct Time
    {
        public int Hour;
        public int Minute;

        public Time(int hour, int minute)
        {
            Hour = hour;
            Minute = minute;
        }

        public static bool operator <(Time a, Time b) => a.Hour < b.Hour || (a.Hour == b.Hour && a.Minute < b.Minute);
        public static bool operator >(Time a, Time b) => a.Hour > b.Hour || (a.Hour == b.Hour && a.Minute > b.Minute);
        public static bool operator <=(Time a, Time b) => a.Hour < b.Hour || (a.Hour == b.Hour && a.Minute <= b.Minute);
        public static bool operator >=(Time a, Time b) => a.Hour > b.Hour || (a.Hour == b.Hour && a.Minute >= b.Minute);
        public override string ToString() => $"{Hour.ToString("D2")}:{Minute.ToString("D2")}";
    };

    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    struct TimeInterval
    {
        public bool Active;
        public Time From;
        public Time To;
        public DayOfWeek Weekday;
    };

    enum Mode
    {
        Off = 0,
        On,
        Timer
    };

    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    struct ControlSettings
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] NetId;

        public Mode Mode;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public TimeInterval[] TimeIntervals;

        public ControlSettings(Mode mode)
        {
            this.NetId = new byte[32];
            this.Mode = mode;
            this.TimeIntervals = new TimeInterval[32];
        }
        public string NetIdStr
        {
            get
            {
                return Encoding.UTF8.GetString(NetId);
            }
            set
            {
                Array.Clear(NetId, 0, NetId.Length);
                Encoding.UTF8.GetBytes(value, NetId);
            }
        }

        public static ControlSettings Constructor()
        {
            ControlSettings str = new ControlSettings();
            str.NetId = new byte[32];
            str.Mode = Mode.Off;
            str.TimeIntervals = new TimeInterval[32];

            return str;
        }

        public static ControlSettings FromByteArray(byte[] bytes)
        {
            ControlSettings str = Constructor();

            int size = Marshal.SizeOf(str);

            if (bytes.Length != size)
                throw new ArgumentException($"Wrong size of byte array. Expecdey: {size}, actual {bytes.Length}.");


            IntPtr ptr = Marshal.AllocHGlobal(size);

            Marshal.Copy(bytes, 0, ptr, size);

            str = (ControlSettings)Marshal.PtrToStructure(ptr, str.GetType());
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
    };
}