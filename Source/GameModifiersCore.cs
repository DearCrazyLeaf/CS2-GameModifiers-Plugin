using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Localization;

using GameModifiers.Modifiers;

namespace GameModifiers;

public class GameModifiersCore : BasePlugin, IPluginConfig<GameModifiersConfig>
{
    public override string ModuleName => "Game Modifiers(hlymcn edition)";
    public override string ModuleAuthor => "Shr00mDev|hlymcn";
    public override string ModuleDescription => "Apply game modifiers dynamically based of pre-defined classes or config files.";
    public override string ModuleVersion => "[hlymcn]2.0.0";
    public GameModifiersConfig Config { get; set; } = new();
    public bool RandomRoundsEnabled { get; private set; } = false;
    private List<GameModifierBase> RegisteredModifiers { get; } = new();
    private List<GameModifierBase> ActiveModifiers { get; } = new();
    private List<GameModifierBase> LastActiveModifiers { get; set; } = new();
    public readonly IStringLocalizer<GameModifiersCore> _localizer;

    private int _minRandomRounds = 1;
    private int _maxRandomRounds = 1;

    public GameModifiersCore(IStringLocalizer<GameModifiersCore> localizer)
    {
        _localizer = localizer;
    }

    public void OnConfigParsed(GameModifiersConfig config)
    {
        Config = config;

        _minRandomRounds = config.MinRandomRounds;
        _maxRandomRounds = config.MaxRandomRounds;
    }

    public override void Load(bool hotReload)
    {
        base.Load(hotReload);

        Initialise();

        Console.WriteLine("[GameModifiers::Load] Successfully loaded!");
    }

    public override void Unload(bool hotReload)
    {
        RemoveAllModifiers();

        foreach (GameModifierBase? modifier in RegisteredModifiers)
        {
            modifier.Unregistered(this);
        }

        LastActiveModifiers.Clear();
        RegisteredModifiers.Clear();

        base.Unload(hotReload);
    }

    private void Initialise()
    {
        InitialiseModifiers();
        InitialiseCvarModifiers();

        // Verify modifier names as they are treated as keys in some commands and should be unique!
        List<string> registeredModifierNames = new List<string>();
        foreach (GameModifierBase? modifier in RegisteredModifiers)
        {
            modifier.Registered(this);

            if (registeredModifierNames.Contains(modifier.Name))
            {
                Console.WriteLine($"[GameModifiers::Initialise] WARNING: Found duplicate modifier name entry for {modifier.Name} all modifier names should be unique!");
                continue;
            }

            registeredModifierNames.Add(modifier.Name);
        }
        
        if (Config.RandomRoundsEnabledByDefault)
        {
            if (RegisteredModifiers.Count <= 0)
            {
                Console.WriteLine("[GameModifiers::Initialise] WARNING: No modifiers are registered! Cannot activate random rounds by default!");
            }
            else
            {
                RandomRoundsEnabled = true;
            }
        }
    }

    private void InitialiseModifiers()
    {
        RegisteredModifiers.Clear();

        List<Type> modifierTypes = GameModifiersUtils.GetAllChildClasses<GameModifierBase>();
        if (!modifierTypes.Any())
        {
            Console.WriteLine("[GameModifiers::InitialiseModifiers] No implemented modifiers found!");
            return;
        }

        foreach (Type modifierType in modifierTypes)
        {
            GameModifierBase modifierInstance = (GameModifierBase)Activator.CreateInstance(modifierType)!;
            if (modifierInstance.IsRegistered && !Config.DisabledModifiers.Any(x => x.Equals(modifierInstance.Name, StringComparison.OrdinalIgnoreCase)))
            {
                RegisteredModifiers.Add(modifierInstance);
                Console.WriteLine($"[GameModifiersCore::InitialiseModifiers] Registered modifier: {modifierInstance.Name}");
            }
            else
            {
                Console.WriteLine($"[GameModifiersCore::InitialiseModifiers] Disabled modifier: {modifierInstance.Name}");
            }
        }
    }

    private void InitialiseCvarModifiers()
    {
        var configPath = Path.Combine(GameModifiersUtils.GetConfigPath(ModulePath), "ConVarModifiers");
        var pluginPath = Path.Combine(GameModifiersUtils.GetPluginPath(ModulePath), "ConVarModifiers");

        ParseFileForCvarModifiers(configPath);
        ParseFileForCvarModifiers(pluginPath);
    }

    public void ParseFileForCvarModifiers(string configFolderPath)
    {
        if (Directory.Exists(configFolderPath) == false)
        {
            Directory.CreateDirectory(configFolderPath);
            return;
        }

        var configFiles = Directory.GetFiles(configFolderPath, "*.cfg");

        foreach (var configFile in configFiles)
        {
            GameModifierCvar cvarModifierInstance = new GameModifierCvar();

            if (cvarModifierInstance.ParseConfigFile(configFile))
            {
                if (!Config.DisabledModifiers.Contains(cvarModifierInstance.Name))
                {
                    RegisteredModifiers.Add(cvarModifierInstance);
                    Console.WriteLine($"[GameModifiersCore::InitialiseCvarModifiers] Registered modifier config: {configFile}");
                }
                else
                {
                    Console.WriteLine($"[GameModifiersCore::InitialiseCvarModifiers] Disabled modifier config: {configFile}");
                }
            }
            else
            {
                Console.WriteLine($"[GameModifiersCore::InitialiseCvarModifiers] Failed to load config: {configFile}");
            }
        }
    }

    // !ReloadModifiers - Re-initialises all modifiers.
    [ConsoleCommand("css_reloadmodifiers", "Re-initialises all registered modifiers. (This will remove all active modifiers too)")]
    [RequiresPermissions("@css/root")]
    public void OnReloadModifiers(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null)
        {
            return;
        }

        GameModifiersUtils.PrintTitleToChat(player, _localizer["Reloading Modifiers..."], _localizer);

        RemoveAllModifiers();
        Initialise();
    }

    // !ListModifiers - List registered modifiers and descriptions.
    [ConsoleCommand("css_listmodifiers", "Prints the name and description for each registered modifier.")]
    public void OnListModifiers(CCSPlayerController? player, CommandInfo info)
    {
        GameModifiersUtils.PrintModifiersToChat(player, RegisteredModifiers, _localizer["Registered modifiers"], _localizer);
    }

    // !ListActiveModifiers - List active modifiers and descriptions.
    [ConsoleCommand("css_listactivemodifiers", "Prints the name and description for each active modifier.")]
    public void OnListActiveModifiers(CCSPlayerController? player, CommandInfo info)
    {
        GameModifiersUtils.PrintModifiersToChat(player, ActiveModifiers, _localizer["Active modifiers"], _localizer);
    }

    // !AddModifier - Add a modifier by name.
    [ConsoleCommand("css_addmodifier", "Add a modifier that will persist until the end of the game. \u2029" + "(If random rounds are enabled it will only act as a re-roll for the current round)")]
    [CommandHelper(1, "<modifier name>")]
    [RequiresPermissions("@css/root")]
    public void OnAddModifier(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null)
        {
            return;
        }

        var modifierName = info.GetArg(1);
        if (AddModifierByName(modifierName, out string addMessage))
        {
            GameModifiersUtils.PrintTitleToChat(player, _localizer["Added {0} modifier.", modifierName], _localizer);
        }
        else
        {
            GameModifiersUtils.PrintTitleToChat(player, addMessage, _localizer);
        }
    }

    // !ToggleModifier - Enabled/Disables a given modifier by name.
    [ConsoleCommand("css_togglemodifier", "Enabled/Disables a given modifier by name. \u2029" + "(If random rounds are enabled it will only act as a re-roll for the current round)")]
    [CommandHelper(1, "<modifier name>")]
    [RequiresPermissions("@css/root")]
    public void OnToggleModifier(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null)
        {
            return;
        }

        var modifierName = info.GetArg(1);
        bool modifierWasActive = IsModifierActiveByName(modifierName);

        if (ToggleModifierByName(modifierName, out string toggleMessage))
        {
             string modifierAction = modifierWasActive ? _localizer["Removed"] : _localizer["Added"];
             GameModifiersUtils.PrintTitleToChat(player, _localizer["{0} {1} modifier.", modifierAction, modifierName], _localizer);
        }
        else
        {
            GameModifiersUtils.PrintTitleToChat(player, toggleMessage, _localizer);
        }
    }

    // !AddRandomModifier - Add a random modifier.
    [ConsoleCommand("css_addrandommodifier", "Add a random modifier to be activated immediately.")]
    [RequiresPermissions("@css/root")]
    public void OnAddRandomModifier(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null)
        {
            return;
        }

        if (!AddRandomModifier(out GameModifierBase? addedModifier))
        {
            GameModifiersUtils.PrintTitleToChat(player, _localizer["Failed to add random modifier."], _localizer);
        }
    }

    // !AddRandomModifiers - Add a random number of modifiers.
    [ConsoleCommand("css_addrandommodifiers", "Add a random number of modifiers to be activated immediately.")]
    [CommandHelper(1, "<modifier count>")]
    [RequiresPermissions("@css/root")]
    public void OnAddRandomModifiers(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null)
        {
            return;
        }

        if (int.TryParse(info.GetArg(1), out int modifierCount))
        {
            if (AddRandomModifiers(modifierCount, out List<GameModifierBase> addedModifiers))
            {
                if (modifierCount == addedModifiers.Count)
                {
                    GameModifiersUtils.PrintTitleToChat(player, _localizer["Adding {0} random modifiers.", modifierCount.ToString()], _localizer);
                }
                else
                {
                    GameModifiersUtils.PrintTitleToChat(player, _localizer["Only added {0} random modifiers.", addedModifiers.Count.ToString()], _localizer);
                }

                return;
            }
        }

        GameModifiersUtils.PrintTitleToChat(player, _localizer["Failed to add random modifiers."], _localizer);
    }

    // !RemoveModifier - Remove a modifier by name.
    [ConsoleCommand("css_removemodifier", "Remove an active modifier.")]
    [CommandHelper(1, "<modifier name>")]
    [RequiresPermissions("@css/root")]
    public void OnRemoveModifier(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null)
        {
            return;
        }

        var modifierName = info.GetArg(1);
        RemoveModifierByName(modifierName, out string removeMessage);
        GameModifiersUtils.PrintTitleToChat(player, removeMessage, _localizer);
    }

    // !RemoveModifiers - Clear / Remove all active modifiers.
    [ConsoleCommand("css_removemodifiers", "Clear / Remove all active random round modifiers.")]
    [RequiresPermissions("@css/root")]
    public void OnRemoveModifiers(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null)
        {
            return;
        }

        RemoveAllModifiers();

        GameModifiersUtils.PrintTitleToChat(player, _localizer["Removed all modifiers."], _localizer);
    }

    // !RandomRounds - Toggle random rounds on/off.
    [ConsoleCommand("css_randomrounds", "Toggle random rounds on/off. This will add a random set of modifiers at the start of each round that persist till the end of the round.")]
    [RequiresPermissions("@css/root")]
    public void OnRandomRounds(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null)
        {
            return;
        }

        if (RandomRoundsEnabled == false && RegisteredModifiers.Count <= 0)
        {
            GameModifiersUtils.PrintTitleToChat(player, _localizer["No modifiers are registered! Cannot activate random rounds!"], _localizer);
            return;
        }

        ToggleRandomRounds();
    }

    // !MinRandomRounds - Set the min number of random round modifiers to be active each round.
    [ConsoleCommand("css_minrandomrounds", "Set the min number of random round modifiers to be active each round.")]
    [CommandHelper(1, "<min number>")]
    [RequiresPermissions("@css/root")]
    public void OnMinRandomRounds(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null)
        {
            return;
        }

        var minInput = info.GetArg(1);
        if (int.TryParse(minInput, out int result))
        {
            GameModifiersUtils.PrintTitleToChat(player, _localizer["Min modifiers for random rounds set to {0}", minInput], _localizer);
            _minRandomRounds = result;
        }
        else
        {
            GameModifiersUtils.PrintTitleToChat(player, _localizer["Failed to set min modifiers for random rounds to {0}", minInput], _localizer);
        }
    }

    // !MaxRandomRounds - Set the min number of random round modifiers to be active each round.
    [ConsoleCommand("css_maxrandomrounds", "Set the max number of random round modifiers to be active each round.")]
    [CommandHelper(1, "<max number>")]
    [RequiresPermissions("@css/root")]
    public void OnMaxRandomRounds(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null)
        {
            return;
        }

        var maxInput = info.GetArg(1);
        if (int.TryParse(maxInput, out int result))
        {
            GameModifiersUtils.PrintTitleToChat(player, _localizer["Max modifiers for random rounds set to {0}", maxInput], _localizer);
            _maxRandomRounds = result;
        }
        else
        {
            GameModifiersUtils.PrintTitleToChat(player, _localizer["Failed to set max modifiers for random rounds to {0}", maxInput], _localizer);
        }
    }

    // !RandomRoundsReRoll - Re-roll the current random round modifiers.
    [ConsoleCommand("css_randomroundsreroll", "Re-roll the current random round modifiers and apply them to the current round.")]
    [RequiresPermissions("@css/root")]
    public void OnRandomRoundsReRoll(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null)
        {
            return;
        }

        if (RandomRoundsEnabled == false)
        {
            GameModifiersUtils.PrintTitleToChat(player, _localizer["Random rounds are not enabled! Cannot re-roll modifiers."], _localizer);
            return;
        }

        if (!RegisteredModifiers.Any())
        {
            GameModifiersUtils.PrintTitleToChat(player, _localizer["No registered modifiers found! Cannot re-roll modifiers."], _localizer);
            return;
        }

        RemoveAllModifiers();
        ApplyRandomRoundsForRound();
    }

    // !bhop - Enable/Disable the bhop modifier for all players.
    //[ConsoleCommand("css_bhop", "Enable/Disable the bhop modifier")]
    //[RequiresPermissions("@css/root")]
    //public void OnBunnyHop(CCSPlayerController? player, CommandInfo info)
    //{
    //    if (player != null)
    //    {
    //        ToggleModifierByNameCommand(player, "Bhop");
    //    }
    //}

    // !surf - Enable/Disable the surf modifier for all players.
    //[ConsoleCommand("css_surf", "Enable/Disable the surf modifier")]
    //[RequiresPermissions("@css/root")]
    //public void OnSurf(CCSPlayerController? player, CommandInfo info)
    //{
    //    if (player != null)
    //    {
    //        ToggleModifierByNameCommand(player, "Surf");
    //    }
    //}

    // !xray - Enable/Disable the xray modifier for all players.
    //[ConsoleCommand("css_xray", "Enable/Disable the xray modifier for all players")]
    //[RequiresPermissions("@css/root")]
    //public void OnXray(CCSPlayerController? player, CommandInfo info)
    //{
    //    if (player != null)
    //    {
    //        ToggleModifierCommand(player, typeof(GameModifierXrayAll));
    //    }
    //}

    [GameEventHandler]
    public HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        if (RandomRoundsEnabled)
        {
            if (!RegisteredModifiers.Any())
            {
                GameModifiersUtils.PrintTitleToChatAll(_localizer["No registered modifiers found! Skipping random round..."], _localizer);
                return HookResult.Continue;
            }
            
            RemoveAllModifiers();
            
            if (Config.DisableRandomRoundsInWarmup && GameModifiersUtils.IsWarmupActive())
            {
                GameModifiersUtils.PrintTitleToChatAll(_localizer["Random rounds will start after warmup period..."], _localizer);
            }
            else
            {
                ApplyRandomRoundsForRound();
            }
        }
        else
        {
            // Re-apply all active modifiers on round start to ensure
            // everything is correctly set-up that could have been reset since last round.
            foreach (GameModifierBase? modifier in ActiveModifiers)
            {
                modifier.Disabled();
                modifier.Enabled();
            }
        }

        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        if (RandomRoundsEnabled)
        {
            RemoveAllModifiers();
        }

        return HookResult.Continue;
    }

    public GameModifierBase? GetRegisteredModifierByName(string modifierName)
    {
        return RegisteredModifiers.FirstOrDefault(modifier => string.Equals(modifier.Name, modifierName, StringComparison.OrdinalIgnoreCase));
    }

    public GameModifierBase? GetActiveModifierByName(string modifierName)
    {
        return ActiveModifiers.FirstOrDefault(modifier => string.Equals(modifier.Name, modifierName, StringComparison.OrdinalIgnoreCase));
    }

    public bool AnyModifiersActive()
    {
        return ActiveModifiers.Any();
    }

    public bool IsModifierActive(GameModifierBase? modifier)
    {
        if (modifier != null)
        {
            return ActiveModifiers.Contains(modifier);
        }

        return false;
    }

    public bool IsModifierActiveByName(string modifierName)
    {
        GameModifierBase? activeModifier = GetActiveModifierByName(modifierName);
        if (activeModifier != null)
        {
            return true;
        }

        return false;
    }

    public bool IsModifierRegistered(GameModifierBase? modifier)
    {
        if (modifier != null)
        {
            return RegisteredModifiers.Contains(modifier);
        }

        return false;
    }

    public bool IsModifierRegisteredByName(string modifierName)
    {
        GameModifierBase? registeredModifier = GetRegisteredModifierByName(modifierName);
        if (registeredModifier != null)
        {
            return true;
        }

        return false;
    }

    public void ToggleRandomRounds()
    {
        RandomRoundsEnabled = !RandomRoundsEnabled;
        if (RandomRoundsEnabled == false)
        {
            RemoveAllModifiers();
        }
        
        GameModifiersUtils.PrintTitleToChatAll(RandomRoundsEnabled ? _localizer["Random rounds enabled for next round!"] : _localizer["Random rounds disabled!"], _localizer);
        // 只在切换随机回合开关时显示简单的中心消息，不使用延长显示
        GameModifiersUtils.ShowMessageCentreAll(_localizer["Random Rounds {0}", (RandomRoundsEnabled ? _localizer["Enabled"] : _localizer["Disabled"])]);
    }

    public void ApplyRandomRoundsForRound()
    {
        Random random = new Random();
        int randomModifiersCount = random.Next(_minRandomRounds, _maxRandomRounds);

        if (AddRandomModifiers(randomModifiersCount, out List<GameModifierBase> addedModifiers))
        {
            LastActiveModifiers = ActiveModifiers.ToList();
        }
        else
        {
            GameModifiersUtils.PrintTitleToChatAll(_localizer["Failed to apply random modifiers! Skipping random round..."], _localizer);
        }
    }

    public bool ToggleModifier(GameModifierBase? modifier, out string message)
    {
        if (modifier == null)
        {
            Console.WriteLine("[GameModifiers::ToggleModifier] WARNING: Trying to toggle null modifier!");
            message = _localizer["Modifier is null?"];
            return false;
        }

        if (IsModifierActive(modifier))
        {
            return RemoveModifier(modifier, out message);
        }

        return AddModifier(modifier, out message);
    }

    public bool ToggleModifierByName(string modifierName, out string message)
    {
        if (IsModifierRegisteredByName(modifierName) == false)
        {
            Console.WriteLine($"[GameModifiers::ToggleModifierByName] Trying to toggle un-registered modifier {modifierName}!");
            message = _localizer["{0} modifier is not registered.", modifierName];
            return false;
        }

        if (IsModifierActiveByName(modifierName))
        {
            RemoveModifierByName(modifierName, out message);
            return true;
        }

        return AddModifierByName(modifierName, out message);
    }

    private void ToggleModifierCommand(CCSPlayerController? player, Type modifierType)
    {
        if (player == null)
        {
            return;
        }

        GameModifierBase? modifierInstance = RegisteredModifiers.FirstOrDefault(modifier => modifier.GetType() == modifierType);
        if (modifierInstance == null)
        {
            Console.WriteLine($"[GameModifiers::ToggleModifierCommand] Trying to toggle un-registered modifier {modifierType.Name}!");
            return;
        }

        bool modifierWasActive = IsModifierActive(modifierInstance);

        if (ToggleModifier(modifierInstance, out string toggleMessage))
        {
            GameModifiersUtils.PrintTitleToChat(player, _localizer["{0} {1}.", modifierInstance.Name, (modifierWasActive ? _localizer["Disabled"] : _localizer["Enabled"])], _localizer);
        }
        else
        {
            GameModifiersUtils.PrintTitleToChat(player, toggleMessage, _localizer);
        }
    }

    private void ToggleModifierByNameCommand(CCSPlayerController? player, string modifierName)
    {
        bool modifierWasActive = IsModifierActiveByName(modifierName);

        if (ToggleModifierByName(modifierName, out string toggleMessage))
        {
            GameModifiersUtils.PrintTitleToChat(player, _localizer["{0} {1}.", modifierName, (modifierWasActive ? _localizer["Disabled"] : _localizer["Enabled"])], _localizer);
        }
        else
        {
            GameModifiersUtils.PrintTitleToChat(player, toggleMessage, _localizer);
        }
    }

    public bool AddModifierByName(string modifierName, out string message)
    {
        if (!RegisteredModifiers.Any())
        {
            message = _localizer["No modifiers are registered."];
            return false;
        }

        GameModifierBase? registeredModifier = GetRegisteredModifierByName(modifierName);
        if (registeredModifier != null)
        {
            return AddModifier(registeredModifier, out message);
        }

        message = _localizer["{0} modifier is not registered!", modifierName];
        return false;
    }

    public bool AddModifier(GameModifierBase? modifier, out string message)
    {
        if (modifier == null)
        {
            Console.WriteLine("[GameModifiers::AddModifier] WARNING: Trying to add null modifier!");
            message = _localizer["Modifier is null?"];
            return false;
        }

        List<string> blockingModifierNames = new List<string>();
        foreach (GameModifierBase activeModifier in ActiveModifiers)
        {
            if (activeModifier.CheckIfIncompatible(modifier) || modifier.CheckIfIncompatible(activeModifier))
            {
                blockingModifierNames.Add(activeModifier.Name);
            }
        }

        if (blockingModifierNames.Any())
        {
            message = _localizer["{0} modifier is blocked by:", modifier.Name];
            foreach (var blockingModifierName in blockingModifierNames)
            {
                message += $"\u2029• {blockingModifierName}";
            }

            return false;
        }

        if (ActiveModifiers.Contains(modifier))
        {
            message = _localizer["{0} modifier is already active.", modifier.Name];
            return false;
        }

        ActivateModifier(modifier);
        message = _localizer["Successfully added {0} modifier.", modifier.Name];
        return true;
    }

    public void RemoveModifierByName(string modifierName, out string message)
    {
        if (!ActiveModifiers.Any())
        {
            message = _localizer["No modifiers are active."];

            return;
        }
        
        foreach (GameModifierBase? modifier in ActiveModifiers)
        {
            if (string.Equals(modifier.Name, modifierName, StringComparison.OrdinalIgnoreCase))
            {
                RemoveModifier(modifier, out message);
                return;
            }
        }

        message = _localizer["{0} modifier is not active.", modifierName];
    }

    public bool RemoveModifier(GameModifierBase? modifier, out string message)
    {
        if (modifier == null)
        {
            Console.WriteLine("[GameModifiers::RemoveModifier] WARNING: Trying to remove null modifier!");
            message = _localizer["Modifier is null?"];
            return false;
        }

        if (!ActiveModifiers.Contains(modifier))
        {
            message = _localizer["{0} modifier is not active.", modifier.Name];
            return true;
        }

        modifier.Disabled();
        ActiveModifiers.Remove(modifier);
        message = _localizer["Removed {0} modifier.", modifier.Name];
        return true;
    }

    public void RemoveAllModifiers()
    {
        if (!ActiveModifiers.Any())
        {
            return;
        }
        
        // 构建完整的移除消息，包含标题和所有修改器名称
        string completeMessage = _localizer["Removing modifiers:"];
        for (var index = 0; index < ActiveModifiers.Count; index++)
        {
            completeMessage += ActiveModifiers[index].Name;
            if (index < ActiveModifiers.Count - 1)
            {
                completeMessage += ", ";
            }
        }
        
        GameModifiersUtils.PrintTitleToChatAll(completeMessage, _localizer);

        // Undo modifiers in the order they were applied.
        for (var index = ActiveModifiers.Count - 1; index >= 0; index--)
        {
            var modifier = ActiveModifiers[index];
            modifier.Disabled();
        }

        ActiveModifiers.Clear();
    }

    public bool AddRandomModifier(out GameModifierBase? addedModifier)
    {
        if (AddRandomModifiers(1, out List<GameModifierBase> addedModifiers))
        {
            addedModifier = addedModifiers[0];
            return true;
        }

        addedModifier = null;
        return false;
    }

    public bool AddRandomModifiers(int modifierCount, out List<GameModifierBase> addedModifiers)
    {
        addedModifiers = new List<GameModifierBase>();

        if (modifierCount <= 0)
        {
            return true;
        }
        
        if (!RegisteredModifiers.Any())
        {
            Console.WriteLine("[GameModifiers::AddRandomModifiers] No registered modifiers available!");
            return false;
        }

        // Filter out modifiers not supporting random rounds and those currently active.
        List<GameModifierBase> randomModifiersPool = RegisteredModifiers
            .Where(modifier => modifier.SupportsRandomRounds && !ActiveModifiers.Contains(modifier) && (Config.CanRepeat || !LastActiveModifiers.Contains(modifier)))
            .ToList();

        // Randomly remove modifiers that are incompatible within the randomModifiersPool.
        List<GameModifierBase> possibleModifiersPool = randomModifiersPool.ToList();
        Random random = new Random();

        for (int a = 0; a < randomModifiersPool.Count; a++)
        {
            for (int b = a + 1; b < randomModifiersPool.Count; b++)
            {
                if (randomModifiersPool[a].CheckIfIncompatible(randomModifiersPool[b]) ||
                    randomModifiersPool[b].CheckIfIncompatible(randomModifiersPool[a]))
                {
                    possibleModifiersPool.Remove(random.Next(0, 2) == 0 ? randomModifiersPool[a] : randomModifiersPool[b]);
                }
            }
        }

        if (!possibleModifiersPool.Any())
        {
            Console.WriteLine("[GameModifiers::AddRandomModifiers] Modifier pool is empty!");
            return false;
        }

        // Adjust modifierCount if not enough candidates.
        if (modifierCount > possibleModifiersPool.Count)
        {
            Console.WriteLine($"[GameModifiers::AddRandomModifiers] Not enough modifiers in possible modifiers pool, reduced random modifier count from {modifierCount} to {possibleModifiersPool.Count}!");
            modifierCount = possibleModifiersPool.Count;
        }

        // Generate a list of random modifiers from possibleModifiersPool.
        for (int i = 0; i < modifierCount; i++)
        {
            int randomIndex = random.Next(possibleModifiersPool.Count);
            addedModifiers.Add(possibleModifiersPool[randomIndex]);
            possibleModifiersPool.RemoveAt(randomIndex);
        }

        if (!addedModifiers.Any())
        {
            return false;
        }

        ActivateModifiers(addedModifiers);
        return true;
    }

    private void ActivateModifier(GameModifierBase? modifier)
    {
        if (modifier == null)
        {
            Console.WriteLine("[RoundModifiers::ActivateModifier] WARNING: Attempting to activate a null modifier!");
            return;
        }

        ActivateModifiers(new List<GameModifierBase> { modifier });
    }

    private void ActivateModifiers(List<GameModifierBase> modifiers)
    {
        if (!modifiers.Any())
        {
            return;
        }

        if (Config.ShowCentreMsg)
        {
            // 构建包含游戏模式和描述的详细中心消息
            string centreActivationMsg = _localizer["Activating Modifiers:"] + "\n";

            for (var index = 0; index < modifiers.Count; index++)
            {
                if (index > 0)
                {
                    centreActivationMsg += "\n";
                }

                centreActivationMsg += $"{modifiers[index].Name} - {modifiers[index].Description}";
            }

            // 使用新的延长显示时间方法，只在激活时显示两次，传递this插件实例
            GameModifiersUtils.ShowMessageCentreAllWithExtendedDuration(centreActivationMsg, this);
        }

        // 构建完整的消息，包含标题和所有修改器
        string completeMessage = _localizer["Activating modifiers:"];
        for (var index = 0; index < modifiers.Count; index++)
        {
            completeMessage += $"{modifiers[index].Name} - [{ChatColors.Lime}{modifiers[index].Description}{ChatColors.Default}]";
            if (index < modifiers.Count - 1)
            {
                completeMessage += ", ";
            }
        }
        
        GameModifiersUtils.PrintTitleToChatAll(completeMessage, _localizer);

        foreach (GameModifierBase? modifier in modifiers)
        {
            modifier.Enabled();
            ActiveModifiers.Add(modifier);
        }
    }
}
