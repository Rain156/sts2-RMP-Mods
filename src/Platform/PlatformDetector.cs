using System;

namespace RemoveMultiplayerPlayerLimit.Platform;

/// <summary>
/// Static platform detection — cached at first access.
/// Used to conditionally load platform-specific modules.
/// </summary>
public static class PlatformDetector
{
    public static bool IsMacOS => OperatingSystem.IsMacOS();
    public static bool IsLinux => OperatingSystem.IsLinux();
    public static bool IsWindows => OperatingSystem.IsWindows();

    /// <summary>True if running on Apple Silicon (ARM64 macOS).</summary>
    public static bool IsMacOSArm64 =>
        IsMacOS && System.Runtime.InteropServices.RuntimeInformation.OSArchitecture
            == System.Runtime.InteropServices.Architecture.Arm64;
}
