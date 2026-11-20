namespace SnowDesk;

public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

/// <summary>
/// Pure geometry for the taskbar drift: which edge a bar sits on, where an
/// auto-hidden bar collapses, and how screen pixels become overlay-local ones.
/// </summary>
public static class TaskbarLayout
{
    public static TaskbarEdge GuessEdge(PixelRect window, PixelRect monitor)
    {
        if (window.Width >= window.Height)
        {
            int toTop = Math.Abs(window.Top - monitor.Top);
            int toBottom = Math.Abs(monitor.Bottom - window.Bottom);
            return toTop < toBottom ? TaskbarEdge.Top : TaskbarEdge.Bottom;
        }

        int toLeft = Math.Abs(window.Left - monitor.Left);
        int toRight = Math.Abs(monitor.Right - window.Right);
        return toLeft < toRight ? TaskbarEdge.Left : TaskbarEdge.Right;
    }

    public static PixelRect PinToEdge(PixelRect monitor, TaskbarEdge edge)
    {
        return edge switch
        {
            TaskbarEdge.Top => new PixelRect(monitor.Left, monitor.Top, monitor.Right, monitor.Top),
            TaskbarEdge.Left => new PixelRect(monitor.Left, monitor.Top, monitor.Left, monitor.Bottom),
            TaskbarEdge.Right => new PixelRect(monitor.Right, monitor.Top, monitor.Right, monitor.Bottom),
            _ => new PixelRect(monitor.Left, monitor.Bottom, monitor.Right, monitor.Bottom),
        };
    }

    public static TaskbarBand ToBand(PixelRect rect, TaskbarEdge edge, bool autoHide, int originX, int originY, string name)
    {
        int x = rect.Left - originX;
        int y = rect.Top - originY;
        int width = rect.Width;
        int height = rect.Height;
        bool horizontal = edge is TaskbarEdge.Top or TaskbarEdge.Bottom;
        if (horizontal)
        {
            if (width < 1)
                width = 1;
            if (height < 0)
                height = 0;
        }
        else
        {
            if (height < 1)
                height = 1;
            if (width < 0)
                width = 0;
        }

        return new TaskbarBand
        {
            Id = $"{name}:{edge}:{x}:{y}:{width}:{height}:{autoHide}",
            Edge = edge,
            X = x,
            Y = y,
            Width = width,
            Height = height,
            AutoHide = autoHide,
        };
    }
}
