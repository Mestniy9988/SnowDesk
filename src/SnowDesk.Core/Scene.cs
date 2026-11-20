namespace SnowDesk;

public enum TaskbarEdge
{
    Left = 0,
    Top = 1,
    Right = 2,
    Bottom = 3,
}

public sealed class TaskbarBand
{
    public required string Id { get; init; }
    public TaskbarEdge Edge { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public bool AutoHide { get; init; }

    public bool Horizontal => Edge is TaskbarEdge.Top or TaskbarEdge.Bottom;

    public int Length => Math.Max(1, Horizontal ? Width : Height);
}

public sealed class Scene
{
    public int OriginX { get; init; }
    public int OriginY { get; init; }
    public int Width { get; init; } = 1;
    public int Height { get; init; } = 1;
    public IReadOnlyList<TaskbarBand> Taskbars { get; init; } = Array.Empty<TaskbarBand>();

    public string Key
    {
        get
        {
            var parts = new string[Taskbars.Count];
            for (int i = 0; i < Taskbars.Count; i++)
                parts[i] = Taskbars[i].Id;
            return $"{OriginX},{OriginY},{Width}x{Height}|{string.Join(";", parts)}";
        }
    }
}
