using System;
using System.Collections.Generic;
using System.Linq;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Utils;

namespace GameModifiers.Modifiers;

public abstract class GameModifierRemoveWeapons : GameModifierBase
{
    protected readonly Dictionary<int, List<string>> CachedItems = new();
    private bool _isProcessingRemoval = false; // guard to avoid re-entrancy during spawn events
    private readonly HashSet<int> _pendingReturnSlots = new(); // players whose weapons will be restored on next spawn (were dead on disable)

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

        Utilities.GetPlayers().ForEach(RemoveWeapons);

        if (Core != null && Core._localizer != null)
        {
            var loc = Core._localizer;
            GameModifiersUtils.PrintTitleToChatAll(loc["RemovingItemsMessage"], loc);
        }
        else
        {
            Utilities.GetPlayers().ForEach(player => player.PrintToChat("GameModifiers Removing items, they will be returned when the modifier is disabled."));
        }
    }

    public override void Disabled()
    {
        // Do NOT immediately deregister spawn handler; we may need it to restore dead players next round.
        if (Core != null)
        {
            Core.RemoveListener<Listeners.OnClientDisconnect>(OnClientDisconnect);
        }

        _pendingReturnSlots.Clear();
        foreach (var cachedWeaponPair in CachedItems.ToList())
        {
            int slot = cachedWeaponPair.Key;
            CCSPlayerController? player = Utilities.GetPlayerFromSlot(slot);
            if (!TryReturnWeaponsImmediate(player))
            {
                // Keep for later spawn restore.
                _pendingReturnSlots.Add(slot);
            }
        }

        if (_pendingReturnSlots.Any())
        {
            // Keep spawn handler active to restore later.
            if (Core != null)
            {
                // Ensure handler is registered (if modifier disabled while still registered it's fine).
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
            // All restored immediately; safe to deregister spawn handler.
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

    private void RemoveWeapons(CCSPlayerController? player)
    {
        if (player == null || !player.IsValid || _isProcessingRemoval)
        {
            return;
        }
        _isProcessingRemoval = true;
        try
        {
            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn == null || !playerPawn.IsValid)
            {
                return;
            }

            List<CHandle<CBasePlayerWeapon>>? weaponHandles = playerPawn.WeaponServices?.MyWeapons.ToList();
            if (weaponHandles == null)
            {
                return;
            }

            List<string> cachedWeapons = new();
            List<uint> toKillIndexes = new();
            foreach (CHandle<CBasePlayerWeapon> weaponHandle in weaponHandles)
            {
                if (!weaponHandle.IsValid || weaponHandle.Value == null)
                {
                    continue;
                }
                var weapon = weaponHandle.Value;
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
                    {
                        cachedWeapons.Add(weapon.DesignerName);
                        playerPawn.WeaponServices!.ActiveWeapon.Raw = weapon.EntityHandle.Raw;
                        player.DropActiveWeapon();
                        toKillIndexes.Add(weapon.Index);
                        break;
                    }
                    default: break; // keep knife etc.
                }
            }

            CachedItems[player.Slot] = cachedWeapons;

            if (toKillIndexes.Any())
            {
                Server.NextFrame(() =>
                {
                    foreach (var idx in toKillIndexes)
                    {
                        CBasePlayerWeapon? w = Utilities.GetEntityFromIndex<CBasePlayerWeapon>((int)idx);
                        if (w != null && w.IsValid)
                        {
                            w.AcceptInput("Kill");
                        }
                    }
                });
            }
        }
        finally
        {
            _isProcessingRemoval = false;
        }
    }

    private bool TryReturnWeaponsImmediate(CCSPlayerController? player)
    {
        if (player == null || !player.IsValid || !player.PawnIsAlive)
        {
            return false;
        }

        if (!CachedItems.TryGetValue(player.Slot, out var items))
        {
            return true;
        }

        var pawn = player.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid)
        {
            return false;
        }

        var existing = pawn.WeaponServices?.MyWeapons.Where(h => h.IsValid && h.Value != null)
            .Select(h => h.Value!.DesignerName).ToHashSet() ?? new HashSet<string>();

        foreach (var itemName in items)
        {
            if (!existing.Contains(itemName))
            {
                player.GiveNamedItem(itemName);
            }
        }

        CachedItems.Remove(player.Slot);
        return true;
    }

    private void TryReturnWeaponsDeferred(CCSPlayerController? player)
    {
        if (TryReturnWeaponsImmediate(player))
        {
            _pendingReturnSlots.Remove(player!.Slot);
            if (!CachedItems.Any())
            {
                if (Core != null)
                {
                    Core.DeregisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
                }
            }
        }
    }

    private HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player == null || !player.IsValid)
        {
            return HookResult.Continue;
        }

        if (IsActive)
        {
            // Fresh spawn under active modifier: remove weapons after spawn
            Server.NextFrame(() => RemoveWeapons(player));
            return HookResult.Continue;
        }

        // Modifier disabled but we still have pending returns
        if (_pendingReturnSlots.Contains(player.Slot) || CachedItems.ContainsKey(player.Slot))
        {
            Server.NextFrame(() => TryReturnWeaponsDeferred(player));
        }

        return HookResult.Continue;
    }

    private void OnClientDisconnect(int slot)
    {
        if (CachedItems.ContainsKey(slot))
        {
            CachedItems.Remove(slot);
        }
        if (_pendingReturnSlots.Contains(slot))
        {
            _pendingReturnSlots.Remove(slot);
        }
    }
}

/* KnifeOnly mode disabled per user request
public class GameModifierKnifeOnly : GameModifierRemoveWeapons
{
    public override bool SupportsRandomRounds => true;
    public override HashSet<string> IncompatibleModifiers =>
    [
        GameModifiersUtils.GetModifierName<GameModifierRandomWeapon>(),
        GameModifiersUtils.GetModifierName<GameModifierGrenadesOnly>(),
        GameModifiersUtils.GetModifierName<GameModifierRandomWeapons>()
    ];

    public GameModifierKnifeOnly()
    {
        Name = "KnivesOnly";
        Description = "Buy menu is disabled, knives only";
    }
}
*/

// RandomWeapon implementation
public class GameModifierRandomWeapon : GameModifierBase
{
    public override bool SupportsRandomRounds => true;
    public override HashSet<string> IncompatibleModifiers =>
    [
        // KnifeOnly removed
        GameModifiersUtils.GetModifierName<GameModifierGrenadesOnly>(),
        GameModifiersUtils.GetModifierName<GameModifierRandomWeapons>()
    ];

    protected readonly Dictionary<int, List<string>> _originalLoadouts = new();
    protected readonly HashSet<int> _randomizedThisRound = new();

    public GameModifierRandomWeapon()
    {
        Name = "RandomWeapon";
        Description = "Buy menu is disabled, random weapon only";
    }

    public override void Enabled()
    {
        base.Enabled();
        _originalLoadouts.Clear();
        _randomizedThisRound.Clear();
        if (Core != null)
        {
            Core.RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
            Core.RegisterListener<Listeners.OnClientDisconnect>(OnClientDisconnect);
        }
        // Process already alive players
        Utilities.GetPlayers().ForEach(p => ScheduleRandomize(p));
    }

    public override void Disabled()
    {
        if (Core != null)
        {
            Core.DeregisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
            Core.RemoveListener<Listeners.OnClientDisconnect>(OnClientDisconnect);
        }

        foreach (var kv in _originalLoadouts.ToList())
        {
            var player = Utilities.GetPlayerFromSlot(kv.Key);
            if (player == null || !player.IsValid || !player.PawnIsAlive) continue;
            GameModifiersUtils.RemoveWeapons(player);
            foreach (var weaponName in kv.Value)
            {
                player.GiveNamedItem(weaponName);
            }
        }
        _originalLoadouts.Clear();
        _randomizedThisRound.Clear();
        base.Disabled();
    }

    private HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        ScheduleRandomize(@event.Userid);
        return HookResult.Continue;
    }

    private void ScheduleRandomize(CCSPlayerController? player)
    {
        if (Core == null || player == null || !player.IsValid) return;
        Core.AddTimer(0.5f, () => CaptureAndRandomize(player));
    }

    protected virtual void CaptureAndRandomize(CCSPlayerController? player)
    {
        if (player == null || !player.IsValid || !player.PawnIsAlive) return;
        if (_randomizedThisRound.Contains(player.Slot)) return;
        if (!_originalLoadouts.ContainsKey(player.Slot))
        {
            _originalLoadouts[player.Slot] = GetCurrentLoadout(player);
        }
        GameModifiersUtils.RemoveWeapons(player);
        string randomWeaponName = GameModifiersUtils.GetRandomRangedWeaponName();
        GameModifiersUtils.GiveAndEquipWeapon(player, randomWeaponName);
        _randomizedThisRound.Add(player.Slot);
        if (Core != null && Core._localizer != null)
        {
            var loc = Core._localizer;
            string display = randomWeaponName.Substring(7);
            GameModifiersUtils.PrintTitleToChat(player, loc["RandomWeaponRound", display], loc);
        }
    }

    protected virtual List<string> GetCurrentLoadout(CCSPlayerController player)
    {
        List<string> list = new();
        var pawn = player.PlayerPawn.Value;
        if (pawn?.WeaponServices == null) return list;
        foreach (var h in pawn.WeaponServices.MyWeapons)
        {
            if (!h.IsValid || h.Value == null) continue;
            var w = h.Value;
            switch (GameModifiersUtils.GetWeaponType(w))
            {
                case CSWeaponType.WEAPONTYPE_PISTOL:
                case CSWeaponType.WEAPONTYPE_SUBMACHINEGUN:
                case CSWeaponType.WEAPONTYPE_RIFLE:
                case CSWeaponType.WEAPONTYPE_SHOTGUN:
                case CSWeaponType.WEAPONTYPE_SNIPER_RIFLE:
                case CSWeaponType.WEAPONTYPE_MACHINEGUN:
                case CSWeaponType.WEAPONTYPE_TASER:
                case CSWeaponType.WEAPONTYPE_GRENADE:
                    list.Add(w.DesignerName);
                    break;
                default: break;
            }
        }
        return list;
    }

    private void OnClientDisconnect(int slot)
    {
        _originalLoadouts.Remove(slot);
        _randomizedThisRound.Remove(slot);
    }
}

public class GameModifierRandomWeapons : GameModifierRandomWeapon
{
    public GameModifierRandomWeapons()
    {
        Name = "RandomWeapons";
        Description = "Buy menu is disabled, random weapons are given out";
    }

    protected override void CaptureAndRandomize(CCSPlayerController? player)
    {
        if (player == null || !player.IsValid || !player.PawnIsAlive) return;
        if (_randomizedThisRound.Contains(player.Slot)) return;
        if (!_originalLoadouts.ContainsKey(player.Slot))
        {
            _originalLoadouts[player.Slot] = GetCurrentLoadout(player);
        }
        GameModifiersUtils.RemoveWeapons(player);
        string primary = GameModifiersUtils.GetRandomRangedWeaponName();
        string secondary = GameModifiersUtils.GetRandomRangedWeaponName();
        GameModifiersUtils.GiveAndEquipWeapon(player, primary);
        GameModifiersUtils.GiveAndEquipWeapon(player, secondary);
        _randomizedThisRound.Add(player.Slot);
        if (Core != null && Core._localizer != null)
        {
            var loc = Core._localizer;
            GameModifiersUtils.PrintTitleToChat(player, loc["RandomWeaponsRound", primary.Substring(7), secondary.Substring(7)], loc);
        }
    }
}

public class GameModifierGrenadesOnly : GameModifierRemoveWeapons
{
    public override bool SupportsRandomRounds => true;
    public override HashSet<string> IncompatibleModifiers =>
    [
        GameModifiersUtils.GetModifierName<GameModifierRandomWeapon>(),
        /* KnifeOnly removed */
        GameModifiersUtils.GetModifierName<GameModifierRandomWeapons>()
    ];

    public GameModifierGrenadesOnly()
    {
        Name = "GrenadesOnly";
        Description = "Buy menu is disabled, grenades only";
    }

    public override void Enabled()
    {
        base.Enabled();
        Utilities.GetPlayers().ForEach(player =>
        {
            GameModifiersUtils.GiveAndEquipWeapon(player, "weapon_molotov");
            GameModifiersUtils.GiveAndEquipWeapon(player, "weapon_smokegrenade");
            GameModifiersUtils.GiveAndEquipWeapon(player, "weapon_hegrenade");
            GameModifiersUtils.GiveAndEquipWeapon(player, "weapon_flashbang");
        });
    }
}
