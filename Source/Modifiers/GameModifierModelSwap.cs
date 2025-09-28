using System;
using System.Collections.Generic;
using System.Linq;

using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using GameModifiers.Modifiers; // ensure dispatcher symbol resolution

namespace GameModifiers.Modifiers;

public abstract class GameModifierModelSwap : GameModifierBase
{
    private Dictionary<int, string> _playerModelCache = new();

    public override void Enabled()
    {
        base.Enabled();

        if (Core != null)
        {
            Core.RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
            Core.RegisterListener<Listeners.OnClientDisconnect>(OnClientDisconnect);
        }

        ActivateSwap();
    }

    public override void Disabled()
    {
        if (Core != null)
        {
            Core.DeregisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
            Core.RemoveListener<Listeners.OnClientDisconnect>(OnClientDisconnect);
        }

        DeactivateSwap();
        _playerModelCache.Clear();

        base.Disabled();
    }

    protected virtual void ActivateSwap()
    {
        Utilities.GetPlayers().ForEach(ApplyPlayerModel);
    }

    protected virtual void DeactivateSwap()
    {
        Utilities.GetPlayers().ForEach(ResetPlayerModel);
    }

    protected void SetPlayerModel(CCSPlayerController? player, CsTeam teamModel)
    {
        if (player == null || !player.IsValid)
        {
            return;
        }

        var playerPawn = player.PlayerPawn.Value;
        if (playerPawn == null || !playerPawn.IsValid)
        {
            return;
        }

        string playerModel = playerPawn.CBodyComponent!.SceneNode!.GetSkeletonInstance().ModelState.ModelName;
        _playerModelCache[player.Slot] = playerModel;

        if (teamModel == CsTeam.Terrorist)
        {
            playerPawn.SetModel("characters/models/tm_phoenix/tm_phoenix.vmdl");
        }
        else if (teamModel == CsTeam.CounterTerrorist)
        {
            playerPawn.SetModel("characters/models/ctm_sas/ctm_sas.vmdl");
        }
        else
        {
            Console.WriteLine("[GameModifierModelSwap::ChangePlayerModel] Attempting to use unsupported team model!");
        }

        Utilities.SetStateChanged(playerPawn, "CBaseModelEntity", "m_clrRender");
    }

    protected virtual void ResetPlayerModel(CCSPlayerController? player)
    {
        if (player == null || !player.IsValid)
        {
            return;
        }

        if (!_playerModelCache.ContainsKey(player.Slot))
        {
            return;
        }

        var playerPawn = player.PlayerPawn.Value;
        if (playerPawn == null || !playerPawn.IsValid)
        {
            return;
        }

        playerPawn.SetModel(_playerModelCache[player.Slot]);
    }

    protected virtual void ApplyPlayerModel(CCSPlayerController? player)
    {
        // Implement in child...
    }

    private HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player == null || player.IsValid == false || player.Connected != PlayerConnectedState.PlayerConnected)
        {
            return HookResult.Continue;
        }

        ApplyPlayerModel(player);
        return HookResult.Continue;
    }

    private void OnClientDisconnect(int slot)
    {
        CCSPlayerController? player = Utilities.GetPlayerFromSlot(slot);
        if (player == null || player.IsValid is not true)
        {
            if (_playerModelCache.ContainsKey(slot))
            {
                _playerModelCache.Remove(slot);
            }

            return;
        }

        ResetPlayerModel(player);
    }
}

public class GameModifierImposters : GameModifierModelSwap
{
    public override bool SupportsRandomRounds => true;
    public override HashSet<string> IncompatibleModifiers =>
    [
        //GameModifiersUtils.GetModifierName<GameModifierRandomSpawn>()
    ];

    public GameModifierImposters()
    {
        Name = "Imposters"; // 保持名称兼容
        Description = "FFA safe period (no damage) ends 10s after freeze";
    }

    private string? _oldTeammatesEnemies;
    private string? _oldFriendlyFire;

    private Timer? _preRoundMessageTimer;
    private Timer? _countdownTimer;
    private float _freezeTime = 0f;
    private int _countdownRemaining = 0;
    private bool _roundStarted = false;
    private bool _damageProtected = true; // 是否处于安全期

    protected override void ActivateSwap()
    {
        CacheAndOverrideState();
        StartPreRoundNotices();
        if (Core != null)
        {
            Core.RegisterEventHandler<EventRoundStart>(OnRoundStart);
        }
        _dispatcherId = DamageHookDispatcher.Add(OnGlobalDamage); // central dispatcher
    }

    protected override void DeactivateSwap()
    {
        if (Core != null)
        {
            Core.DeregisterEventHandler<EventRoundStart>(OnRoundStart);
        }
        CancelAllTimers();
        RestoreState();
        if (_dispatcherId != Guid.Empty)
        {
            DamageHookDispatcher.Remove(_dispatcherId);
            _dispatcherId = Guid.Empty;
        }
    }

    private Guid _dispatcherId = Guid.Empty;

    // Dispatcher callback
    private void OnGlobalDamage(CTakeDamageInfo dmg)
    {
        if (_damageProtected)
        {
            dmg.Damage = 0f;
        }
    }

    private void CacheAndOverrideState()
    {
        var cFreeze = ConVar.Find("mp_freezetime");
        if (cFreeze != null && float.TryParse(cFreeze.StringValue, out float fz)) _freezeTime = fz; else _freezeTime = 0f;

        var cTeammatesEnemies = ConVar.Find("mp_teammates_are_enemies");
        if (cTeammatesEnemies != null)
        {
            _oldTeammatesEnemies = cTeammatesEnemies.StringValue;
            cTeammatesEnemies.SetValue("1");
        }
        var cFriendlyFire = ConVar.Find("mp_friendlyfire");
        if (cFriendlyFire != null)
        {
            _oldFriendlyFire = cFriendlyFire.StringValue;
            cFriendlyFire.SetValue("1");
        }

        _damageProtected = true;
        _roundStarted = false;
    }

    private void RestoreState()
    {
        var cTeammatesEnemies = ConVar.Find("mp_teammates_are_enemies");
        if (cTeammatesEnemies != null && _oldTeammatesEnemies != null)
        {
            cTeammatesEnemies.SetValue(_oldTeammatesEnemies);
        }
        var cFriendlyFire = ConVar.Find("mp_friendlyfire");
        if (cFriendlyFire != null && _oldFriendlyFire != null)
        {
            cFriendlyFire.SetValue(_oldFriendlyFire);
        }
        _oldFriendlyFire = null;
        _oldTeammatesEnemies = null;
    }

    private void StartPreRoundNotices()
    {
        CancelPreRoundTimer();
        if (Core == null) return;
        SendPreRoundMessage();
        _preRoundMessageTimer = Core.AddTimer(3.0f, () =>
        {
            if (!_roundStarted)
            {
                SendPreRoundMessage();
            }
        }, TimerFlags.REPEAT);
    }

    private void SendPreRoundMessage()
    {
        var loc = Core?._localizer;
        string text = loc?["Imposters.SafePeriodPreRound"] ?? "当前为安全期，无法造成伤害";
        GameModifiersUtils.PrintTitleToChatAll(text, loc!);
    }

    private HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        if (Core == null)
        {
            return HookResult.Continue;
        }
        _roundStarted = true;
        CancelPreRoundTimer();

        float delayBeforeCountdown = _freezeTime > 0 ? _freezeTime : 0f;
        if (delayBeforeCountdown <= 0f)
        {
            BeginCountdownPhase();
        }
        else
        {
            Core.AddTimer(delayBeforeCountdown, BeginCountdownPhase);
        }
        return HookResult.Continue;
    }

    private void BeginCountdownPhase()
    {
        if (Core == null) return;
        StartCountdownMessages(10);
        Core.AddTimer(10.0f, EndSafePeriod);
    }

    private void StartCountdownMessages(int seconds)
    {
        CancelCountdownTimer();
        _countdownRemaining = seconds;
        SendCountdownMessage();
        if (Core == null) return;
        _countdownTimer = Core.AddTimer(1.0f, () =>
        {
            if (_countdownRemaining > 0)
            {
                SendCountdownMessage();
            }
        }, TimerFlags.REPEAT);
    }

    private void SendCountdownMessage()
    {
        if (_countdownRemaining <= 0)
        {
            CancelCountdownTimer();
            return;
        }
        var loc = Core?._localizer;
        string raw = loc?["Imposters.SafePeriodEndsIn", _countdownRemaining.ToString()] ?? $"{_countdownRemaining}秒后解除安全期！";
        GameModifiersUtils.PrintTitleToChatAll(raw, loc!);
        _countdownRemaining--;
        if (_countdownRemaining <= 0)
        {
            CancelCountdownTimer();
        }
    }

    private void EndSafePeriod()
    {
        _damageProtected = false;
        var loc = Core?._localizer;
        string msg = loc?["Imposters.SafePeriodEnded"] ?? "安全期已结束，开始战斗！";
        GameModifiersUtils.PrintTitleToChatAll(msg, loc!);
    }

    private void CancelPreRoundTimer()
    {
        if (_preRoundMessageTimer != null)
        {
            _preRoundMessageTimer.Kill();
            _preRoundMessageTimer = null;
        }
    }

    private void CancelCountdownTimer()
    {
        if (_countdownTimer != null)
        {
            _countdownTimer.Kill();
            _countdownTimer = null;
        }
    }

    private void CancelAllTimers()
    {
        CancelPreRoundTimer();
        CancelCountdownTimer();
    }

    protected override void ApplyPlayerModel(CCSPlayerController? player)
    {
        // 不做模型或位置变换，保持默认。
    }
}


