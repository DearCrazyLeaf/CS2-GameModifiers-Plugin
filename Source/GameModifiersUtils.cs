using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Threading.Tasks;

using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Utils;

using GameModifiers.Modifiers;
using GameModifiers.ThirdParty;
using Microsoft.Extensions.Localization;
using Microsoft.VisualBasic.CompilerServices;

namespace GameModifiers;

internal static class GameModifiersUtils
{
    public static readonly List<string> RangedWeaponNames =
    [
        "weapon_scar20",
        "weapon_revolver",
        "weapon_m249",
        "weapon_mac10",
        "weapon_ak47",
        "weapon_deagle",
        "weapon_m4a1",
        "weapon_m4a1_silencer",
        "weapon_tec9",
        "weapon_xm1014",
        "weapon_p250",
        "weapon_famas",
        "weapon_aug",
        "weapon_mp5sd",
        "weapon_mag7",
        "weapon_bizon",
        "weapon_ssg08",
        "weapon_ump45",
        "weapon_mp9",
        "weapon_p90",
        "weapon_hkp2000",
        "weapon_glock",
        "weapon_awp",
        "weapon_sawedoff",
        "weapon_taser",
        "weapon_mp7",
        "weapon_sg556",
        "weapon_nova",
        "weapon_fiveseven",
        "weapon_cz75a",
        "weapon_usp_silencer",
        "weapon_g3sg1",
        "weapon_negev"
    ];

    // Added categorized lists for primary/pistol selection
    private static readonly string[] PistolWeaponNames = new[]
    {
        "weapon_glock","weapon_hkp2000","weapon_usp_silencer","weapon_p250","weapon_cz75a","weapon_fiveseven","weapon_deagle","weapon_revolver","weapon_tec9"
    };
    private static readonly HashSet<string> PistolSet = PistolWeaponNames.ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] PrimaryWeaponNames = RangedWeaponNames
        .Where(w => !PistolSet.Contains(w) && w != "weapon_taser")
        .ToArray();

    public static string GetRandomPrimaryWeaponName()
        => PrimaryWeaponNames.Length == 0 ? GetRandomRangedWeaponName() : PrimaryWeaponNames[Random.Shared.Next(PrimaryWeaponNames.Length)];

    public static string GetRandomPistolWeaponName()
        => PistolWeaponNames.Length == 0 ? GetRandomRangedWeaponName() : PistolWeaponNames[Random.Shared.Next(PistolWeaponNames.Length)];

    public static List<CCSPlayerController> GetPlayerFromName(string name)
    {
        var players = Utilities.GetPlayers();
        return players.FindAll(x => x.PlayerName.Contains(name, StringComparison.OrdinalIgnoreCase));
    }

    public static void ShowMessageCentreAll(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        var players = Utilities.GetPlayers();
        foreach (var controller in players)
        {
            controller.PrintToCenter(message);
        }
    }

    // 显示多次的中心消息方法，用于在倒计时阶段提高可见性（保留既有逻辑）
    public static void ShowMessageCentreAllWithExtendedDuration(string message, BasePlugin? plugin = null)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        ShowMessageCentreAll(message);
        if (plugin != null)
        {
            // 第二次显示 - 3.5秒后
            plugin.AddTimer(3.5f, () => ShowMessageCentreAll(message));
            // 第三次显示 - 7秒后
            plugin.AddTimer(7.0f, () => ShowMessageCentreAll(message));
        }
        else
        {
            // 理论上外部调用都会传递 plugin，这里仅作为兼容保留
            Task.Run(async () =>
            {
                await Task.Delay(3500);
                Server.NextFrame(() => ShowMessageCentreAll(message));
            });
            Task.Run(async () =>
            {
                await Task.Delay(7000);
                Server.NextFrame(() => ShowMessageCentreAll(message));
            });
        }
    }

    public static void PrintTitleToChat<T>(CCSPlayerController? player, string message, IStringLocalizer<T> localizer)
    {
        if (player == null || string.IsNullOrEmpty(message)) return;
        player.PrintToChat($"{localizer["GameModifiers"]}{message}");
    }

    public static void PrintModifiersToChat<T>(CCSPlayerController? player, List<GameModifierBase> modifiers, string message, IStringLocalizer<T> localizer, bool withDescriptions = true)
    {
        if (player == null) return;
        PrintTitleToChat(player, message, localizer);
        if (!modifiers.Any())
        {
            player.PrintToChat($"{localizer["None"]}");
            return;
        }
        foreach (var modifier in modifiers)
        {
            string description = withDescriptions ? $" - [{ChatColors.Lime}{modifier.Description}{ChatColors.Default}]{ChatColors.Default}" : "";
            player.PrintToChat($"{modifier.Name}{description}");
        }
    }

    public static void PrintTitleToChatAll<T>(string message, IStringLocalizer<T> localizer)
    {
        if (string.IsNullOrEmpty(message)) return;
        var players = Utilities.GetPlayers();
        foreach (var controller in players)
        {
            PrintTitleToChat(controller, message, localizer);
        }
    }

    public static void PrintToChatAll(string message)
    {
        if (string.IsNullOrEmpty(message)) return;
        var players = Utilities.GetPlayers();
        foreach (var controller in players)
        {
            controller.PrintToChat(message);
        }
    }

    public static void ExecuteCommandFromServerOnAllClients(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return;
        var players = Utilities.GetPlayers();
        foreach (var controller in players)
        {
            controller.ExecuteClientCommandFromServer(command);
        }
    }

    public static List<Type> GetAllChildClasses<T>()
    {
        var assembly = Assembly.GetAssembly(typeof(T));
        if (assembly == null) return new List<Type>();
        return assembly.GetTypes().Where(type => type.IsSubclassOf(typeof(T)) && !type.IsAbstract).ToList();
    }

    public static string GetConfigPath(string modulePath)
    {
        DirectoryInfo? moduleDirectory = new FileInfo(modulePath).Directory;
        DirectoryInfo? csSharpDirectory = moduleDirectory?.Parent?.Parent;
        if (csSharpDirectory == null) return string.Empty;
        return Path.Combine(csSharpDirectory.FullName, "configs", "plugins", moduleDirectory!.Name);
    }

    public static string GetPluginPath(string modulePath)
    {
        DirectoryInfo? moduleDirectory = new FileInfo(modulePath).Directory;
        return moduleDirectory?.FullName ?? string.Empty;
    }
    
    public static object? TryGetGameRule(string rule)
    {
        CCSGameRulesProxy? gameRulesProxy = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault();
        if (gameRulesProxy?.GameRules == null) return null;
        var ruleProperty = gameRulesProxy.GameRules.GetType().GetProperty(rule);
        if (ruleProperty != null && ruleProperty.CanRead)
        {
            return ruleProperty.GetValue(gameRulesProxy.GameRules);
        }
        return null;
    }

    public static bool IsWarmupActive()
    {
        var warmupPeriod = TryGetGameRule("WarmupPeriod");
        return warmupPeriod is bool b && b;
    }

    public static string GetModifierName<T>() where T : GameModifierBase, new()
        => new T().Name;

    public static string GetConVarStringValue(ConVar? conVar)
    {
        if (conVar == null) return string.Empty;
        return conVar.Type switch
        {
            ConVarType.Bool => conVar.GetPrimitiveValue<bool>().ToString(CultureInfo.InvariantCulture),
            ConVarType.Float32 => conVar.GetPrimitiveValue<float>().ToString(CultureInfo.InvariantCulture),
            ConVarType.Float64 => conVar.GetPrimitiveValue<double>().ToString(CultureInfo.InvariantCulture),
            ConVarType.UInt16 => conVar.GetPrimitiveValue<ushort>().ToString(CultureInfo.InvariantCulture),
            ConVarType.Int16 => conVar.GetPrimitiveValue<short>().ToString(CultureInfo.InvariantCulture),
            ConVarType.UInt32 => conVar.GetPrimitiveValue<uint>().ToString(CultureInfo.InvariantCulture),
            ConVarType.Int32 => conVar.GetPrimitiveValue<int>().ToString(CultureInfo.InvariantCulture),
            ConVarType.Int64 => conVar.GetPrimitiveValue<long>().ToString(CultureInfo.InvariantCulture),
            ConVarType.UInt64 => conVar.GetPrimitiveValue<ulong>().ToString(CultureInfo.InvariantCulture),
            ConVarType.String => conVar.StringValue,
            ConVarType.Qangle => conVar.GetNativeValue<QAngle>().ToString(),
            ConVarType.Vector2 =>
                conVar.GetNativeValue<Vector2D>() is var v2 ? $"{v2.X:n2} {v2.Y:n2}" : string.Empty,
            ConVarType.Vector3 => conVar.GetNativeValue<Vector>().ToString(),
            ConVarType.Vector4 or ConVarType.Color =>
                conVar.GetNativeValue<Vector4D>() is var v4 ? $"{v4.X:n2} {v4.Y:n2} {v4.Z:n2} {v4.W:n2}" : string.Empty,
            _ => string.Empty
        };
    }

    public static bool ApplyEntityGlowEffect(CBaseEntity? entity, out CDynamicProp? modelRelay, out CDynamicProp? modelGlow)
    {
        if (entity == null)
        {
            modelRelay = null;
            modelGlow = null;
            return false;
        }
        modelRelay = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic");
        modelGlow = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic");
        if (modelRelay == null || !modelRelay.IsValid || modelGlow == null || !modelGlow.IsValid)
        {
            return false;
        }
        string modelName = entity.CBodyComponent!.SceneNode!.GetSkeletonInstance().ModelState.ModelName;
        Console.WriteLine($"Adding glow for: {entity.Globalname} using model: {modelName}");
        modelRelay.Spawnflags = 256u;
        modelRelay.RenderMode = RenderMode_t.kRenderNone;
        modelRelay.CBodyComponent!.SceneNode!.Owner!.Entity!.Flags &= ~(uint)(1 << 2);
        modelRelay.SetModel(modelName);
        modelRelay.DispatchSpawn();
        modelRelay.AcceptInput("FollowEntity", entity, modelRelay, "!activator");
        modelGlow.CBodyComponent!.SceneNode!.Owner!.Entity!.Flags &= ~(uint)(1 << 2);
        modelGlow.SetModel(modelName);
        modelGlow.DispatchSpawn();
        modelGlow.AcceptInput("FollowEntity", modelRelay, modelGlow, "!activator");
        modelGlow.Render = Color.FromArgb(1, 255, 255, 255);
        modelGlow.Spawnflags = 256u;
        modelGlow.RenderMode = RenderMode_t.kRenderGlow;
        modelGlow.Glow.GlowRange = 5000;
        modelGlow.Glow.GlowTeam = -1;
        modelGlow.Glow.GlowType = 3;
        modelGlow.Glow.GlowRangeMin = 20;
        return true;
    }

    public static bool RemoveEntityGlowEffect(int relayIndex, int glowIndex)
    {
        CDynamicProp? modelRelay = Utilities.GetEntityFromIndex<CDynamicProp>(relayIndex);
        if (modelRelay != null && modelRelay.IsValid)
        {
            modelRelay.AcceptInput("Kill");
        }
        CDynamicProp? modelGlow = Utilities.GetEntityFromIndex<CDynamicProp>(glowIndex);
        if (modelGlow != null && modelGlow.IsValid)
        {
            modelGlow.AcceptInput("Kill");
        }
        return true;
    }

    public static bool SetPlayerMaxHealth(CCSPlayerPawn? playerPawn, int health)
    {
        if (playerPawn == null || !playerPawn.IsValid) return false;
        playerPawn.MaxHealth = playerPawn.Health = health;
        Utilities.SetStateChanged(playerPawn, "CBaseEntity", "m_iHealth");
        Utilities.SetStateChanged(playerPawn, "CBaseEntity", "m_iMaxHealth");
        return true;
    }

    public static bool SetPlayerHealth(CCSPlayerPawn? playerPawn, int health)
    {
        if (playerPawn == null || !playerPawn.IsValid) return false;
        playerPawn.Health = health;
        Utilities.SetStateChanged(playerPawn, "CBaseEntity", "m_iHealth");
        return true;
    }

    public static Vector? GetRandomLocation()
    {
        try { return NavMesh.GetRandomPosition(); }
        catch (Exception ex)
        {
            Console.WriteLine($"[GameModifiersUtils::GetRandomLocation] WARNING: Failed to get random location from NavMesh: {ex.Message}");
            var spawnPoints = Utilities.FindAllEntitiesByDesignerName<SpawnPoint>("info_player_terrorist")
                .Concat(Utilities.FindAllEntitiesByDesignerName<SpawnPoint>("info_player_counterterrorist")).ToList();
            if (spawnPoints.Any())
            {
                var randomSpawn = spawnPoints[Random.Shared.Next(spawnPoints.Count)];
                return randomSpawn.AbsOrigin;
            }
            return null;
        }
    }

    public static Vector? GetSpawnLocation(CsTeam team)
    {
        List<SpawnPoint> spawnPoints = team switch
        {
            CsTeam.Terrorist => Utilities.FindAllEntitiesByDesignerName<SpawnPoint>("info_player_terrorist").ToList(),
            CsTeam.CounterTerrorist => Utilities.FindAllEntitiesByDesignerName<SpawnPoint>("info_player_counterterrorist").ToList(),
            _ => new List<SpawnPoint>()
        };
        if (!spawnPoints.Any()) return null;
        return spawnPoints[Random.Shared.Next(spawnPoints.Count)].AbsOrigin;
    }

    public static bool SwapPlayerLocations(CCSPlayerController? firstPlayer, CCSPlayerController? secondPlayer)
    {
        if (firstPlayer == secondPlayer) return false;
        if (firstPlayer == null || !firstPlayer.IsValid || secondPlayer == null || !secondPlayer.IsValid) return false;
        CCSPlayerPawn? firstPawn = firstPlayer.PlayerPawn.Value;
        CCSPlayerPawn? secondPawn = secondPlayer.PlayerPawn.Value;
        if (firstPawn == null || !firstPawn.IsValid || secondPawn == null || !secondPawn.IsValid) return false;
        if (firstPawn.AbsOrigin == null || secondPawn.AbsOrigin == null) return false;
        Vector? firstPos = firstPawn.AbsOrigin != null ? new Vector { X = firstPawn.AbsOrigin.X, Y = firstPawn.AbsOrigin.Y, Z = firstPawn.AbsOrigin.Z } : null;
        TeleportPlayer(firstPlayer, secondPawn.AbsOrigin);
        TeleportPlayer(secondPlayer, firstPos);
        return true;
    }

    public static float GetPlayerSpeedMultiplier(CCSPlayerController? player)
    {
        if (player == null || !player.IsValid) return 1.0f;
        CCSPlayerPawn? pawn = player.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid) return 1.0f;
        return pawn.VelocityModifier;
    }

    public static bool SetPlayerSpeedMultiplier(CCSPlayerController? player, float speedMultiplier)
    {
        if (player == null || !player.IsValid) return false;
        CCSPlayerPawn? pawn = player.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid) return false;
        pawn.VelocityModifier = speedMultiplier;
        Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_flVelocityModifier");
        return true;
    }

    public static bool TeleportPlayerToRandomSpot(CCSPlayerController? player)
    {
        if (player == null || !player.IsValid) return false;
        var pawn = player.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid) return false;
        Vector? randomLocation = GetRandomLocation();
        if (randomLocation == null)
        {
            Console.WriteLine($"[GameModifiersUtils::TeleportPlayerToRandomSpot] WARNING: Failed to find random location for {player.PlayerName}, using spawn area instead!");
            return TeleportPlayerToSpawnArea(player);
        }
        TeleportPlayer(player, randomLocation);
        return true;
    }

    public static bool TeleportPlayerToSpawnArea(CCSPlayerController? player)
    {
        if (player == null || !player.IsValid) return false;
        var pawn = player.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid) return false;
        Vector? spawnLocation = GetSpawnLocation(player.Team);
        if (spawnLocation == null)
        {
            Console.WriteLine($"[GameModifiersUtils::TeleportPlayerToSpawnArea] WARNING: Failed to find spawn point for {player.PlayerName}!");
            return false;
        }
        TeleportPlayer(player, spawnLocation);
        return true;
    }

    public static void TeleportPlayer(CCSPlayerController? player, Vector? position, QAngle? angles = null, Vector? velocity = null)
    {
        if (player == null || !player.IsValid) return;
        var pawn = player.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid) return;
        pawn.Teleport(position, angles, velocity);
        pawn.Collision.CollisionGroup = (byte)CollisionGroup.COLLISION_GROUP_DISSOLVING;
        pawn.Collision.CollisionAttribute.CollisionGroup = (byte)CollisionGroup.COLLISION_GROUP_DISSOLVING;
        Utilities.SetStateChanged(player, "CCollisionProperty", "m_CollisionGroup");
        Utilities.SetStateChanged(player, "VPhysicsCollisionAttribute_t", "m_nCollisionGroup");
        Server.NextFrame(() =>
        {
            if (!pawn.IsValid || pawn.LifeState != (int)LifeState_t.LIFE_ALIVE) return;
            pawn.Collision.CollisionGroup = (byte)CollisionGroup.COLLISION_GROUP_PLAYER;
            pawn.Collision.CollisionAttribute.CollisionGroup = (byte)CollisionGroup.COLLISION_GROUP_PLAYER;
            Utilities.SetStateChanged(player, "CCollisionProperty", "m_CollisionGroup");
            Utilities.SetStateChanged(player, "VPhysicsCollisionAttribute_t", "m_nCollisionGroup");
        });
    }

    public static CBasePlayerWeapon? GetActiveWeapon(CCSPlayerController? player)
        => player?.PlayerPawn.Value?.WeaponServices?.ActiveWeapon.Value;

    public static CSWeaponType GetActiveWeaponType(CCSPlayerController? player)
        => GetWeaponType(GetActiveWeapon(player));

    public static CBasePlayerWeapon? GetWeapon(CCSPlayerController? player, string weaponName)
        => player?.PlayerPawn.Value?.WeaponServices?.MyWeapons.FirstOrDefault(w => w.Value!.DesignerName.Contains(weaponName))?.Value;

    public static List<CBasePlayerWeapon?> GetWeapons(CCSPlayerController? player)
    {
        var weaponHandles = player?.PlayerPawn.Value?.WeaponServices?.MyWeapons.ToList();
        if (weaponHandles == null) return new List<CBasePlayerWeapon?>();
        List<CBasePlayerWeapon?> outWeapons = new();
        foreach (var weaponHandle in weaponHandles)
        {
            if (weaponHandle.IsValid) outWeapons.Add(weaponHandle.Value);
        }
        return outWeapons;
    }

    public static CBasePlayerWeapon? GiveAndEquipWeapon(CCSPlayerController? player, string weaponName)
    {
        if (player == null || !player.IsValid) return null;
        var pawn = player.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid) return null;
        if (pawn.WeaponServices == null) return null;
        CBasePlayerWeapon? weapon = player.GiveNamedItem<CBasePlayerWeapon>(weaponName);
        if (weapon == null || !weapon.IsValid) return null;
        pawn.WeaponServices.ActiveWeapon.Raw = weapon.EntityHandle;
        return weapon;
    }

    public static void RemoveWeapons(CCSPlayerController? player)
    {
        if (player == null || !player.IsValid) return;
        var pawn = player.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid || pawn.WeaponServices == null) return;
        List<CBasePlayerWeapon?> weapons = GetWeapons(player);
        if (!weapons.Any()) return;
        foreach (var weapon in weapons)
        {
            if (weapon == null || !weapon.IsValid) continue;
            switch (GetWeaponType(weapon))
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
                    pawn.WeaponServices.ActiveWeapon.Raw = weapon.EntityHandle.Raw;
                    player.DropActiveWeapon();
                    Server.NextFrame(() =>
                    {
                        if (weapon != null && weapon.IsValid)
                        {
                            weapon.AcceptInput("Kill");
                        }
                    });
                    break;
                }
                default: break;
            }
        }
        foreach (var weaponHandle in pawn.WeaponServices.MyWeapons)
        {
            pawn.WeaponServices.ActiveWeapon.Raw = weaponHandle.Raw;
            break;
        }
    }
    
    public static float GetWeaponDamage(CBasePlayerWeapon? weapon)
    {
        if (weapon == null || !weapon.IsValid) return 0.0f;
        CCSWeaponBaseVData? weaponVData = weapon.As<CCSWeaponBase>().VData;
        return weaponVData?.Damage ?? 0.0f;
    }

    public static void ResetWeaponAmmo(CBasePlayerWeapon? weapon)
    {
        if (weapon == null || !weapon.IsValid) return;
        CCSWeaponBaseVData? weaponVData = weapon.As<CCSWeaponBase>().VData;
        if (weaponVData == null) return;
        if (!IsRangedWeapon(weapon)) return;
        Server.NextFrame(() =>
        {
            weapon.Clip1 = weaponVData.MaxClip1;
            weapon.Clip2 = weaponVData.SecondaryReserveAmmoMax;
            weapon.ReserveAmmo[0] = weaponVData.PrimaryReserveAmmoMax;
            Utilities.SetStateChanged(weapon, "CBasePlayerWeapon", "m_iClip1");
            Utilities.SetStateChanged(weapon, "CBasePlayerWeapon", "m_iClip2");
            Utilities.SetStateChanged(weapon, "CBasePlayerWeapon", "m_pReserveAmmo");
        });
    }

    public static bool IsRangedWeapon(CBasePlayerWeapon? weapon)
    {
        if (weapon == null || !weapon.IsValid) return false;
        CCSWeaponBaseVData? weaponVData = weapon.As<CCSWeaponBase>().VData;
        if (weaponVData == null) return false;
        return weaponVData.WeaponType switch
        {
            CSWeaponType.WEAPONTYPE_PISTOL or
            CSWeaponType.WEAPONTYPE_SUBMACHINEGUN or
            CSWeaponType.WEAPONTYPE_RIFLE or
            CSWeaponType.WEAPONTYPE_SHOTGUN or
            CSWeaponType.WEAPONTYPE_SNIPER_RIFLE or
            CSWeaponType.WEAPONTYPE_MACHINEGUN => true,
            _ => false
        };
    }

    public static CSWeaponType GetWeaponType(CBasePlayerWeapon? weapon)
    {
        if (weapon == null || !weapon.IsValid) return CSWeaponType.WEAPONTYPE_UNKNOWN;
        CCSWeaponBaseVData? weaponVData = weapon.As<CCSWeaponBase>().VData;
        return weaponVData?.WeaponType ?? CSWeaponType.WEAPONTYPE_UNKNOWN;
    }

    public static string GetRandomRangedWeaponName()
        => RangedWeaponNames[Random.Shared.Next(RangedWeaponNames.Count)];

    public static List<CCSPlayerController> GetSpectatingPlayers()
        => Utilities.GetPlayers().Where(player => player.Team == CsTeam.Spectator).ToList();

    public static List<CCSPlayerController> GetCounterTerroristPlayers()
        => Utilities.GetPlayers().Where(player => player.Team == CsTeam.CounterTerrorist).ToList();

    public static List<CCSPlayerController> GetTerroristPlayers()
        => Utilities.GetPlayers().Where(player => player.Team == CsTeam.Terrorist).ToList();
}
