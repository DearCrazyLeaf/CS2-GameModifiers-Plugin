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
    protected readonly Dictionary<int, List<string>> _originalLoadouts = new();
    protected readonly Dictionary<int, string> _granted = new();
    private RandomCategory _chosenCategory;
    private Timer? _enforceTimer;
    private float _interval = 0.25f;
    private int _stableCycles = 0;
    private const int StableThreshold = 8; // after this with zero changes slow down

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
        ApplyInitial();
        StartEnforcementLoop();
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

    private void ApplyInitial()
    {
        foreach (var p in Utilities.GetPlayers())
        {
            if (p == null || !p.IsValid || !p.PawnIsAlive) continue;
            CaptureIfNeeded(p);
            GiveIfMissing(p);
        }
    }

    private void StartEnforcementLoop()
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
            if (!_originalLoadouts.ContainsKey(p.Slot)) CaptureIfNeeded(p);
            if (EnforcePlayer(p)) anyChange = true;
        }
        if (anyChange)
        {
            _stableCycles = 0;
            if (_interval > 0.25f)
            {
                // Speed up again
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
                // Slow down
                _enforceTimer?.Kill();
                _interval = 1.0f;
                _enforceTimer = new Timer(_interval, EnforcementTick, TimerFlags.REPEAT);
            }
        }
    }

    private void CaptureIfNeeded(CCSPlayerController player)
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
            if (t == CSWeaponType.WEAPONTYPE_KNIFE || t == CSWeaponType.WEAPONTYPE_C4) continue; // don't cache
            // cache all removable ranged / utility weapons
            list.Add(w.DesignerName);
        }
        _originalLoadouts[player.Slot] = list;
    }

    private bool EnforcePlayer(CCSPlayerController player)
    {
        bool changed = false;
        var pawn = player.PlayerPawn.Value;
        if (pawn?.WeaponServices == null) return false;
        string granted = GiveIfMissing(player);
        var handles = pawn.WeaponServices.MyWeapons.ToList();
        List<uint> toKill = new();
        foreach (var h in handles)
        {
            if (!h.IsValid || h.Value == null) continue;
            var w = h.Value;
            var name = w.DesignerName;
            if (string.IsNullOrEmpty(name)) continue;
            if (name.Contains("knife", StringComparison.OrdinalIgnoreCase)) continue; // keep knife
            if (name.Equals(granted, StringComparison.OrdinalIgnoreCase)) continue; // keep granted
            // remove everything else
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

    private string GiveIfMissing(CCSPlayerController player)
    {
        if (!_granted.TryGetValue(player.Slot, out var weapon) || string.IsNullOrEmpty(weapon))
        {
            weapon = _chosenCategory == RandomCategory.Primary ? GameModifiersUtils.GetRandomPrimaryWeaponName() : GameModifiersUtils.GetRandomPistolWeaponName();
            _granted[player.Slot] = weapon;
            GameModifiersUtils.GiveAndEquipWeapon(player, weapon);
            if (Core != null && Core._localizer != null)
            {
                var loc = Core._localizer;
                string key = _chosenCategory == RandomCategory.Primary ? "RandomPrimaryWeaponRound" : "RandomPistolWeaponRound";
                GameModifiersUtils.PrintTitleToChat(player, loc[key, weapon.Substring(7)], loc);
            }
        }
        else
        {
            // ensure player still has it
            var pawn = player.PlayerPawn.Value;
            bool has = pawn?.WeaponServices?.MyWeapons.Any(h => h.IsValid && h.Value != null && h.Value.DesignerName == weapon) == true;
            if (!has)
            {
                GameModifiersUtils.GiveAndEquipWeapon(player, weapon);
            }
        }
        return weapon;
    }

    private void RestoreOriginalForAlivePlayers()
    {
        foreach (var kv in _originalLoadouts.ToList())
        {
            var player = Utilities.GetPlayerFromSlot(kv.Key);
            if (player == null || !player.IsValid || !player.PawnIsAlive) continue;
            // strip granted weapon
            var pawn = player.PlayerPawn.Value;
            if (pawn?.WeaponServices != null)
            {
                var handles = pawn.WeaponServices.MyWeapons.ToList();
                List<uint> toKill = new();
                foreach (var h in handles)
                {
                    if (!h.IsValid || h.Value == null) continue;
                    var w = h.Value;
                    if (w.DesignerName == null) continue;
                    if (w.DesignerName.Contains("knife", StringComparison.OrdinalIgnoreCase)) continue;
                    if (_granted.TryGetValue(player.Slot, out var gw) && string.Equals(gw, w.DesignerName, StringComparison.OrdinalIgnoreCase))
                    {
                        pawn.WeaponServices.ActiveWeapon.Raw = w.EntityHandle.Raw;
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

    private Timer? _enforceTimer;
    private float _interval = 0.25f;
    private int _stableCycles = 0;
    private const int StableThreshold = 8;

    protected override bool ShouldCacheAndRemoveWeapon(CBasePlayerWeapon weapon)
    {
        var t = GameModifiersUtils.GetWeaponType(weapon);
        // remove everything that is not grenade or C4
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
        GiveGrenadesAll();
        StartLoop();
    }

    public override void Disabled()
    {
        _enforceTimer?.Kill();
        _enforceTimer = null;
        base.Disabled();
    }

    private void GiveGrenadesAll()
    {
        Utilities.GetPlayers().ForEach(player =>
        {
            if (player == null || !player.IsValid || !player.PawnIsAlive) return;
            GameModifiersUtils.GiveAndEquipWeapon(player, "weapon_molotov");
            GameModifiersUtils.GiveAndEquipWeapon(player, "weapon_smokegrenade");
            GameModifiersUtils.GiveAndEquipWeapon(player, "weapon_hegrenade");
            GameModifiersUtils.GiveAndEquipWeapon(player, "weapon_flashbang");
        });
    }

    private void StartLoop()
    {
        _enforceTimer?.Kill();
        _interval = 0.25f;
        _stableCycles = 0;
        _enforceTimer = new Timer(_interval, Enforce, TimerFlags.REPEAT);
    }

    private void Enforce()
    {
        bool anyChange = false;
        foreach (var p in Utilities.GetPlayers())
        {
            if (p == null || !p.IsValid || !p.PawnIsAlive) continue;
            if (EnforcePlayer(p)) anyChange = true;
        }
        if (anyChange)
        {
            _stableCycles = 0;
            if (_interval > 0.25f)
            {
                _enforceTimer?.Kill();
                _interval = 0.25f;
                _enforceTimer = new Timer(_interval, Enforce, TimerFlags.REPEAT);
            }
        }
        else
        {
            _stableCycles++;
            if (_stableCycles >= StableThreshold && _interval < 1.0f)
            {
                _enforceTimer?.Kill();
                _interval = 1.0f;
                _enforceTimer = new Timer(_interval, Enforce, TimerFlags.REPEAT);
            }
        }
    }

    private bool EnforcePlayer(CCSPlayerController player)
    {
        bool changed = false;
        var pawn = player.PlayerPawn.Value;
        if (pawn?.WeaponServices == null) return false;
        var handles = pawn.WeaponServices.MyWeapons.ToList();
        List<uint> toKill = new();
        foreach (var h in handles)
        {
            if (!h.IsValid || h.Value == null) continue;
            var w = h.Value;
            var t = GameModifiersUtils.GetWeaponType(w);
            if (t == CSWeaponType.WEAPONTYPE_GRENADE || t == CSWeaponType.WEAPONTYPE_C4) continue;
            // remove
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
        // ensure grenades set present (re-grant if missing any)
        string[] required = ["weapon_molotov","weapon_smokegrenade","weapon_hegrenade","weapon_flashbang"];
        var existing = handles.Where(h => h.IsValid && h.Value != null).Select(h => h.Value!.DesignerName).ToHashSet();
        foreach (var g in required)
        {
            if (!existing.Contains(g)) { GameModifiersUtils.GiveAndEquipWeapon(player, g); changed = true; }
        }
        return changed;
    }
}
// End of file
