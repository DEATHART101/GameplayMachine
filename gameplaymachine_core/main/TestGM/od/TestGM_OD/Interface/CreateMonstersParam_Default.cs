namespace TestGM_OD.Interfaces
{
	public partial struct CreateMonstersParam_Default
	{
		public ODCore.EventError CanExecute(GMCore.GameplayMachine.GameplayMachineProxy machine)
		{
			return true;
		}
		public void DoExecute(GMCore.GameplayMachine.GameplayMachineProxy machine, ref TestGM_OD.Interfaces.CreateMonstersParamOut outResult)
		{
			for (int i = 0; i < Count; i++)
			{
				machine.GetGame().Units.Add(machine.CreateGameplayObject<Monster>());
			}
		}
	}
}
