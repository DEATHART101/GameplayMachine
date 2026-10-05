using GMCore;

namespace TestGM_OD;

public static class TestGameRoot
{
    public static Game GetGame(this GameplayMachine machine)
    {
        if (machine.HasRoot)
        {
            return machine.GetRoot().Cast<Game>() ??
                throw new InvalidOperationException("GameplayMachine root is not a Game");
        }

        Game game = machine.CreateGameplayObject<Game>();
        machine.SetRoot(game);
        return game;
    }

    public static Game GetGame(this GameplayMachine.GameplayMachineProxy machine)
    {
        return machine.GetRoot().Cast<Game>() ??
            throw new InvalidOperationException("GameplayMachine root is not a Game");
    }
}
