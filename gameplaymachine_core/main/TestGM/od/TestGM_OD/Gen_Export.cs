namespace TestGM_OD
{
	[System.Serializable]
	public partial struct GameAttribute : System.IEquatable<TestGM_OD.GameAttribute>
	{
		public static TestGM_OD.StructNames GetStructName()
		{
			return TestGM_OD.StructNames.TestGM_OD_GameAttribute;
		}
		public ODCore.Math.Integer Value;
		public static TestGM_OD.GameAttribute operator +(TestGM_OD.GameAttribute lhs, TestGM_OD.GameAttribute rhs)
		{
			return lhs.Value + rhs.Value;
		}
		public static TestGM_OD.GameAttribute operator -(TestGM_OD.GameAttribute lhs, TestGM_OD.GameAttribute rhs)
		{
			return lhs.Value - rhs.Value;
		}
		public static TestGM_OD.GameAttribute operator *(TestGM_OD.GameAttribute lhs, TestGM_OD.GameAttribute rhs)
		{
			return lhs.Value * rhs.Value;
		}
		public static TestGM_OD.GameAttribute operator /(TestGM_OD.GameAttribute lhs, TestGM_OD.GameAttribute rhs)
		{
			return lhs.Value / rhs.Value;
		}
		public static System.Boolean operator <(TestGM_OD.GameAttribute lhs, TestGM_OD.GameAttribute rhs)
		{
			return lhs.Value < rhs.Value;
		}
		public static System.Boolean operator >(TestGM_OD.GameAttribute lhs, TestGM_OD.GameAttribute rhs)
		{
			return lhs.Value > rhs.Value;
		}
		public static System.Boolean operator <=(TestGM_OD.GameAttribute lhs, TestGM_OD.GameAttribute rhs)
		{
			return lhs.Value <= rhs.Value;
		}
		public static System.Boolean operator >=(TestGM_OD.GameAttribute lhs, TestGM_OD.GameAttribute rhs)
		{
			return lhs.Value >= rhs.Value;
		}
		public static TestGM_OD.GameAttribute operator %(TestGM_OD.GameAttribute lhs, TestGM_OD.GameAttribute rhs)
		{
			return lhs.Value % rhs.Value;
		}
		public static TestGM_OD.GameAttribute operator ++(TestGM_OD.GameAttribute lhs)
		{
			return ++lhs.Value;
		}
		public static TestGM_OD.GameAttribute operator --(TestGM_OD.GameAttribute lhs)
		{
			return --lhs.Value;
		}
		public static implicit operator TestGM_OD.GameAttribute(System.Int32 val)
		{
			TestGM_OD.GameAttribute result;
			result.Value = val;
			return result;
		}
		public static implicit operator TestGM_OD.GameAttribute(ODCore.Math.Integer val)
		{
			TestGM_OD.GameAttribute result;
			result.Value = val;
			return result;
		}
		public override string ToString()
		{
			return Value.ToString();
		}
		public bool Equals(GameAttribute other)
		{
			return Value.Equals(other.Value);
		}
		public static System.Boolean operator ==(TestGM_OD.GameAttribute lhs, TestGM_OD.GameAttribute rhs)
		{
			return lhs.Equals(rhs);
		}
		public static System.Boolean operator !=(TestGM_OD.GameAttribute lhs, TestGM_OD.GameAttribute rhs)
		{
			return !lhs.Equals(rhs);
		}
		public override bool Equals(object obj)
		{
			if (obj is TestGM_OD.GameAttribute other)
			{
				return this.Equals(other);
			}
			return false;
		}
		public override int GetHashCode()
		{
			return System.HashCode.Combine(Value);
		}
	}
}
namespace TestGM_OD
{
	[System.Serializable]
	public partial struct Locations
	{
		public static TestGM_OD.StructNames GetStructName()
		{
			return TestGM_OD.StructNames.TestGM_OD_Locations;
		}
		public System.Collections.Generic.List<System.Int32> IntArray;
		public System.Collections.Generic.List<System.Int32> Ints;
		public System.Collections.Generic.Dictionary<System.Int32,TestGM_OD.Unit> IDUnits;
	}
}
namespace TestGM_OD
{
	public interface IGame
	{
		public TestGM_OD.Player? Player { get; set; }
		public TestGM_OD.Monster? A { get; set; }
		public TestGM_OD.Monster? B { get; set; }
		public TestCommon.NotSerializableInt? NotSerializableInt { get; set; }
		public GMCore.Collections.List<System.Int32> IntArray { get; set; }
		public GMCore.Collections.Set<TestGM_OD.Unit> Units { get; set; }
		public GMCore.Collections.Set<TestGM_OD.Unit> Units2 { get; set; }
		public GMCore.Collections.Map<System.Int32,TestGM_OD.Unit> IDUnits { get; set; }
	}
}
namespace TestGM_OD
{
	public interface IUnit
	{
		public System.String? Name { get; set; }
		public TestGM_OD.GameAttribute Health { get; set; }
		public TestGM_OD.GameAttribute Attack { get; set; }
		public System.Boolean IsNearDeath { get; }
		public System.String? UnitStatus { get; }
	}
}
namespace TestGM_OD
{
	public interface IMonster
	{
		public TestGM_OD.GameAttribute MonsterSkill { get; set; }
	}
}
namespace TestGM_OD
{
	public interface IPlayer
	{
		public TestGM_OD.GameAttribute PlayerSkill { get; set; }
	}
}
namespace TestGM_OD
{
	[System.Serializable]
	public partial struct Game : GMCore.IGameplayObjectOperator
		, GMCore.IODClass
		, IGame
	{
		public GMCore.GameplayMachine Machine { get; set; }
		public GMCore.GObjectID ObjectID { get; set; }
		public void Delete()
		{
			Machine.DeleteGameplayObject(ObjectID);
		}
		public TestGM_OD.Player? Player
		{
			get { return GMCore.GameplayMachine.ResolveGameplayObjectReference(Machine.GetGameplayObjectValue<TestGM_OD.Player?>(ObjectID, new GMCore.ODFieldName() { NameObject = Game.FieldNames.Player })); }
			set { Machine.SetGameplayObjectValue(ObjectID, new GMCore.ODFieldName() { NameObject = Game.FieldNames.Player }, value); }
		}
		public GMCore.FieldChangeEventBinder AfterPlayerChanged
		{
			get { return new GMCore.FieldChangeEventBinder() { Machine = Machine, ObjectID = ObjectID, FieldName = new GMCore.ODFieldName() { NameObject = Game.FieldNames.Player } }; }
		}
		public TestGM_OD.Monster? A
		{
			get { return GMCore.GameplayMachine.ResolveGameplayObjectReference(Machine.GetGameplayObjectValue<TestGM_OD.Monster?>(ObjectID, new GMCore.ODFieldName() { NameObject = Game.FieldNames.A })); }
			set { Machine.SetGameplayObjectValue(ObjectID, new GMCore.ODFieldName() { NameObject = Game.FieldNames.A }, value); }
		}
		public GMCore.FieldChangeEventBinder AfterAChanged
		{
			get { return new GMCore.FieldChangeEventBinder() { Machine = Machine, ObjectID = ObjectID, FieldName = new GMCore.ODFieldName() { NameObject = Game.FieldNames.A } }; }
		}
		public TestGM_OD.Monster? B
		{
			get { return GMCore.GameplayMachine.ResolveGameplayObjectReference(Machine.GetGameplayObjectValue<TestGM_OD.Monster?>(ObjectID, new GMCore.ODFieldName() { NameObject = Game.FieldNames.B })); }
			set { Machine.SetGameplayObjectValue(ObjectID, new GMCore.ODFieldName() { NameObject = Game.FieldNames.B }, value); }
		}
		public GMCore.FieldChangeEventBinder AfterBChanged
		{
			get { return new GMCore.FieldChangeEventBinder() { Machine = Machine, ObjectID = ObjectID, FieldName = new GMCore.ODFieldName() { NameObject = Game.FieldNames.B } }; }
		}
		public TestCommon.NotSerializableInt? NotSerializableInt
		{
			get { return Machine.GetGameplayObjectValue<TestCommon.NotSerializableInt?>(ObjectID, new GMCore.ODFieldName() { NameObject = Game.FieldNames.NotSerializableInt }); }
			set { Machine.SetGameplayObjectValue(ObjectID, new GMCore.ODFieldName() { NameObject = Game.FieldNames.NotSerializableInt }, value); }
		}
		public GMCore.FieldChangeEventBinder AfterNotSerializableIntChanged
		{
			get { return new GMCore.FieldChangeEventBinder() { Machine = Machine, ObjectID = ObjectID, FieldName = new GMCore.ODFieldName() { NameObject = Game.FieldNames.NotSerializableInt } }; }
		}
		public GMCore.Collections.List<System.Int32> IntArray
		{
			get { return new GMCore.Collections.List<System.Int32>() { Machine = Machine, ObjectID = ObjectID, FieldName = new GMCore.ODFieldName() { NameObject = Game.FieldNames.IntArray } }; }
			set { (new GMCore.Collections.List<System.Int32>() { Machine = Machine, ObjectID = ObjectID, FieldName = new GMCore.ODFieldName() { NameObject = Game.FieldNames.IntArray } }).SetToField(value); }
		}
		public GMCore.FieldChangeEventBinder IntArrayBinder
		{
			get { return new GMCore.FieldChangeEventBinder() { Machine = Machine, ObjectID = ObjectID, FieldName = new GMCore.ODFieldName() { NameObject = Game.FieldNames.IntArray } }; }
		}
		public GMCore.Collections.Set<TestGM_OD.Unit> Units
		{
			get { return new GMCore.Collections.Set<TestGM_OD.Unit>() { Machine = Machine, ObjectID = ObjectID, FieldName = new GMCore.ODFieldName() { NameObject = Game.FieldNames.Units } }; }
			set { (new GMCore.Collections.Set<TestGM_OD.Unit>() { Machine = Machine, ObjectID = ObjectID, FieldName = new GMCore.ODFieldName() { NameObject = Game.FieldNames.Units } }).SetToField(value); }
		}
		public GMCore.CollectionBinder<TestGM_OD.Unit> UnitsBinder
		{
			get { return new GMCore.CollectionBinder<TestGM_OD.Unit>() { Machine = Machine, ObjectID = ObjectID, FieldName = new GMCore.ODFieldName() { NameObject = Game.FieldNames.Units } }; }
		}
		public GMCore.Collections.Set<TestGM_OD.Unit> Units2
		{
			get { return new GMCore.Collections.Set<TestGM_OD.Unit>() { Machine = Machine, ObjectID = ObjectID, FieldName = new GMCore.ODFieldName() { NameObject = Game.FieldNames.Units2 } }; }
			set { (new GMCore.Collections.Set<TestGM_OD.Unit>() { Machine = Machine, ObjectID = ObjectID, FieldName = new GMCore.ODFieldName() { NameObject = Game.FieldNames.Units2 } }).SetToField(value); }
		}
		public GMCore.CollectionBinder<TestGM_OD.Unit> Units2Binder
		{
			get { return new GMCore.CollectionBinder<TestGM_OD.Unit>() { Machine = Machine, ObjectID = ObjectID, FieldName = new GMCore.ODFieldName() { NameObject = Game.FieldNames.Units2 } }; }
		}
		public GMCore.Collections.Map<System.Int32,TestGM_OD.Unit> IDUnits
		{
			get { return new GMCore.Collections.Map<System.Int32,TestGM_OD.Unit>() { Machine = Machine, ObjectID = ObjectID, FieldName = new GMCore.ODFieldName() { NameObject = Game.FieldNames.IDUnits } }; }
			set { (new GMCore.Collections.Map<System.Int32,TestGM_OD.Unit>() { Machine = Machine, ObjectID = ObjectID, FieldName = new GMCore.ODFieldName() { NameObject = Game.FieldNames.IDUnits } }).SetToField(value); }
		}
		public GMCore.CollectionBinder<System.Int32,TestGM_OD.Unit> IDUnitsBinder
		{
			get { return new GMCore.CollectionBinder<System.Int32,TestGM_OD.Unit>() { Machine = Machine, ObjectID = ObjectID, FieldName = new GMCore.ODFieldName() { NameObject = Game.FieldNames.IDUnits } }; }
		}
		public enum FieldNames
		{
			Player,
			A,
			B,
			NotSerializableInt,
			IntArray,
			Units,
			Units2,
			IDUnits,
		}
		public GMCore.ODClassName GetODClassName()
		{
			return new GMCore.ODClassName() { NameObject = TestGM_OD.ClassNames.TestGM_OD_Game };
		}
		public void Return()
		{
			Delete();
		}
		public System.Boolean IsA<T>() where T : struct, GMCore.IODClass
		{
			return GMCore.GameplayMachineBase.CanCastTo(ObjectClass, new T().GetODClassName());
		}
		public T? Cast<T>() where T : struct, GMCore.IGameplayObjectOperator, GMCore.IODClass
		{
			return Machine.Cast<T>(this);
		}
		public GMCore.ODClassName ObjectClass
		{
			get
			{
				return Machine.GetGameplayObjectClass(ObjectID);
			}
		}
		public static GMCore.ODFieldName GetODFieldName(FieldNames fieldName)
		{
			return new GMCore.ODFieldName() { NameObject = fieldName };
		}
		[System.Serializable]
		public class InnerLayout : GMCore.IGameplayObjectCreateParam
		{
			public TestGM_OD.Player? Player;
			public TestGM_OD.Monster? A;
			public TestGM_OD.Monster? B;
			public TestCommon.NotSerializableInt? NotSerializableInt;
			public GMCore.Collections.List<System.Int32> IntArray;
			public GMCore.Collections.Set<TestGM_OD.Unit> Units;
			public GMCore.Collections.Set<TestGM_OD.Unit> Units2;
			public GMCore.Collections.Map<System.Int32,TestGM_OD.Unit> IDUnits;
			public void Set(GMCore.IGameplayObjectOperator gameplayObject)
			{
				GMCore.GameplayMachine machine = gameplayObject.Machine;
				machine.SetGameplayObjectValue(gameplayObject.ObjectID, new GMCore.ODFieldName() { NameObject = Game.FieldNames.Player }, Player);
				machine.SetGameplayObjectValue(gameplayObject.ObjectID, new GMCore.ODFieldName() { NameObject = Game.FieldNames.A }, A);
				machine.SetGameplayObjectValue(gameplayObject.ObjectID, new GMCore.ODFieldName() { NameObject = Game.FieldNames.B }, B);
				machine.SetGameplayObjectValue(gameplayObject.ObjectID, new GMCore.ODFieldName() { NameObject = Game.FieldNames.NotSerializableInt }, NotSerializableInt);
				machine.SetGameplayObjectValue(gameplayObject.ObjectID, new GMCore.ODFieldName() { NameObject = Game.FieldNames.IntArray }, IntArray);
				machine.SetGameplayObjectValue(gameplayObject.ObjectID, new GMCore.ODFieldName() { NameObject = Game.FieldNames.Units }, Units);
				machine.SetGameplayObjectValue(gameplayObject.ObjectID, new GMCore.ODFieldName() { NameObject = Game.FieldNames.Units2 }, Units2);
				machine.SetGameplayObjectValue(gameplayObject.ObjectID, new GMCore.ODFieldName() { NameObject = Game.FieldNames.IDUnits }, IDUnits);
			}
		}
	}
}
namespace TestGM_OD
{
	[System.Serializable]
	public partial struct Unit : GMCore.IGameplayObjectOperator
		, GMCore.IODClass
		, IUnit
	{
		public GMCore.GameplayMachine Machine { get; set; }
		public GMCore.GObjectID ObjectID { get; set; }
		public void Delete()
		{
			Machine.DeleteGameplayObject(ObjectID);
		}
		public System.String? Name
		{
			get { return Machine.GetGameplayObjectValue<System.String?>(ObjectID, new GMCore.ODFieldName() { NameObject = Unit.FieldNames.Name }); }
			set { Machine.SetGameplayObjectValue(ObjectID, new GMCore.ODFieldName() { NameObject = Unit.FieldNames.Name }, value, true); }
		}
		public GMCore.FieldChangeEventBinder AfterNameChanged
		{
			get { return new GMCore.FieldChangeEventBinder() { Machine = Machine, ObjectID = ObjectID, FieldName = new GMCore.ODFieldName() { NameObject = Unit.FieldNames.Name } }; }
		}
		public TestGM_OD.GameAttribute Health
		{
			get { return Machine.GetGameplayObjectValue<TestGM_OD.GameAttribute>(ObjectID, new GMCore.ODFieldName() { NameObject = Unit.FieldNames.Health }); }
			set { Machine.SetGameplayObjectValue(ObjectID, new GMCore.ODFieldName() { NameObject = Unit.FieldNames.Health }, value); }
		}
		public GMCore.FieldChangeEventBinder AfterHealthChanged
		{
			get { return new GMCore.FieldChangeEventBinder() { Machine = Machine, ObjectID = ObjectID, FieldName = new GMCore.ODFieldName() { NameObject = Unit.FieldNames.Health } }; }
		}
		public TestGM_OD.GameAttribute Attack
		{
			get { return Machine.GetGameplayObjectValue<TestGM_OD.GameAttribute>(ObjectID, new GMCore.ODFieldName() { NameObject = Unit.FieldNames.Attack }); }
			set { Machine.SetGameplayObjectValue(ObjectID, new GMCore.ODFieldName() { NameObject = Unit.FieldNames.Attack }, value); }
		}
		public GMCore.FieldChangeEventBinder AfterAttackChanged
		{
			get { return new GMCore.FieldChangeEventBinder() { Machine = Machine, ObjectID = ObjectID, FieldName = new GMCore.ODFieldName() { NameObject = Unit.FieldNames.Attack } }; }
		}
		public System.Boolean IsNearDeath
		{
			get { return DrivenValueImplementations.Get_Unit_IsNearDeath(Health); }
		}
		public GMCore.FieldChangeEventBinder AfterIsNearDeathChanged
		{
			get { return new GMCore.FieldChangeEventBinder() { Machine = Machine, ObjectID = ObjectID, FieldName = new GMCore.ODFieldName() { NameObject = Unit.FieldNames.IsNearDeath } }; }
		}
		public System.String? UnitStatus
		{
			get { return DrivenValueImplementations.Get_Unit_UnitStatus(Attack, IsNearDeath); }
		}
		public GMCore.FieldChangeEventBinder AfterUnitStatusChanged
		{
			get { return new GMCore.FieldChangeEventBinder() { Machine = Machine, ObjectID = ObjectID, FieldName = new GMCore.ODFieldName() { NameObject = Unit.FieldNames.UnitStatus } }; }
		}
		public enum FieldNames
		{
			Name,
			Health,
			Attack,
			IsNearDeath,
			UnitStatus,
		}
		public GMCore.ODClassName GetODClassName()
		{
			return new GMCore.ODClassName() { NameObject = TestGM_OD.ClassNames.TestGM_OD_Unit };
		}
		public void Return()
		{
			Delete();
		}
		public System.Boolean IsA<T>() where T : struct, GMCore.IODClass
		{
			return GMCore.GameplayMachineBase.CanCastTo(ObjectClass, new T().GetODClassName());
		}
		public T? Cast<T>() where T : struct, GMCore.IGameplayObjectOperator, GMCore.IODClass
		{
			return Machine.Cast<T>(this);
		}
		public GMCore.ODClassName ObjectClass
		{
			get
			{
				return Machine.GetGameplayObjectClass(ObjectID);
			}
		}
		public static GMCore.ODFieldName GetODFieldName(FieldNames fieldName)
		{
			return new GMCore.ODFieldName() { NameObject = fieldName };
		}
		[System.Serializable]
		public class InnerLayout : GMCore.IGameplayObjectCreateParam
		{
			public System.String? Name;
			public TestGM_OD.GameAttribute Health;
			public TestGM_OD.GameAttribute Attack;
			public System.Boolean IsNearDeath;
			public System.String? UnitStatus;
			public void Set(GMCore.IGameplayObjectOperator gameplayObject)
			{
				GMCore.GameplayMachine machine = gameplayObject.Machine;
				machine.SetGameplayObjectValue(gameplayObject.ObjectID, new GMCore.ODFieldName() { NameObject = Unit.FieldNames.Name }, Name);
				machine.SetGameplayObjectValue(gameplayObject.ObjectID, new GMCore.ODFieldName() { NameObject = Unit.FieldNames.Health }, Health);
				machine.SetGameplayObjectValue(gameplayObject.ObjectID, new GMCore.ODFieldName() { NameObject = Unit.FieldNames.Attack }, Attack);
				machine.SetGameplayObjectValue(gameplayObject.ObjectID, new GMCore.ODFieldName() { NameObject = Unit.FieldNames.IsNearDeath }, IsNearDeath);
				machine.SetGameplayObjectValue(gameplayObject.ObjectID, new GMCore.ODFieldName() { NameObject = Unit.FieldNames.UnitStatus }, UnitStatus);
			}
		}
	}
}
namespace TestGM_OD
{
	[System.Serializable]
	public partial struct Monster : GMCore.IGameplayObjectOperator
		, GMCore.IODClass
		, IMonster
		, IUnit
	{
		public GMCore.GameplayMachine Machine { get; set; }
		public GMCore.GObjectID ObjectID { get; set; }
		public void Delete()
		{
			Machine.DeleteGameplayObject(ObjectID);
		}
		public TestGM_OD.GameAttribute MonsterSkill
		{
			get { return Machine.GetGameplayObjectValue<TestGM_OD.GameAttribute>(ObjectID, new GMCore.ODFieldName() { NameObject = Monster.FieldNames.MonsterSkill }); }
			set { Machine.SetGameplayObjectValue(ObjectID, new GMCore.ODFieldName() { NameObject = Monster.FieldNames.MonsterSkill }, value); }
		}
		public GMCore.FieldChangeEventBinder AfterMonsterSkillChanged
		{
			get { return new GMCore.FieldChangeEventBinder() { Machine = Machine, ObjectID = ObjectID, FieldName = new GMCore.ODFieldName() { NameObject = Monster.FieldNames.MonsterSkill } }; }
		}
		public System.String? Name
		{
			get { return Machine.GetGameplayObjectValue<System.String?>(ObjectID, new GMCore.ODFieldName() { NameObject = Unit.FieldNames.Name }); }
			set { Machine.SetGameplayObjectValue(ObjectID, new GMCore.ODFieldName() { NameObject = Unit.FieldNames.Name }, value, true); }
		}
		public GMCore.FieldChangeEventBinder AfterNameChanged
		{
			get { return new GMCore.FieldChangeEventBinder() { Machine = Machine, ObjectID = ObjectID, FieldName = new GMCore.ODFieldName() { NameObject = Unit.FieldNames.Name } }; }
		}
		public TestGM_OD.GameAttribute Health
		{
			get { return Machine.GetGameplayObjectValue<TestGM_OD.GameAttribute>(ObjectID, new GMCore.ODFieldName() { NameObject = Unit.FieldNames.Health }); }
			set { Machine.SetGameplayObjectValue(ObjectID, new GMCore.ODFieldName() { NameObject = Unit.FieldNames.Health }, value); }
		}
		public GMCore.FieldChangeEventBinder AfterHealthChanged
		{
			get { return new GMCore.FieldChangeEventBinder() { Machine = Machine, ObjectID = ObjectID, FieldName = new GMCore.ODFieldName() { NameObject = Unit.FieldNames.Health } }; }
		}
		public TestGM_OD.GameAttribute Attack
		{
			get { return Machine.GetGameplayObjectValue<TestGM_OD.GameAttribute>(ObjectID, new GMCore.ODFieldName() { NameObject = Unit.FieldNames.Attack }); }
			set { Machine.SetGameplayObjectValue(ObjectID, new GMCore.ODFieldName() { NameObject = Unit.FieldNames.Attack }, value); }
		}
		public GMCore.FieldChangeEventBinder AfterAttackChanged
		{
			get { return new GMCore.FieldChangeEventBinder() { Machine = Machine, ObjectID = ObjectID, FieldName = new GMCore.ODFieldName() { NameObject = Unit.FieldNames.Attack } }; }
		}
		public System.Boolean IsNearDeath
		{
			get { return DrivenValueImplementations.Get_Unit_IsNearDeath(Health); }
		}
		public GMCore.FieldChangeEventBinder AfterIsNearDeathChanged
		{
			get { return new GMCore.FieldChangeEventBinder() { Machine = Machine, ObjectID = ObjectID, FieldName = new GMCore.ODFieldName() { NameObject = Unit.FieldNames.IsNearDeath } }; }
		}
		public System.String? UnitStatus
		{
			get { return DrivenValueImplementations.Get_Unit_UnitStatus(Attack, IsNearDeath); }
		}
		public GMCore.FieldChangeEventBinder AfterUnitStatusChanged
		{
			get { return new GMCore.FieldChangeEventBinder() { Machine = Machine, ObjectID = ObjectID, FieldName = new GMCore.ODFieldName() { NameObject = Unit.FieldNames.UnitStatus } }; }
		}
		public enum FieldNames
		{
			MonsterSkill,
		}
		public GMCore.ODClassName GetODClassName()
		{
			return new GMCore.ODClassName() { NameObject = TestGM_OD.ClassNames.TestGM_OD_Monster };
		}
		public void Return()
		{
			Delete();
		}
		public System.Boolean IsA<T>() where T : struct, GMCore.IODClass
		{
			return GMCore.GameplayMachineBase.CanCastTo(ObjectClass, new T().GetODClassName());
		}
		public T? Cast<T>() where T : struct, GMCore.IGameplayObjectOperator, GMCore.IODClass
		{
			return Machine.Cast<T>(this);
		}
		public GMCore.ODClassName ObjectClass
		{
			get
			{
				return Machine.GetGameplayObjectClass(ObjectID);
			}
		}
		public static implicit operator TestGM_OD.Unit(TestGM_OD.Monster ths)
		{
			TestGM_OD.Unit result = default;
			result.Machine = ths.Machine;
			result.ObjectID = ths.ObjectID;
			return result;
		}
		public static GMCore.ODFieldName GetODFieldName(FieldNames fieldName)
		{
			return new GMCore.ODFieldName() { NameObject = fieldName };
		}
		[System.Serializable]
		public class InnerLayout : GMCore.IGameplayObjectCreateParam
		{
			public TestGM_OD.GameAttribute MonsterSkill;
			public System.String? Name;
			public TestGM_OD.GameAttribute Health;
			public TestGM_OD.GameAttribute Attack;
			public System.Boolean IsNearDeath;
			public System.String? UnitStatus;
			public void Set(GMCore.IGameplayObjectOperator gameplayObject)
			{
				GMCore.GameplayMachine machine = gameplayObject.Machine;
				machine.SetGameplayObjectValue(gameplayObject.ObjectID, new GMCore.ODFieldName() { NameObject = Monster.FieldNames.MonsterSkill }, MonsterSkill);
				machine.SetGameplayObjectValue(gameplayObject.ObjectID, new GMCore.ODFieldName() { NameObject = Unit.FieldNames.Name }, Name);
				machine.SetGameplayObjectValue(gameplayObject.ObjectID, new GMCore.ODFieldName() { NameObject = Unit.FieldNames.Health }, Health);
				machine.SetGameplayObjectValue(gameplayObject.ObjectID, new GMCore.ODFieldName() { NameObject = Unit.FieldNames.Attack }, Attack);
				machine.SetGameplayObjectValue(gameplayObject.ObjectID, new GMCore.ODFieldName() { NameObject = Unit.FieldNames.IsNearDeath }, IsNearDeath);
				machine.SetGameplayObjectValue(gameplayObject.ObjectID, new GMCore.ODFieldName() { NameObject = Unit.FieldNames.UnitStatus }, UnitStatus);
			}
		}
	}
}
namespace TestGM_OD
{
	[System.Serializable]
	public partial struct Player : GMCore.IGameplayObjectOperator
		, GMCore.IODClass
		, IPlayer
		, IUnit
	{
		public GMCore.GameplayMachine Machine { get; set; }
		public GMCore.GObjectID ObjectID { get; set; }
		public void Delete()
		{
			Machine.DeleteGameplayObject(ObjectID);
		}
		public TestGM_OD.GameAttribute PlayerSkill
		{
			get { return Machine.GetGameplayObjectValue<TestGM_OD.GameAttribute>(ObjectID, new GMCore.ODFieldName() { NameObject = Player.FieldNames.PlayerSkill }); }
			set { Machine.SetGameplayObjectValue(ObjectID, new GMCore.ODFieldName() { NameObject = Player.FieldNames.PlayerSkill }, value); }
		}
		public GMCore.FieldChangeEventBinder AfterPlayerSkillChanged
		{
			get { return new GMCore.FieldChangeEventBinder() { Machine = Machine, ObjectID = ObjectID, FieldName = new GMCore.ODFieldName() { NameObject = Player.FieldNames.PlayerSkill } }; }
		}
		public System.String? Name
		{
			get { return Machine.GetGameplayObjectValue<System.String?>(ObjectID, new GMCore.ODFieldName() { NameObject = Unit.FieldNames.Name }); }
			set { Machine.SetGameplayObjectValue(ObjectID, new GMCore.ODFieldName() { NameObject = Unit.FieldNames.Name }, value, true); }
		}
		public GMCore.FieldChangeEventBinder AfterNameChanged
		{
			get { return new GMCore.FieldChangeEventBinder() { Machine = Machine, ObjectID = ObjectID, FieldName = new GMCore.ODFieldName() { NameObject = Unit.FieldNames.Name } }; }
		}
		public TestGM_OD.GameAttribute Health
		{
			get { return Machine.GetGameplayObjectValue<TestGM_OD.GameAttribute>(ObjectID, new GMCore.ODFieldName() { NameObject = Unit.FieldNames.Health }); }
			set { Machine.SetGameplayObjectValue(ObjectID, new GMCore.ODFieldName() { NameObject = Unit.FieldNames.Health }, value); }
		}
		public GMCore.FieldChangeEventBinder AfterHealthChanged
		{
			get { return new GMCore.FieldChangeEventBinder() { Machine = Machine, ObjectID = ObjectID, FieldName = new GMCore.ODFieldName() { NameObject = Unit.FieldNames.Health } }; }
		}
		public TestGM_OD.GameAttribute Attack
		{
			get { return Machine.GetGameplayObjectValue<TestGM_OD.GameAttribute>(ObjectID, new GMCore.ODFieldName() { NameObject = Unit.FieldNames.Attack }); }
			set { Machine.SetGameplayObjectValue(ObjectID, new GMCore.ODFieldName() { NameObject = Unit.FieldNames.Attack }, value); }
		}
		public GMCore.FieldChangeEventBinder AfterAttackChanged
		{
			get { return new GMCore.FieldChangeEventBinder() { Machine = Machine, ObjectID = ObjectID, FieldName = new GMCore.ODFieldName() { NameObject = Unit.FieldNames.Attack } }; }
		}
		public System.Boolean IsNearDeath
		{
			get { return DrivenValueImplementations.Get_Unit_IsNearDeath(Health); }
		}
		public GMCore.FieldChangeEventBinder AfterIsNearDeathChanged
		{
			get { return new GMCore.FieldChangeEventBinder() { Machine = Machine, ObjectID = ObjectID, FieldName = new GMCore.ODFieldName() { NameObject = Unit.FieldNames.IsNearDeath } }; }
		}
		public System.String? UnitStatus
		{
			get { return DrivenValueImplementations.Get_Unit_UnitStatus(Attack, IsNearDeath); }
		}
		public GMCore.FieldChangeEventBinder AfterUnitStatusChanged
		{
			get { return new GMCore.FieldChangeEventBinder() { Machine = Machine, ObjectID = ObjectID, FieldName = new GMCore.ODFieldName() { NameObject = Unit.FieldNames.UnitStatus } }; }
		}
		public enum FieldNames
		{
			PlayerSkill,
		}
		public GMCore.ODClassName GetODClassName()
		{
			return new GMCore.ODClassName() { NameObject = TestGM_OD.ClassNames.TestGM_OD_Player };
		}
		public void Return()
		{
			Delete();
		}
		public System.Boolean IsA<T>() where T : struct, GMCore.IODClass
		{
			return GMCore.GameplayMachineBase.CanCastTo(ObjectClass, new T().GetODClassName());
		}
		public T? Cast<T>() where T : struct, GMCore.IGameplayObjectOperator, GMCore.IODClass
		{
			return Machine.Cast<T>(this);
		}
		public GMCore.ODClassName ObjectClass
		{
			get
			{
				return Machine.GetGameplayObjectClass(ObjectID);
			}
		}
		public static implicit operator TestGM_OD.Unit(TestGM_OD.Player ths)
		{
			TestGM_OD.Unit result = default;
			result.Machine = ths.Machine;
			result.ObjectID = ths.ObjectID;
			return result;
		}
		public static GMCore.ODFieldName GetODFieldName(FieldNames fieldName)
		{
			return new GMCore.ODFieldName() { NameObject = fieldName };
		}
		[System.Serializable]
		public class InnerLayout : GMCore.IGameplayObjectCreateParam
		{
			public TestGM_OD.GameAttribute PlayerSkill;
			public System.String? Name;
			public TestGM_OD.GameAttribute Health;
			public TestGM_OD.GameAttribute Attack;
			public System.Boolean IsNearDeath;
			public System.String? UnitStatus;
			public void Set(GMCore.IGameplayObjectOperator gameplayObject)
			{
				GMCore.GameplayMachine machine = gameplayObject.Machine;
				machine.SetGameplayObjectValue(gameplayObject.ObjectID, new GMCore.ODFieldName() { NameObject = Player.FieldNames.PlayerSkill }, PlayerSkill);
				machine.SetGameplayObjectValue(gameplayObject.ObjectID, new GMCore.ODFieldName() { NameObject = Unit.FieldNames.Name }, Name);
				machine.SetGameplayObjectValue(gameplayObject.ObjectID, new GMCore.ODFieldName() { NameObject = Unit.FieldNames.Health }, Health);
				machine.SetGameplayObjectValue(gameplayObject.ObjectID, new GMCore.ODFieldName() { NameObject = Unit.FieldNames.Attack }, Attack);
				machine.SetGameplayObjectValue(gameplayObject.ObjectID, new GMCore.ODFieldName() { NameObject = Unit.FieldNames.IsNearDeath }, IsNearDeath);
				machine.SetGameplayObjectValue(gameplayObject.ObjectID, new GMCore.ODFieldName() { NameObject = Unit.FieldNames.UnitStatus }, UnitStatus);
			}
		}
	}
}
namespace TestGM_OD
{
	public enum StructNames
	{
		TestGM_OD_GameAttribute,
		TestGM_OD_Locations,
	}
}
namespace TestGM_OD
{
	public enum ClassNames
	{
		TestGM_OD_Game,
		TestGM_OD_Unit,
		TestGM_OD_Monster,
		TestGM_OD_Player,
	}
}
namespace TestGM_OD
{
	public enum EventNames
	{
		TestGM_OD_Routines_DamageRoutineParam,
		TestGM_OD_Interfaces_DamageUnitParam,
		TestGM_OD_Interfaces_TriggerEventParam,
		TestGM_OD_Interfaces_SetUnitHealthsParam,
		TestGM_OD_Interfaces_CreateMonstersParam,
		TestGM_OD_Interfaces_TestUnitsParam,
		TestGM_OD_Interfaces_CreateDeleteParam,
	}
}
namespace TestGM_OD
{
	public sealed class TestGMModule : GMCore.IODModule, GMCore.IODNetworkModule
	{
		public static TestGMModule Instance { get; } = new TestGMModule();
		public GMCore.ODModuleName Name
		{
			get
			{
				return new GMCore.ODModuleName { Name = "TestGM_OD" };
			}
		}
		public System.Collections.Generic.IEnumerable<GMCore.ODClassMeta> GetAllODClassMetas()
		{
			yield return new GMCore.ODClassMeta
			{
				FromModule = new GMCore.ODModuleName { Name = "TestGM_OD" },
				Name = new GMCore.ODClassName() { NameObject = TestGM_OD.ClassNames.TestGM_OD_Game },
				ODType = typeof(TestGM_OD.Game),
				ReplicationMode = GMCore.GameplayObjectReplicationMode.AllClients,
				DirectBaseClasses = new System.Collections.Generic.List<GMCore.ODClassName>()
				{
				},
				Fields = new System.Collections.Generic.List<GMCore.ODFieldMeta>()
				{
					new GMCore.ODFieldMeta()
					{
						FromClass = new GMCore.ODClassName() { NameObject = TestGM_OD.ClassNames.TestGM_OD_Game },
						Name = new GMCore.ODFieldName() { NameObject = Game.FieldNames.Player },
						FieldType = typeof(TestGM_OD.Player),
						DefaultObject = default(TestGM_OD.Player?),
						ReplicationMode = GMCore.ODFieldReplicationMode.AllClients,
						DrivenBys = new System.Collections.Generic.List<GMCore.ODFieldName> {},
						DrivingOfs = new System.Collections.Generic.List<GMCore.ODFieldName> {},
					},
					new GMCore.ODFieldMeta()
					{
						FromClass = new GMCore.ODClassName() { NameObject = TestGM_OD.ClassNames.TestGM_OD_Game },
						Name = new GMCore.ODFieldName() { NameObject = Game.FieldNames.A },
						FieldType = typeof(TestGM_OD.Monster),
						DefaultObject = default(TestGM_OD.Monster?),
						ReplicationMode = GMCore.ODFieldReplicationMode.AllClients,
						DrivenBys = new System.Collections.Generic.List<GMCore.ODFieldName> {},
						DrivingOfs = new System.Collections.Generic.List<GMCore.ODFieldName> {},
					},
					new GMCore.ODFieldMeta()
					{
						FromClass = new GMCore.ODClassName() { NameObject = TestGM_OD.ClassNames.TestGM_OD_Game },
						Name = new GMCore.ODFieldName() { NameObject = Game.FieldNames.B },
						FieldType = typeof(TestGM_OD.Monster),
						DefaultObject = default(TestGM_OD.Monster?),
						ReplicationMode = GMCore.ODFieldReplicationMode.OwnerOnly,
						DrivenBys = new System.Collections.Generic.List<GMCore.ODFieldName> {},
						DrivingOfs = new System.Collections.Generic.List<GMCore.ODFieldName> {},
					},
					new GMCore.ODFieldMeta()
					{
						FromClass = new GMCore.ODClassName() { NameObject = TestGM_OD.ClassNames.TestGM_OD_Game },
						Name = new GMCore.ODFieldName() { NameObject = Game.FieldNames.NotSerializableInt },
						FieldType = typeof(TestCommon.NotSerializableInt),
						DefaultObject = default(TestCommon.NotSerializableInt?),
						ReplicationMode = GMCore.ODFieldReplicationMode.AllClients,
						DrivenBys = new System.Collections.Generic.List<GMCore.ODFieldName> {},
						DrivingOfs = new System.Collections.Generic.List<GMCore.ODFieldName> {},
					},
					new GMCore.ODFieldMeta()
					{
						FromClass = new GMCore.ODClassName() { NameObject = TestGM_OD.ClassNames.TestGM_OD_Game },
						Name = new GMCore.ODFieldName() { NameObject = Game.FieldNames.IntArray },
						FieldType = typeof(GMCore.Collections.List<System.Int32>),
						DefaultObject = default(GMCore.Collections.List<System.Int32>),
						InitialValueFactory = () => new GMCore.Collections.ListCollection<System.Int32>(),
						ReplicationMode = GMCore.ODFieldReplicationMode.AllClients,
						DrivenBys = new System.Collections.Generic.List<GMCore.ODFieldName> {},
						DrivingOfs = new System.Collections.Generic.List<GMCore.ODFieldName> {},
					},
					new GMCore.ODFieldMeta()
					{
						FromClass = new GMCore.ODClassName() { NameObject = TestGM_OD.ClassNames.TestGM_OD_Game },
						Name = new GMCore.ODFieldName() { NameObject = Game.FieldNames.Units },
						FieldType = typeof(GMCore.Collections.Set<TestGM_OD.Unit>),
						DefaultObject = default(GMCore.Collections.Set<TestGM_OD.Unit>),
						InitialValueFactory = () => new GMCore.Collections.SetCollection<TestGM_OD.Unit>(),
						ReplicationMode = GMCore.ODFieldReplicationMode.AllClients,
						DrivenBys = new System.Collections.Generic.List<GMCore.ODFieldName> {},
						DrivingOfs = new System.Collections.Generic.List<GMCore.ODFieldName> {},
					},
					new GMCore.ODFieldMeta()
					{
						FromClass = new GMCore.ODClassName() { NameObject = TestGM_OD.ClassNames.TestGM_OD_Game },
						Name = new GMCore.ODFieldName() { NameObject = Game.FieldNames.Units2 },
						FieldType = typeof(GMCore.Collections.Set<TestGM_OD.Unit>),
						DefaultObject = default(GMCore.Collections.Set<TestGM_OD.Unit>),
						InitialValueFactory = () => new GMCore.Collections.SetCollection<TestGM_OD.Unit>(),
						ReplicationMode = GMCore.ODFieldReplicationMode.AllClients,
						DrivenBys = new System.Collections.Generic.List<GMCore.ODFieldName> {},
						DrivingOfs = new System.Collections.Generic.List<GMCore.ODFieldName> {},
					},
					new GMCore.ODFieldMeta()
					{
						FromClass = new GMCore.ODClassName() { NameObject = TestGM_OD.ClassNames.TestGM_OD_Game },
						Name = new GMCore.ODFieldName() { NameObject = Game.FieldNames.IDUnits },
						FieldType = typeof(GMCore.Collections.Map<System.Int32,TestGM_OD.Unit>),
						DefaultObject = default(GMCore.Collections.Map<System.Int32,TestGM_OD.Unit>),
						InitialValueFactory = () => new GMCore.Collections.MapCollection<System.Int32,TestGM_OD.Unit>(),
						ReplicationMode = GMCore.ODFieldReplicationMode.AllClients,
						DrivenBys = new System.Collections.Generic.List<GMCore.ODFieldName> {},
						DrivingOfs = new System.Collections.Generic.List<GMCore.ODFieldName> {},
					},
				},
			};
			yield return new GMCore.ODClassMeta
			{
				FromModule = new GMCore.ODModuleName { Name = "TestGM_OD" },
				Name = new GMCore.ODClassName() { NameObject = TestGM_OD.ClassNames.TestGM_OD_Unit },
				ODType = typeof(TestGM_OD.Unit),
				ReplicationMode = GMCore.GameplayObjectReplicationMode.AllClients,
				DirectBaseClasses = new System.Collections.Generic.List<GMCore.ODClassName>()
				{
				},
				Fields = new System.Collections.Generic.List<GMCore.ODFieldMeta>()
				{
					new GMCore.ODFieldMeta()
					{
						FromClass = new GMCore.ODClassName() { NameObject = TestGM_OD.ClassNames.TestGM_OD_Unit },
						Name = new GMCore.ODFieldName() { NameObject = Unit.FieldNames.Name },
						FieldType = typeof(System.String),
						DefaultObject = default(System.String?),
						ReplicationMode = GMCore.ODFieldReplicationMode.AllClients,
						DrivenBys = new System.Collections.Generic.List<GMCore.ODFieldName> {},
						DrivingOfs = new System.Collections.Generic.List<GMCore.ODFieldName> {},
					},
					new GMCore.ODFieldMeta()
					{
						FromClass = new GMCore.ODClassName() { NameObject = TestGM_OD.ClassNames.TestGM_OD_Unit },
						Name = new GMCore.ODFieldName() { NameObject = Unit.FieldNames.Health },
						FieldType = typeof(TestGM_OD.GameAttribute),
						DefaultObject = default(TestGM_OD.GameAttribute),
						ReplicationMode = GMCore.ODFieldReplicationMode.AllClients,
						DrivenBys = new System.Collections.Generic.List<GMCore.ODFieldName> {},
						DrivingOfs = new System.Collections.Generic.List<GMCore.ODFieldName> {new GMCore.ODFieldName() { NameObject = Unit.FieldNames.IsNearDeath }, new GMCore.ODFieldName() { NameObject = Unit.FieldNames.UnitStatus }},
					},
					new GMCore.ODFieldMeta()
					{
						FromClass = new GMCore.ODClassName() { NameObject = TestGM_OD.ClassNames.TestGM_OD_Unit },
						Name = new GMCore.ODFieldName() { NameObject = Unit.FieldNames.Attack },
						FieldType = typeof(TestGM_OD.GameAttribute),
						DefaultObject = default(TestGM_OD.GameAttribute),
						ReplicationMode = GMCore.ODFieldReplicationMode.AllClients,
						DrivenBys = new System.Collections.Generic.List<GMCore.ODFieldName> {},
						DrivingOfs = new System.Collections.Generic.List<GMCore.ODFieldName> {new GMCore.ODFieldName() { NameObject = Unit.FieldNames.UnitStatus }},
					},
					new GMCore.ODFieldMeta()
					{
						FromClass = new GMCore.ODClassName() { NameObject = TestGM_OD.ClassNames.TestGM_OD_Unit },
						Name = new GMCore.ODFieldName() { NameObject = Unit.FieldNames.IsNearDeath },
						FieldType = typeof(System.Boolean),
						DefaultObject = default(System.Boolean),
						ReplicationMode = GMCore.ODFieldReplicationMode.None,
						DrivenBys = new System.Collections.Generic.List<GMCore.ODFieldName> {new GMCore.ODFieldName() { NameObject = Unit.FieldNames.Health }},
						DrivingOfs = new System.Collections.Generic.List<GMCore.ODFieldName> {new GMCore.ODFieldName() { NameObject = Unit.FieldNames.UnitStatus }},
					},
					new GMCore.ODFieldMeta()
					{
						FromClass = new GMCore.ODClassName() { NameObject = TestGM_OD.ClassNames.TestGM_OD_Unit },
						Name = new GMCore.ODFieldName() { NameObject = Unit.FieldNames.UnitStatus },
						FieldType = typeof(System.String),
						DefaultObject = default(System.String?),
						ReplicationMode = GMCore.ODFieldReplicationMode.None,
						DrivenBys = new System.Collections.Generic.List<GMCore.ODFieldName> {new GMCore.ODFieldName() { NameObject = Unit.FieldNames.Attack }, new GMCore.ODFieldName() { NameObject = Unit.FieldNames.Health }},
						DrivingOfs = new System.Collections.Generic.List<GMCore.ODFieldName> {},
					},
				},
			};
			yield return new GMCore.ODClassMeta
			{
				FromModule = new GMCore.ODModuleName { Name = "TestGM_OD" },
				Name = new GMCore.ODClassName() { NameObject = TestGM_OD.ClassNames.TestGM_OD_Monster },
				ODType = typeof(TestGM_OD.Monster),
				ReplicationMode = GMCore.GameplayObjectReplicationMode.AllClients,
				DirectBaseClasses = new System.Collections.Generic.List<GMCore.ODClassName>()
				{
					new GMCore.ODClassName() { NameObject = TestGM_OD.ClassNames.TestGM_OD_Unit },
				},
				Fields = new System.Collections.Generic.List<GMCore.ODFieldMeta>()
				{
					new GMCore.ODFieldMeta()
					{
						FromClass = new GMCore.ODClassName() { NameObject = TestGM_OD.ClassNames.TestGM_OD_Monster },
						Name = new GMCore.ODFieldName() { NameObject = Monster.FieldNames.MonsterSkill },
						FieldType = typeof(TestGM_OD.GameAttribute),
						DefaultObject = default(TestGM_OD.GameAttribute),
						ReplicationMode = GMCore.ODFieldReplicationMode.AllClients,
						DrivenBys = new System.Collections.Generic.List<GMCore.ODFieldName> {},
						DrivingOfs = new System.Collections.Generic.List<GMCore.ODFieldName> {},
					},
				},
			};
			yield return new GMCore.ODClassMeta
			{
				FromModule = new GMCore.ODModuleName { Name = "TestGM_OD" },
				Name = new GMCore.ODClassName() { NameObject = TestGM_OD.ClassNames.TestGM_OD_Player },
				ODType = typeof(TestGM_OD.Player),
				ReplicationMode = GMCore.GameplayObjectReplicationMode.AllClients,
				DirectBaseClasses = new System.Collections.Generic.List<GMCore.ODClassName>()
				{
					new GMCore.ODClassName() { NameObject = TestGM_OD.ClassNames.TestGM_OD_Unit },
				},
				Fields = new System.Collections.Generic.List<GMCore.ODFieldMeta>()
				{
					new GMCore.ODFieldMeta()
					{
						FromClass = new GMCore.ODClassName() { NameObject = TestGM_OD.ClassNames.TestGM_OD_Player },
						Name = new GMCore.ODFieldName() { NameObject = Player.FieldNames.PlayerSkill },
						FieldType = typeof(TestGM_OD.GameAttribute),
						DefaultObject = default(TestGM_OD.GameAttribute),
						ReplicationMode = GMCore.ODFieldReplicationMode.None,
						DrivenBys = new System.Collections.Generic.List<GMCore.ODFieldName> {},
						DrivingOfs = new System.Collections.Generic.List<GMCore.ODFieldName> {},
					},
				},
			};
		}
		public System.Collections.Generic.IEnumerable<GMCore.ODStructMeta> GetAllODStructMetas()
		{
			yield return new GMCore.ODStructMeta
			{
				FromModule = new GMCore.ODModuleName { Name = "TestGM_OD" },
				Name = new GMCore.ODStructName() { NameObject = TestGM_OD.StructNames.TestGM_OD_GameAttribute },
				ODType = typeof(TestGM_OD.GameAttribute),
				BaseType = typeof(ODCore.Math.Integer),
			};
			yield return new GMCore.ODStructMeta
			{
				FromModule = new GMCore.ODModuleName { Name = "TestGM_OD" },
				Name = new GMCore.ODStructName() { NameObject = TestGM_OD.StructNames.TestGM_OD_Locations },
				ODType = typeof(TestGM_OD.Locations),
			};
		}
		public System.Collections.Generic.IEnumerable<GMCore.ODEventMeta> GetAllODEventMetas()
		{
			yield return new GMCore.ODEventMeta
			{
				FromModule = new GMCore.ODModuleName { Name = "TestGM_OD" },
				Name = new GMCore.ODEventName() { NameObject = TestGM_OD.EventNames.TestGM_OD_Routines_DamageRoutineParam },
				ODType = typeof(TestGM_OD.Routines.DamageRoutineParam),
			};
			yield return new GMCore.ODEventMeta
			{
				FromModule = new GMCore.ODModuleName { Name = "TestGM_OD" },
				Name = new GMCore.ODEventName() { NameObject = TestGM_OD.EventNames.TestGM_OD_Interfaces_DamageUnitParam },
				ODType = typeof(TestGM_OD.Interfaces.DamageUnitParam),
			};
			yield return new GMCore.ODEventMeta
			{
				FromModule = new GMCore.ODModuleName { Name = "TestGM_OD" },
				Name = new GMCore.ODEventName() { NameObject = TestGM_OD.EventNames.TestGM_OD_Interfaces_TriggerEventParam },
				ODType = typeof(TestGM_OD.Interfaces.TriggerEventParam),
			};
			yield return new GMCore.ODEventMeta
			{
				FromModule = new GMCore.ODModuleName { Name = "TestGM_OD" },
				Name = new GMCore.ODEventName() { NameObject = TestGM_OD.EventNames.TestGM_OD_Interfaces_SetUnitHealthsParam },
				ODType = typeof(TestGM_OD.Interfaces.SetUnitHealthsParam),
			};
			yield return new GMCore.ODEventMeta
			{
				FromModule = new GMCore.ODModuleName { Name = "TestGM_OD" },
				Name = new GMCore.ODEventName() { NameObject = TestGM_OD.EventNames.TestGM_OD_Interfaces_CreateMonstersParam },
				ODType = typeof(TestGM_OD.Interfaces.CreateMonstersParam),
			};
			yield return new GMCore.ODEventMeta
			{
				FromModule = new GMCore.ODModuleName { Name = "TestGM_OD" },
				Name = new GMCore.ODEventName() { NameObject = TestGM_OD.EventNames.TestGM_OD_Interfaces_TestUnitsParam },
				ODType = typeof(TestGM_OD.Interfaces.TestUnitsParam),
			};
			yield return new GMCore.ODEventMeta
			{
				FromModule = new GMCore.ODModuleName { Name = "TestGM_OD" },
				Name = new GMCore.ODEventName() { NameObject = TestGM_OD.EventNames.TestGM_OD_Interfaces_CreateDeleteParam },
				ODType = typeof(TestGM_OD.Interfaces.CreateDeleteParam),
			};
		}
		public System.Collections.Generic.IEnumerable<GMCore.ODClassSaveMeta> GetAllODClassSaveMetas()
		{
			yield return new GMCore.ODClassSaveMeta
			{
				StableID = GMCore.ODSaveHash.Compute("TestGM_OD.Game"),
				StableName = "TestGM_OD.Game",
				ClassName = new GMCore.ODClassName() { NameObject = TestGM_OD.ClassNames.TestGM_OD_Game },
			};
			yield return new GMCore.ODClassSaveMeta
			{
				StableID = GMCore.ODSaveHash.Compute("TestGM_OD.Unit"),
				StableName = "TestGM_OD.Unit",
				ClassName = new GMCore.ODClassName() { NameObject = TestGM_OD.ClassNames.TestGM_OD_Unit },
			};
			yield return new GMCore.ODClassSaveMeta
			{
				StableID = GMCore.ODSaveHash.Compute("TestGM_OD.Monster"),
				StableName = "TestGM_OD.Monster",
				ClassName = new GMCore.ODClassName() { NameObject = TestGM_OD.ClassNames.TestGM_OD_Monster },
			};
			yield return new GMCore.ODClassSaveMeta
			{
				StableID = GMCore.ODSaveHash.Compute("TestGM_OD.Player"),
				StableName = "TestGM_OD.Player",
				ClassName = new GMCore.ODClassName() { NameObject = TestGM_OD.ClassNames.TestGM_OD_Player },
			};
		}
		public System.Collections.Generic.IEnumerable<GMCore.ODFieldSaveMeta> GetAllODFieldSaveMetas()
		{
			yield return new GMCore.ODFieldSaveMeta
			{
				StableID = GMCore.ODSaveHash.Compute("TestGM_OD.Game.Player"),
				StableName = "TestGM_OD.Game.Player",
				FieldName = new GMCore.ODFieldName() { NameObject = Game.FieldNames.Player },
				Writer = Write_TestGM_OD_Game_Player,
				Reader = Read_TestGM_OD_Game_Player,
			};
			yield return new GMCore.ODFieldSaveMeta
			{
				StableID = GMCore.ODSaveHash.Compute("TestGM_OD.Game.A"),
				StableName = "TestGM_OD.Game.A",
				FieldName = new GMCore.ODFieldName() { NameObject = Game.FieldNames.A },
				Writer = Write_TestGM_OD_Game_A,
				Reader = Read_TestGM_OD_Game_A,
			};
			yield return new GMCore.ODFieldSaveMeta
			{
				StableID = GMCore.ODSaveHash.Compute("TestGM_OD.Game.B"),
				StableName = "TestGM_OD.Game.B",
				FieldName = new GMCore.ODFieldName() { NameObject = Game.FieldNames.B },
				Writer = Write_TestGM_OD_Game_B,
				Reader = Read_TestGM_OD_Game_B,
			};
			yield return new GMCore.ODFieldSaveMeta
			{
				StableID = GMCore.ODSaveHash.Compute("TestGM_OD.Game.NotSerializableInt"),
				StableName = "TestGM_OD.Game.NotSerializableInt",
				FieldName = new GMCore.ODFieldName() { NameObject = Game.FieldNames.NotSerializableInt },
				Writer = Write_TestGM_OD_Game_NotSerializableInt,
				Reader = Read_TestGM_OD_Game_NotSerializableInt,
			};
			yield return new GMCore.ODFieldSaveMeta
			{
				StableID = GMCore.ODSaveHash.Compute("TestGM_OD.Game.IntArray"),
				StableName = "TestGM_OD.Game.IntArray",
				FieldName = new GMCore.ODFieldName() { NameObject = Game.FieldNames.IntArray },
				Writer = Write_TestGM_OD_Game_IntArray,
				Reader = Read_TestGM_OD_Game_IntArray,
			};
			yield return new GMCore.ODFieldSaveMeta
			{
				StableID = GMCore.ODSaveHash.Compute("TestGM_OD.Game.Units"),
				StableName = "TestGM_OD.Game.Units",
				FieldName = new GMCore.ODFieldName() { NameObject = Game.FieldNames.Units },
				Writer = Write_TestGM_OD_Game_Units,
				Reader = Read_TestGM_OD_Game_Units,
				DeltaKind = GMCore.ODCollectionDeltaKind.Set,
				DeltaKeyWriter = WriteDeltaUnit,
				DeltaKeyReader = ReadDeltaUnit,
			};
			yield return new GMCore.ODFieldSaveMeta
			{
				StableID = GMCore.ODSaveHash.Compute("TestGM_OD.Game.Units2"),
				StableName = "TestGM_OD.Game.Units2",
				FieldName = new GMCore.ODFieldName() { NameObject = Game.FieldNames.Units2 },
				Writer = Write_TestGM_OD_Game_Units2,
				Reader = Read_TestGM_OD_Game_Units2,
				DeltaKind = GMCore.ODCollectionDeltaKind.Set,
				DeltaKeyWriter = WriteDeltaUnit,
				DeltaKeyReader = ReadDeltaUnit,
			};
			yield return new GMCore.ODFieldSaveMeta
			{
				StableID = GMCore.ODSaveHash.Compute("TestGM_OD.Game.IDUnits"),
				StableName = "TestGM_OD.Game.IDUnits",
				FieldName = new GMCore.ODFieldName() { NameObject = Game.FieldNames.IDUnits },
				Writer = Write_TestGM_OD_Game_IDUnits,
				Reader = Read_TestGM_OD_Game_IDUnits,
				DeltaKind = GMCore.ODCollectionDeltaKind.Map,
				DeltaKeyWriter = (writer, value) => writer.WriteInt32((System.Int32)value),
				DeltaKeyReader = reader => reader.ReadInt32(),
				DeltaValueWriter = WriteDeltaUnit,
				DeltaValueReader = ReadDeltaUnit,
			};
			yield return new GMCore.ODFieldSaveMeta
			{
				StableID = GMCore.ODSaveHash.Compute("TestGM_OD.Unit.Name"),
				StableName = "TestGM_OD.Unit.Name",
				FieldName = new GMCore.ODFieldName() { NameObject = Unit.FieldNames.Name },
				Writer = Write_TestGM_OD_Unit_Name,
				Reader = Read_TestGM_OD_Unit_Name,
			};
			yield return new GMCore.ODFieldSaveMeta
			{
				StableID = GMCore.ODSaveHash.Compute("TestGM_OD.Unit.Health"),
				StableName = "TestGM_OD.Unit.Health",
				FieldName = new GMCore.ODFieldName() { NameObject = Unit.FieldNames.Health },
				Writer = Write_TestGM_OD_Unit_Health,
				Reader = Read_TestGM_OD_Unit_Health,
			};
			yield return new GMCore.ODFieldSaveMeta
			{
				StableID = GMCore.ODSaveHash.Compute("TestGM_OD.Unit.Attack"),
				StableName = "TestGM_OD.Unit.Attack",
				FieldName = new GMCore.ODFieldName() { NameObject = Unit.FieldNames.Attack },
				Writer = Write_TestGM_OD_Unit_Attack,
				Reader = Read_TestGM_OD_Unit_Attack,
			};
			yield return new GMCore.ODFieldSaveMeta
			{
				StableID = GMCore.ODSaveHash.Compute("TestGM_OD.Unit.IsNearDeath"),
				StableName = "TestGM_OD.Unit.IsNearDeath",
				FieldName = new GMCore.ODFieldName() { NameObject = Unit.FieldNames.IsNearDeath },
				Writer = Write_TestGM_OD_Unit_IsNearDeath,
				Reader = Read_TestGM_OD_Unit_IsNearDeath,
			};
			yield return new GMCore.ODFieldSaveMeta
			{
				StableID = GMCore.ODSaveHash.Compute("TestGM_OD.Unit.UnitStatus"),
				StableName = "TestGM_OD.Unit.UnitStatus",
				FieldName = new GMCore.ODFieldName() { NameObject = Unit.FieldNames.UnitStatus },
				Writer = Write_TestGM_OD_Unit_UnitStatus,
				Reader = Read_TestGM_OD_Unit_UnitStatus,
			};
			yield return new GMCore.ODFieldSaveMeta
			{
				StableID = GMCore.ODSaveHash.Compute("TestGM_OD.Monster.MonsterSkill"),
				StableName = "TestGM_OD.Monster.MonsterSkill",
				FieldName = new GMCore.ODFieldName() { NameObject = Monster.FieldNames.MonsterSkill },
				Writer = Write_TestGM_OD_Monster_MonsterSkill,
				Reader = Read_TestGM_OD_Monster_MonsterSkill,
			};
			yield return new GMCore.ODFieldSaveMeta
			{
				StableID = GMCore.ODSaveHash.Compute("TestGM_OD.Player.PlayerSkill"),
				StableName = "TestGM_OD.Player.PlayerSkill",
				FieldName = new GMCore.ODFieldName() { NameObject = Player.FieldNames.PlayerSkill },
				Writer = Write_TestGM_OD_Player_PlayerSkill,
				Reader = Read_TestGM_OD_Player_PlayerSkill,
			};
		}
		public ulong NetworkSchemaID
		{
			get
			{
				return GMCore.ODSaveHash.Compute("class:TestGM_OD.Game|field:TestGM_OD.Game.A:TestGM_OD.Monster|field:TestGM_OD.Game.B:TestGM_OD.Monster|field:TestGM_OD.Game.IDUnits:ODCore.Collections.Map`2[System.Int32,TestGM_OD.Unit]|field:TestGM_OD.Game.IntArray:ODCore.Collections.List`1[System.Int32]|field:TestGM_OD.Game.NotSerializableInt:TestCommon.NotSerializableInt|field:TestGM_OD.Game.Player:TestGM_OD.Player|field:TestGM_OD.Game.Units:ODCore.Collections.Set`1[TestGM_OD.Unit]|field:TestGM_OD.Game.Units2:ODCore.Collections.Set`1[TestGM_OD.Unit]|class:TestGM_OD.Monster|field:TestGM_OD.Monster.MonsterSkill:TestGM_OD.GameAttribute|class:TestGM_OD.Player|field:TestGM_OD.Player.PlayerSkill:TestGM_OD.GameAttribute|class:TestGM_OD.Unit|field:TestGM_OD.Unit.Attack:TestGM_OD.GameAttribute|field:TestGM_OD.Unit.Health:TestGM_OD.GameAttribute|field:TestGM_OD.Unit.IsNearDeath:System.Boolean|field:TestGM_OD.Unit.Name:System.String|field:TestGM_OD.Unit.UnitStatus:System.String|rpc:TestGM_OD.Interfaces.CreateDeleteParam:TestGM_OD.Interfaces.CreateDeleteParamOut|rpc:TestGM_OD.Interfaces.CreateMonstersParam:TestGM_OD.Interfaces.CreateMonstersParamOut|rpcin:Count:System.Int32|rpc:TestGM_OD.Interfaces.DamageUnitParam:TestGM_OD.Interfaces.DamageUnitParamOut|rpcin:A:TestGM_OD.Unit|rpcin:B:TestGM_OD.Unit|rpcin:Damage:TestGM_OD.GameAttribute|rpc:TestGM_OD.Interfaces.SetUnitHealthsParam:TestGM_OD.Interfaces.SetUnitHealthsParamOut|rpcin:Healths:System.Collections.Generic.List`1[TestGM_OD.GameAttribute]|rpcin:Unit:TestGM_OD.Unit|rpc:TestGM_OD.Interfaces.TestUnitsParam:TestGM_OD.Interfaces.TestUnitsParamOut|rpcin:Adds:System.Collections.Generic.List`1[GMCore.KVPair`2[System.Int32,TestGM_OD.Unit]]|rpcin:Removes:System.Collections.Generic.List`1[System.Int32]|rpc:TestGM_OD.Interfaces.TriggerEventParam:TestGM_OD.Interfaces.TriggerEventParamOut|rpcin:TriggerValue:System.Int32");
			}
		}
		public System.Collections.Generic.IEnumerable<GMCore.ODRpcMeta> GetAllODRpcMetas()
		{
			yield return new GMCore.ODRpcMeta
			{
				StableID = GMCore.ODSaveHash.Compute("TestGM_OD.Interfaces.CreateDeleteParam"),
				StableName = "TestGM_OD.Interfaces.CreateDeleteParam",
				ParamType = typeof(TestGM_OD.Interfaces.CreateDeleteParam),
				ResultType = typeof(TestGM_OD.Interfaces.CreateDeleteParamOut),
				ParamWriter = WriteRpcParam_TestGM_OD_Interfaces_CreateDeleteParam,
				ParamReader = ReadRpcParam_TestGM_OD_Interfaces_CreateDeleteParam,
				ResultWriter = WriteRpcResult_TestGM_OD_Interfaces_CreateDeleteParam,
				ResultReader = ReadRpcResult_TestGM_OD_Interfaces_CreateDeleteParam,
				Execute = ExecuteRpc_TestGM_OD_Interfaces_CreateDeleteParam,
			};
			yield return new GMCore.ODRpcMeta
			{
				StableID = GMCore.ODSaveHash.Compute("TestGM_OD.Interfaces.CreateMonstersParam"),
				StableName = "TestGM_OD.Interfaces.CreateMonstersParam",
				ParamType = typeof(TestGM_OD.Interfaces.CreateMonstersParam),
				ResultType = typeof(TestGM_OD.Interfaces.CreateMonstersParamOut),
				ParamWriter = WriteRpcParam_TestGM_OD_Interfaces_CreateMonstersParam,
				ParamReader = ReadRpcParam_TestGM_OD_Interfaces_CreateMonstersParam,
				ResultWriter = WriteRpcResult_TestGM_OD_Interfaces_CreateMonstersParam,
				ResultReader = ReadRpcResult_TestGM_OD_Interfaces_CreateMonstersParam,
				Execute = ExecuteRpc_TestGM_OD_Interfaces_CreateMonstersParam,
			};
			yield return new GMCore.ODRpcMeta
			{
				StableID = GMCore.ODSaveHash.Compute("TestGM_OD.Interfaces.DamageUnitParam"),
				StableName = "TestGM_OD.Interfaces.DamageUnitParam",
				ParamType = typeof(TestGM_OD.Interfaces.DamageUnitParam),
				ResultType = typeof(TestGM_OD.Interfaces.DamageUnitParamOut),
				ParamWriter = WriteRpcParam_TestGM_OD_Interfaces_DamageUnitParam,
				ParamReader = ReadRpcParam_TestGM_OD_Interfaces_DamageUnitParam,
				ResultWriter = WriteRpcResult_TestGM_OD_Interfaces_DamageUnitParam,
				ResultReader = ReadRpcResult_TestGM_OD_Interfaces_DamageUnitParam,
				Execute = ExecuteRpc_TestGM_OD_Interfaces_DamageUnitParam,
			};
			yield return new GMCore.ODRpcMeta
			{
				StableID = GMCore.ODSaveHash.Compute("TestGM_OD.Interfaces.SetUnitHealthsParam"),
				StableName = "TestGM_OD.Interfaces.SetUnitHealthsParam",
				ParamType = typeof(TestGM_OD.Interfaces.SetUnitHealthsParam),
				ResultType = typeof(TestGM_OD.Interfaces.SetUnitHealthsParamOut),
				ParamWriter = WriteRpcParam_TestGM_OD_Interfaces_SetUnitHealthsParam,
				ParamReader = ReadRpcParam_TestGM_OD_Interfaces_SetUnitHealthsParam,
				ResultWriter = WriteRpcResult_TestGM_OD_Interfaces_SetUnitHealthsParam,
				ResultReader = ReadRpcResult_TestGM_OD_Interfaces_SetUnitHealthsParam,
				Execute = ExecuteRpc_TestGM_OD_Interfaces_SetUnitHealthsParam,
			};
			yield return new GMCore.ODRpcMeta
			{
				StableID = GMCore.ODSaveHash.Compute("TestGM_OD.Interfaces.TestUnitsParam"),
				StableName = "TestGM_OD.Interfaces.TestUnitsParam",
				ParamType = typeof(TestGM_OD.Interfaces.TestUnitsParam),
				ResultType = typeof(TestGM_OD.Interfaces.TestUnitsParamOut),
				ParamWriter = WriteRpcParam_TestGM_OD_Interfaces_TestUnitsParam,
				ParamReader = ReadRpcParam_TestGM_OD_Interfaces_TestUnitsParam,
				ResultWriter = WriteRpcResult_TestGM_OD_Interfaces_TestUnitsParam,
				ResultReader = ReadRpcResult_TestGM_OD_Interfaces_TestUnitsParam,
				Execute = ExecuteRpc_TestGM_OD_Interfaces_TestUnitsParam,
			};
			yield return new GMCore.ODRpcMeta
			{
				StableID = GMCore.ODSaveHash.Compute("TestGM_OD.Interfaces.TriggerEventParam"),
				StableName = "TestGM_OD.Interfaces.TriggerEventParam",
				ParamType = typeof(TestGM_OD.Interfaces.TriggerEventParam),
				ResultType = typeof(TestGM_OD.Interfaces.TriggerEventParamOut),
				ParamWriter = WriteRpcParam_TestGM_OD_Interfaces_TriggerEventParam,
				ParamReader = ReadRpcParam_TestGM_OD_Interfaces_TriggerEventParam,
				ResultWriter = WriteRpcResult_TestGM_OD_Interfaces_TriggerEventParam,
				ResultReader = ReadRpcResult_TestGM_OD_Interfaces_TriggerEventParam,
				Execute = ExecuteRpc_TestGM_OD_Interfaces_TriggerEventParam,
			};
		}
		private static void Write_TestGM_OD_Game_Player(GMCore.GameplaySaveWriter writer, object boxedValue)
		{
			TestGM_OD.Player value = boxedValue is TestGM_OD.Player reference ? reference : default;
			writer.WriteObjectID(value.ObjectID);
		}
		private static object Read_TestGM_OD_Game_Player(GMCore.GameplaySaveReader reader)
		{
			TestGM_OD.Player value0 = default;
			value0.Machine = reader.Machine;
			value0.ObjectID = reader.ReadObjectID();
			return value0;
		}
		private static void Write_TestGM_OD_Game_A(GMCore.GameplaySaveWriter writer, object boxedValue)
		{
			TestGM_OD.Monster value = boxedValue is TestGM_OD.Monster reference ? reference : default;
			writer.WriteObjectID(value.ObjectID);
		}
		private static object Read_TestGM_OD_Game_A(GMCore.GameplaySaveReader reader)
		{
			TestGM_OD.Monster value0 = default;
			value0.Machine = reader.Machine;
			value0.ObjectID = reader.ReadObjectID();
			return value0;
		}
		private static void Write_TestGM_OD_Game_B(GMCore.GameplaySaveWriter writer, object boxedValue)
		{
			TestGM_OD.Monster value = boxedValue is TestGM_OD.Monster reference ? reference : default;
			writer.WriteObjectID(value.ObjectID);
		}
		private static object Read_TestGM_OD_Game_B(GMCore.GameplaySaveReader reader)
		{
			TestGM_OD.Monster value0 = default;
			value0.Machine = reader.Machine;
			value0.ObjectID = reader.ReadObjectID();
			return value0;
		}
		private static void Write_TestGM_OD_Game_NotSerializableInt(GMCore.GameplaySaveWriter writer, object boxedValue)
		{
			TestCommon.NotSerializableInt value = (TestCommon.NotSerializableInt)boxedValue;
			writer.WriteCustom(typeof(TestCommon.NotSerializableInt), value);
		}
		private static object Read_TestGM_OD_Game_NotSerializableInt(GMCore.GameplaySaveReader reader)
		{
			TestCommon.NotSerializableInt value0 = reader.ReadCustom<TestCommon.NotSerializableInt>();
			return value0;
		}
		private static void Write_TestGM_OD_Game_IntArray(GMCore.GameplaySaveWriter writer, object boxedValue)
		{
			System.Collections.Generic.ICollection<System.Int32> values0 = boxedValue as System.Collections.Generic.ICollection<System.Int32>;
			writer.WriteCount(values0 == null ? -1 : values0.Count);
			if (values0 != null)
			{
				foreach (System.Int32 item in values0)
				{
					writer.WriteInt32(item);
				}
			}
		}
		private static object Read_TestGM_OD_Game_IntArray(GMCore.GameplaySaveReader reader)
		{
			int count2 = reader.ReadCount();
			GMCore.Collections.ListCollection<System.Int32> collection1 = null;
			if (count2 >= 0)
			{
				collection1 = new GMCore.Collections.ListCollection<System.Int32>();
				for (int i = 0; i < count2; i++)
				{
					System.Int32 value3 = reader.ReadInt32();
					collection1.Add(value3);
				}
			}
			return collection1;
		}
		private static void Write_TestGM_OD_Game_Units(GMCore.GameplaySaveWriter writer, object boxedValue)
		{
			System.Collections.Generic.ICollection<TestGM_OD.Unit> values0 = boxedValue as System.Collections.Generic.ICollection<TestGM_OD.Unit>;
			writer.WriteCount(values0 == null ? -1 : values0.Count);
			if (values0 != null)
			{
				foreach (TestGM_OD.Unit item in values0)
				{
					writer.WriteObjectID(item.ObjectID);
				}
			}
		}
		private static void WriteDeltaUnit(GMCore.GameplaySaveWriter writer, object boxedValue)
		{
			writer.WriteObjectID(((TestGM_OD.Unit)boxedValue).ObjectID);
		}
		private static object ReadDeltaUnit(GMCore.GameplaySaveReader reader)
		{
			return new TestGM_OD.Unit { Machine = reader.Machine, ObjectID = reader.ReadObjectID() };
		}
		private static object Read_TestGM_OD_Game_Units(GMCore.GameplaySaveReader reader)
		{
			int count2 = reader.ReadCount();
			GMCore.Collections.SetCollection<TestGM_OD.Unit> collection1 = null;
			if (count2 >= 0)
			{
				collection1 = new GMCore.Collections.SetCollection<TestGM_OD.Unit>();
				for (int i = 0; i < count2; i++)
				{
					TestGM_OD.Unit value3 = default;
					value3.Machine = reader.Machine;
					value3.ObjectID = reader.ReadObjectID();
					collection1.Add(value3);
				}
			}
			return collection1;
		}
		private static void Write_TestGM_OD_Game_Units2(GMCore.GameplaySaveWriter writer, object boxedValue)
		{
			System.Collections.Generic.ICollection<TestGM_OD.Unit> values0 = boxedValue as System.Collections.Generic.ICollection<TestGM_OD.Unit>;
			writer.WriteCount(values0 == null ? -1 : values0.Count);
			if (values0 != null)
			{
				foreach (TestGM_OD.Unit item in values0)
				{
					writer.WriteObjectID(item.ObjectID);
				}
			}
		}
		private static object Read_TestGM_OD_Game_Units2(GMCore.GameplaySaveReader reader)
		{
			int count2 = reader.ReadCount();
			GMCore.Collections.SetCollection<TestGM_OD.Unit> collection1 = null;
			if (count2 >= 0)
			{
				collection1 = new GMCore.Collections.SetCollection<TestGM_OD.Unit>();
				for (int i = 0; i < count2; i++)
				{
					TestGM_OD.Unit value3 = default;
					value3.Machine = reader.Machine;
					value3.ObjectID = reader.ReadObjectID();
					collection1.Add(value3);
				}
			}
			return collection1;
		}
		private static void Write_TestGM_OD_Game_IDUnits(GMCore.GameplaySaveWriter writer, object boxedValue)
		{
			System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<System.Int32,TestGM_OD.Unit>> values0 = boxedValue as System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<System.Int32,TestGM_OD.Unit>>;
			writer.WriteCount(values0 == null ? -1 : values0.Count);
			if (values0 != null)
			{
				foreach (System.Collections.Generic.KeyValuePair<System.Int32,TestGM_OD.Unit> item in values0)
				{
					writer.WriteInt32(item.Key);
					writer.WriteObjectID(item.Value.ObjectID);
				}
			}
		}
		private static object Read_TestGM_OD_Game_IDUnits(GMCore.GameplaySaveReader reader)
		{
			int count2 = reader.ReadCount();
			GMCore.Collections.MapCollection<System.Int32,TestGM_OD.Unit> collection1 = null;
			if (count2 >= 0)
			{
				collection1 = new GMCore.Collections.MapCollection<System.Int32,TestGM_OD.Unit>();
				for (int i = 0; i < count2; i++)
				{
					System.Int32 value3 = reader.ReadInt32();
					TestGM_OD.Unit value4 = default;
					value4.Machine = reader.Machine;
					value4.ObjectID = reader.ReadObjectID();
					collection1.Add(value3, value4);
				}
			}
			return collection1;
		}
		private static void Write_TestGM_OD_Unit_Name(GMCore.GameplaySaveWriter writer, object boxedValue)
		{
			System.String value = (System.String)boxedValue;
			writer.WriteString(value);
		}
		private static object Read_TestGM_OD_Unit_Name(GMCore.GameplaySaveReader reader)
		{
			System.String value0 = reader.ReadString();
			return value0;
		}
		private static void Write_TestGM_OD_Unit_Health(GMCore.GameplaySaveWriter writer, object boxedValue)
		{
			TestGM_OD.GameAttribute value = (TestGM_OD.GameAttribute)boxedValue;
			writer.WriteInt32(value.Value.Value);
		}
		private static object Read_TestGM_OD_Unit_Health(GMCore.GameplaySaveReader reader)
		{
			TestGM_OD.GameAttribute value0 = default;
			ODCore.Math.Integer value1 = default;
			System.Int32 value2 = reader.ReadInt32();
			value1.Value = value2;
			value0.Value = value1;
			return value0;
		}
		private static void Write_TestGM_OD_Unit_Attack(GMCore.GameplaySaveWriter writer, object boxedValue)
		{
			TestGM_OD.GameAttribute value = (TestGM_OD.GameAttribute)boxedValue;
			writer.WriteInt32(value.Value.Value);
		}
		private static object Read_TestGM_OD_Unit_Attack(GMCore.GameplaySaveReader reader)
		{
			TestGM_OD.GameAttribute value0 = default;
			ODCore.Math.Integer value1 = default;
			System.Int32 value2 = reader.ReadInt32();
			value1.Value = value2;
			value0.Value = value1;
			return value0;
		}
		private static void Write_TestGM_OD_Unit_IsNearDeath(GMCore.GameplaySaveWriter writer, object boxedValue)
		{
			System.Boolean value = (System.Boolean)boxedValue;
			writer.WriteBoolean(value);
		}
		private static object Read_TestGM_OD_Unit_IsNearDeath(GMCore.GameplaySaveReader reader)
		{
			System.Boolean value0 = reader.ReadBoolean();
			return value0;
		}
		private static void Write_TestGM_OD_Unit_UnitStatus(GMCore.GameplaySaveWriter writer, object boxedValue)
		{
			System.String value = (System.String)boxedValue;
			writer.WriteString(value);
		}
		private static object Read_TestGM_OD_Unit_UnitStatus(GMCore.GameplaySaveReader reader)
		{
			System.String value0 = reader.ReadString();
			return value0;
		}
		private static void Write_TestGM_OD_Monster_MonsterSkill(GMCore.GameplaySaveWriter writer, object boxedValue)
		{
			TestGM_OD.GameAttribute value = (TestGM_OD.GameAttribute)boxedValue;
			writer.WriteInt32(value.Value.Value);
		}
		private static object Read_TestGM_OD_Monster_MonsterSkill(GMCore.GameplaySaveReader reader)
		{
			TestGM_OD.GameAttribute value0 = default;
			ODCore.Math.Integer value1 = default;
			System.Int32 value2 = reader.ReadInt32();
			value1.Value = value2;
			value0.Value = value1;
			return value0;
		}
		private static void Write_TestGM_OD_Player_PlayerSkill(GMCore.GameplaySaveWriter writer, object boxedValue)
		{
			TestGM_OD.GameAttribute value = (TestGM_OD.GameAttribute)boxedValue;
			writer.WriteInt32(value.Value.Value);
		}
		private static object Read_TestGM_OD_Player_PlayerSkill(GMCore.GameplaySaveReader reader)
		{
			TestGM_OD.GameAttribute value0 = default;
			ODCore.Math.Integer value1 = default;
			System.Int32 value2 = reader.ReadInt32();
			value1.Value = value2;
			value0.Value = value1;
			return value0;
		}
		private static void WriteRpcParam_TestGM_OD_Interfaces_DamageUnitParam(GMCore.GameplaySaveWriter writer, object boxedValue)
		{
			TestGM_OD.Interfaces.DamageUnitParam value = (TestGM_OD.Interfaces.DamageUnitParam)boxedValue;
			writer.WriteObjectID(value.A.ObjectID);
			writer.WriteObjectID(value.B.ObjectID);
			writer.WriteInt32(value.Damage.Value.Value);
		}
		private static object ReadRpcParam_TestGM_OD_Interfaces_DamageUnitParam(GMCore.GameplaySaveReader reader)
		{
			TestGM_OD.Interfaces.DamageUnitParam result = default;
			TestGM_OD.Unit value0 = default;
			value0.Machine = reader.Machine;
			value0.ObjectID = reader.ReadObjectID();
			result.A = value0;
			TestGM_OD.Unit value1 = default;
			value1.Machine = reader.Machine;
			value1.ObjectID = reader.ReadObjectID();
			result.B = value1;
			TestGM_OD.GameAttribute value2 = default;
			ODCore.Math.Integer value3 = default;
			System.Int32 value4 = reader.ReadInt32();
			value3.Value = value4;
			value2.Value = value3;
			result.Damage = value2;
			return result;
		}
		private static void WriteRpcResult_TestGM_OD_Interfaces_DamageUnitParam(GMCore.GameplaySaveWriter writer, object boxedValue)
		{
			TestGM_OD.Interfaces.DamageUnitParamOut value = (TestGM_OD.Interfaces.DamageUnitParamOut)boxedValue;
		}
		private static object ReadRpcResult_TestGM_OD_Interfaces_DamageUnitParam(GMCore.GameplaySaveReader reader)
		{
			TestGM_OD.Interfaces.DamageUnitParamOut result = default;
			return result;
		}
		private static GMCore.ODRpcExecutionResult ExecuteRpc_TestGM_OD_Interfaces_DamageUnitParam(GMCore.GameplayMachine machine, object boxedValue)
		{
			TestGM_OD.Interfaces.DamageUnitParam value = (TestGM_OD.Interfaces.DamageUnitParam)boxedValue;
			var result = machine.Execute(value);
			return new GMCore.ODRpcExecutionResult
			{
				Error = result.ErrorMessage.Error,
				Result = result.Result,
			};
		}
		private static void WriteRpcParam_TestGM_OD_Interfaces_TriggerEventParam(GMCore.GameplaySaveWriter writer, object boxedValue)
		{
			TestGM_OD.Interfaces.TriggerEventParam value = (TestGM_OD.Interfaces.TriggerEventParam)boxedValue;
			writer.WriteInt32(value.TriggerValue);
		}
		private static object ReadRpcParam_TestGM_OD_Interfaces_TriggerEventParam(GMCore.GameplaySaveReader reader)
		{
			TestGM_OD.Interfaces.TriggerEventParam result = default;
			System.Int32 value0 = reader.ReadInt32();
			result.TriggerValue = value0;
			return result;
		}
		private static void WriteRpcResult_TestGM_OD_Interfaces_TriggerEventParam(GMCore.GameplaySaveWriter writer, object boxedValue)
		{
			TestGM_OD.Interfaces.TriggerEventParamOut value = (TestGM_OD.Interfaces.TriggerEventParamOut)boxedValue;
		}
		private static object ReadRpcResult_TestGM_OD_Interfaces_TriggerEventParam(GMCore.GameplaySaveReader reader)
		{
			TestGM_OD.Interfaces.TriggerEventParamOut result = default;
			return result;
		}
		private static GMCore.ODRpcExecutionResult ExecuteRpc_TestGM_OD_Interfaces_TriggerEventParam(GMCore.GameplayMachine machine, object boxedValue)
		{
			TestGM_OD.Interfaces.TriggerEventParam value = (TestGM_OD.Interfaces.TriggerEventParam)boxedValue;
			var result = machine.Execute(value);
			return new GMCore.ODRpcExecutionResult
			{
				Error = result.ErrorMessage.Error,
				Result = result.Result,
			};
		}
		private static void WriteRpcParam_TestGM_OD_Interfaces_SetUnitHealthsParam(GMCore.GameplaySaveWriter writer, object boxedValue)
		{
			TestGM_OD.Interfaces.SetUnitHealthsParam value = (TestGM_OD.Interfaces.SetUnitHealthsParam)boxedValue;
			writer.WriteObjectID(value.Unit.ObjectID);
			System.Collections.Generic.ICollection<TestGM_OD.GameAttribute> values0 = value.Healths as System.Collections.Generic.ICollection<TestGM_OD.GameAttribute>;
			writer.WriteCount(values0 == null ? -1 : values0.Count);
			if (values0 != null)
			{
				foreach (TestGM_OD.GameAttribute item in values0)
				{
					writer.WriteInt32(item.Value.Value);
				}
			}
		}
		private static object ReadRpcParam_TestGM_OD_Interfaces_SetUnitHealthsParam(GMCore.GameplaySaveReader reader)
		{
			TestGM_OD.Interfaces.SetUnitHealthsParam result = default;
			TestGM_OD.Unit value0 = default;
			value0.Machine = reader.Machine;
			value0.ObjectID = reader.ReadObjectID();
			result.Unit = value0;
			int count3 = reader.ReadCount();
			System.Collections.Generic.List<TestGM_OD.GameAttribute> collection2 = null;
			if (count3 >= 0)
			{
				collection2 = new System.Collections.Generic.List<TestGM_OD.GameAttribute>();
				for (int i = 0; i < count3; i++)
				{
					TestGM_OD.GameAttribute value4 = default;
					ODCore.Math.Integer value5 = default;
					System.Int32 value6 = reader.ReadInt32();
					value5.Value = value6;
					value4.Value = value5;
					collection2.Add(value4);
				}
			}
			result.Healths = collection2;
			return result;
		}
		private static void WriteRpcResult_TestGM_OD_Interfaces_SetUnitHealthsParam(GMCore.GameplaySaveWriter writer, object boxedValue)
		{
			TestGM_OD.Interfaces.SetUnitHealthsParamOut value = (TestGM_OD.Interfaces.SetUnitHealthsParamOut)boxedValue;
		}
		private static object ReadRpcResult_TestGM_OD_Interfaces_SetUnitHealthsParam(GMCore.GameplaySaveReader reader)
		{
			TestGM_OD.Interfaces.SetUnitHealthsParamOut result = default;
			return result;
		}
		private static GMCore.ODRpcExecutionResult ExecuteRpc_TestGM_OD_Interfaces_SetUnitHealthsParam(GMCore.GameplayMachine machine, object boxedValue)
		{
			TestGM_OD.Interfaces.SetUnitHealthsParam value = (TestGM_OD.Interfaces.SetUnitHealthsParam)boxedValue;
			var result = machine.Execute(value);
			return new GMCore.ODRpcExecutionResult
			{
				Error = result.ErrorMessage.Error,
				Result = result.Result,
			};
		}
		private static void WriteRpcParam_TestGM_OD_Interfaces_CreateMonstersParam(GMCore.GameplaySaveWriter writer, object boxedValue)
		{
			TestGM_OD.Interfaces.CreateMonstersParam value = (TestGM_OD.Interfaces.CreateMonstersParam)boxedValue;
			writer.WriteInt32(value.Count);
		}
		private static object ReadRpcParam_TestGM_OD_Interfaces_CreateMonstersParam(GMCore.GameplaySaveReader reader)
		{
			TestGM_OD.Interfaces.CreateMonstersParam result = default;
			System.Int32 value0 = reader.ReadInt32();
			result.Count = value0;
			return result;
		}
		private static void WriteRpcResult_TestGM_OD_Interfaces_CreateMonstersParam(GMCore.GameplaySaveWriter writer, object boxedValue)
		{
			TestGM_OD.Interfaces.CreateMonstersParamOut value = (TestGM_OD.Interfaces.CreateMonstersParamOut)boxedValue;
		}
		private static object ReadRpcResult_TestGM_OD_Interfaces_CreateMonstersParam(GMCore.GameplaySaveReader reader)
		{
			TestGM_OD.Interfaces.CreateMonstersParamOut result = default;
			return result;
		}
		private static GMCore.ODRpcExecutionResult ExecuteRpc_TestGM_OD_Interfaces_CreateMonstersParam(GMCore.GameplayMachine machine, object boxedValue)
		{
			TestGM_OD.Interfaces.CreateMonstersParam value = (TestGM_OD.Interfaces.CreateMonstersParam)boxedValue;
			var result = machine.Execute(value);
			return new GMCore.ODRpcExecutionResult
			{
				Error = result.ErrorMessage.Error,
				Result = result.Result,
			};
		}
		private static void WriteRpcParam_TestGM_OD_Interfaces_TestUnitsParam(GMCore.GameplaySaveWriter writer, object boxedValue)
		{
			TestGM_OD.Interfaces.TestUnitsParam value = (TestGM_OD.Interfaces.TestUnitsParam)boxedValue;
			System.Collections.Generic.ICollection<GMCore.KVPair<System.Int32,TestGM_OD.Unit>> values0 = value.Adds as System.Collections.Generic.ICollection<GMCore.KVPair<System.Int32,TestGM_OD.Unit>>;
			writer.WriteCount(values0 == null ? -1 : values0.Count);
			if (values0 != null)
			{
				foreach (GMCore.KVPair<System.Int32,TestGM_OD.Unit> item in values0)
				{
					writer.WriteInt32(item.Key);
					writer.WriteObjectID(item.Value.ObjectID);
				}
			}
			System.Collections.Generic.ICollection<System.Int32> values1 = value.Removes as System.Collections.Generic.ICollection<System.Int32>;
			writer.WriteCount(values1 == null ? -1 : values1.Count);
			if (values1 != null)
			{
				foreach (System.Int32 item in values1)
				{
					writer.WriteInt32(item);
				}
			}
		}
		private static object ReadRpcParam_TestGM_OD_Interfaces_TestUnitsParam(GMCore.GameplaySaveReader reader)
		{
			TestGM_OD.Interfaces.TestUnitsParam result = default;
			int count2 = reader.ReadCount();
			System.Collections.Generic.List<GMCore.KVPair<System.Int32,TestGM_OD.Unit>> collection1 = null;
			if (count2 >= 0)
			{
				collection1 = new System.Collections.Generic.List<GMCore.KVPair<System.Int32,TestGM_OD.Unit>>();
				for (int i = 0; i < count2; i++)
				{
					GMCore.KVPair<System.Int32,TestGM_OD.Unit> value3 = default;
					System.Int32 value4 = reader.ReadInt32();
					value3.Key = value4;
					TestGM_OD.Unit value5 = default;
					value5.Machine = reader.Machine;
					value5.ObjectID = reader.ReadObjectID();
					value3.Value = value5;
					collection1.Add(value3);
				}
			}
			result.Adds = collection1;
			int count8 = reader.ReadCount();
			System.Collections.Generic.List<System.Int32> collection7 = null;
			if (count8 >= 0)
			{
				collection7 = new System.Collections.Generic.List<System.Int32>();
				for (int i = 0; i < count8; i++)
				{
					System.Int32 value9 = reader.ReadInt32();
					collection7.Add(value9);
				}
			}
			result.Removes = collection7;
			return result;
		}
		private static void WriteRpcResult_TestGM_OD_Interfaces_TestUnitsParam(GMCore.GameplaySaveWriter writer, object boxedValue)
		{
			TestGM_OD.Interfaces.TestUnitsParamOut value = (TestGM_OD.Interfaces.TestUnitsParamOut)boxedValue;
		}
		private static object ReadRpcResult_TestGM_OD_Interfaces_TestUnitsParam(GMCore.GameplaySaveReader reader)
		{
			TestGM_OD.Interfaces.TestUnitsParamOut result = default;
			return result;
		}
		private static GMCore.ODRpcExecutionResult ExecuteRpc_TestGM_OD_Interfaces_TestUnitsParam(GMCore.GameplayMachine machine, object boxedValue)
		{
			TestGM_OD.Interfaces.TestUnitsParam value = (TestGM_OD.Interfaces.TestUnitsParam)boxedValue;
			var result = machine.Execute(value);
			return new GMCore.ODRpcExecutionResult
			{
				Error = result.ErrorMessage.Error,
				Result = result.Result,
			};
		}
		private static void WriteRpcParam_TestGM_OD_Interfaces_CreateDeleteParam(GMCore.GameplaySaveWriter writer, object boxedValue)
		{
			TestGM_OD.Interfaces.CreateDeleteParam value = (TestGM_OD.Interfaces.CreateDeleteParam)boxedValue;
		}
		private static object ReadRpcParam_TestGM_OD_Interfaces_CreateDeleteParam(GMCore.GameplaySaveReader reader)
		{
			TestGM_OD.Interfaces.CreateDeleteParam result = default;
			return result;
		}
		private static void WriteRpcResult_TestGM_OD_Interfaces_CreateDeleteParam(GMCore.GameplaySaveWriter writer, object boxedValue)
		{
			TestGM_OD.Interfaces.CreateDeleteParamOut value = (TestGM_OD.Interfaces.CreateDeleteParamOut)boxedValue;
		}
		private static object ReadRpcResult_TestGM_OD_Interfaces_CreateDeleteParam(GMCore.GameplaySaveReader reader)
		{
			TestGM_OD.Interfaces.CreateDeleteParamOut result = default;
			return result;
		}
		private static GMCore.ODRpcExecutionResult ExecuteRpc_TestGM_OD_Interfaces_CreateDeleteParam(GMCore.GameplayMachine machine, object boxedValue)
		{
			TestGM_OD.Interfaces.CreateDeleteParam value = (TestGM_OD.Interfaces.CreateDeleteParam)boxedValue;
			var result = machine.Execute(value);
			return new GMCore.ODRpcExecutionResult
			{
				Error = result.ErrorMessage.Error,
				Result = result.Result,
			};
		}
	}
}
namespace TestGM_OD.Routines
{
	public partial struct DamageRoutineParam : GMCore.ICheckableRoutine
	{
		public TestGM_OD.Unit A;
		public TestGM_OD.Unit B;
		public TestGM_OD.GameAttribute Damage;
		public GMCore.ICheckableRoutine GetCallParam()
		{
			return DamageRoutineParam_Default.From(this);
		}
		public ODCore.EventError CanExecute(GMCore.GameplayMachine.GameplayMachineProxy machine)
		{
			return GetCallParam().CanExecute(machine);
		}
		public System.Collections.IEnumerator DoExecute(GMCore.GameplayMachine.GameplayMachineProxy machine)
		{
			return GetCallParam().DoExecute(machine);
		}
	}
	public partial struct DamageRoutineParam_Default : GMCore.ICheckableRoutine
	{
		public TestGM_OD.Unit A;
		public TestGM_OD.Unit B;
		public TestGM_OD.GameAttribute Damage;
		public static DamageRoutineParam_Default From(DamageRoutineParam param)
		{
			return new DamageRoutineParam_Default()
			{
				A = param.A,
				B = param.B,
				Damage = param.Damage,
			};
		}
	}
}
namespace TestGM_OD.Interfaces
{
	public partial struct DamageUnitParam : GMCore.ICheckableInterface<TestGM_OD.Interfaces.DamageUnitParamOut>
	{
		public TestGM_OD.Unit A;
		public TestGM_OD.Unit B;
		public TestGM_OD.GameAttribute Damage;
		public GMCore.ICheckableInterface<TestGM_OD.Interfaces.DamageUnitParamOut> GetCallParam()
		{
			if (A.IsA<TestGM_OD.Player>() && B.IsA<TestGM_OD.Monster>())
			{
				return DamageUnitParam_PlayerAttackMonster.From(this);
			}
			else if (A.IsA<TestGM_OD.Player>())
			{
				return DamageUnitParam_PlayerAttack.From(this);
			}
			return DamageUnitParam_Default.From(this);
		}
		public ODCore.EventError CanExecute(GMCore.GameplayMachine.GameplayMachineProxy machine)
		{
			return GetCallParam().CanExecute(machine);
		}
		public void DoExecute(GMCore.GameplayMachine.GameplayMachineProxy machine, ref TestGM_OD.Interfaces.DamageUnitParamOut outResult)
		{
			GetCallParam().DoExecute(machine, ref outResult);
		}
	}
	public partial struct DamageUnitParamOut
	{
	}
	public partial struct DamageUnitParam_PlayerAttackMonster : GMCore.ICheckableInterface<TestGM_OD.Interfaces.DamageUnitParamOut>
	{
		public TestGM_OD.Player A;
		public TestGM_OD.Monster B;
		public TestGM_OD.GameAttribute Damage;
		public static DamageUnitParam_PlayerAttackMonster From(DamageUnitParam param)
		{
			return new DamageUnitParam_PlayerAttackMonster()
			{
				A = param.A.Cast<TestGM_OD.Player>().Value,
				B = param.B.Cast<TestGM_OD.Monster>().Value,
				Damage = param.Damage,
			};
		}
	}
	public partial struct DamageUnitParam_PlayerAttack : GMCore.ICheckableInterface<TestGM_OD.Interfaces.DamageUnitParamOut>
	{
		public TestGM_OD.Player A;
		public TestGM_OD.Unit B;
		public TestGM_OD.GameAttribute Damage;
		public static DamageUnitParam_PlayerAttack From(DamageUnitParam param)
		{
			return new DamageUnitParam_PlayerAttack()
			{
				A = param.A.Cast<TestGM_OD.Player>().Value,
				B = param.B,
				Damage = param.Damage,
			};
		}
	}
	public partial struct DamageUnitParam_Default : GMCore.ICheckableInterface<TestGM_OD.Interfaces.DamageUnitParamOut>
	{
		public TestGM_OD.Unit A;
		public TestGM_OD.Unit B;
		public TestGM_OD.GameAttribute Damage;
		public static DamageUnitParam_Default From(DamageUnitParam param)
		{
			return new DamageUnitParam_Default()
			{
				A = param.A,
				B = param.B,
				Damage = param.Damage,
			};
		}
	}
}
namespace TestGM_OD.Interfaces
{
	public partial struct TriggerEventParam : GMCore.ICheckableInterface<TestGM_OD.Interfaces.TriggerEventParamOut>
	{
		public System.Int32 TriggerValue;
		public GMCore.ICheckableInterface<TestGM_OD.Interfaces.TriggerEventParamOut> GetCallParam()
		{
			return TriggerEventParam_Default.From(this);
		}
		public ODCore.EventError CanExecute(GMCore.GameplayMachine.GameplayMachineProxy machine)
		{
			return GetCallParam().CanExecute(machine);
		}
		public void DoExecute(GMCore.GameplayMachine.GameplayMachineProxy machine, ref TestGM_OD.Interfaces.TriggerEventParamOut outResult)
		{
			GetCallParam().DoExecute(machine, ref outResult);
		}
	}
	public partial struct TriggerEventParamOut
	{
	}
	public partial struct TriggerEventParam_Default : GMCore.ICheckableInterface<TestGM_OD.Interfaces.TriggerEventParamOut>
	{
		public System.Int32 TriggerValue;
		public static TriggerEventParam_Default From(TriggerEventParam param)
		{
			return new TriggerEventParam_Default()
			{
				TriggerValue = param.TriggerValue,
			};
		}
	}
}
namespace TestGM_OD.Interfaces
{
	public partial struct SetUnitHealthsParam : GMCore.ICheckableInterface<TestGM_OD.Interfaces.SetUnitHealthsParamOut>
	{
		public TestGM_OD.Unit Unit;
		public System.Collections.Generic.List<TestGM_OD.GameAttribute> Healths;
		public GMCore.ICheckableInterface<TestGM_OD.Interfaces.SetUnitHealthsParamOut> GetCallParam()
		{
			return SetUnitHealthsParam_Default.From(this);
		}
		public ODCore.EventError CanExecute(GMCore.GameplayMachine.GameplayMachineProxy machine)
		{
			return GetCallParam().CanExecute(machine);
		}
		public void DoExecute(GMCore.GameplayMachine.GameplayMachineProxy machine, ref TestGM_OD.Interfaces.SetUnitHealthsParamOut outResult)
		{
			GetCallParam().DoExecute(machine, ref outResult);
		}
	}
	public partial struct SetUnitHealthsParamOut
	{
	}
	public partial struct SetUnitHealthsParam_Default : GMCore.ICheckableInterface<TestGM_OD.Interfaces.SetUnitHealthsParamOut>
	{
		public TestGM_OD.Unit Unit;
		public System.Collections.Generic.List<TestGM_OD.GameAttribute> Healths;
		public static SetUnitHealthsParam_Default From(SetUnitHealthsParam param)
		{
			return new SetUnitHealthsParam_Default()
			{
				Unit = param.Unit,
				Healths = param.Healths,
			};
		}
	}
}
namespace TestGM_OD.Interfaces
{
	public partial struct CreateMonstersParam : GMCore.ICheckableInterface<TestGM_OD.Interfaces.CreateMonstersParamOut>
	{
		public System.Int32 Count;
		public GMCore.ICheckableInterface<TestGM_OD.Interfaces.CreateMonstersParamOut> GetCallParam()
		{
			return CreateMonstersParam_Default.From(this);
		}
		public ODCore.EventError CanExecute(GMCore.GameplayMachine.GameplayMachineProxy machine)
		{
			return GetCallParam().CanExecute(machine);
		}
		public void DoExecute(GMCore.GameplayMachine.GameplayMachineProxy machine, ref TestGM_OD.Interfaces.CreateMonstersParamOut outResult)
		{
			GetCallParam().DoExecute(machine, ref outResult);
		}
	}
	public partial struct CreateMonstersParamOut
	{
	}
	public partial struct CreateMonstersParam_Default : GMCore.ICheckableInterface<TestGM_OD.Interfaces.CreateMonstersParamOut>
	{
		public System.Int32 Count;
		public static CreateMonstersParam_Default From(CreateMonstersParam param)
		{
			return new CreateMonstersParam_Default()
			{
				Count = param.Count,
			};
		}
	}
}
namespace TestGM_OD.Interfaces
{
	public partial struct TestUnitsParam : GMCore.ICheckableInterface<TestGM_OD.Interfaces.TestUnitsParamOut>
	{
		public System.Collections.Generic.List<GMCore.KVPair<System.Int32,TestGM_OD.Unit>> Adds;
		public System.Collections.Generic.List<System.Int32> Removes;
		public GMCore.ICheckableInterface<TestGM_OD.Interfaces.TestUnitsParamOut> GetCallParam()
		{
			return TestUnitsParam_Default.From(this);
		}
		public ODCore.EventError CanExecute(GMCore.GameplayMachine.GameplayMachineProxy machine)
		{
			return GetCallParam().CanExecute(machine);
		}
		public void DoExecute(GMCore.GameplayMachine.GameplayMachineProxy machine, ref TestGM_OD.Interfaces.TestUnitsParamOut outResult)
		{
			GetCallParam().DoExecute(machine, ref outResult);
		}
	}
	public partial struct TestUnitsParamOut
	{
	}
	public partial struct TestUnitsParam_Default : GMCore.ICheckableInterface<TestGM_OD.Interfaces.TestUnitsParamOut>
	{
		public System.Collections.Generic.List<GMCore.KVPair<System.Int32,TestGM_OD.Unit>> Adds;
		public System.Collections.Generic.List<System.Int32> Removes;
		public static TestUnitsParam_Default From(TestUnitsParam param)
		{
			return new TestUnitsParam_Default()
			{
				Adds = param.Adds,
				Removes = param.Removes,
			};
		}
	}
}
namespace TestGM_OD.Interfaces
{
	public partial struct CreateDeleteParam : GMCore.ICheckableInterface<TestGM_OD.Interfaces.CreateDeleteParamOut>
	{
		public GMCore.ICheckableInterface<TestGM_OD.Interfaces.CreateDeleteParamOut> GetCallParam()
		{
			return CreateDeleteParam_Default.From(this);
		}
		public ODCore.EventError CanExecute(GMCore.GameplayMachine.GameplayMachineProxy machine)
		{
			return GetCallParam().CanExecute(machine);
		}
		public void DoExecute(GMCore.GameplayMachine.GameplayMachineProxy machine, ref TestGM_OD.Interfaces.CreateDeleteParamOut outResult)
		{
			GetCallParam().DoExecute(machine, ref outResult);
		}
	}
	public partial struct CreateDeleteParamOut
	{
	}
	public partial struct CreateDeleteParam_Default : GMCore.ICheckableInterface<TestGM_OD.Interfaces.CreateDeleteParamOut>
	{
		public static CreateDeleteParam_Default From(CreateDeleteParam param)
		{
			return new CreateDeleteParam_Default()
			{
			};
		}
	}
}
