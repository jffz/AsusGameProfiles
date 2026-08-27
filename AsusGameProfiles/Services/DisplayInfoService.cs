using System.Runtime.InteropServices;

namespace AsusGameProfiles.Services;

public record DisplayModeInfo(int Width, int Height, int RefreshHz);

/// <summary>
/// Lit le mode d'affichage courant (résolution, fréquence) d'un moniteur Windows via l'API Win32
/// standard EnumDisplaySettings -- complémentaire de dwc.exe, qui ne donne que modèle/série/firmware,
/// pas les informations de mode vidéo courant.
/// </summary>
public static class DisplayInfoService
{
    private const int EnumCurrentSettings = -1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);

    /// <summary>
    /// Résolution et fréquence de rafraîchissement courantes du moniteur identifié par
    /// <paramref name="deviceName"/> (ex: <c>\\.\DISPLAY1</c>, exactement le "Device ID" rapporté par
    /// <c>dwc.exe info</c>). Retourne null si le device est inconnu de Windows.
    /// </summary>
    public static DisplayModeInfo? GetCurrentMode(string deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName)) return null;

        try
        {
            var mode = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
            if (!EnumDisplaySettings(deviceName, EnumCurrentSettings, ref mode))
                return null;

            return new DisplayModeInfo(mode.dmPelsWidth, mode.dmPelsHeight, mode.dmDisplayFrequency);
        }
        catch
        {
            return null;
        }
    }
}
