using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using MegaCrit.Sts2.Core.Logging;

namespace RemoveMultiplayerPlayerLimit;

public static partial class ModEntry
{
	private const int RtldNow = 2;

	private const int RtldGlobal = 0x100;

	private const string LinuxHarmonyDependencyFailureHelp = "Failed to preload Linux Harmony dependencies. If patching fails, verify libgcc_s.so.1, libstdc++.so.6, libunwind.so.8, and libunwind-x86_64.so.8 are installed and visible to the game process.";

	private static readonly string[] LinuxHarmonyDependencyCandidates = new[]
	{
		"libgcc_s.so.1",
		"libstdc++.so.6",
		"libunwind.so.8",
		"libunwind-x86_64.so.8",
		"/lib/x86_64-linux-gnu/libgcc_s.so.1",
		"/usr/lib/x86_64-linux-gnu/libgcc_s.so.1",
		"/lib64/libgcc_s.so.1",
		"/usr/lib64/libgcc_s.so.1",
		"/lib/x86_64-linux-gnu/libstdc++.so.6",
		"/usr/lib/x86_64-linux-gnu/libstdc++.so.6",
		"/lib64/libstdc++.so.6",
		"/usr/lib64/libstdc++.so.6",
		"/lib/x86_64-linux-gnu/libunwind.so.8",
		"/usr/lib/x86_64-linux-gnu/libunwind.so.8",
		"/lib/x86_64-linux-gnu/libunwind-x86_64.so.8",
		"/usr/lib/x86_64-linux-gnu/libunwind-x86_64.so.8"
	};

	private static readonly List<nint> LinuxHarmonyDependencyHandles = new List<nint>();

	private readonly record struct LinuxLibraryLoadResult(string Candidate, bool Loaded, string? Error);

	private static void EnsureLinuxHarmonyDependenciesLoaded()
	{
		if (!OperatingSystem.IsLinux())
		{
			return;
		}
		List<LinuxLibraryLoadResult> loadResults = EnumerateLinuxHarmonyDependencyCandidates()
			.Select(LoadLinuxLibraryGlobally)
			.ToList();
		string[] loadedLibraries = loadResults
			.Where(static result => result.Loaded)
			.Select(static result => result.Candidate)
			.Distinct()
			.ToArray();
		string[] failedLibraries = loadResults
			.Where(static result => !result.Loaded && !string.IsNullOrWhiteSpace(result.Error))
			.Select(static result => $"{result.Candidate} ({result.Error})")
			.Distinct()
			.ToArray();
		if (loadedLibraries.Length > 0)
		{
			Log.Info($"Preloaded Linux Harmony dependencies with RTLD_GLOBAL: {string.Join(", ", loadedLibraries)}");
		}
		else
		{
			Log.Warn(LinuxHarmonyDependencyFailureHelp);
		}
		if (failedLibraries.Length > 0)
		{
			Log.Warn($"Linux Harmony dependency load failures: {string.Join("; ", failedLibraries)}");
		}
	}

	private static IEnumerable<string> EnumerateLinuxHarmonyDependencyCandidates()
	{
		HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
		foreach (string candidate in LinuxHarmonyDependencyCandidates)
		{
			if (!seen.Add(candidate))
			{
				continue;
			}
			if (Path.IsPathRooted(candidate) && !File.Exists(candidate))
			{
				continue;
			}
			yield return candidate;
		}
	}

	private static LinuxLibraryLoadResult LoadLinuxLibraryGlobally(string libraryNameOrPath)
	{
		try
		{
			nint handle = dlopen(libraryNameOrPath, RtldNow | RtldGlobal);
			if (handle != 0)
			{
				LinuxHarmonyDependencyHandles.Add(handle);
				return new LinuxLibraryLoadResult(libraryNameOrPath, Loaded: true, Error: null);
			}
			return new LinuxLibraryLoadResult(libraryNameOrPath, Loaded: false, Error: ReadDlError());
		}
		catch (Exception ex)
		{
			return new LinuxLibraryLoadResult(libraryNameOrPath, Loaded: false, Error: ex.Message);
		}
	}

	private static string? ReadDlError()
	{
		nint errorPtr = dlerror();
		return errorPtr == 0 ? null : Marshal.PtrToStringAnsi(errorPtr);
	}

	[DllImport("libdl.so.2", EntryPoint = "dlopen")]
	private static extern nint dlopen(string fileName, int flags);

	[DllImport("libdl.so.2", EntryPoint = "dlerror")]
	private static extern nint dlerror();
}