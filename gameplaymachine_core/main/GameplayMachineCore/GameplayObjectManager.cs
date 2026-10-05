using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;

namespace GMCore
{
    [System.Serializable]
    public struct GObjectID : System.IEquatable<GObjectID>, System.IComparable<GObjectID>, CommonSerialize.IStringSerializable
    {
        public int ID;

        public bool IsValid
        {
            get
            {
                return ID != 0;
            }
        }

        public bool Equals(GObjectID other)
        {
            return ID.Equals(other.ID);
        }

        public override bool Equals(object obj)
        {
            if (obj is GObjectID other)
            {
                return this.Equals(other);
            }
            return false;
        }

        public override int GetHashCode()
        {
            return ID.GetHashCode();
        }

        public int CompareTo(GObjectID other)
        {
            // Client-local IDs are allocated -1, -2, ...; keep their canonical order aligned
            // with allocation order while authority IDs retain normal ascending order.
            if (ID < 0 && other.ID < 0)
                return other.ID.CompareTo(ID);
            return ID.CompareTo(other.ID);
        }

        public void ConvertFrom(string str)
        {
            ID = int.Parse(str);
        }

        public string ConvertTo()
        {
            return ID.ToString();
        }

        public static GObjectID operator ++(GObjectID id)
        {
            id.ID++;
            return id;
        }
    }

    public interface IGameplayObjectCreateParam
    {
        public void Set(GMCore.IGameplayObjectOperator gameplayObject);
    }

    public interface IGameplayObjectOperator : XLifetimeObject.IReturnableHandle
    {
        public GameplayMachine Machine { get; set; }

        public GObjectID ObjectID { get; set; }

        public bool IsA<T>()
            where T : struct, IODClass;

        public T? Cast<T>()
            where T : struct, GMCore.IGameplayObjectOperator, IODClass;

        public void Delete();

        public ODClassName ObjectClass { get; }
    }

    public struct CommonGameplayObject : IGameplayObjectOperator
    {
        public GameplayMachine Machine { get; set; }

        public GObjectID ObjectID { get; set; }

        public bool IsA<T>()
            where T : struct, IODClass
        {
            return GameplayMachineBase.CanCastTo(ObjectClass, new T().GetODClassName());
        }

        public T? Cast<T>()
            where T : struct, GMCore.IGameplayObjectOperator, IODClass
        {
            return Machine.Cast<T>(this);
        }

        public void Return()
        {
            Delete();
        }

        public void Delete()
        {
            Machine.DeleteGameplayObject(ObjectID);
        }

        public ODClassName ObjectClass
        {
            get
            {
                return Machine.GetGameplayObjectClass(ObjectID);
            }
        }
    }

    namespace StateSync
    {
    public struct PlayerController : IGameplayObjectOperator, IODClass, IEquatable<PlayerController>, IComparable<PlayerController>, IComparable
    {
        public PlayerController(IGameplayObjectOperator gameplayObject)
        {
            if (gameplayObject == null)
                throw new ArgumentNullException(nameof(gameplayObject));
            Machine = gameplayObject.Machine;
            ObjectID = gameplayObject.ObjectID;
        }

        public GameplayMachine Machine { get; set; }

        public GObjectID ObjectID { get; set; }

        public static readonly ODClassName ClassName = new ODClassName
        {
            NameObject = "GMCore.StateSync.PlayerController",
        };

        public Pawn? Pawn
        {
            get
            {
                return GameplayMachine.ResolveGameplayObjectReference(
                    Machine.GetGameplayObjectValue<Pawn?>(ObjectID, Fields.Pawn));
            }
            set { Machine.SetGameplayObjectValue(ObjectID, Fields.Pawn, value); }
        }

        public FieldChangeEventBinder AfterPawnChanged
        {
            get { return new FieldChangeEventBinder { Machine = Machine, ObjectID = ObjectID, FieldName = Fields.Pawn }; }
        }

        public float ViewRange
        {
            get { return Machine.GetGameplayObjectValue<float>(ObjectID, Fields.ViewRange); }
            set { Machine.SetGameplayObjectValue(ObjectID, Fields.ViewRange, value); }
        }

        public FieldChangeEventBinder AfterViewRangeChanged
        {
            get { return new FieldChangeEventBinder { Machine = Machine, ObjectID = ObjectID, FieldName = Fields.ViewRange }; }
        }

        public bool IsA<T>()
            where T : struct, IODClass
        {
            return GameplayMachineBase.CanCastTo(ObjectClass, new T().GetODClassName());
        }

        public T? Cast<T>()
            where T : struct, IGameplayObjectOperator, IODClass
        {
            return Machine.Cast<T>(this);
        }

        public void Return()
        {
            Delete();
        }

        public void Delete()
        {
            Machine.DeleteGameplayObject(ObjectID);
        }

        public ODClassName ObjectClass
        {
            get { return Machine.GetGameplayObjectClass(ObjectID); }
        }

        public ODClassName GetODClassName() { return ClassName; }

        public bool Equals(PlayerController other)
        {
            return ReferenceEquals(Machine, other.Machine) && ObjectID.Equals(other.ObjectID);
        }

        public int CompareTo(PlayerController other) { return ObjectID.CompareTo(other.ObjectID); }
        int IComparable.CompareTo(object other)
        {
            if (other is PlayerController value) return CompareTo(value);
            throw new ArgumentException("Object is not a PlayerController", nameof(other));
        }

        public override bool Equals(object obj) { return obj is PlayerController other && Equals(other); }
        public override int GetHashCode() { return HashCode.Combine(Machine, ObjectID); }
        public static bool operator ==(PlayerController left, PlayerController right) { return left.Equals(right); }
        public static bool operator !=(PlayerController left, PlayerController right) { return !left.Equals(right); }

        public static class Fields
        {
            public static readonly ODFieldName Pawn = new ODFieldName
            {
                NameObject = "GMCore.StateSync.PlayerController.Pawn",
            };
            public static readonly ODFieldName ViewRange = new ODFieldName
            {
                NameObject = "GMCore.StateSync.PlayerController.ViewRange",
            };
        }

        public static implicit operator PlayerController(CommonGameplayObject gameplayObject)
        {
            return new PlayerController(gameplayObject);
        }

        public static implicit operator CommonGameplayObject(PlayerController playerController)
        {
            return new CommonGameplayObject
            {
                Machine = playerController.Machine,
                ObjectID = playerController.ObjectID,
            };
        }
    }
    }

    namespace Lockstep
    {
    public struct PlayerController : IGameplayObjectOperator, IODClass,
        IEquatable<PlayerController>, IComparable<PlayerController>, IComparable
    {
        public PlayerController(IGameplayObjectOperator gameplayObject)
        {
            if (gameplayObject == null)
                throw new ArgumentNullException(nameof(gameplayObject));
            Machine = gameplayObject.Machine;
            ObjectID = gameplayObject.ObjectID;
        }

        public GameplayMachine Machine { get; set; }
        public GObjectID ObjectID { get; set; }

        public static readonly ODClassName ClassName = new ODClassName
        {
            NameObject = "GMCore.Lockstep.PlayerController",
        };

        public int SlotID
        {
            get { return Machine.GetGameplayObjectValue<int>(ObjectID, Fields.SlotID); }
            internal set { Machine.SetGameplayObjectValue(ObjectID, Fields.SlotID, value); }
        }

        public FieldChangeEventBinder AfterSlotIDChanged =>
            new FieldChangeEventBinder { Machine = Machine, ObjectID = ObjectID, FieldName = Fields.SlotID };

        public bool IsA<T>() where T : struct, IODClass =>
            GameplayMachineBase.CanCastTo(ObjectClass, new T().GetODClassName());
        public T? Cast<T>() where T : struct, IGameplayObjectOperator, IODClass => Machine.Cast<T>(this);
        public void Return() { Delete(); }
        public void Delete() { Machine.DeleteGameplayObject(ObjectID); }
        public ODClassName ObjectClass => Machine.GetGameplayObjectClass(ObjectID);
        public ODClassName GetODClassName() => ClassName;
        public bool Equals(PlayerController other) =>
            ReferenceEquals(Machine, other.Machine) && ObjectID.Equals(other.ObjectID);
        public int CompareTo(PlayerController other) => ObjectID.CompareTo(other.ObjectID);
        int IComparable.CompareTo(object other)
        {
            if (other is PlayerController value) return CompareTo(value);
            throw new ArgumentException("Object is not a Lockstep PlayerController", nameof(other));
        }
        public override bool Equals(object obj) => obj is PlayerController other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Machine, ObjectID);
        public static bool operator ==(PlayerController left, PlayerController right) => left.Equals(right);
        public static bool operator !=(PlayerController left, PlayerController right) => !left.Equals(right);

        public static class Fields
        {
            public static readonly ODFieldName SlotID = new ODFieldName
            {
                NameObject = "GMCore.Lockstep.PlayerController.SlotID",
            };
        }

        public static implicit operator PlayerController(CommonGameplayObject gameplayObject) =>
            new PlayerController(gameplayObject);
        public static implicit operator CommonGameplayObject(PlayerController playerController) =>
            new CommonGameplayObject { Machine = playerController.Machine, ObjectID = playerController.ObjectID };
    }
    }

    public struct Actor : IGameplayObjectOperator, IODClass, IEquatable<Actor>
    {
        public static readonly ODClassName ClassName = new ODClassName { NameObject = "GMCore.Actor" };
        public GameplayMachine Machine { get; set; }
        public GObjectID ObjectID { get; set; }

        public Transform Transform
        {
            get { return Machine.GetGameplayObjectValue<Transform>(ObjectID, Fields.Transform); }
            set { Machine.SetGameplayObjectValue(ObjectID, Fields.Transform, value); }
        }

        public FieldChangeEventBinder AfterTransformChanged =>
            new FieldChangeEventBinder { Machine = Machine, ObjectID = ObjectID, FieldName = Fields.Transform };
        public bool IsA<T>() where T : struct, IODClass =>
            GameplayMachineBase.CanCastTo(ObjectClass, new T().GetODClassName());
        public T? Cast<T>() where T : struct, IGameplayObjectOperator, IODClass => Machine.Cast<T>(this);
        public void Return() { Delete(); }
        public void Delete() { Machine.DeleteGameplayObject(ObjectID); }
        public ODClassName ObjectClass => Machine.GetGameplayObjectClass(ObjectID);
        public ODClassName GetODClassName() => ClassName;
        public bool Equals(Actor other) => ReferenceEquals(Machine, other.Machine) && ObjectID.Equals(other.ObjectID);
        public override bool Equals(object obj) => obj is Actor other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Machine, ObjectID);
        public static bool operator ==(Actor left, Actor right) => left.Equals(right);
        public static bool operator !=(Actor left, Actor right) => !left.Equals(right);
        public static implicit operator CommonGameplayObject(Actor actor) =>
            new CommonGameplayObject { Machine = actor.Machine, ObjectID = actor.ObjectID };

        public static class Fields
        {
            public static readonly ODFieldName Transform = new ODFieldName
            {
                NameObject = "GMCore.Actor.Transform",
            };
        }
    }

    public struct Pawn : IGameplayObjectOperator, IODClass, IEquatable<Pawn>
    {
        public static readonly ODClassName ClassName = new ODClassName
        {
            NameObject = "GMCore.Pawn",
        };

        public GameplayMachine Machine { get; set; }
        public GObjectID ObjectID { get; set; }

        public StateSync.PlayerController? PlayerController
        {
            get
            {
                return GameplayMachine.ResolveGameplayObjectReference(
                    Machine.GetGameplayObjectValue<StateSync.PlayerController?>(ObjectID, Fields.PlayerController));
            }
            set { Machine.SetGameplayObjectValue(ObjectID, Fields.PlayerController, value); }
        }

        public Transform Transform
        {
            get { return Machine.GetGameplayObjectValue<Transform>(ObjectID, Actor.Fields.Transform); }
            set { Machine.SetGameplayObjectValue(ObjectID, Actor.Fields.Transform, value); }
        }

        public FieldChangeEventBinder AfterTransformChanged =>
            new FieldChangeEventBinder { Machine = Machine, ObjectID = ObjectID, FieldName = Actor.Fields.Transform };

        public FieldChangeEventBinder AfterPlayerControllerChanged
        {
            get
            {
                return new FieldChangeEventBinder
                {
                    Machine = Machine,
                    ObjectID = ObjectID,
                    FieldName = Fields.PlayerController,
                };
            }
        }

        public bool IsA<T>() where T : struct, IODClass
        {
            return GameplayMachineBase.CanCastTo(ObjectClass, new T().GetODClassName());
        }

        public T? Cast<T>() where T : struct, IGameplayObjectOperator, IODClass
        {
            return Machine.Cast<T>(this);
        }

        public void Return() { Delete(); }
        public void Delete() { Machine.DeleteGameplayObject(ObjectID); }
        public ODClassName ObjectClass { get { return Machine.GetGameplayObjectClass(ObjectID); } }
        public ODClassName GetODClassName() { return ClassName; }

        public bool Equals(Pawn other)
        {
            return ReferenceEquals(Machine, other.Machine) && ObjectID.Equals(other.ObjectID);
        }

        public override bool Equals(object obj) { return obj is Pawn other && Equals(other); }
        public override int GetHashCode() { return HashCode.Combine(Machine, ObjectID); }
        public static bool operator ==(Pawn left, Pawn right) { return left.Equals(right); }
        public static bool operator !=(Pawn left, Pawn right) { return !left.Equals(right); }

        public static implicit operator CommonGameplayObject(Pawn pawn)
        {
            return new CommonGameplayObject { Machine = pawn.Machine, ObjectID = pawn.ObjectID };
        }

        public static implicit operator Actor(Pawn pawn)
        {
            return new Actor { Machine = pawn.Machine, ObjectID = pawn.ObjectID };
        }

        public static class Fields
        {
            public static readonly ODFieldName PlayerController = new ODFieldName
            {
                NameObject = "GMCore.Pawn.PlayerController",
            };
        }
    }

    public interface IGameplayObjectManager
    {
        public bool ContainsGameplayObject(GObjectID id);

        public bool TryGetGameplayObject(GObjectID id, out IGameplayObject gameplayObject);
        
        public IGameplayObject CreateGameplayObject(ODClassName nameOfObject, GObjectID? overrideID = null);
        
        public bool RemoveGameplayObject(GObjectID id);

        public GObjectID GenerateUniqueID();

        public IGameplayObject GetGameplayObject(GObjectID id)
        {
            IGameplayObject result;
            if (!TryGetGameplayObject(id, out result))
            {
                return null;
            }

            return result;
        }
    }

    public interface IGameplayObjectSaveManager
    {
        GObjectID IDAllocator { get; }
        int GameplayObjectCount { get; }
        IEnumerable<IGameplayObject> GetGameplayObjectsForSave();
        int GetStoredFieldCount(GObjectID objectID);
        IEnumerable<KeyValuePair<ODFieldName, object>> GetStoredFieldsForSave(GObjectID objectID);

        void ResetForLoad(GObjectID idAllocator);
        void SetStoredFieldForLoad(GObjectID objectID, ODFieldName fieldName, object value);
    }

    public interface IGameplayObjectReplicationManager : IGameplayObjectSaveManager
    {
        void EnsureIDAllocatorForReplication(GObjectID objectID);
        void ClearStoredFieldsForReplication(GObjectID objectID);
    }

    [System.Serializable]
    public class GameplayObjectManager : IGameplayObjectManager, IGameplayObjectReplicationManager
    {
        public const string MetaName = ".META";

        public GameplayMachine m_machine;

        private GObjectID m_idAllocator;

        private Dictionary<GObjectID, GameplayObject> m_objects = new Dictionary<GObjectID, GameplayObject>();

        public GameplayObjectManager(GameplayMachine machine)
        {
            m_machine = machine;
        }

        public GObjectID IDAllocator { get { return m_idAllocator; } }
        public int GameplayObjectCount { get { return m_objects.Count; } }
        public bool ContainsGameplayObject(GObjectID id)
        {
            return m_objects.ContainsKey(id);
        }

        public IGameplayObject GetGameplayObject(GObjectID id)
        {
            GameplayObject result;
            if (m_objects.TryGetValue(id, out result))
            {
                return result;
            }

            return null;
        }

        public bool TryGetGameplayObject(GObjectID id, out IGameplayObject gameplayObject)
        {
            GameplayObject outObj;
            bool result = m_objects.TryGetValue(id, out outObj);
            gameplayObject = outObj;
            return result;
        }

        public bool RemoveGameplayObject(GObjectID id)
        {
            return m_objects.Remove(id);
        }

        public IGameplayObject CreateGameplayObject(ODClassName nameOfObject, GObjectID? overrideID = null)
        {
            GObjectID newID;
            if (overrideID != null)
            {
                newID = overrideID.Value;
            }
            else
            {
                newID = GenerateUniqueID();
            }

            GameplayObject result = new GameplayObject();
            result.ObjectID = newID;
            result.ObjectClass = nameOfObject;

            m_objects.Add(newID, result);

            return result;
        }

        public IEnumerable<CommonGameplayObject> GetAllGameplayObjects()
        {
            foreach (var item in m_objects.Values)
            {
                CommonGameplayObject result = default;
                result.Machine = m_machine;
                result.ObjectID = item.ObjectID;
                yield return result;
            }
        }

        public IEnumerable<IGameplayObject> GetGameplayObjectsForSave()
        {
            return m_objects.Values;
        }

        public int GetStoredFieldCount(GObjectID objectID)
        {
            GameplayObject gameplayObject;
            if (!m_objects.TryGetValue(objectID, out gameplayObject))
            {
                throw new KeyNotFoundException($"GameplayObject {objectID.ID} does not exist");
            }
            return gameplayObject.StoredFieldCount;
        }

        public IEnumerable<KeyValuePair<ODFieldName, object>> GetStoredFieldsForSave(GObjectID objectID)
        {
            GameplayObject gameplayObject;
            if (!m_objects.TryGetValue(objectID, out gameplayObject))
            {
                throw new KeyNotFoundException($"GameplayObject {objectID.ID} does not exist");
            }
            return gameplayObject.GetStoredFields();
        }

        public void ResetForLoad(GObjectID idAllocator)
        {
            if (idAllocator.ID < 0)
            {
                throw new InvalidDataException("GameplayObject allocator cannot be negative");
            }
            m_idAllocator = idAllocator;
            m_objects.Clear();
        }

        public void SetStoredFieldForLoad(GObjectID objectID, ODFieldName fieldName, object value)
        {
            GameplayObject gameplayObject;
            if (!m_objects.TryGetValue(objectID, out gameplayObject))
            {
                throw new KeyNotFoundException($"GameplayObject {objectID.ID} does not exist");
            }
            gameplayObject.SetStoredField(fieldName.Meta, value);
        }

        public void EnsureIDAllocatorForReplication(GObjectID objectID)
        {
            if (objectID.ID <= 0)
            {
                throw new InvalidDataException(
                    $"Authority replicated invalid GameplayObject ID {objectID.ID}");
            }
            if (objectID.ID > m_idAllocator.ID)
            {
                m_idAllocator = objectID;
            }
        }

        public void ClearStoredFieldsForReplication(GObjectID objectID)
        {
            GameplayObject gameplayObject;
            if (!m_objects.TryGetValue(objectID, out gameplayObject))
            {
                throw new KeyNotFoundException($"GameplayObject {objectID.ID} does not exist");
            }
            gameplayObject.ClearStoredFields();
        }

        public GObjectID GenerateUniqueID()
        {
            if (m_idAllocator.ID == int.MaxValue)
            {
                throw new InvalidOperationException("Authority GameplayObject ID space is exhausted");
            }
            m_idAllocator++;
            return m_idAllocator;
        }

    }
}
