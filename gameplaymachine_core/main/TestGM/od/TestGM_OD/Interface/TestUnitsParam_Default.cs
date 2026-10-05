namespace TestGM_OD.Interfaces
{
	public partial struct TestUnitsParam_Default
	{
		public ODCore.EventError CanExecute(GMCore.GameplayMachine.GameplayMachineProxy machine)
		{
			return true;
		}
		public void DoExecute(GMCore.GameplayMachine.GameplayMachineProxy machine, ref TestGM_OD.Interfaces.TestUnitsParamOut outResult)
		{
            var units = machine.GetGame().IDUnits;

            if (Adds != null)
			{
                foreach (var item in Adds)
                {
                    units[item.Key] = item.Value;
                }
            }

            if (Removes != null)
            {
                foreach (var item in Removes)
                {
                    units.Remove(item);
                }
            }
        }
	}
}
