﻿using System;
using System.Collections.Generic;
using System.Drawing;

using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace GameModifiers.Modifiers;

public class GameModifierLongerFlashes : GameModifierBase
{
    public override bool SupportsRandomRounds => true;

    public GameModifierLongerFlashes()
    {
        Name = "LongerFlashes";
        Description = "Flash bang effect lasts 3 times longer";
    }

    public override void Enabled()
    {
        base.Enabled();

        if (Core != null)
        {
            Core.RegisterEventHandler<EventPlayerBlind>(OnPlayerBlind, HookMode.Pre);
        }
    }

    public override void Disabled()
    {
        if (Core != null)
        {
            Core.DeregisterEventHandler<EventPlayerBlind>(OnPlayerBlind, HookMode.Pre);
        }

        base.Disabled();
    }

    private HookResult OnPlayerBlind(EventPlayerBlind @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player == null || player.IsValid == false || player.Connected != PlayerConnectedState.PlayerConnected)
        {
            return HookResult.Continue;
        }

        var playerPawn = player.PlayerPawn.Value;
        if (playerPawn == null || !playerPawn.IsValid)
        {
            return HookResult.Continue;
        }

        @event.BlindDuration = 1.0f + Random.Shared.Next(1, 10);
        playerPawn.FlashDuration = @event.BlindDuration;
        Utilities.SetStateChanged(playerPawn, "CCSPlayerPawnBase", "m_flFlashDuration");

        playerPawn.BlindUntilTime = Server.CurrentTime + playerPawn.FlashDuration;
        Utilities.SetStateChanged(playerPawn, "CCSPlayerPawnBase", "m_blindUntilTime");

        return HookResult.Continue;
    }
}

public abstract class GameModifierGrenadeSpawned : GameModifierBase
{
    public override void Enabled()
    {
        base.Enabled();

        if (Core != null)
        {
            Core.RegisterListener<Listeners.OnEntitySpawned>(OnEntitySpawned);
        }
    }

    public override void Disabled()
    {
        if (Core != null)
        {
            Core.RemoveListener<Listeners.OnEntitySpawned>(OnEntitySpawned);
        }

        base.Disabled();
    }

    private void OnEntitySpawned(CEntityInstance entityInstance)
    {
        switch (entityInstance.DesignerName)
        {
            case "hegrenade_projectile":
            case "flashbang_projectile":
            case "smokegrenade_projectile":
            case "decoy_projectile":
            case "inferno":
            {
                CBaseCSGrenadeProjectile grenade = entityInstance.As<CBaseCSGrenadeProjectile>();
                if (grenade.IsValid)
                {
                    OnGrenadeSpawned(grenade);
                }
            }
            break;
            default: break;
        }
    }

    protected virtual void OnGrenadeSpawned(CBaseCSGrenadeProjectile grenadeProjectile)
    {
        // Implement in child class...
    }
}

public class GameModifierRandomGrenadeTime : GameModifierGrenadeSpawned
{
    public override bool SupportsRandomRounds => true;

    public GameModifierRandomGrenadeTime()
    {
        Name = "DodgyGrenades";
        Description = "Timers on flashes and HE's are randomized";
    }

    protected override void OnGrenadeSpawned(CBaseCSGrenadeProjectile grenadeProjectile)
    {
        switch (grenadeProjectile.DesignerName)
        {
            case "smokegrenade_projectile":
            {
                // TODO: Cannot figure out a way to get smokes to pop after random ammount of time. For now it's just flash bangs and grenades.
            }
            break;
            case "hegrenade_projectile":
            case "flashbang_projectile":
            {
                Server.NextFrame(() =>
                {
                    Random random = new Random();
                    grenadeProjectile.DetonateTime = Server.CurrentTime + (float)random.Next(1, 12);

                    Utilities.SetStateChanged(grenadeProjectile, "CBaseGrenade", "m_flDetonateTime");
                });
            }
            break;
        }
    }
}