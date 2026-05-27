using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Modding;

namespace RemoveMultiplayerPlayerLimit.Infrastructure;

/// <summary>
/// Central reflection cache — all game-internal access goes through here.
/// Caches FieldInfo/MethodInfo/PropertyInfo/Type lookups for performance.
/// </summary>
public class ReflectionCache
{
    private readonly Dictionary<string, FieldInfo?> _fields = new();
    private readonly Dictionary<string, MethodInfo?> _methods = new();
    private readonly Dictionary<string, PropertyInfo?> _properties = new();
    private readonly Dictionary<string, Type?> _types = new();

    private readonly Assembly _gameAssembly;

    public ReflectionCache()
    {
        _gameAssembly = typeof(ModInitializerAttribute).Assembly;
    }

    // ── Type ──────��───────────────────────────────────────────────────

    public Type? GetType(string fullName)
    {
        if (!_types.TryGetValue(fullName, out var type))
        {
            type = _gameAssembly.GetType(fullName);
            _types[fullName] = type;
        }
        return type;
    }

    // ── Field ──────────────────────────────────��──────────────────────

    public FieldInfo? GetField(string typeName, string fieldName)
    {
        string key = $"{typeName}.{fieldName}";
        if (!_fields.TryGetValue(key, out var field))
        {
            var type = GetType(typeName);
            field = type?.GetField(fieldName,
                BindingFlags.Instance | BindingFlags.Static |
                BindingFlags.Public | BindingFlags.NonPublic);
            _fields[key] = field;
        }
        return field;
    }

    public FieldInfo? GetField(Type type, string fieldName)
    {
        string key = $"{type.FullName}.{fieldName}";
        if (!_fields.TryGetValue(key, out var field))
        {
            field = type.GetField(fieldName,
                BindingFlags.Instance | BindingFlags.Static |
                BindingFlags.Public | BindingFlags.NonPublic);
            _fields[key] = field;
        }
        return field;
    }

    // ── Method ───────���────────────────────────────────────────────────

    public MethodInfo? GetMethod(string typeName, string methodName, Type[]? paramTypes = null)
    {
        string key = paramTypes == null
            ? $"{typeName}.{methodName}"
            : $"{typeName}.{methodName}({string.Join(",", paramTypes.Select(t => t.Name))})";

        if (!_methods.TryGetValue(key, out var method))
        {
            var type = GetType(typeName);
            method = paramTypes == null
                ? type?.GetMethod(methodName,
                    BindingFlags.Instance | BindingFlags.Static |
                    BindingFlags.Public | BindingFlags.NonPublic)
                : type?.GetMethod(methodName,
                    BindingFlags.Instance | BindingFlags.Static |
                    BindingFlags.Public | BindingFlags.NonPublic,
                    null, paramTypes, null);
            _methods[key] = method;
        }
        return method;
    }

    public MethodInfo? GetMethod(Type type, string methodName, Type[]? paramTypes = null)
    {
        return GetMethod(type.FullName!, methodName, paramTypes);
    }

    // ── Property ───���──────────────────────────────────────────────────

    public PropertyInfo? GetProperty(string typeName, string propertyName)
    {
        string key = $"{typeName}.{propertyName}";
        if (!_properties.TryGetValue(key, out var prop))
        {
            var type = GetType(typeName);
            prop = type?.GetProperty(propertyName,
                BindingFlags.Instance | BindingFlags.Static |
                BindingFlags.Public | BindingFlags.NonPublic);
            _properties[key] = prop;
        }
        return prop;
    }

    public PropertyInfo? GetProperty(Type type, string propertyName)
    {
        return GetProperty(type.FullName!, propertyName);
    }

    // ��─ Safe helpers ─────────��────────────────────────────────────────

    public bool TrySetField(object? target, string typeName, string fieldName, object? value)
    {
        var field = GetField(typeName, fieldName);
        if (field == null)
        {
            GD.PrintErr($"[RMP] Field not found: {typeName}.{fieldName}");
            return false;
        }
        field.SetValue(target, value);
        return true;
    }

    public T? TryGetField<T>(object? target, string typeName, string fieldName, T? fallback = default)
    {
        var field = GetField(typeName, fieldName);
        if (field == null) return fallback;
        var value = field.GetValue(target);
        return value is T typed ? typed : fallback;
    }

    public bool TrySetField(object? target, Type type, string fieldName, object? value)
    {
        return TrySetField(target, type.FullName!, fieldName, value);
    }

    public T? TryGetField<T>(object? target, Type type, string fieldName, T? fallback = default)
    {
        return TryGetField(target, type.FullName!, fieldName, fallback);
    }

    public object? TryInvokeMethod(object? target, string typeName, string methodName, object?[]? args = null)
    {
        var method = GetMethod(typeName, methodName);
        if (method == null)
        {
            GD.PrintErr($"[RMP] Method not found: {typeName}.{methodName}");
            return null;
        }
        return method.Invoke(target, args);
    }
}
