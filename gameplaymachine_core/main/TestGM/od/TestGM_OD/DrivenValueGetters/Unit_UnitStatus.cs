namespace TestGM_OD
{
	public static partial class DrivenValueImplementations
	{
		public static System.String Get_Unit_UnitStatus(TestGM_OD.GameAttribute attack, System.Boolean isNearDeath)
		{
			return $"Unit attack:{attack}, nearDeath:{isNearDeath}";
		}
	}
}
