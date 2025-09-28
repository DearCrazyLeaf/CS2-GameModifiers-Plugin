using System.Collections.Generic;

using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;

namespace GameModifiers.Modifiers;

public abstract class GameModifierVelocity : GameModifierBase
{
    public virtual float SpeedMultiplier { get; protected set; } = 1.0f;
    private Timer? _verifyTimer;

    public override void Enabled()
    {
        base.Enabled();
        if (Core != null)
        {
            Core.RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
        }
        var players = Utilities.GetPlayers();
        foreach (var controller in players)
        {
            ApplySpeed(controller, force:true);
        }
        // 低频校验：每2秒检查一次，只有被外部改掉才写回，减少无谓状态同步
        _verifyTimer = new Timer(2.0f, VerifySpeeds, TimerFlags.REPEAT);
    }

    public override void Disabled()
    {
        if (Core != null)
        {
            Core.DeregisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
        }
        _verifyTimer?.Kill();
        _verifyTimer = null;
        var players = Utilities.GetPlayers();
        foreach (var controller in players)
        {
            ResetSpeed(controller);
        }
        base.Disabled();
    }

    private void VerifySpeeds()
    {
        var players = Utilities.GetPlayers();
        foreach (var controller in players)
        {
            ApplySpeed(controller, force:false);
        }
    }

    private void ApplySpeed(CCSPlayerController? controller, bool force)
    {
        if (controller == null || !controller.IsValid) return;
        float current = GameModifiersUtils.GetPlayerSpeedMultiplier(controller);
        if (force || System.Math.Abs(current - SpeedMultiplier) > 0.01f)
        {
            GameModifiersUtils.SetPlayerSpeedMultiplier(controller, SpeedMultiplier);
        }
    }

    private void ResetSpeed(CCSPlayerController? controller)
    {
        if (controller == null || !controller.IsValid) return;
        float current = GameModifiersUtils.GetPlayerSpeedMultiplier(controller);
        if (System.Math.Abs(current - 1.0f) > 0.01f)
        {
            GameModifiersUtils.SetPlayerSpeedMultiplier(controller, 1.0f);
        }
    }

    private HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player == null || player.IsValid == false || player.Connected != PlayerConnectedState.PlayerConnected)
        {
            return HookResult.Continue;
        }
        ApplySpeed(player, force:true);
        return HookResult.Continue;
    }
}

public class GameModifierLightweight : GameModifierVelocity
{
    public override bool SupportsRandomRounds => true;
    public override float SpeedMultiplier { get; protected set; } = 1.3f;
    public override HashSet<string> IncompatibleModifiers =>
    [
        "LeadBoots"
    ];

    public GameModifierLightweight()
    {
        Name = "Lightweight";
        Description = "Max movement speed is much faster";
    }
}
