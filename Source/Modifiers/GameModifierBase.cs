using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Localization;

namespace GameModifiers.Modifiers;

public abstract class GameModifierBase
{
    // 私有字段存储默认值
    private string _defaultName = "Unnamed";
    private string _defaultDescription = "";

    // 本地化的 Name 属性
    public virtual string Name
    {
        get
        {
            if (Core?._localizer != null)
            {
                var localizedName = Core._localizer[_defaultName];
                if (!string.IsNullOrEmpty(localizedName) && localizedName != _defaultName)
                {
                    return localizedName;
                }
            }
            return _defaultName;
        }
        protected set { _defaultName = value; }
    }

    // 本地化的 Description 属性
    public virtual string Description
    {
        get
        {
            if (Core?._localizer != null)
            {
                var localizedDescription = Core._localizer[_defaultName + "Description"];
                if (!string.IsNullOrEmpty(localizedDescription) && localizedDescription != _defaultName + "Description")
                {
                    return localizedDescription;
                }
            }
            return _defaultDescription;
        }
        protected set { _defaultDescription = value; }
    }

    public virtual bool SupportsRandomRounds { get; protected set; } = false;
    public virtual bool IsRegistered { get; protected set; } = true;
    public virtual bool IsActive { get; protected set; } = false;
    public virtual HashSet<string> IncompatibleModifiers { get; protected set; } = new HashSet<string>();
    public GameModifiersCore? Core { get; protected set; } = null;
    public ModifierConfig? Config { get; protected set; } = null;
    protected IStringLocalizer? Localizer => Core?._localizer;

    public virtual void Registered(GameModifiersCore? core)
    {
        if (core == null)
        {
            return;
        }

        Core = core;

        var pluginConfigPath = Path.Combine(GameModifiersUtils.GetPluginPath(core.ModulePath), "ModifierConfig");
        if (TryParseConfigPath(pluginConfigPath) == false)
        {
            var configPath = Path.Combine(GameModifiersUtils.GetConfigPath(core.ModulePath), "ModifierConfig");
            TryParseConfigPath(configPath);
        }
    }

    public virtual void Unregistered(GameModifiersCore? core)
    {
        Core = null;
    }

    public virtual void Enabled()
    {
        IsActive = true;

        if (Config != null)
        {
            Config.ApplyConfig();
        }
    }

    public virtual void Disabled()
    {
        IsActive = false;

        if (Config != null)
        {
            Config.RemoveConfig();
        }
    }

    public bool CheckIfIncompatible(GameModifierBase? modifier)
    {
        if (modifier == null)
        {
            return false;
        }

        return IncompatibleModifiers.Contains(modifier._defaultName);
    }

    public bool CheckIfIncompatibleByName(string modifierName)
    {
        return IncompatibleModifiers.Contains(modifierName);
    }

    private bool TryParseConfigPath(string path)
    {
        if (Directory.Exists(path) == false)
        {
            Directory.CreateDirectory(path);
            return false;
        }

        var configFiles = Directory.GetFiles(path, "*.cfg");
        foreach (var configFile in configFiles)
        {
            if (Path.GetFileNameWithoutExtension(configFile) == _defaultName)
            {
                var tempConfig = new ModifierConfig();
                if (tempConfig.ParseConfigFile(configFile))
                {
                    Config = tempConfig;
                    return true;
                }

                break;
            }
        }

        return false;
    }
}
