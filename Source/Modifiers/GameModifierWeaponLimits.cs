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
            GameModifiersUtils.PrintTitleToChatAll("Removing items, they will be returned when the modifier is disabled.", Core._localizer);
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
                GameModifiersUtils.PrintTitleToChatAll("Returning items for alive players now; dead players will get items on next spawn...", Core._localizer);
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
                GameModifiersUtils.PrintTitleToChatAll("Returning items...", Core._localizer);
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
                        // Drop then schedule kill
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

    // Returns true if restoration done; false if deferred
    private bool TryReturnWeaponsImmediate(CCSPlayerController? player)
    {
        if (player == null || !player.IsValid || !player.PawnIsAlive)
        {
            return false; // defer
        }

        if (!CachedItems.TryGetValue(player.Slot, out var items))
        {
            return true; // nothing to restore
        }

        var pawn = player.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid)
        {
            return false; // defer
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

public class GameModifierRandomWeapon : GameModifierRemoveWeapons
{
    public override bool SupportsRandomRounds => true;
    public override HashSet<string> IncompatibleModifiers =>
    [
        GameModifiersUtils.GetModifierName<GameModifierKnifeOnly>(),
        GameModifiersUtils.GetModifierName<GameModifierGrenadesOnly>(),
        GameModifiersUtils.GetModifierName<GameModifierRandomWeapons>()
    ];

    public GameModifierRandomWeapon()
    {
        Name = "RandomWeapon";
        Description = "Buy menu is disabled, random weapon only";
    }

    public override void Enabled()
    {
        base.Enabled();
        ApplyRandomWeapon();
    }

    protected virtual void ApplyRandomWeapon()
    {
        string randomWeaponName = GameModifiersUtils.GetRandomRangedWeaponName();
        if (Core != null && Core._localizer != null)
        {
            GameModifiersUtils.PrintTitleToChatAll($"{randomWeaponName.Substring(7)} round.", Core._localizer);
        }
        else
        {
            Utilities.GetPlayers().ForEach(player => player.PrintToChat($"GameModifiers {randomWeaponName.Substring(7)} round."));
        }
        Utilities.GetPlayers().ForEach(player =>
        {
            GameModifiersUtils.GiveAndEquipWeapon(player, randomWeaponName);
        });
    }
}

public class GameModifierRandomWeapons : GameModifierRandomWeapon
{
    public override bool SupportsRandomRounds => true;
    public override HashSet<string> IncompatibleModifiers =>
    [
        GameModifiersUtils.GetModifierName<GameModifierKnifeOnly>(),
        GameModifiersUtils.GetModifierName<GameModifierGrenadesOnly>(),
        GameModifiersUtils.GetModifierName<GameModifierRandomWeapon>()
    ];

    public GameModifierRandomWeapons()
    {
        Name = "RandomWeapons";
        Description = "Buy menu is disabled, random weapons are given out";
    }

    protected override void ApplyRandomWeapon()
    {
        Utilities.GetPlayers().ForEach(player =>
        {
            string randomWeaponName = GameModifiersUtils.GetRandomRangedWeaponName();
            if (Core != null && Core._localizer != null)
            {
                GameModifiersUtils.PrintTitleToChat(player, $"{randomWeaponName.Substring(7)} for random weapon round.", Core._localizer);
            }
            else
            {
                player.PrintToChat($"GameModifiers {randomWeaponName.Substring(7)} for random weapon round.");
            }
            GameModifiersUtils.GiveAndEquipWeapon(player, randomWeaponName);
        });
    }
}

public class GameModifierGrenadesOnly : GameModifierRemoveWeapons
{
    public override bool SupportsRandomRounds => true;
    public override HashSet<string> IncompatibleModifiers =>
    [
        GameModifiersUtils.GetModifierName<GameModifierRandomWeapon>(),
        GameModifiersUtils.GetModifierName<GameModifierKnifeOnly>(),
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
