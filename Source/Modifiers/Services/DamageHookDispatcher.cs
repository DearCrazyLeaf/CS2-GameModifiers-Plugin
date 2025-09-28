using System;
using System.Collections.Generic;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Core;

namespace GameModifiers.Modifiers;

public static class DamageHookDispatcher
{
    private static readonly object _lock = new();
    private static readonly Dictionary<Guid, Action<CTakeDamageInfo>> _subscribers = new();
    private static bool _hookInstalled = false;

    public static Guid Add(Action<CTakeDamageInfo> callback)
    {
        if (callback == null) throw new ArgumentNullException(nameof(callback));
        lock (_lock)
        {
            Guid id = Guid.NewGuid();
            _subscribers[id] = callback;
            if (!_hookInstalled)
            {
                try
                {
                    VirtualFunctions.CBaseEntity_TakeDamageOldFunc.Hook(OnTakeDamage, HookMode.Pre);
                    _hookInstalled = true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DamageHookDispatcher] Failed to install hook: {ex.Message}");
                }
            }
            return id;
        }
    }

    public static void Remove(Guid id)
    {
        lock (_lock)
        {
            if (_subscribers.Remove(id) && _subscribers.Count == 0 && _hookInstalled)
            {
                try
                {
                    VirtualFunctions.CBaseEntity_TakeDamageOldFunc.Unhook(OnTakeDamage, HookMode.Pre);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DamageHookDispatcher] Failed to unhook: {ex.Message}");
                }
                _hookInstalled = false;
            }
        }
    }

    private static HookResult OnTakeDamage(DynamicHook hook)
    {
        CTakeDamageInfo info;
        try
        {
            info = hook.GetParam<CTakeDamageInfo>(1);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DamageHookDispatcher] Failed to read damage info: {ex.Message}");
            return HookResult.Continue;
        }

        KeyValuePair<Guid, Action<CTakeDamageInfo>>[] list;
        lock (_lock)
        {
            if (_subscribers.Count == 0) return HookResult.Continue;
            list = new KeyValuePair<Guid, Action<CTakeDamageInfo>>[_subscribers.Count];
            int i = 0;
            foreach (var kv in _subscribers)
            {
                list[i++] = kv;
            }
        }

        foreach (var kv in list)
        {
            try { kv.Value(info); }
            catch (Exception ex) { Console.WriteLine($"[DamageHookDispatcher] Subscriber threw: {ex.Message}"); }
        }
        return HookResult.Continue;
    }
}
