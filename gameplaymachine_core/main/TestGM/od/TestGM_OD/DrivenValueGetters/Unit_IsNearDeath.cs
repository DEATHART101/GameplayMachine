namespace TestGM_OD
{
	public static partial class DrivenValueImplementations
	{
		public static System.Boolean Get_Unit_IsNearDeath(TestGM_OD.GameAttribute health)
		{
			return health <= 0;
		}
	}
}
