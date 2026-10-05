using System;
using System.Collections.Generic;
using System.Linq;
using ODCore;
using PlayerController = GMCore.StateSync.PlayerController;
using LockstepPlayerController = GMCore.Lockstep.PlayerController;

namespace GMCore
{
    public struct PossessParam : IReplicatedInterface, IODEvent, IGameplayInterface
    {
        public PlayerController PlayerController;
        public Pawn Pawn;

        public static readonly ODEventName EventName = new ODEventName
        {
            NameObject = "GMCore.Possess",
        };

        public GameplayRpcMode RpcMode { get { return GameplayRpcMode.None; } }
        public ODEventName GetODEventName() { return EventName; }

        public EventError CanExecute(GameplayMachine.GameplayMachineProxy machine)
        {
            EventError validation = BuiltInGameplayInterfaces.ValidateControllerAndPawn(PlayerController, Pawn);
            if (!validation)
                return validation;
            GameplayRpcContext? context = machine.CurrentRpcContext;
            if (context.HasValue &&
                (!context.Value.PlayerController.HasValue ||
                 context.Value.PlayerController.Value != PlayerController))
                return "A client can only possess a Pawn with its own PlayerController";
            return true;
        }

        public void DoExecute(GameplayMachine.GameplayMachineProxy machine)
        {
            Pawn? previousPawn = PlayerController.Pawn;
            if (previousPawn.HasValue && previousPawn.Value == Pawn)
                return;

            if (previousPawn.HasValue)
            {
                Pawn value = previousPawn.Value;
                value.PlayerController = null;
                machine.ClearNetworkOwner(value);
            }

            PlayerController? previousController = Pawn.PlayerController;
            if (previousController.HasValue && previousController.Value != PlayerController)
            {
                PlayerController value = previousController.Value;
                value.Pawn = null;
            }

            PlayerController.Pawn = Pawn;
            Pawn.PlayerController = PlayerController;
            machine.SetNetworkOwner(Pawn, PlayerController);
        }
    }

    public struct UnPossessParam : IReplicatedInterface, IODEvent, IGameplayInterface
    {
        public PlayerController PlayerController;

        public static readonly ODEventName EventName = new ODEventName
        {
            NameObject = "GMCore.UnPossess",
        };

        public GameplayRpcMode RpcMode { get { return GameplayRpcMode.None; } }
        public ODEventName GetODEventName() { return EventName; }

        public EventError CanExecute(GameplayMachine.GameplayMachineProxy machine)
        {
            EventError validation = BuiltInGameplayInterfaces.ValidatePlayerController(PlayerController);
            if (!validation)
                return validation;
            GameplayRpcContext? context = machine.CurrentRpcContext;
            if (context.HasValue &&
                (!context.Value.PlayerController.HasValue ||
                 context.Value.PlayerController.Value != PlayerController))
                return "A client can only unpossess its own PlayerController";
            return true;
        }

        public void DoExecute(GameplayMachine.GameplayMachineProxy machine)
        {
            BuiltInGameplayInterfaces.UnPossess(machine, PlayerController);
        }
    }

    public struct PushPlayerTransformParam : IReplicatedInterface, IODEvent, IPlayerInputInterface
    {
        public Transform Transform;

        public static readonly ODEventName EventName = new ODEventName
        {
            NameObject = "GMCore.PushPlayerTransform",
        };

        public GameplayRpcMode RpcMode { get { return GameplayRpcMode.Authority; } }
        public ODEventName GetODEventName() { return EventName; }

        public EventError CanExecute(GameplayMachine.GameplayMachineProxy machine)
        {
            if (!machine.TryGetExecutingPlayerController(out PlayerController playerController))
                return "PushPlayerTransform requires a PlayerController execution context";
            EventError validation = BuiltInGameplayInterfaces.ValidatePossessedPawn(machine, playerController,
                out _);
            if (!validation)
                return validation;
            return BuiltInGameplayInterfaces.ValidatePlayerTransform(Transform);
        }

        public void DoExecute(GameplayMachine.GameplayMachineProxy machine)
        {
            PlayerController playerController = machine.GetExecutingPlayerController();
            BuiltInGameplayInterfaces.ValidatePossessedPawn(machine, playerController, out Pawn pawn);
            Transform transform = Transform;
            transform.Scale = Float3.One;
            pawn.Transform = transform;
        }
    }

    public struct PushViewRangeParam : IReplicatedInterface, IODEvent, IPlayerInputInterface
    {
        public float Range;

        public static readonly ODEventName EventName = new ODEventName
        {
            NameObject = "GMCore.PushViewRange",
        };

        public GameplayRpcMode RpcMode { get { return GameplayRpcMode.Authority; } }
        public ODEventName GetODEventName() { return EventName; }

        public EventError CanExecute(GameplayMachine.GameplayMachineProxy machine)
        {
            if (!machine.TryGetExecutingPlayerController(out PlayerController playerController))
                return "PushViewRange requires a PlayerController execution context";
            EventError validation = BuiltInGameplayInterfaces.ValidatePlayerController(playerController);
            if (!validation)
                return validation;
            if (!float.IsFinite(Range) || Range <= 0)
                return "View range must be finite and greater than zero";
            return true;
        }

        public void DoExecute(GameplayMachine.GameplayMachineProxy machine)
        {
            PlayerController playerController = machine.GetExecutingPlayerController();
            playerController.ViewRange = Range;
        }
    }

    internal static class BuiltInGameplayInterfaces
    {
        public static EventError ValidatePlayerController(PlayerController playerController)
        {
            if (playerController.Machine == null || !playerController.ObjectID.IsValid ||
                !playerController.Machine.ContainsGameplayObject(playerController.ObjectID))
                return "PlayerController does not exist";
            if (!playerController.ObjectClass.Equals(PlayerController.ClassName))
                return "GameplayObject is not a PlayerController";
            return true;
        }

        public static EventError ValidateControllerAndPawn(PlayerController playerController, Pawn pawn)
        {
            EventError controllerValidation = ValidatePlayerController(playerController);
            if (!controllerValidation)
                return controllerValidation;
            if (pawn.Machine == null || !ReferenceEquals(playerController.Machine, pawn.Machine) ||
                !pawn.ObjectID.IsValid || !pawn.Machine.ContainsGameplayObject(pawn.ObjectID))
                return "Pawn does not exist in the PlayerController's GameplayMachine";
            if (!GameplayMachineBase.CanCastTo(pawn.ObjectClass, Pawn.ClassName))
                return "GameplayObject is not a Pawn";
            return true;
        }

        public static EventError ValidatePossessedPawn(GameplayMachine.GameplayMachineProxy machine,
            PlayerController playerController, out Pawn pawn)
        {
            pawn = default;
            EventError controllerValidation = ValidatePlayerController(playerController);
            if (!controllerValidation)
                return controllerValidation;
            Pawn? possessedPawn = playerController.Pawn;
            if (!possessedPawn.HasValue)
                return "PlayerController does not possess a Pawn";
            pawn = possessedPawn.Value;
            EventError pawnValidation = ValidateControllerAndPawn(playerController, pawn);
            if (!pawnValidation)
                return pawnValidation;
            PlayerController? owner = machine.GetNetworkOwner(pawn);
            if (!owner.HasValue || owner.Value != playerController)
                return "Possessed Pawn is not owned by the PlayerController";
            return true;
        }

        public static EventError ValidatePlayerTransform(Transform transform)
        {
            Position position = transform.Position;
            Rotation rotation = transform.Rotation;
            if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z) ||
                !float.IsFinite(rotation.X) || !float.IsFinite(rotation.Y) ||
                !float.IsFinite(rotation.Z) || !float.IsFinite(rotation.W))
                return "Player transform must contain finite position and rotation values";
            float rotationLength = rotation.X * rotation.X + rotation.Y * rotation.Y +
                                   rotation.Z * rotation.Z + rotation.W * rotation.W;
            if (rotationLength <= .98f || rotationLength >= 1.02f)
                return "Player rotation must be normalized";
            return true;
        }

        public static void UnPossess(GameplayMachine.GameplayMachineProxy machine,
            PlayerController playerController)
        {
            Pawn? pawn = playerController.Pawn;
            if (!pawn.HasValue)
                return;
            playerController.Pawn = null;
            if (pawn.Value.PlayerController == playerController)
            {
                Pawn value = pawn.Value;
                value.PlayerController = null;
            }
            machine.ClearNetworkOwner(pawn.Value);
        }
    }

    internal sealed class BuiltInGameplayModule : IODModule, IODNetworkModule, IODDebugModule
    {
        public static readonly BuiltInGameplayModule Instance = new BuiltInGameplayModule();
        private const string PlayerControllerClass = "GMCore.StateSync.PlayerController";
        private const string LockstepPlayerControllerClass = "GMCore.Lockstep.PlayerController";
        private const string ActorClass = "GMCore.Actor";
        private const string PawnClass = "GMCore.Pawn";
        private const string PlayerControllerPawnField = "GMCore.StateSync.PlayerController.Pawn";
        private const string PlayerControllerViewRangeField = "GMCore.StateSync.PlayerController.ViewRange";
        private const string LockstepPlayerControllerSlotIDField = "GMCore.Lockstep.PlayerController.SlotID";
        private const string ActorTransformField = "GMCore.Actor.Transform";
        private const string PawnPlayerControllerField = "GMCore.Pawn.PlayerController";

        public ODModuleName Name { get; } = new ODModuleName { Name = "GMCore.BuiltIn" };

        public ulong NetworkSchemaID
        {
            get
            {
                return ODSaveHash.Compute(
                    "GMCore.BuiltIn.Network.11|Replication:None,OwnerOnly,AllClients|StateSync.PlayerController:OwnerOnly|Lockstep.PlayerController:None|Actor:AllClients|Pawn:AllClients|StateSync.PlayerController.Pawn:OwnerOnly|StateSync.PlayerController.ViewRange:OwnerOnly|Lockstep.PlayerController.SlotID:None|Actor.Transform:AllClients|Pawn.PlayerController:OwnerOnly|PushPlayerTransform:PlayerInput:Authority|PushViewRange:PlayerInput:Authority");
            }
        }

        public ulong DebugSchemaID
        {
            get { return ODSaveHash.Compute("GMCore.BuiltIn.Debug.8"); }
        }

        public IEnumerable<ODClassMeta> GetAllODClassMetas()
        {
            yield return new ODClassMeta
            {
                FromModule = Name,
                Name = Actor.ClassName,
                ODType = typeof(Actor),
                ReplicationMode = GameplayObjectReplicationMode.AllClients,
                RelevancyMode = GameplayObjectRelevancyMode.Always,
                PartitionMode = GameplayObjectPartitionMode.Manual,
                DirectBaseClasses = new List<ODClassName>(),
                Fields = new List<ODFieldMeta>
                {
                    new ODFieldMeta
                    {
                        FromClass = Actor.ClassName,
                        Name = Actor.Fields.Transform,
                        DefaultObject = Transform.Identity,
                        FieldType = typeof(Transform),
                        ReplicationMode = ODFieldReplicationMode.AllClients,
                        DrivenBys = new List<ODFieldName>(),
                        DrivingOfs = new List<ODFieldName>(),
                    },
                },
            };
            yield return new ODClassMeta
            {
                FromModule = Name,
                Name = PlayerController.ClassName,
                ODType = typeof(PlayerController),
                ReplicationMode = GameplayObjectReplicationMode.OwnerOnly,
                RelevancyMode = GameplayObjectRelevancyMode.Always,
                PartitionMode = GameplayObjectPartitionMode.Persistent,
                IsEngineManaged = true,
                DirectBaseClasses = new List<ODClassName>(),
                Fields = new List<ODFieldMeta>
                {
                    new ODFieldMeta
                    {
                        FromClass = PlayerController.ClassName,
                        Name = PlayerController.Fields.Pawn,
                        DefaultObject = null,
                        FieldType = typeof(Pawn?),
                        ReplicationMode = ODFieldReplicationMode.OwnerOnly,
                        DrivenBys = new List<ODFieldName>(),
                        DrivingOfs = new List<ODFieldName>(),
                    },
                    new ODFieldMeta
                    {
                        FromClass = PlayerController.ClassName,
                        Name = PlayerController.Fields.ViewRange,
                        DefaultObject = 64f,
                        FieldType = typeof(float),
                        ReplicationMode = ODFieldReplicationMode.OwnerOnly,
                        DrivenBys = new List<ODFieldName>(),
                        DrivingOfs = new List<ODFieldName>(),
                    },
                },
            };
            yield return new ODClassMeta
            {
                FromModule = Name,
                Name = LockstepPlayerController.ClassName,
                ODType = typeof(LockstepPlayerController),
                ReplicationMode = GameplayObjectReplicationMode.None,
                RelevancyMode = GameplayObjectRelevancyMode.Always,
                PartitionMode = GameplayObjectPartitionMode.Persistent,
                IsEngineManaged = true,
                DirectBaseClasses = new List<ODClassName>(),
                Fields = new List<ODFieldMeta>
                {
                    new ODFieldMeta
                    {
                        FromClass = LockstepPlayerController.ClassName,
                        Name = LockstepPlayerController.Fields.SlotID,
                        DefaultObject = -1,
                        FieldType = typeof(int),
                        ReplicationMode = ODFieldReplicationMode.None,
                        DrivenBys = new List<ODFieldName>(),
                        DrivingOfs = new List<ODFieldName>(),
                    },
                },
            };
            yield return new ODClassMeta
            {
                FromModule = Name,
                Name = Pawn.ClassName,
                ODType = typeof(Pawn),
                ReplicationMode = GameplayObjectReplicationMode.AllClients,
                RelevancyMode = GameplayObjectRelevancyMode.Always,
                PartitionMode = GameplayObjectPartitionMode.Manual,
                DirectBaseClasses = new List<ODClassName> { Actor.ClassName },
                Fields = new List<ODFieldMeta>
                {
                    new ODFieldMeta
                    {
                        FromClass = Pawn.ClassName,
                        Name = Pawn.Fields.PlayerController,
                        DefaultObject = null,
                        FieldType = typeof(PlayerController?),
                        ReplicationMode = ODFieldReplicationMode.OwnerOnly,
                        DrivenBys = new List<ODFieldName>(),
                        DrivingOfs = new List<ODFieldName>(),
                    },
                },
            };
        }

        public IEnumerable<ODStructMeta> GetAllODStructMetas()
        {
            yield return StructMeta("GMCore.Float3", typeof(Float3));
            yield return StructMeta("GMCore.Rotation", typeof(Rotation));
            yield return StructMeta("GMCore.Position", typeof(Position));
            yield return StructMeta("GMCore.Transform", typeof(Transform));
        }

        public IEnumerable<ODEventMeta> GetAllODEventMetas()
        {
            yield return new ODEventMeta { FromModule = Name, Name = PossessParam.EventName, ODType = typeof(PossessParam) };
            yield return new ODEventMeta { FromModule = Name, Name = UnPossessParam.EventName, ODType = typeof(UnPossessParam) };
            yield return new ODEventMeta { FromModule = Name, Name = PushPlayerTransformParam.EventName, ODType = typeof(PushPlayerTransformParam) };
            yield return new ODEventMeta { FromModule = Name, Name = PushViewRangeParam.EventName, ODType = typeof(PushViewRangeParam) };
        }

        public IEnumerable<ODClassSaveMeta> GetAllODClassSaveMetas()
        {
            yield return ClassSaveMeta(ActorClass, Actor.ClassName);
            yield return ClassSaveMeta(PlayerControllerClass, PlayerController.ClassName);
            yield return ClassSaveMeta(LockstepPlayerControllerClass, LockstepPlayerController.ClassName);
            yield return ClassSaveMeta(PawnClass, Pawn.ClassName);
        }

        public IEnumerable<ODFieldSaveMeta> GetAllODFieldSaveMetas()
        {
            yield return ReferenceFieldSaveMeta(PlayerControllerPawnField, PlayerController.Fields.Pawn,
                reader => ReadPawn(reader));
            yield return ScalarFieldSaveMeta(PlayerControllerViewRangeField, PlayerController.Fields.ViewRange,
                (writer, value) => writer.WriteSingle((float)value), reader => reader.ReadSingle());
            yield return ScalarFieldSaveMeta(LockstepPlayerControllerSlotIDField, LockstepPlayerController.Fields.SlotID,
                (writer, value) => writer.WriteInt32((int)value), reader => reader.ReadInt32());
            ODFieldSaveMeta transform = ScalarFieldSaveMeta(ActorTransformField, Actor.Fields.Transform,
                (writer, value) => WriteTransform(writer, (Transform)value), reader => ReadTransform(reader));
            transform.PatchKind = ODFieldPatchKind.Transform;
            yield return transform;
            yield return ReferenceFieldSaveMeta(PawnPlayerControllerField, Pawn.Fields.PlayerController,
                reader => ReadPlayerController(reader));
        }

        public IEnumerable<ODRpcMeta> GetAllODRpcMetas()
        {
            yield return new ODRpcMeta
            {
                StableID = ODSaveHash.Compute("GMCore.PushPlayerTransform"),
                StableName = "GMCore.PushPlayerTransform",
                Mode = GameplayRpcMode.Authority,
                InterfaceType = GameplayInterfaceType.PlayerInput,
                ParamType = typeof(PushPlayerTransformParam),
                ParamWriter = (writer, value) => WriteTransform(writer, ((PushPlayerTransformParam)value).Transform),
                ParamReader = reader => new PushPlayerTransformParam { Transform = ReadTransform(reader) },
            };
            yield return new ODRpcMeta
            {
                StableID = ODSaveHash.Compute("GMCore.PushViewRange"),
                StableName = "GMCore.PushViewRange",
                Mode = GameplayRpcMode.Authority,
                InterfaceType = GameplayInterfaceType.PlayerInput,
                ParamType = typeof(PushViewRangeParam),
                ParamWriter = (writer, value) => writer.WriteSingle(((PushViewRangeParam)value).Range),
                ParamReader = reader => new PushViewRangeParam { Range = reader.ReadSingle() },
            };
        }

        public IEnumerable<ODDebugClassMeta> GetAllODDebugClassMetas()
        {
            ulong playerControllerID = ODSaveHash.Compute(PlayerControllerClass);
            ulong lockstepPlayerControllerID = ODSaveHash.Compute(LockstepPlayerControllerClass);
            ulong actorID = ODSaveHash.Compute(ActorClass);
            ulong pawnID = ODSaveHash.Compute(PawnClass);
            yield return new ODDebugClassMeta
            {
                StableID = actorID,
                StableName = ActorClass,
                DisplayName = "Actor",
                ClassName = Actor.ClassName,
                AssignableClassIDs = new List<ulong> { actorID, pawnID },
                Fields = new List<ODDebugFieldMeta> { DebugTransformField() },
            };
            yield return new ODDebugClassMeta
            {
                StableID = playerControllerID,
                StableName = PlayerControllerClass,
                DisplayName = "PlayerController",
                ClassName = PlayerController.ClassName,
                IsEngineManaged = true,
                AssignableClassIDs = new List<ulong> { playerControllerID },
                Fields = new List<ODDebugFieldMeta>
                {
                    new ODDebugFieldMeta
                    {
                        StableID = ODSaveHash.Compute(PlayerControllerPawnField),
                        StableName = PlayerControllerPawnField,
                        DisplayName = "Pawn",
                        FieldName = PlayerController.Fields.Pawn,
                        ValueType = typeof(Pawn),
                        Container = GameplayDebugContainerKind.Single,
                        ReplicationMode = ODFieldReplicationMode.OwnerOnly,
                        IsGameplayObjectReference = true,
                        GameplayObjectClassID = pawnID,
                        Read = (machine, objectID) => machine.GetGameplayObject<PlayerController>(objectID).Pawn,
                        Write = (machine, objectID, value) =>
                        {
                            PlayerController target = machine.GetGameplayObject<PlayerController>(objectID);
                            target.Pawn = (Pawn?)value;
                        },
                    },
                    new ODDebugFieldMeta
                    {
                        StableID = ODSaveHash.Compute(PlayerControllerViewRangeField),
                        StableName = PlayerControllerViewRangeField,
                        DisplayName = "ViewRange",
                        FieldName = PlayerController.Fields.ViewRange,
                        ValueType = typeof(float),
                        Container = GameplayDebugContainerKind.Single,
                        ReplicationMode = ODFieldReplicationMode.OwnerOnly,
                        Read = (machine, objectID) => machine.GetGameplayObject<PlayerController>(objectID).ViewRange,
                        Write = (machine, objectID, value) =>
                        {
                            PlayerController target = machine.GetGameplayObject<PlayerController>(objectID);
                            target.ViewRange = (float)value;
                        },
                    },
                },
            };
            yield return new ODDebugClassMeta
            {
                StableID = lockstepPlayerControllerID,
                StableName = LockstepPlayerControllerClass,
                DisplayName = "PlayerController",
                ClassName = LockstepPlayerController.ClassName,
                IsEngineManaged = true,
                AssignableClassIDs = new List<ulong> { lockstepPlayerControllerID },
                Fields = new List<ODDebugFieldMeta>
                {
                    new ODDebugFieldMeta
                    {
                        StableID = ODSaveHash.Compute(LockstepPlayerControllerSlotIDField),
                        StableName = LockstepPlayerControllerSlotIDField,
                        DisplayName = "SlotID",
                        FieldName = LockstepPlayerController.Fields.SlotID,
                        ValueType = typeof(int),
                        Container = GameplayDebugContainerKind.Single,
                        ReplicationMode = ODFieldReplicationMode.None,
                        Read = (machine, objectID) =>
                            machine.GetGameplayObject<LockstepPlayerController>(objectID).SlotID,
                    },
                },
            };
            yield return new ODDebugClassMeta
            {
                StableID = pawnID,
                StableName = PawnClass,
                DisplayName = "Pawn",
                ClassName = Pawn.ClassName,
                AssignableClassIDs = new List<ulong> { pawnID },
                Fields = new List<ODDebugFieldMeta>
                {
                    DebugTransformField(),
                    new ODDebugFieldMeta
                    {
                        StableID = ODSaveHash.Compute(PawnPlayerControllerField),
                        StableName = PawnPlayerControllerField,
                        DisplayName = "PlayerController",
                        FieldName = Pawn.Fields.PlayerController,
                        ValueType = typeof(PlayerController),
                        Container = GameplayDebugContainerKind.Single,
                        ReplicationMode = ODFieldReplicationMode.OwnerOnly,
                        IsGameplayObjectReference = true,
                        GameplayObjectClassID = playerControllerID,
                        Read = (machine, objectID) => machine.GetGameplayObject<Pawn>(objectID).PlayerController,
                        Write = (machine, objectID, value) =>
                        {
                            Pawn target = machine.GetGameplayObject<Pawn>(objectID);
                            target.PlayerController = (PlayerController?)value;
                        },
                    },
                },
            };
        }

        private static ODDebugFieldMeta DebugTransformField()
        {
            return new ODDebugFieldMeta
            {
                StableID = ODSaveHash.Compute(ActorTransformField),
                StableName = ActorTransformField,
                DisplayName = "Transform",
                FieldName = Actor.Fields.Transform,
                ValueType = typeof(Transform),
                Container = GameplayDebugContainerKind.Single,
                ReplicationMode = ODFieldReplicationMode.AllClients,
                Read = (machine, objectID) => machine.GetGameplayObjectValue<Transform>(objectID, Actor.Fields.Transform),
                Write = (machine, objectID, value) =>
                    machine.SetGameplayObjectValue(objectID, Actor.Fields.Transform, (Transform)value),
            };
        }

        public IEnumerable<ODDebugInterfaceMeta> GetAllODDebugInterfaceMetas()
        {
            ulong playerControllerID = ODSaveHash.Compute(PlayerControllerClass);
            ulong pawnID = ODSaveHash.Compute(PawnClass);
            yield return new ODDebugInterfaceMeta
            {
                StableID = ODSaveHash.Compute("GMCore.Possess"),
                StableName = "GMCore.Possess",
                DisplayName = "Possess",
                RpcMode = GameplayRpcMode.None,
                ParamType = typeof(PossessParam),
                Inputs = new List<ODDebugInterfaceFieldMeta>
                {
                    DebugReferenceInput("PlayerController", typeof(PlayerController), playerControllerID),
                    DebugReferenceInput("Pawn", typeof(Pawn), pawnID),
                },
                Outputs = new List<ODDebugInterfaceFieldMeta>(),
                Invoke = (machine, inputs) =>
                {
                    PossessParam parameter = new PossessParam
                    {
                        PlayerController = GameplayDebugValueConverter.ConvertTo<PlayerController>(inputs["PlayerController"], machine),
                        Pawn = GameplayDebugValueConverter.ConvertTo<Pawn>(inputs["Pawn"], machine),
                    };
                    CheckableInterfaceResult result = machine.Execute((IReplicatedInterface)parameter);
                    return result.Sussceeded
                        ? ODDebugInvocationResult.Success()
                        : ODDebugInvocationResult.Failure(result.ErrorMessage.ToString());
                },
            };
            yield return new ODDebugInterfaceMeta
            {
                StableID = ODSaveHash.Compute("GMCore.UnPossess"),
                StableName = "GMCore.UnPossess",
                DisplayName = "UnPossess",
                RpcMode = GameplayRpcMode.None,
                ParamType = typeof(UnPossessParam),
                Inputs = new List<ODDebugInterfaceFieldMeta>
                {
                    DebugReferenceInput("PlayerController", typeof(PlayerController), playerControllerID),
                },
                Outputs = new List<ODDebugInterfaceFieldMeta>(),
                Invoke = (machine, inputs) =>
                {
                    UnPossessParam parameter = new UnPossessParam
                    {
                        PlayerController = GameplayDebugValueConverter.ConvertTo<PlayerController>(inputs["PlayerController"], machine),
                    };
                    CheckableInterfaceResult result = machine.Execute((IReplicatedInterface)parameter);
                    return result.Sussceeded
                        ? ODDebugInvocationResult.Success()
                        : ODDebugInvocationResult.Failure(result.ErrorMessage.ToString());
                },
            };
            yield return new ODDebugInterfaceMeta
            {
                StableID = ODSaveHash.Compute("GMCore.PushPlayerTransform"),
                StableName = "GMCore.PushPlayerTransform",
                DisplayName = "PushPlayerTransform",
                RpcMode = GameplayRpcMode.Authority,
                InterfaceType = GameplayInterfaceType.PlayerInput,
                ParamType = typeof(PushPlayerTransformParam),
                Inputs = new List<ODDebugInterfaceFieldMeta>
                {
                    DebugInput("Transform", typeof(Transform)),
                },
                Outputs = new List<ODDebugInterfaceFieldMeta>(),
                Invoke = (machine, inputs) => DebugResult(machine.Execute(new PushPlayerTransformParam
                {
                    Transform = GameplayDebugValueConverter.ConvertTo<Transform>(inputs["Transform"], machine),
                })),
            };
            yield return new ODDebugInterfaceMeta
            {
                StableID = ODSaveHash.Compute("GMCore.PushViewRange"),
                StableName = "GMCore.PushViewRange",
                DisplayName = "PushViewRange",
                RpcMode = GameplayRpcMode.Authority,
                InterfaceType = GameplayInterfaceType.PlayerInput,
                ParamType = typeof(PushViewRangeParam),
                Inputs = new List<ODDebugInterfaceFieldMeta>
                {
                    DebugInput("Range", typeof(float)),
                },
                Outputs = new List<ODDebugInterfaceFieldMeta>(),
                Invoke = (machine, inputs) => DebugResult(machine.Execute(new PushViewRangeParam
                {
                    Range = GameplayDebugValueConverter.ConvertTo<float>(inputs["Range"], machine),
                })),
            };
        }

        private static ODDebugInvocationResult DebugResult(CheckableInterfaceResult result)
        {
            return result.Sussceeded
                ? ODDebugInvocationResult.Success()
                : ODDebugInvocationResult.Failure(result.ErrorMessage.ToString());
        }

        private static ODDebugInterfaceFieldMeta DebugInput(string name, Type type)
        {
            return new ODDebugInterfaceFieldMeta
            {
                Name = name,
                DisplayName = name,
                ValueType = type,
                Container = GameplayDebugContainerKind.Single,
            };
        }

        private static ODDebugInterfaceFieldMeta DebugReferenceInput(string name, Type type, ulong classID)
        {
            return new ODDebugInterfaceFieldMeta
            {
                Name = name,
                DisplayName = name,
                ValueType = type,
                Container = GameplayDebugContainerKind.Single,
                NotNull = true,
                IsGameplayObjectReference = true,
                GameplayObjectClassID = classID,
            };
        }

        private static ODStructMeta StructMeta(string stableName, Type type)
        {
            return new ODStructMeta
            {
                FromModule = Instance.Name,
                Name = new ODStructName { NameObject = stableName },
                ODType = type,
            };
        }

        private static ODClassSaveMeta ClassSaveMeta(string stableName, ODClassName className)
        {
            return new ODClassSaveMeta
            {
                StableID = ODSaveHash.Compute(stableName),
                StableName = stableName,
                ClassName = className,
            };
        }

        private static ODFieldSaveMeta ReferenceFieldSaveMeta(
            string stableName,
            ODFieldName fieldName,
            ODSaveValueReader reader)
        {
            return new ODFieldSaveMeta
            {
                StableID = ODSaveHash.Compute(stableName),
                StableName = stableName,
                FieldName = fieldName,
                Writer = WriteOptionalReference,
                Reader = reader,
            };
        }

        private static ODFieldSaveMeta ScalarFieldSaveMeta(
            string stableName,
            ODFieldName fieldName,
            ODSaveValueWriter writer,
            ODSaveValueReader reader)
        {
            return new ODFieldSaveMeta
            {
                StableID = ODSaveHash.Compute(stableName),
                StableName = stableName,
                FieldName = fieldName,
                Writer = writer,
                Reader = reader,
            };
        }

        private static void WriteTransform(GameplaySaveWriter writer, Transform value)
        {
            writer.WriteString(value.Position.Space.Value);
            writer.WriteSingle(value.Position.X);
            writer.WriteSingle(value.Position.Y);
            writer.WriteSingle(value.Position.Z);
            writer.WriteSingle(value.Rotation.X);
            writer.WriteSingle(value.Rotation.Y);
            writer.WriteSingle(value.Rotation.Z);
            writer.WriteSingle(value.Rotation.W);
            writer.WriteSingle(value.Scale.X);
            writer.WriteSingle(value.Scale.Y);
            writer.WriteSingle(value.Scale.Z);
        }

        private static Transform ReadTransform(GameplaySaveReader reader)
        {
            var position = new Position(new GameplaySpatialSpaceID(reader.ReadString()),
                reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            var rotation = new Rotation(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            var scale = new Float3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            return new Transform(position, rotation, scale);
        }

        private static void WriteOptionalReference(GameplaySaveWriter writer, object value)
        {
            IGameplayObjectOperator gameplayObject = value as IGameplayObjectOperator;
            writer.WriteBoolean(gameplayObject != null && gameplayObject.ObjectID.IsValid);
            if (gameplayObject != null && gameplayObject.ObjectID.IsValid)
                writer.WriteObjectID(gameplayObject.ObjectID);
        }

        private static object ReadPawn(GameplaySaveReader reader)
        {
            return reader.ReadBoolean() ? (Pawn?)ReadPawnRequired(reader) : null;
        }

        private static object ReadPlayerController(GameplaySaveReader reader)
        {
            return reader.ReadBoolean() ? (PlayerController?)ReadPlayerControllerRequired(reader) : null;
        }

        private static Pawn ReadPawnRequired(GameplaySaveReader reader)
        {
            return new Pawn { Machine = reader.Machine, ObjectID = reader.ReadObjectID() };
        }

        private static PlayerController ReadPlayerControllerRequired(GameplaySaveReader reader)
        {
            return new PlayerController { Machine = reader.Machine, ObjectID = reader.ReadObjectID() };
        }
    }
}
