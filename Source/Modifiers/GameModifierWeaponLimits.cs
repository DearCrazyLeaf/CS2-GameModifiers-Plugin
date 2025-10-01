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

    private sealed class GrantedInfo
    {
        public string Name = string.Empty;
        public int EntityIndex = -1; // tracked for stability even if designer name duplicates
        public DateTime LastGrantUtc = DateTime.MinValue;
    }

    protected readonly Dictionary<int, List<string>> _originalLoadouts = new();
    private readonly Dictionary<int, GrantedInfo> _granted = new();
    private RandomCategory _chosenCategory;
    private Timer? _enforceTimer;
    private float _interval = 0.25f;
    private int _stableCycles = 0;
    private const int StableThreshold = 8;
    private static readonly TimeSpan RegrantCooldown = TimeSpan.FromSeconds(0.5);

    public GameModifierRandomWeapon()
    {
        Name = "RandomWeapon";
        Description = "Buy menu is disabled, random primary OR pistol (one category only)";
    }

    public override void Enabled()
    {
        base.Enabled();
        _originalLoadouts.Clear();
        _granted.Clear();
        _chosenCategory = Random.Shared.Next(2) == 0 ? RandomCategory.Primary : RandomCategory.Pistol;
        CaptureInitial();
        StartLoop();
    }

    public override void Disabled()
    {
        _enforceTimer?.Kill();
        _enforceTimer = null;
        RestoreOriginalForAlivePlayers();
        _originalLoadouts.Clear();
        _granted.Clear();
        base.Disabled();
    }

    private void CaptureInitial()
    {
        foreach (var p in Utilities.GetPlayers())
        {
            if (p == null || !p.IsValid || !p.PawnIsAlive) continue;
            CaptureOriginal(p);
            EnsureGrantAfterClean(p, force:true);
        }
    }

    private void StartLoop()
    {
        _enforceTimer?.Kill();
        _interval = 0.25f;
        _stableCycles = 0;
        _enforceTimer = new Timer(_interval, EnforcementTick, TimerFlags.REPEAT);
    }

    private void EnforcementTick()
    {
        bool anyChange = false;
        foreach (var p in Utilities.GetPlayers())
        {
            if (p == null || !p.IsValid || !p.PawnIsAlive) continue;
            if (!_originalLoadouts.ContainsKey(p.Slot)) CaptureOriginal(p);
            if (CleanIllegal(p)) anyChange = true;
            if (EnsureGrantAfterClean(p)) anyChange = true; // grant only after cleaning
        }
        if (anyChange)
        {
            _stableCycles = 0;
            if (_interval > 0.25f)
            {
                _enforceTimer?.Kill();
                _interval = 0.25f;
                _enforceTimer = new Timer(_interval, EnforcementTick, TimerFlags.REPEAT);
            }
        }
        else
        {
            _stableCycles++;
            if (_stableCycles >= StableThreshold && _interval < 1.0f)
            {
                _enforceTimer?.Kill();
                _interval = 1.0f;
                _enforceTimer = new Timer(_interval, EnforcementTick, TimerFlags.REPEAT);
            }
        }
    }

    private void CaptureOriginal(CCSPlayerController player)
    {
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

    // Remove all non-knife, non-granted weapons. Return true if changes occurred.
    private bool CleanIllegal(CCSPlayerController player)
    {
        var pawn = player.PlayerPawn.Value;
        if (pawn?.WeaponServices == null) return false;
        _granted.TryGetValue(player.Slot, out var gi);
        bool changed = false;
        List<uint> toKill = new();
        foreach (var h in pawn.WeaponServices.MyWeapons.ToList())
        {
            if (!h.IsValid || h.Value == null) continue;
            var w = h.Value;
            string name = w.DesignerName ?? string.Empty;
            if (name.Contains("knife", StringComparison.OrdinalIgnoreCase)) continue;
            // keep granted if entity index matches OR name matches
            if (gi != null && (w.Index == gi.EntityIndex || name.Equals(gi.Name, StringComparison.OrdinalIgnoreCase))) continue;
            // otherwise remove
            pawn.WeaponServices.ActiveWeapon.Raw = w.EntityHandle.Raw;
            player.DropActiveWeapon();
            toKill.Add(w.Index);
            changed = true;
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
        return changed;
    }

    // Ensure player has granted weapon after cleaning. Returns true if we granted (change state).
    private bool EnsureGrantAfterClean(CCSPlayerController player, bool force = false)
    {
        var now = DateTime.UtcNow;
        var pawn = player.PlayerPawn.Value;
        if (pawn?.WeaponServices == null) return false;

        _granted.TryGetValue(player.Slot, out var gi);
        bool hasGranted = false;
        if (gi != null)
        {
            foreach (var h in pawn.WeaponServices.MyWeapons)
            {
                if (!h.IsValid || h.Value == null) continue;
                var w = h.Value;
                if (w.Index == gi.EntityIndex || (gi.EntityIndex == -1 && w.DesignerName != null && w.DesignerName.Equals(gi.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    hasGranted = true;
                    break;
                }
            }
        }
        if (!hasGranted && gi != null)
        {
            // check cooldown
            if (!force && now - gi.LastGrantUtc < RegrantCooldown) return false;
        }
        if (hasGranted && !force) return false; // nothing to do

        // Need to grant
        string weaponName;
        if (gi == null)
        {
            weaponName = _chosenCategory == RandomCategory.Primary ? GameModifiersUtils.GetRandomPrimaryWeaponName() : GameModifiersUtils.GetRandomPistolWeaponName();
            gi = new GrantedInfo { Name = weaponName };
            _granted[player.Slot] = gi;
        }
        else
        {
            weaponName = gi.Name;
        }

        var given = GameModifiersUtils.GiveAndEquipWeapon(player, weaponName);
        gi.LastGrantUtc = now;
        gi.EntityIndex = given != null && given.IsValid ? (int)given.Index : -1;
        if (Core != null && Core._localizer != null && force) // announce only first time to reduce spam
        {
            var loc = Core._localizer;
            string key = _chosenCategory == RandomCategory.Primary ? "RandomPrimaryWeaponRound" : "RandomPistolWeaponRound";
            GameModifiersUtils.PrintTitleToChat(player, loc[key, weaponName.Substring(7)], loc);
        }
        return true;
    }

    private void RestoreOriginalForAlivePlayers()
    {
        foreach (var kv in _originalLoadouts.ToList())
        {
            var player = Utilities.GetPlayerFromSlot(kv.Key);
            if (player == null || !player.IsValid || !player.PawnIsAlive) continue;
            var pawn = player.PlayerPawn.Value;
            if (pawn?.WeaponServices != null && _granted.TryGetValue(player.Slot, out var gi))
            {
                foreach (var h in pawn.WeaponServices.MyWeapons.ToList())
                {
                    if (!h.IsValid || h.Value == null) continue;
                    var w = h.Value;
                    if (w.Index == gi.EntityIndex || (w.DesignerName != null && w.DesignerName.Equals(gi.Name, StringComparison.OrdinalIgnoreCase)))
                    {
                        pawn.WeaponServices.ActiveWeapon.Raw = w.EntityHandle.Raw;
                        player.DropActiveWeapon();
                        var idx = w.Index;
                        Server.NextFrame(() =>
                        {
                            var ent = Utilities.GetEntityFromIndex<CBasePlayerWeapon>((int)idx);
                            if (ent != null && ent.IsValid) ent.AcceptInput("Kill");
                        });
                    }
                }
            }
            foreach (var orig in kv.Value)
            {
                player.GiveNamedItem(orig);
            }
        }
    }

    private void OnClientDisconnect(int slot)
    {
        _originalLoadouts.Remove(slot);
        _granted.Remove(slot);
    }
}

public class GameModifierGrenadesOnly : GameModifierRemoveWeapons
{
    public override bool SupportsRandomRounds => true;
    public override HashSet<string> IncompatibleModifiers =>
    [
        GameModifiersUtils.GetModifierName<GameModifierRandomWeapon>()
    ];

    private Timer? _loop;
    private float _interval = 0.25f;
    private int _stable = 0;
    private const int StableThreshold = 8;
    private readonly Dictionary<int, DateTime> _lastGrant = new();
    private static readonly TimeSpan GrenadeRegrantCooldown = TimeSpan.FromSeconds(0.5);
    private static readonly string[] RequiredGrenades = ["weapon_molotov","weapon_smokegrenade","weapon_hegrenade","weapon_flashbang"];

    protected override bool ShouldCacheAndRemoveWeapon(CBasePlayerWeapon weapon)
    {
        var t = GameModifiersUtils.GetWeaponType(weapon);
        return t switch
        {
            CSWeaponType.WEAPONTYPE_GRENADE => false,
            CSWeaponType.WEAPONTYPE_C4 => false,
            _ => t != CSWeaponType.WEAPONTYPE_UNKNOWN
        };
    }

    public GameModifierGrenadesOnly()
    {
        Name = "GrenadesOnly";
        Description = "Buy menu is disabled, grenades only";
    }

    public override void Enabled()
    {
        base.Enabled();
        GiveInitial();
        StartLoop();
    }

    public override void Disabled()
    {
        _loop?.Kill();
        _loop = null;
        _lastGrant.Clear();
        base.Disabled();
    }

    private void GiveInitial()
    {
        foreach (var p in Utilities.GetPlayers())
        {
            if (p == null || !p.IsValid || !p.PawnIsAlive) continue;
            foreach (var g in RequiredGrenades)
            {
                GameModifiersUtils.GiveAndEquipWeapon(p, g);
            }
            _lastGrant[p.Slot] = DateTime.UtcNow;
        }
    }

    private void StartLoop()
    {
        _loop?.Kill();
        _interval = 0.25f;
        _stable = 0;
        _loop = new Timer(_interval, LoopTick, TimerFlags.REPEAT);
    }

    private void LoopTick()
    {
        bool anyChange = false;
        var now = DateTime.UtcNow;
        foreach (var p in Utilities.GetPlayers())
        {
            if (p == null || !p.IsValid || !p.PawnIsAlive) continue;
            if (CleanIllegalGrenadePlayer(p)) anyChange = true;
            if (EnsureGrenades(p, now)) anyChange = true;
        }
        if (anyChange)
        {
            _stable = 0;
            if (_interval > 0.25f)
            {
                _loop?.Kill();
                _interval = 0.25f;
                _loop = new Timer(_interval, LoopTick, TimerFlags.REPEAT);
            }
        }
        else
        {
            _stable++;
            if (_stable >= StableThreshold && _interval < 1.0f)
            {
                _loop?.Kill();
                _interval = 1.0f;
                _loop = new Timer(_interval, LoopTick, TimerFlags.REPEAT);
            }
        }
    }

    private bool CleanIllegalGrenadePlayer(CCSPlayerController player)
    {
        var pawn = player.PlayerPawn.Value;
        if (pawn?.WeaponServices == null) return false;
        bool changed = false;
        List<uint> toKill = new();
        foreach (var h in pawn.WeaponServices.MyWeapons.ToList())
        {
            if (!h.IsValid || h.Value == null) continue;
            var w = h.Value;
            var t = GameModifiersUtils.GetWeaponType(w);
            if (t == CSWeaponType.WEAPONTYPE_GRENADE || t == CSWeaponType.WEAPONTYPE_C4) continue;
            pawn.WeaponServices.ActiveWeapon.Raw = w.EntityHandle.Raw;
            player.DropActiveWeapon();
            toKill.Add(w.Index);
            changed = true;
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
        return changed;
    }

    private bool EnsureGrenades(CCSPlayerController player, DateTime now)
    {
        var pawn = player.PlayerPawn.Value;
        if (pawn?.WeaponServices == null) return false;
        var existing = pawn.WeaponServices.MyWeapons.Where(h => h.IsValid && h.Value != null)
            .Select(h => h.Value!.DesignerName).ToHashSet();
        bool changed = false;
        foreach (var g in RequiredGrenades)
        {
            if (!existing.Contains(g))
            {
                if (!_lastGrant.TryGetValue(player.Slot, out var last) || now - last >= GrenadeRegrantCooldown)
                {
                    GameModifiersUtils.GiveAndEquipWeapon(player, g);
                    _lastGrant[player.Slot] = now;
                    changed = true;
                }
            }
        }
        return changed;
    }
}
