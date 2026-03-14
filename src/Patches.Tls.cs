using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;

namespace RemoveMultiplayerPlayerLimit;

public static partial class ModEntry
{
	private static bool MacOsTlsWorkaroundLogged { get; set; }

	[HarmonyPatch]
	private static class MacOsMultiplayerTlsClientPatch
	{
		private static IEnumerable<MethodBase> TargetMethods()
		{
			return typeof(TlsOptions)
				.GetMethods(BindingFlags.Public | BindingFlags.Static)
				.Where((MethodInfo method) => method.Name == nameof(TlsOptions.Client) && method.ReturnType == typeof(TlsOptions));
		}

		private static bool Prefix(ref TlsOptions __result, object[] __args)
		{
			if (!ShouldBypassMacOsTlsValidation())
			{
				return true;
			}
			if (!MacOsTlsWorkaroundLogged)
			{
				Log.Warn("Applying macOS multiplayer TLS workaround: using an unsafe TLS client to bypass BadCert/unknown ca handshake failures.");
				MacOsTlsWorkaroundLogged = true;
			}
			__result = CreateUnsafeTlsOptions(ExtractTrustedChain(__args));
			return false;
		}
	}

	private static bool ShouldBypassMacOsTlsValidation()
	{
		if (!OperatingSystem.IsMacOS() || !MacOsTlsWorkaroundEnabled)
		{
			return false;
		}
		// Restrict the workaround to multiplayer call sites so unrelated TLS traffic keeps normal verification.
		StackTrace stackTrace = new StackTrace(false);
		foreach (StackFrame frame in stackTrace.GetFrames() ?? Array.Empty<StackFrame>())
		{
			string? declaringTypeName = frame.GetMethod()?.DeclaringType?.FullName;
			if (string.IsNullOrWhiteSpace(declaringTypeName))
			{
				continue;
			}
			if (declaringTypeName.StartsWith("MegaCrit.Sts2.Core.Multiplayer.", StringComparison.Ordinal) ||
				declaringTypeName.StartsWith("MegaCrit.Sts2.Core.Platform.Steam.SteamJoinCallbackHandler", StringComparison.Ordinal) ||
				declaringTypeName.StartsWith("MegaCrit.Sts2.Core.Nodes.Screens.MainMenu.NJoinFriendScreen", StringComparison.Ordinal) ||
				declaringTypeName.StartsWith("MegaCrit.Sts2.Core.Nodes.Screens.MainMenu.NMultiplayer", StringComparison.Ordinal) ||
				declaringTypeName.StartsWith("MegaCrit.Sts2.Core.Nodes.Debug.Multiplayer.", StringComparison.Ordinal))
			{
				return true;
			}
		}
		return false;
	}

	private static X509Certificate? ExtractTrustedChain(object[] args)
	{
		foreach (object? arg in args)
		{
			if (arg is X509Certificate certificate)
			{
				return certificate;
			}
		}
		return null;
	}

	private static TlsOptions CreateUnsafeTlsOptions(X509Certificate? trustedChain)
	{
		MethodInfo? withCertificate = AccessTools.Method(typeof(TlsOptions), nameof(TlsOptions.ClientUnsafe), new[] { typeof(X509Certificate) });
		if (withCertificate != null)
		{
			return (TlsOptions)withCertificate.Invoke(null, new object?[] { trustedChain })!;
		}
		MethodInfo? withoutParameters = AccessTools.Method(typeof(TlsOptions), nameof(TlsOptions.ClientUnsafe), Type.EmptyTypes);
		if (withoutParameters != null)
		{
			return (TlsOptions)withoutParameters.Invoke(null, Array.Empty<object>())!;
		}
		throw new MissingMethodException(typeof(TlsOptions).FullName, nameof(TlsOptions.ClientUnsafe));
	}
}
