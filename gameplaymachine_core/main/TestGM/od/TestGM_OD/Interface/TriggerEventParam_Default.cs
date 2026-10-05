namespace TestGM_OD.Interfaces
{
	public partial struct TriggerEventParam_Default
	{
		public ODCore.EventError CanExecute(GMCore.GameplayMachine.GameplayMachineProxy machine)
		{
			return true;
		}
		public void DoExecute(GMCore.GameplayMachine.GameplayMachineProxy machine, ref TestGM_OD.Interfaces.TriggerEventParamOut outResult)
		{
			object evt = new TestEventParam()
			{
				TestValue = TriggerValue,
            };

			machine.BroadCastMachineEvent(evt);
		}
	}
}
