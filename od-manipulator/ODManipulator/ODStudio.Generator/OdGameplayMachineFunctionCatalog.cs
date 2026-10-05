using ODStudio.Model;

namespace ODStudio.Generator;

public enum OdGameplayMachineFunction
{
    OnGameStart,
    OnTick,
    OnAuthorityStarted,
    OnPlayerJoin,
    OnPlayerDisconnected,
    OnProvisionPartitions,
    OnQueryPartitions,
    TryResolvePartition,
}

[Flags]
public enum OdGameplayMachineNetworkModes
{
    None = 0,
    StateSync = 1,
    Lockstep = 2,
    All = StateSync | Lockstep,
}

public sealed record OdGameplayMachineFunctionDescriptor(
    OdGameplayMachineFunction Function,
    Guid CodeOwnerId,
    string Name,
    string Signature,
    string Description,
    string FileName,
    OdGameplayMachineNetworkModes NetworkModes = OdGameplayMachineNetworkModes.All);

public static class OdGameplayMachineFunctionCatalog
{
    public static IReadOnlyList<OdGameplayMachineFunctionDescriptor> All { get; } =
    [
        new(
            OdGameplayMachineFunction.OnGameStart,
            Guid.Parse("7d4205f4-87ae-4905-9211-ce9846abf107"),
            "OnGameStart",
            "void OnGameStart()",
            "Runs once after Lockstep player slots are assigned and before the first tick.",
            "OnGameStart.cs",
            OdGameplayMachineNetworkModes.Lockstep),
        new(
            OdGameplayMachineFunction.OnTick,
            Guid.Parse("7d4205f4-87ae-4905-9211-ce9846abf106"),
            "OnTick",
            "void OnTick()",
            "Runs at the configured fixed interval after player input for the current tick.",
            "OnTick.cs"),
        new(
            OdGameplayMachineFunction.OnAuthorityStarted,
            Guid.Parse("7d4205f4-87ae-4905-9211-ce9846abf100"),
            "OnAuthorityStarted",
            "void OnAuthorityStarted()",
            "Called once after the Authority root and initial scene have been created.",
            "OnAuthorityStarted.cs"),
        new(
            OdGameplayMachineFunction.OnPlayerJoin,
            Guid.Parse("7d4205f4-87ae-4905-9211-ce9846abf101"),
            "OnPlayerJoin",
            "void OnPlayerJoin(PlayerController playerController)",
            "Called after a PlayerController joins this Authority gameplay machine.",
            "OnPlayerJoin.cs"),
        new(
            OdGameplayMachineFunction.OnPlayerDisconnected,
            Guid.Parse("7d4205f4-87ae-4905-9211-ce9846abf102"),
            "OnPlayerDisconnected",
            "void OnPlayerDisconnected(PlayerController playerController)",
            "Called when a PlayerController disconnects from this Authority gameplay machine.",
            "OnPlayerDisconnected.cs"),
        new(
            OdGameplayMachineFunction.OnProvisionPartitions,
            Guid.Parse("7d4205f4-87ae-4905-9211-ce9846abf103"),
            "OnProvisionPartitions",
            "void OnProvisionPartitions(Position center, float range)",
            "Creates missing partitions around an interest origin when dynamic provisioning is enabled.",
            "OnProvisionPartitions.cs",
            OdGameplayMachineNetworkModes.StateSync),
        new(
            OdGameplayMachineFunction.OnQueryPartitions,
            Guid.Parse("7d4205f4-87ae-4905-9211-ce9846abf104"),
            "OnQueryPartitions",
            "void OnQueryPartitions(Position center, float range, ISet<PartitionID> results)",
            "Adds visible partitions for a custom partition resolver.",
            "OnQueryPartitions.cs",
            OdGameplayMachineNetworkModes.StateSync),
        new(
            OdGameplayMachineFunction.TryResolvePartition,
            Guid.Parse("7d4205f4-87ae-4905-9211-ce9846abf105"),
            "TryResolvePartition",
            "bool TryResolvePartition(Position position, out PartitionID partition)",
            "Resolves a position to one partition for a custom partition resolver.",
            "TryResolvePartition.cs",
            OdGameplayMachineNetworkModes.StateSync),
    ];

    public static OdGameplayMachineFunctionDescriptor Get(OdGameplayMachineFunction function) =>
        All.Single(item => item.Function == function);

    public static bool IsAvailable(OdProject project, OdGameplayMachineFunction function) =>
        IsAvailable(project, Get(function));

    public static bool IsAvailable(OdProject project, OdGameplayMachineFunctionDescriptor descriptor)
    {
        OdGameplayMachineNetworkModes mode = project.Runtime.NetworkMode == OdNetworkMode.Lockstep
            ? OdGameplayMachineNetworkModes.Lockstep
            : OdGameplayMachineNetworkModes.StateSync;
        return (descriptor.NetworkModes & mode) != 0;
    }

    public static bool IsAvailable(OdProject project, OdCodeFile file)
    {
        if (file.Kind != OdCodeFileKind.GameplayMachine || file.OwnerId is not { } ownerId)
            return true;
        OdGameplayMachineFunctionDescriptor? descriptor = All.FirstOrDefault(item => item.CodeOwnerId == ownerId);
        return descriptor is null || IsAvailable(project, descriptor);
    }

    public static string Signature(OdProject project, OdGameplayMachineFunctionDescriptor descriptor)
    {
        return descriptor.Signature;
    }

    public static string RelativePath(OdProject project, OdGameplayMachineFunctionDescriptor descriptor) =>
        $"{OdGenerationContext.NamespaceDirectory(project.DefaultNamespace)}/GameplayMachine/{descriptor.FileName}";

    public static string EmitImplementation(OdProject project, OdGameplayMachineFunctionDescriptor descriptor)
    {
        string projectName = OdProjectCodeManager.SafeProjectName(project);
        string rootNamespace = OdGenerationContext.NamespaceIdentifier(project.DefaultNamespace);
        string body = descriptor.Function switch
        {
            OdGameplayMachineFunction.OnGameStart =>
                "protected override void OnGameStart()\n        {\n        }",
            OdGameplayMachineFunction.OnTick =>
                "protected override void OnTick()\n        {\n        }",
            OdGameplayMachineFunction.OnAuthorityStarted =>
                "protected override void OnAuthorityStarted()\n        {\n        }",
            OdGameplayMachineFunction.OnPlayerJoin =>
                "protected override void OnPlayerJoin(PlayerController playerController)\n        {\n        }",
            OdGameplayMachineFunction.OnPlayerDisconnected =>
                "protected override void OnPlayerDisconnected(PlayerController playerController)\n        {\n        }",
            OdGameplayMachineFunction.OnProvisionPartitions =>
                "protected override void OnProvisionPartitions(Position center, float range)\n        {\n        }",
            OdGameplayMachineFunction.OnQueryPartitions =>
                "protected override void OnQueryPartitions(Position center, float range, ISet<PartitionID> results)\n        {\n        }",
            OdGameplayMachineFunction.TryResolvePartition =>
                "protected override bool TryResolvePartition(Position position, out PartitionID partition)\n        {\n            partition = default;\n            return false;\n        }",
            _ => throw new ArgumentOutOfRangeException(nameof(descriptor)),
        };

        return $$"""
            #nullable enable

            using System.Collections.Generic;
            using GMCore;
            using {{OdNetworkPackage.PlayerControllerNamespace(project.Runtime.NetworkMode)}};

            namespace {{rootNamespace}}
            {
                public partial class {{projectName}}GameplayMachine
                {
                    {{body}}
                }
            }
            """;
    }
}
