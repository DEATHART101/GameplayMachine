using TestGM_OD.Interfaces;

namespace TestGM_OD.Routines
{
	public partial struct DamageRoutineParam_Default
	{
		public ODCore.EventError CanExecute(GMCore.GameplayMachine.GameplayMachineProxy machine)
		{
			return true;
		}
		public System.Collections.IEnumerator DoExecute(GMCore.GameplayMachine.GameplayMachineProxy machine)
		{
			yield return machine.RoutineExecute(new DamageUnitParam()
			{
				A = A,
				B = B,
			});
            yield return machine.RoutineExecute(new DamageUnitParam()
            {
                A = B,
                B = A,
            });
        }
	}
}
