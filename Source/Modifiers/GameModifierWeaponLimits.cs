using System;
using System.Collections.Generic;
using System.Linq;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Timers;

namespace GameModifiers.Modifiers;

public abstract class GameModifierRemoveWeapons : GameModifierBase
{
    protected readonly Dictionary<int, List<string>> CachedItems = new();
    private readonly HashSet<int> _pendingReturnSlots = new();

    protected virtual bool ShouldCacheAndRemoveWeapon(CBasePlayerWeapon weapon)
    {
        switch (GameModifiersUtils.GetWeaponType(weapon))
        {
            case CSWeaponType.WEAPONTYPE_PISTOL:
            case CSWeaponType.WEAPONTYPE_SUBMACHINEGUN:
            case CSWeaponType.WEAPONTYPE_RIFLE:
            case CSWeaponType.WEAPONTYPE_SHOTGUN:
            case CSWeaponType.WEAPONTYPE_SNIPER_RIFLE:
            case CSWeaponType.WEAPONTYPE_MACHINEGUN:
            case CSWeaponType.WEAPONTYPE_TASER:
            case CSWeaponType.WEAPONTYPE_GRENADE:
                return true;
            default:
                return false;
        }
    }

    public override void Enabled()
    {
        base.Enabled();
        CachedItems.Clear();
        _pendingReturnSlots.Clear();
        if (Core != null)
        {
            Core.RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
            Core.RegisterListener<Listeners.OnClientDisconnect>(OnClientDisconnect);
        }
        Utilities.GetPlayers().ForEach(RemoveWeaponsOnce);
        if (Core != null && Core._localizer != null)
        {
            var loc = Core._localizer;
            GameModifiersUtils.PrintTitleToChatAll(loc["RemovingItemsMessage"], loc);
        }
    }

    public override void Disabled()
    {
        if (Core != null)
        {
            Core.RemoveListener<Listeners.OnClientDisconnect>(OnClientDisconnect);
        }
        _pendingReturnSlots.Clear();
        foreach (var pair in CachedItems.ToList())
        {
            var player = Utilities.GetPlayerFromSlot(pair.Key);
            if (!TryReturnWeaponsImmediate(player))
            {
                _pendingReturnSlots.Add(pair.Key);
            }
        }
        if (_pendingReturnSlots.Any())
        {
            if (Core != null)
            {
                Core.RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
            }
            if (Core != null && Core._localizer != null)
            {
                var loc = Core._localizer;
                GameModifiersUtils.PrintTitleToChatAll(loc["ReturningItemsDeferredMessage"], loc);
            }
        }
        else
        {
            if (Core != null)
            {
                Core.DeregisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
            }
            if (Core != null && Core._localizer != null)
            {
                var loc = Core._localizer;
                GameModifiersUtils.PrintTitleToChatAll(loc["ReturningItemsMessage"], loc);
            }
            CachedItems.Clear();
        }
        base.Disabled();
    }

    private void RemoveWeaponsOnce(CCSPlayerController? player)
    {
        if (player == null || !player.IsValid) return;
        var pawn = player.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid) return;
        var handles = pawn.WeaponServices?.MyWeapons.ToList();
        if (handles == null) return;
        List<string> cache;
        if (!CachedItems.TryGetValue(player.Slot, out cache!))
        {
            cache = new List<string>();
            CachedItems[player.Slot] = cache;
        }
        List<uint> toKill = new();
        foreach (var h in handles)
        {
            if (!h.IsValid || h.Value == null) continue;
            var w = h.Value;
            if (!ShouldCacheAndRemoveWeapon(w)) continue;
            if (!cache.Contains(w.DesignerName)) cache.Add(w.DesignerName);
            pawn.WeaponServices!.ActiveWeapon.Raw = w.EntityHandle.Raw;
            player.DropActiveWeapon();
            toKill.Add(w.Index);
        }
        if (toKill.Count > 0)
        {
            Server.NextFrame(() =>
            {
                foreach (var idx in toKill)
                {
                    var ent = Utilities.GetEntityFromIndex<CBasePlayerWeapon>((int)idx);
                    if (ent != null && ent.IsValid) ent.AcceptInput("Kill");
                }
            });
        }
    }

    private bool TryReturnWeaponsImmediate(CCSPlayerController? player)
    {
        if (player == null || !player.IsValid || !player.PawnIsAlive) return false;
        if (!CachedItems.TryGetValue(player.Slot, out var items)) return true;
        var pawn = player.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid) return false;
        // strip current conflicting weapons first
        var handles = pawn.WeaponServices?.MyWeapons.ToList();
        if (handles != null)
        {
            List<uint> toKill = new();
            foreach (var h in handles)
            {
                if (!h.IsValid || h.Value == null) continue;
                var w = h.Value;
                if (ShouldCacheAndRemoveWeapon(w))
                {
                    pawn.WeaponServices!.ActiveWeapon.Raw = w.EntityHandle.Raw;
                    player.DropActiveWeapon();
                    toKill.Add(w.Index);
                }
            }
            if (toKill.Count > 0)
            {
                Server.NextFrame(() =>
                {
                    foreach (var idx in toKill)
                    {
                        var ent = Utilities.GetEntityFromIndex<CBasePlayerWeapon>((int)idx);
                        if (ent != null && ent.IsValid) ent.AcceptInput("Kill");
                    }
                });
            }
        }
        var existing = pawn.WeaponServices?.MyWeapons.Where(h => h.IsValid && h.Value != null).Select(h => h.Value!.DesignerName).ToHashSet() ?? new HashSet<string>();
        foreach (var item in items)
        {
            if (!existing.Contains(item)) player.GiveNamedItem(item);
        }
        CachedItems.Remove(player.Slot);
        return true;
    }

    private void TryReturnWeaponsDeferred(CCSPlayerController? player)
    {
        if (TryReturnWeaponsImmediate(player))
        {
            _pendingReturnSlots.Remove(player!.Slot);
            if (!CachedItems.Any() && Core != null)
            {
                Core.DeregisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
            }
        }
    }

    private HookResult OnPlayerSpawn(EventPlayerSpawn e, GameEventInfo info)
    {
        var player = e.Userid;
        if (player == null || !player.IsValid) return HookResult.Continue;
        if (IsActive)
        {
            Server.NextFrame(() => RemoveWeaponsOnce(player));
        }
        else if (_pendingReturnSlots.Contains(player.Slot) || CachedItems.ContainsKey(player.Slot))
        {
            Server.NextFrame(() => TryReturnWeaponsDeferred(player));
        }
        return HookResult.Continue;
    }

    private void OnClientDisconnect(int slot)
    {
        CachedItems.Remove(slot);
        _pendingReturnSlots.Remove(slot);
    }
}

/* KnifeOnly mode disabled per user request */

public class GameModifierRandomWeapon : GameModifierBase
{
    public override bool SupportsRandomRounds => true;
    public override HashSet<string> IncompatibleModifiers =>
    [
        GameModifiersUtils.GetModifierName<GameModifierGrenadesOnly>()
    ];

    private enum RandomCategory { Primary, Pistol }

    private readonly Dictionary<int, List<string>> _originalLoadouts = new();
    private readonly Dictionary<int, DateTime> _lastGrant = new();
    private RandomCategory _chosenCategory;
    private Timer? _enforceTimer;
    private float _interval = 0.25f;
    private int _stableCycles = 0;
    private const int StableThreshold = 8;
    private static readonly TimeSpan GrantCooldown = TimeSpan.FromSeconds(0.5);

    public GameModifierRandomWeapon()
    {
        Name = "RandomWeapon";
        Description = "Buy menu is disabled, random primary OR pistol (one category only)";
    }

    public override void Enabled()
    {
        base.Enabled();
        _originalLoadouts.Clear();
        _lastGrant.Clear();
        _chosenCategory = Random.Shared.Next(2) == 0 ? RandomCategory.Primary : RandomCategory.Pistol;
        CaptureOriginalAll();
        StartLoop();
    }

    public override void Disabled()
    {
        _enforceTimer?.Kill();
        _enforceTimer = null;
        RestoreOriginal();
        _originalLoadouts.Clear();
        _lastGrant.Clear();
        base.Disabled();
    }

    private void CaptureOriginalAll()
    {
        foreach (var p in Utilities.GetPlayers()) CaptureOriginal(p);
    }

    private void CaptureOriginal(CCSPlayerController? player)
    {
        if (player == null || !player.IsValid || !player.PawnIsAlive) return;
        if (_originalLoadouts.ContainsKey(player.Slot)) return;
        var list = new List<string>();
        var pawn = player.PlayerPawn.Value;
        if (pawn?.WeaponServices == null) return;
        foreach (var h in pawn.WeaponServices.MyWeapons)
        {
            if (!h.IsValid || h.Value == null) continue;
            var w = h.Value;
            var t = GameModifiersUtils.GetWeaponType(w);
            if (t == CSWeaponType.WEAPONTYPE_KNIFE || t == CSWeaponType.WEAPONTYPE_C4) continue;
            list.Add(w.DesignerName);
        }
        _originalLoadouts[player.Slot] = list;
    }

    private void StartLoop()
    {
        _stableCycles = 0;
        _interval = 0.25f;
        _enforceTimer?.Kill();
        _enforceTimer = new Timer(_interval, Tick, TimerFlags.REPEAT);
    }

    private void Tick()
    {
        bool changed = false;
        foreach (var p in Utilities.GetPlayers())
        {
            if (p == null || !p.IsValid || !p.PawnIsAlive) continue;
            if (!_originalLoadouts.ContainsKey(p.Slot)) CaptureOriginal(p);
            if (EnforcePlayer(p)) changed = true;
        }
        if (changed)
        {
            _stableCycles = 0;
            if (_interval > 0.25f)
            {
                _enforceTimer?.Kill();
                _interval = 0.25f;
                _enforceTimer = new Timer(_interval, Tick, TimerFlags.REPEAT);
            }
        }
        else
        {
            _stableCycles++;
            if (_stableCycles >= StableThreshold && _interval < 1.0f)
            {
                _enforceTimer?.Kill();
                _interval = 1.0f;
                _enforceTimer = new Timer(_interval, Tick, TimerFlags.REPEAT);
            }
        }
    }

    private bool EnforcePlayer(CCSPlayerController player)
    {
        var pawn = player.PlayerPawn.Value;
        if (pawn?.WeaponServices == null) return false;
        bool changed = false;
        // Categorize current weapons
        List<(CBasePlayerWeapon w, CSWeaponType t)> weapons = new();
        foreach (var h in pawn.WeaponServices.MyWeapons)
        {
            if (!h.IsValid || h.Value == null) continue;
            var w = h.Value;
            weapons.Add((w, GameModifiersUtils.GetWeaponType(w)));
        }

        bool hasCategoryWeapon = false;
        foreach (var (w, t) in weapons)
        {
            if (t == CSWeaponType.WEAPONTYPE_KNIFE || t == CSWeaponType.WEAPONTYPE_C4) continue;
            bool isCategory = _chosenCategory == RandomCategory.Primary
                ? (t is CSWeaponType.WEAPONTYPE_RIFLE or CSWeaponType.WEAPONTYPE_SUBMACHINEGUN or CSWeaponType.WEAPONTYPE_SHOTGUN or CSWeaponType.WEAPONTYPE_SNIPER_RIFLE or CSWeaponType.WEAPONTYPE_MACHINEGUN)
                : (t is CSWeaponType.WEAPONTYPE_PISTOL or CSWeaponType.WEAPONTYPE_TASER);
            if (isCategory)
            {
                if (!hasCategoryWeapon)
                {
                    hasCategoryWeapon = true; // keep first
                    continue;
                }
            }
            // remove any not first category or any other ranged / grenade
            pawn.WeaponServices.ActiveWeapon.Raw = w.EntityHandle.Raw;
            player.DropActiveWeapon();
            var idx = w.Index;
            Server.NextFrame(() =>
            {
                var ent = Utilities.GetEntityFromIndex<CBasePlayerWeapon>((int)idx);
                if (ent != null && ent.IsValid) ent.AcceptInput("Kill");
            });
            changed = true;
        }

        if (!hasCategoryWeapon)
        {
            var now = DateTime.UtcNow;
            if (!_lastGrant.TryGetValue(player.Slot, out var last) || now - last >= GrantCooldown)
            {
                string weaponName = _chosenCategory == RandomCategory.Primary ? GameModifiersUtils.GetRandomPrimaryWeaponName() : GameModifiersUtils.GetRandomPistolWeaponName();
                GameModifiersUtils.GiveAndEquipWeapon(player, weaponName);
                _lastGrant[player.Slot] = now;
                if (Core != null && Core._localizer != null)
                {
                    var loc = Core._localizer;
                    string key = _chosenCategory == RandomCategory.Primary ? "RandomPrimaryWeaponRound" : "RandomPistolWeaponRound";
                    GameModifiersUtils.PrintTitleToChat(player, loc[key, weaponName.Substring(7)], loc);
                }
                changed = true;
            }
        }
        return changed;
    }

    private void RestoreOriginal()
    {
        foreach (var kv in _originalLoadouts.ToList())
        {
            var player = Utilities.GetPlayerFromSlot(kv.Key);
            if (player == null || !player.IsValid || !player.PawnIsAlive) continue;
            var pawn = player.PlayerPawn.Value;
            if (pawn?.WeaponServices == null) continue;
            // strip all non-knife before restore
            foreach (var h in pawn.WeaponServices.MyWeapons.ToList())
            {
                if (!h.IsValid || h.Value == null) continue;
                var w = h.Value;
                var t = GameModifiersUtils.GetWeaponType(w);
                if (t == CSWeaponType.WEAPONTYPE_KNIFE || t == CSWeaponType.WEAPONTYPE_C4) continue;
                pawn.WeaponServices.ActiveWeapon.Raw = w.EntityHandle.Raw;
                player.DropActiveWeapon();
                var idx = w.Index;
                Server.NextFrame(() =>
                {
                    var ent = Utilities.GetEntityFromIndex<CBasePlayerWeapon>((int)idx);
                    if (ent != null && ent.IsValid) ent.AcceptInput("Kill");
                });
            }
            foreach (var orig in kv.Value) player.GiveNamedItem(orig);
        }
    }

    private void OnClientDisconnect(int slot)
    {
        _originalLoadouts.Remove(slot);
        _lastGrant.Remove(slot);
    }
}

public class GameModifierGrenadesOnly : GameModifierBase
{
    public override bool SupportsRandomRounds => true;
    public override HashSet<string> IncompatibleModifiers =>
    [
        GameModifiersUtils.GetModifierName<GameModifierRandomWeapon>()
    ];

    private static readonly string[] Curated = ["weapon_molotov","weapon_smokegrenade","weapon_hegrenade"]; // no flash, no incgrenade
    private readonly Dictionary<int,List<string>> _originalLoadouts = new();
    private readonly HashSet<int> _pendingRestoreDead = new();
    private readonly Dictionary<int,DateTime> _lastGrant = new();
    private Timer? _loop;
    private float _interval = 0.25f;
    private int _stable = 0;
    private const int StableThreshold = 8;
    private static readonly TimeSpan GrantCooldown = TimeSpan.FromSeconds(0.5);

    public GameModifierGrenadesOnly()
    {
        Name = "GrenadesOnly";
        Description = "Only molotov + smoke + HE (no flash / incgrenade).";
    }

    public override void Enabled()
    {
        base.Enabled();
        _originalLoadouts.Clear();
        _pendingRestoreDead.Clear();
        _lastGrant.Clear();
        if (Core != null)
        {
            Core.RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawnActive);
            Core.RegisterListener<Listeners.OnClientDisconnect>(OnClientDisconnect);
        }
        CaptureAll();
        InitialEnforceAll();
        StartLoop();
    }

    public override void Disabled()
    {
        _loop?.Kill();
        _loop = null;
        if (Core != null)
        {
            Core.DeregisterEventHandler<EventPlayerSpawn>(OnPlayerSpawnActive);
            Core.RemoveListener<Listeners.OnClientDisconnect>(OnClientDisconnect);
        }
        RestoreAll();
        if (_pendingRestoreDead.Any() && Core != null)
        {
            Core.RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawnRestoreDisabled);
        }
        base.Disabled();
    }

    private void CaptureAll()
    {
        foreach (var p in Utilities.GetPlayers()) CaptureOriginal(p);
    }

    private void CaptureOriginal(CCSPlayerController? p)
    {
        if (p == null || !p.IsValid) return;
        if (_originalLoadouts.ContainsKey(p.Slot)) return;
        var list = new List<string>();
        if (p.PlayerPawn.Value?.WeaponServices != null)
        {
            foreach (var h in p.PlayerPawn.Value.WeaponServices.MyWeapons)
            {
                if (!h.IsValid || h.Value == null) continue;
                var name = h.Value.DesignerName; if (string.IsNullOrEmpty(name)) continue;
                if (!list.Contains(name)) list.Add(name);
            }
        }
        _originalLoadouts[p.Slot] = list;
    }

    private void InitialEnforceAll()
    {
        foreach (var p in Utilities.GetPlayers())
        {
            if (p == null || !p.IsValid) continue;
            if (p.PawnIsAlive) EnforcePlayer(p, initial:true);
        }
    }

    private void StartLoop()
    {
        _stable = 0; _interval = 0.25f;
        _loop?.Kill();
        _loop = new Timer(_interval, LoopTick, TimerFlags.REPEAT);
    }

    private void LoopTick()
    {
        bool changed = false;
        foreach (var p in Utilities.GetPlayers())
        {
            if (p == null || !p.IsValid || !p.PawnIsAlive) continue;
            if (!_originalLoadouts.ContainsKey(p.Slot)) CaptureOriginal(p);
            if (EnforcePlayer(p)) changed = true;
        }
        if (changed)
        {
            _stable = 0;
            if (_interval > 0.25f)
            {
                _loop?.Kill(); _interval = 0.25f; _loop = new Timer(_interval, LoopTick, TimerFlags.REPEAT);
            }
        }
        else
        {
            _stable++;
            if (_stable >= StableThreshold && _interval < 1.0f)
            {
                _loop?.Kill(); _interval = 1.0f; _loop = new Timer(_interval, LoopTick, TimerFlags.REPEAT);
            }
        }
    }

    private bool EnforcePlayer(CCSPlayerController p, bool initial = false)
    {
        var pawn = p.PlayerPawn.Value;
        if (pawn?.WeaponServices == null) return false;
        bool changed = false;
        List<uint> toKill = new();
        // Pass 1: Remove any weapon not allowed (allowed: knife, c4, curated molotov/smoke/he)
        var allowed = Curated; // convenience
        foreach (var h in pawn.WeaponServices.MyWeapons.ToList())
        {
            if (!h.IsValid || h.Value == null) continue;
            var w = h.Value;
            var name = w.DesignerName ?? string.Empty;
            var t = GameModifiersUtils.GetWeaponType(w);
            bool keep = false;
            if (t == CSWeaponType.WEAPONTYPE_KNIFE || t == CSWeaponType.WEAPONTYPE_C4)
                keep = true;
            else if (t == CSWeaponType.WEAPONTYPE_GRENADE)
            {
                // Keep only curated subset; treat incgrenade, flashbang, decoy etc. as removable
                if (allowed.Contains(name, StringComparer.OrdinalIgnoreCase)) keep = true;
            }
            if (!keep)
            {
                pawn.WeaponServices.ActiveWeapon.Raw = w.EntityHandle.Raw;
                p.DropActiveWeapon();
                toKill.Add(w.Index);
                changed = true;
            }
        }
        if (toKill.Count > 0)
        {
            Server.NextFrame(() =>
            {
                foreach (var idx in toKill)
                {
                    var ent = Utilities.GetEntityFromIndex<CBasePlayerWeapon>((int)idx);
                    if (ent != null && ent.IsValid) ent.AcceptInput("Kill");
                }
            });
        }
        // Pass 2: Grant curated set if missing (respect cooldown)
        var existing = pawn.WeaponServices.MyWeapons.Where(h => h.IsValid && h.Value != null)
            .Select(h => h.Value!.DesignerName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var now = DateTime.UtcNow;
        foreach (var g in Curated)
        {
            if (!existing.Contains(g))
            {
                if (!_lastGrant.TryGetValue(p.Slot, out var last) || now - last >= GrantCooldown || initial)
                {
                    GameModifiersUtils.GiveAndEquipWeapon(p, g);
                    _lastGrant[p.Slot] = now;
                    changed = true;
                }
            }
        }
        return changed;
    }

    private void RestoreAll()
    {
        foreach (var kv in _originalLoadouts.ToList())
        {
            var p = Utilities.GetPlayerFromSlot(kv.Key);
            if (p == null || !p.IsValid) continue;
            if (p.PawnIsAlive) RestorePlayer(p, kv.Value);
            else _pendingRestoreDead.Add(kv.Key);
        }
    }

    private void RestorePlayer(CCSPlayerController p, List<string> items)
    {
        var pawn = p.PlayerPawn.Value;
        if (pawn?.WeaponServices == null) return;
        List<uint> toKill = new();
        foreach (var h in pawn.WeaponServices.MyWeapons.ToList())
        {
            if (!h.IsValid || h.Value == null) continue;
            var w = h.Value;
            var name = w.DesignerName ?? string.Empty;
            if (Curated.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                pawn.WeaponServices.ActiveWeapon.Raw = w.EntityHandle.Raw;
                p.DropActiveWeapon();
                toKill.Add(w.Index);
            }
        }
        if (toKill.Count > 0)
        {
            Server.NextFrame(() =>
            {
                foreach (var idx in toKill)
                {
                    var ent = Utilities.GetEntityFromIndex<CBasePlayerWeapon>((int)idx);
                    if (ent != null && ent.IsValid) ent.AcceptInput("Kill");
                }
            });
        }
        var existing = pawn.WeaponServices.MyWeapons.Where(h => h.IsValid && h.Value != null)
            .Select(h => h.Value!.DesignerName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var it in items)
        {
            if (!existing.Contains(it))
            {
                p.GiveNamedItem(it);
                existing.Add(it);
            }
        }
    }

    private HookResult OnPlayerSpawnActive(EventPlayerSpawn ev, GameEventInfo info)
    {
        var p = ev.Userid;
        if (p == null || !p.IsValid) return HookResult.Continue;
        if (!IsActive) return HookResult.Continue;
        Server.NextFrame(() =>
        {
            if (!p.IsValid || !p.PawnIsAlive) return;
            CaptureOriginal(p);
            EnforcePlayer(p, initial:true);
        });
        return HookResult.Continue;
    }

    private HookResult OnPlayerSpawnRestoreDisabled(EventPlayerSpawn ev, GameEventInfo info)
    {
        var p = ev.Userid;
        if (p == null || !p.IsValid) return HookResult.Continue;
        if (IsActive) return HookResult.Continue;
        if (_pendingRestoreDead.Contains(p.Slot) && _originalLoadouts.TryGetValue(p.Slot, out var items))
        {
            Server.NextFrame(() => RestorePlayer(p, items));
            _pendingRestoreDead.Remove(p.Slot);
            if (!_pendingRestoreDead.Any() && Core != null)
            {
                Core.DeregisterEventHandler<EventPlayerSpawn>(OnPlayerSpawnRestoreDisabled);
            }
        }
        return HookResult.Continue;
    }

    private void OnClientDisconnect(int slot)
    {
        _originalLoadouts.Remove(slot);
        _pendingRestoreDead.Remove(slot);
        _lastGrant.Remove(slot);
    }
}
// End of file
