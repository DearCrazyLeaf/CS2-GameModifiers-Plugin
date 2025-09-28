using System.Collections.Generic;
using System.Collections.Concurrent;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace GameModifiers.Modifiers;

public class GameModifierSwapPlacesOnKill : GameModifierBase
{
    public override bool SupportsRandomRounds => true;
    public override HashSet<string> IncompatibleModifiers =>
    [
        GameModifiersUtils.GetModifierName<GameModifierSwapPlacesOnHit>(),
        GameModifiersUtils.GetModifierName<GameModifierResetOnReload>()
    ];

    public GameModifierSwapPlacesOnKill()
    {
        Name = "SwapOnDeath";
        Description = "Players will swap places on kill.";
    }

    public override void Enabled()
    {
        base.Enabled();

        if (Core != null)
        {
            Core.RegisterEventHandler<EventPlayerDeath>(OnPlayerDeath);
        }
    }

    public override void Disabled()
    {
        if (Core != null)
        {
            Core.DeregisterEventHandler<EventPlayerDeath>(OnPlayerDeath);
        }

        base.Disabled();
    }

    private HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        CCSPlayerController? attackerPlayer = @event.Attacker;
        CCSPlayerController? victimPlayer = @event.Userid;
        if (attackerPlayer == victimPlayer)
        {
            return HookResult.Continue;
        }

        if (attackerPlayer == null || !attackerPlayer.IsValid || victimPlayer == null || !victimPlayer.IsValid)
        {
            return HookResult.Continue;
        }

        CCSPlayerPawn? attackingPawn = attackerPlayer.PlayerPawn.Value;
        CCSPlayerPawn? victimPawn = victimPlayer.PlayerPawn.Value;
        if (attackingPawn == null || !attackingPawn.IsValid || victimPawn == null || !victimPawn.IsValid)
        {
            return HookResult.Continue;
        }

        if (victimPawn.AbsOrigin == null)
        {
            return HookResult.Continue;
        }

        GameModifiersUtils.TeleportPlayer(attackerPlayer, victimPawn.AbsOrigin);
        return HookResult.Continue;
    }
}

public class GameModifierSwapPlacesOnHit : GameModifierBase
{
    public override bool SupportsRandomRounds => true;
    public override HashSet<string> IncompatibleModifiers =>
    [
        GameModifiersUtils.GetModifierName<GameModifierSwapPlacesOnKill>(),
        GameModifiersUtils.GetModifierName<GameModifierResetOnReload>()
    ];

    private readonly ConcurrentDictionary<int, float> _lastSwapTime = new();
    private const float Cooldown = 0.15f; // 150ms

    public GameModifierSwapPlacesOnHit()
    {
        Name = "SwapOnHit";
        Description = "Players will swap places on hit";
    }

    public override void Enabled()
    {
        base.Enabled();

        if (Core != null)
        {
            Core.RegisterEventHandler<EventPlayerHurt>(OnPlayerHurtEvent);
        }
    }

    public override void Disabled()
    {
        if (Core != null)
        {
            Core.DeregisterEventHandler<EventPlayerHurt>(OnPlayerHurtEvent);
        }

        _lastSwapTime.Clear();
        base.Disabled();
    }

    private HookResult OnPlayerHurtEvent(EventPlayerHurt @event, GameEventInfo info)
    {
        var attacker = @event.Attacker;
        var victim = @event.Userid;
        if (attacker == null || victim == null || !attacker.IsValid || !victim.IsValid) return HookResult.Continue;

        float now = Server.CurrentTime;
        if (attacker.IsValid)
        {
            if (_lastSwapTime.TryGetValue(attacker.Slot, out var last) && now - last < Cooldown)
                return HookResult.Continue;
            _lastSwapTime[attacker.Slot] = now;
        }
        if (victim.IsValid)
        {
            if (_lastSwapTime.TryGetValue(victim.Slot, out var lastV) && now - lastV < Cooldown)
                return HookResult.Continue;
            _lastSwapTime[victim.Slot] = now;
        }

        GameModifiersUtils.SwapPlayerLocations(attacker, victim);
        return HookResult.Continue;
    }
}

public class GameModifierResetOnReload : GameModifierBase
{
    public override bool SupportsRandomRounds => true;
    public override HashSet<string> IncompatibleModifiers =>
    [
        GameModifiersUtils.GetModifierName<GameModifierSwapPlacesOnKill>(),
        GameModifiersUtils.GetModifierName<GameModifierSwapPlacesOnHit>()
    ];

    public GameModifierResetOnReload()
    {
        Name = "ResetOnReload";
        Description = "Players are teleported back to their spawn on reload";
    }

    public override void Enabled()
    {
        base.Enabled();

        if (Core != null)
        {
            Core.RegisterEventHandler<EventWeaponReload>(OnPlayerReload);
        }
    }

    public override void Disabled()
    {
        if (Core != null)
        {
            Core.DeregisterEventHandler<EventWeaponReload>(OnPlayerReload);
        }

        base.Disabled();
    }

    private HookResult OnPlayerReload(EventWeaponReload @event, GameEventInfo info)
    {
        CCSPlayerController? reloadingPlayer = @event.Userid;
        if (reloadingPlayer == null || !reloadingPlayer.IsValid || !reloadingPlayer.PawnIsAlive)
        {
            return HookResult.Continue;
        }

        GameModifiersUtils.TeleportPlayerToSpawnArea(reloadingPlayer);
        return HookResult.Continue;
    }
}
