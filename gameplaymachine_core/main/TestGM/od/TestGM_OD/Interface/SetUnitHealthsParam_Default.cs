namespace TestGM_OD.Interfaces
{
	public partial struct SetUnitHealthsParam_Default
	{
		public ODCore.EventError CanExecute(GMCore.GameplayMachine.GameplayMachineProxy machine)
		{
			return true;
		}
		public void DoExecute(GMCore.GameplayMachine.GameplayMachineProxy machine, ref TestGM_OD.Interfaces.SetUnitHealthsParamOut outResult)
		{
			foreach (var item in Healths)
			{
				if (item == 0)
				{
					Unit.Delete();
					return;
				}
				else
				{
                    Unit.Health = item;
                }
			}
		}
	}
}
