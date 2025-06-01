using Silk.NET.Maths;

namespace TheAdventure.Models;

public class PlayerObject : RenderableGameObject
{
    private const int _speed = 128;
    public int Lives { get; private set; } = 3;

    public void LoseLife()
    {
        Lives--;
        if (Lives > 0)
        {
            Console.WriteLine($"You have {Lives} lives left.");
        }

        if (Lives <= 0)
        {
            GameOver();
        }
    }

    public enum PlayerStateDirection
    {
        None = 0, Down, Up, Left, Right,
    }

    public enum PlayerState
    {
        None = 0, Idle, Move, Attack, GameOver
    }

    public (PlayerState State, PlayerStateDirection Direction) State { get; private set; }

    public PlayerObject(SpriteSheet spriteSheet, int x, int y) : base(spriteSheet, (x, y))
    {
        SetState(PlayerState.Idle, PlayerStateDirection.Down);
    }

    public void SetState(PlayerState state) => SetState(state, State.Direction);

    public void SetState(PlayerState state, PlayerStateDirection direction)
    {
        if (State.State == PlayerState.GameOver || (State.State == state && State.Direction == direction))
            return;

        if (state == PlayerState.None && direction == PlayerStateDirection.None)
            SpriteSheet.ActivateAnimation(null);
        else if (state == PlayerState.GameOver)
            SpriteSheet.ActivateAnimation(Enum.GetName(state));
        else
            SpriteSheet.ActivateAnimation($"{Enum.GetName(state)}{Enum.GetName(direction)}");

        State = (state, direction);
    }

    public void GameOver() => SetState(PlayerState.GameOver, PlayerStateDirection.None);

    public void Attack()
    {
        if (State.State != PlayerState.GameOver)
            SetState(PlayerState.Attack, State.Direction);
    }

    public void UpdatePosition(double up, double down, double left, double right, int width, int height, double time)
    {
        if (State.State == PlayerState.GameOver) return;

        var pixelsToMove = _speed * (time / 1000.0);
        var x = Position.X + (int)(right * pixelsToMove) - (int)(left * pixelsToMove);
        var y = Position.Y + (int)(down * pixelsToMove) - (int)(up * pixelsToMove);

        var newState = (x == Position.X && y == Position.Y) ?
            (State.State == PlayerState.Attack && SpriteSheet.AnimationFinished ? PlayerState.Idle : PlayerState.Idle)
            : PlayerState.Move;

        var newDirection = State.Direction;
        if (y < Position.Y) newDirection = PlayerStateDirection.Up;
        else if (y > Position.Y) newDirection = PlayerStateDirection.Down;
        else if (x < Position.X) newDirection = PlayerStateDirection.Left;
        else if (x > Position.X) newDirection = PlayerStateDirection.Right;

        if (newState != State.State || newDirection != State.Direction)
            SetState(newState, newDirection);

        Position = (x, y);
    }
}

