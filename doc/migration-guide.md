# Harmony → Reflection Migration Reference

## Migration Map

This document maps every Harmony patch type to its reflection/SceneTree replacement.
Use this as a checklist when converting existing patches.

---

## Category 1: Value Overrides

**Harmony pattern**: Postfix that modifies `__result` or `ref` parameter.

**Reflection replacement**: Periodic field/property write via `_Process()`.

```
Harmony:
  [HarmonyPatch(typeof(X), "get_SomeProperty")]
  static void Postfix(ref int __result) => __result = newValue;

Reflection:
  // In _Process():
  var field = cache.GetField("Namespace.X", "someBackingField");
  if ((int)field.GetValue(instance) != desiredValue)
      field.SetValue(instance, desiredValue);
```

**Throttling**: Don't check every frame. Use a frame counter:
```csharp
private int _frameCounter = 0;
public override void _Process(double delta)
{
    if (++_frameCounter % 30 != 0) return; // Check every 30 frames (~0.5s at 60fps)
    // ... reflection check ...
}
```

---

## Category 2: Method Hooks (Prefix — before method runs)

**Harmony pattern**: Prefix that runs code before the original method, optionally skipping it.

**Reflection replacement**: State transition detection via polling.

```
Harmony:
  [HarmonyPatch(typeof(X), "DoSomething")]
  static bool Prefix(X __instance) {
      MyCode();
      return false; // skip original
  }

Reflection — can't skip original, but can:
  1. Detect when the state changes (before/after the method runs)
  2. Override the state immediately after
  3. Monitor the preconditions and act before the game does

  // In _Process():
  if (StateChangedSinceLastFrame())
      OverrideState();
```

**Important**: Pure reflection cannot prevent a method from executing. If you need to truly intercept, consider whether you can achieve the same result by immediately overriding the output state instead.

---

## Category 3: Method Hooks (Postfix — after method runs)

**Harmony pattern**: Postfix that runs code after the original method.

**Reflection replacement**: State transition detection.

```
Harmony:
  [HarmonyPatch(typeof(X), "Initialize")]
  static void Postfix(X __instance) { ModifyState(__instance); }

Reflection:
  // Detect when the thing was initialized
  // _Process() checks for "newly appeared" instances
  private HashSet<int> _processedIds = new();

  public override void _Process(double delta)
  {
      var instances = FindAllInstances();
      foreach (var inst in instances)
      {
          var id = inst.GetHashCode();
          if (!_processedIds.Contains(id))
          {
              _processedIds.Add(id);
              ModifyState(inst); // Our "postfix" equivalent
          }
      }
  }
```

---

## Category 4: UI Injection (was Postfix on UI build)

**Harmony pattern**: Postfix on UI setup method to add custom controls.

**Reflection replacement**: SceneTree monitoring + node injection.

```
Harmony:
  [HarmonyPatch(typeof(SettingsScreen), "BuildUI")]
  static void Postfix(SettingsScreen __instance)
  { __instance.AddChild(myControl); }

SceneTree:
  public override void _Process(double delta)
  {
      if (_alreadyInjected) return;

      var settingsScreen = FindNodeByName(
          ((SceneTree)Engine.GetMainLoop()).Root, "SettingsScreen");
      if (settingsScreen == null) return;

      settingsScreen.AddChild(myControl);
      _alreadyInjected = true;
  }
```

**Reset**: Set `_alreadyInjected = false` when the scene changes (settings closed).

---

## Category 5: Network Interception

**Harmony pattern**: Patch on network send/receive to modify packets.

**Reflection replacement**: Custom network channel running in parallel.

```
Harmony:
  [HarmonyPatch(typeof(NetworkManager), "SendPacket")]
  static void Prefix(ref byte[] data) { /* modify packet */ }

Custom Channel:
  // Don't intercept game packets — add our own channel
  [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = ...)]
  public void SendRMPPacket(byte[] data) { /* our protocol */ }

  // The RMP protocol runs alongside game packets, not modifying them
```

---

## Performance Considerations

| Approach | Cost per Frame | When to Use |
|----------|---------------|-------------|
| `_Process` every frame | ~0.01ms | UI state that changes instantly |
| `_Process` every 30 frames | ~0.0003ms avg | Value overrides, slow-changing state |
| `_Process` every 60 frames | ~0.00016ms avg | Infrequent checks (config reload) |
| Signal-based (Godot signals) | 0ms (event-driven) | When game exposes public signals |
| One-time injection | 0ms after setup | Scene modifications, UI injection |

**Rule**: Always start with the least frequent check that works. Optimize later if needed.

---

## Null Safety

Every reflection call can return null if the game updates and renames things:

```csharp
// WRONG — will crash on game update
var field = cache.GetField("Namespace.X", "someField");
field.SetValue(target, value); // NullReferenceException if field was renamed

// RIGHT — graceful degradation
if (!cache.TrySetField(target, "Namespace.X", "someField", value))
{
    // Log once, don't spam
    if (!_loggedFieldMissing)
    {
        Log.Warn("[RMP] Field 'someField' not found — game version may be incompatible");
        _loggedFieldMissing = true;
    }
}
```
