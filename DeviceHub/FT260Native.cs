using System;
using System.Runtime.InteropServices;
using System.Text;

namespace TERMINAL.DeviceHub
{
    public static class FT260Native
    {
        private const string DLL_NAME = "libFT260.dll";

        public const int FT260_OK = 0;

        // Флаги I2C
        public const byte FT260_I2C_START_AND_STOP = 0x06;
        public const byte FT260_I2C_START = 0x02;
        public const byte FT260_I2C_STOP = 0x04;

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FT260_CreateDeviceList(out uint deviceCount);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern int FT260_GetDevicePath(StringBuilder pDevicePath, uint bufferLength, uint deviceIndex);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern int FT260_OpenByDevicePath(string pDevicePath, out IntPtr handle);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FT260_Close(IntPtr handle);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FT260_I2CMaster_Init(IntPtr handle, uint kbps);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FT260_I2CMaster_Write(
            IntPtr handle, byte deviceAddress, byte flag, byte[] buffer, ushort bufferSize, out ushort transferred);

        // ★ ИСПРАВЛЕНО: Добавлен параметр wait_timer (таймаут)
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FT260_I2CMaster_Read(
            IntPtr handle, byte deviceAddress, byte flag, byte[] buffer, ushort bufferSize, out ushort transferred, uint waitTimer);
    }
}