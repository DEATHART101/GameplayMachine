namespace TestGM_OD.Interfaces
{
	public partial struct DamageUnitParam_PlayerAttack
	{
		public ODCore.EventError CanExecute(GMCore.GameplayMachine.GameplayMachineProxy machine)
		{
            return true;
        }
		public void DoExecute(GMCore.GameplayMachine.GameplayMachineProxy machine, ref TestGM_OD.Interfaces.DamageUnitParamOut outResult)
		{
			B.Health = B.Health - A.PlayerSkill;
		}
	}
}
