namespace SnowDesk;

public sealed class Flake
{
    public float X;
    public float Y;
    public float Speed;
    public float Size;
    public float Phase;
    public float SwayAmp;
    public float SwayFreq;
    public float Alpha;
    public int Sprite;
}

public struct SnowPuff
{
    public float X;
    public float Y;
    public float Vx;
    public float Vy;
    public float Life;
    public float MaxLife;
    public float Size;
}

public sealed class DriftBand
{
    public string Id { get; private set; }
    public TaskbarBand Geometry { get; private set; }
    public float[] Heights { get; private set; }
    public float MaxHeight { get; private set; }
    public bool Plowing { get; internal set; }
    public float Travel { get; internal set; }
    public float ElapsedSincePlow { get; internal set; }
    public float Blink { get; internal set; }
    public List<SnowPuff> Puffs { get; } = new();

    float _mean;
    float _peak;
    bool _dirty = true;

    internal DriftBand(TaskbarBand geometry, float maxHeight)
    {
        Id = geometry.Id;
        Geometry = geometry;
        MaxHeight = maxHeight;
        Heights = new float[geometry.Length];
    }

    public float Mean
    {
        get
        {
            EnsureStats();
            return _mean;
        }
    }

    public float Peak
    {
        get
        {
            EnsureStats();
            return _peak;
        }
    }

    public float Fullness => MaxHeight <= 0.01f ? 0f : Mean / MaxHeight;

    internal void MarkDirty() => _dirty = true;

    internal void ApplyGeometry(TaskbarBand geometry, float maxHeight)
    {
        Geometry = geometry;
        Id = geometry.Id;
        MaxHeight = maxHeight;
        if (Heights.Length != geometry.Length)
        {
            var next = new float[geometry.Length];
            int copy = Math.Min(Heights.Length, next.Length);
            Array.Copy(Heights, next, copy);
            Heights = next;
        }

        for (int i = 0; i < Heights.Length; i++)
        {
            if (Heights[i] > maxHeight)
                Heights[i] = maxHeight;
        }

        _dirty = true;
    }

    void EnsureStats()
    {
        if (!_dirty)
            return;

        float sum = 0f;
        float peak = 0f;
        for (int i = 0; i < Heights.Length; i++)
        {
            float value = Heights[i];
            sum += value;
            if (value > peak)
                peak = value;
        }

        _mean = Heights.Length == 0 ? 0f : sum / Heights.Length;
        _peak = peak;
        _dirty = false;
    }
}

public sealed class SnowSimulation
{
    public const int DepositRadius = 6;
    public const float DepositSigma = 2.4f;
    public const int MaxPuffs = 72;
    public const int BladeClearExtra = 20;

    readonly List<Flake> _flakes = new();
    readonly List<DriftBand> _bands = new();
    readonly Random _random;

    IntensityProfile _profile = IntensityProfile.FromLevel(40);
    int _appliedLevel = -1;
    bool _appliedTaskbar = true;
    string _sceneKey = "";
    float _deposit = 0.2f;
    float _sideDeposit = 0.4f;
    bool _plowRequested;
    int _width = 1;
    int _height = 1;

    public SnowSimulation(int? seed = null)
    {
        _random = seed.HasValue ? new Random(seed.Value) : new Random();
    }

    public int Intensity { get; set; } = 40;
    public bool TaskbarSnow { get; set; } = true;
    public int PlowIntervalMinutes { get; set; } = 12;
    public float Time { get; private set; }
    public IntensityProfile Profile => _profile;
    public IReadOnlyList<Flake> Flakes => _flakes;
    public IReadOnlyList<DriftBand> Bands => _bands;
    internal int PlowsStarted { get; private set; }

    public void CallPlow() => _plowRequested = true;

    public void Update(float dt, Scene scene)
    {
        if (scene.Width <= 0 || scene.Height <= 0)
            return;
        if (dt <= 0f)
            return;
        if (dt > 0.1f)
            dt = 0.1f;

        Time += dt;
        _width = scene.Width;
        _height = scene.Height;
        EnsureProfile();
        SyncBands(scene);
        UpdateFlakes(dt, scene);
        UpdateBands(dt);
    }

    internal void StampSnow(float height)
    {
        foreach (DriftBand band in _bands)
        {
            float clamped = Math.Clamp(height, 0f, band.MaxHeight);
            Array.Fill(band.Heights, clamped);
            band.MarkDirty();
        }
    }

    void EnsureProfile()
    {
        int level = Math.Clamp(Intensity, 1, 100);
        if (_appliedLevel == level && _appliedTaskbar == TaskbarSnow)
            return;

        _profile = IntensityProfile.FromLevel(level);
        _appliedLevel = level;
        _appliedTaskbar = TaskbarSnow;
        _sceneKey = "";

        while (_flakes.Count < _profile.TargetFlakes)
            _flakes.Add(CreateFlake(scatter: true));
        if (_flakes.Count > _profile.TargetFlakes)
            _flakes.RemoveRange(_profile.TargetFlakes, _flakes.Count - _profile.TargetFlakes);

        foreach (Flake flake in _flakes)
            AssignAppearance(flake);

        foreach (DriftBand band in _bands)
            band.ApplyGeometry(band.Geometry, _profile.MaxDrift);
    }

    void SyncBands(Scene scene)
    {
        if (_sceneKey == scene.Key && _bands.Count == scene.Taskbars.Count)
            return;

        var next = new List<DriftBand>(scene.Taskbars.Count);
        foreach (TaskbarBand geometry in scene.Taskbars)
        {
            DriftBand? existing = null;
            foreach (DriftBand band in _bands)
            {
                if (band.Id == geometry.Id)
                {
                    existing = band;
                    break;
                }
            }

            if (existing == null)
                existing = new DriftBand(geometry, _profile.MaxDrift);
            else
                existing.ApplyGeometry(geometry, _profile.MaxDrift);

            next.Add(existing);
        }

        _bands.Clear();
        _bands.AddRange(next);
        _sceneKey = scene.Key;
        RecomputeDeposit(scene);
    }

    void RecomputeDeposit(Scene scene)
    {
        // Appearance assignment biases speed upward (mean weight ~0.33/0.62). Match that
        // so a blizzard still takes about FillSeconds to cap the drift, not less.
        float expectedSpeed = _profile.MinFall * 0.33f + _profile.MaxFall * 0.62f;
        float flux = _profile.TargetFlakes * expectedSpeed / Math.Max(1f, scene.Height);
        float sumW = GaussianSum();
        float cover = 0f;
        float lengthSum = 0f;
        int count = 0;
        foreach (TaskbarBand bar in scene.Taskbars)
        {
            if (bar.Edge is not (TaskbarEdge.Top or TaskbarEdge.Bottom))
                continue;
            cover += bar.Width / (float)Math.Max(1, scene.Width);
            lengthSum += bar.Length;
            count++;
        }

        if (count == 0 || flux <= 0.001f)
        {
            _deposit = 0.15f;
        }
        else
        {
            cover = Math.Clamp(cover, 0.05f, 1.5f);
            float averageLength = lengthSum / count;
            float perBar = flux * Math.Min(cover, 1f) / count;
            float need = _profile.MaxDrift / Math.Max(1f, _profile.FillSeconds);
            _deposit = need * averageLength / (perBar * sumW);
        }

        _deposit = Math.Clamp(_deposit, 0.004f, 8f);
        _sideDeposit = Math.Clamp(_deposit * 6f, 0.01f, 5f);
    }

    void UpdateFlakes(float dt, Scene scene)
    {
        float wind = MathF.Sin(Time * 0.17f) * _profile.Sway * 0.40f
                   + MathF.Sin(Time * 0.053f) * _profile.Sway * 0.22f;
        float width = scene.Width;
        float height = scene.Height;

        for (int i = 0; i < _flakes.Count; i++)
        {
            Flake flake = _flakes[i];
            float sway = MathF.Sin(Time * flake.SwayFreq + flake.Phase) * flake.SwayAmp;
            flake.X += (wind * 0.30f + sway) * dt;
            flake.Y += flake.Speed * dt;
            if (flake.X < 0f)
                flake.X += width;
            else if (flake.X >= width)
                flake.X -= width;

            bool landed = false;
            if (TaskbarSnow)
            {
                for (int b = 0; b < _bands.Count; b++)
                {
                    if (TryLand(_bands[b], flake))
                    {
                        landed = true;
                        break;
                    }
                }
            }

            if (landed || flake.Y > height + flake.Size)
                Respawn(flake, scene, depositOnTop: !landed);
        }
    }

    bool TryLand(DriftBand band, Flake flake)
    {
        TaskbarBand g = band.Geometry;
        switch (g.Edge)
        {
            case TaskbarEdge.Bottom:
            {
                if (flake.X < g.X || flake.X >= g.X + g.Width)
                    return false;
                int column = Math.Clamp((int)(flake.X - g.X), 0, band.Heights.Length - 1);
                float surface = g.Y - band.Heights[column];
                if (flake.Y < surface)
                    return false;
                Deposit(band, column, _deposit);
                return true;
            }
            case TaskbarEdge.Left:
            {
                if (flake.Y < g.Y || flake.Y >= g.Y + g.Height)
                    return false;
                int column = Math.Clamp((int)(flake.Y - g.Y), 0, band.Heights.Length - 1);
                float surface = g.X + g.Width + band.Heights[column];
                if (flake.X > surface || flake.X < g.X - 2f)
                    return false;
                Deposit(band, column, _sideDeposit);
                return true;
            }
            case TaskbarEdge.Right:
            {
                if (flake.Y < g.Y || flake.Y >= g.Y + g.Height)
                    return false;
                int column = Math.Clamp((int)(flake.Y - g.Y), 0, band.Heights.Length - 1);
                float surface = g.X - band.Heights[column];
                if (flake.X < surface || flake.X > g.X + g.Width + 2f)
                    return false;
                Deposit(band, column, _sideDeposit);
                return true;
            }
            default:
                return false;
        }
    }

    void Respawn(Flake flake, Scene scene, bool depositOnTop)
    {
        if (depositOnTop && TaskbarSnow)
        {
            for (int i = 0; i < _bands.Count; i++)
            {
                DriftBand band = _bands[i];
                if (band.Geometry.Edge != TaskbarEdge.Top)
                    continue;
                TaskbarBand g = band.Geometry;
                if (flake.X < g.X || flake.X >= g.X + g.Width)
                    continue;
                int column = Math.Clamp((int)(flake.X - g.X), 0, band.Heights.Length - 1);
                Deposit(band, column, _deposit);
                break;
            }
        }

        flake.X = _random.NextSingle() * scene.Width;
        flake.Y = -flake.Size - _random.NextSingle() * 36f;
    }

    void Deposit(DriftBand band, int column, float amount)
    {
        float[] heights = band.Heights;
        float inv = 1f / (2f * DepositSigma * DepositSigma);
        float max = band.MaxHeight;
        for (int i = -DepositRadius; i <= DepositRadius; i++)
        {
            int index = column + i;
            if ((uint)index >= (uint)heights.Length)
                continue;
            float weight = MathF.Exp(-(i * i) * inv);
            float value = heights[index] + amount * weight;
            heights[index] = value > max ? max : value;
        }

        band.MarkDirty();
    }

    void UpdateBands(float dt)
    {
        bool requested = _plowRequested && _bands.Count > 0;
        float interval = Math.Clamp(PlowIntervalMinutes, 1, 120) * 60f;

        for (int i = 0; i < _bands.Count; i++)
        {
            DriftBand band = _bands[i];
            band.Blink += dt;
            TickPuffs(band, dt);
            if (!TaskbarSnow)
                continue;

            if (band.Plowing)
            {
                float speed = MathF.Max(40f, band.Geometry.Length / 14.4f);
                float previous = band.Travel;
                band.Travel += speed * dt;
                Sweep(band, previous, band.Travel);
                if (band.Travel > band.Geometry.Length + 56f)
                {
                    band.Plowing = false;
                    band.ElapsedSincePlow = 0f;
                }

                continue;
            }

            band.ElapsedSincePlow += dt;
            bool full = band.Fullness >= 0.92f || band.Peak >= band.MaxHeight * 0.98f;
            bool due = band.ElapsedSincePlow >= interval;
            bool called = requested;
            if (called || (full && band.Peak > 1.5f) || (due && band.Peak > 3f))
                StartPlow(band);
        }

        if (requested)
            _plowRequested = false;
    }

    void StartPlow(DriftBand band)
    {
        if (band.Plowing)
            return;
        band.Plowing = true;
        band.Travel = -36f;
        band.Puffs.Clear();
        PlowsStarted++;
    }

    void Sweep(DriftBand band, float from, float to)
    {
        float[] heights = band.Heights;
        int start = Math.Clamp((int)MathF.Floor(from) - BladeClearExtra, 0, heights.Length);
        int end = Math.Clamp((int)MathF.Ceiling(to), 0, heights.Length);
        for (int i = start; i < end; i++)
        {
            if (heights[i] > 1.1f && (i & 3) == 0)
                SpawnPuff(band, i, heights[i]);
            heights[i] = 0f;
        }

        if (end > start)
            band.MarkDirty();
    }

    void SpawnPuff(DriftBand band, int column, float height)
    {
        if (band.Puffs.Count >= MaxPuffs)
            return;

        TaskbarBand g = band.Geometry;
        float x;
        float y;
        float vx;
        float vy;
        switch (g.Edge)
        {
            case TaskbarEdge.Bottom:
                x = g.X + column;
                y = g.Y - height;
                vx = 20f + _random.NextSingle() * 50f;
                vy = -30f - _random.NextSingle() * 50f;
                break;
            case TaskbarEdge.Top:
                x = g.X + column;
                y = g.Y + g.Height + height;
                vx = 20f + _random.NextSingle() * 50f;
                vy = 20f + _random.NextSingle() * 40f;
                break;
            case TaskbarEdge.Left:
                x = g.X + g.Width + height;
                y = g.Y + column;
                vx = 16f + _random.NextSingle() * 36f;
                vy = 20f + _random.NextSingle() * 40f;
                break;
            default:
                x = g.X - height;
                y = g.Y + column;
                vx = -16f - _random.NextSingle() * 36f;
                vy = 20f + _random.NextSingle() * 40f;
                break;
        }

        float life = 0.35f + _random.NextSingle() * 0.45f;
        band.Puffs.Add(new SnowPuff
        {
            X = x,
            Y = y,
            Vx = vx,
            Vy = vy,
            Life = life,
            MaxLife = life,
            Size = 4f + _random.NextSingle() * 6f,
        });
    }

    void TickPuffs(DriftBand band, float dt)
    {
        List<SnowPuff> puffs = band.Puffs;
        for (int i = puffs.Count - 1; i >= 0; i--)
        {
            SnowPuff puff = puffs[i];
            puff.Life -= dt;
            if (puff.Life <= 0f)
            {
                puffs.RemoveAt(i);
                continue;
            }

            puff.X += puff.Vx * dt;
            puff.Y += puff.Vy * dt;
            puff.Vy -= 18f * dt;
            puffs[i] = puff;
        }
    }

    Flake CreateFlake(bool scatter)
    {
        var flake = new Flake();
        AssignAppearance(flake);
        flake.X = _random.NextSingle() * _width;
        flake.Y = scatter ? _random.NextSingle() * _height : -flake.Size;
        return flake;
    }

    void AssignAppearance(Flake flake)
    {
        float biased = _random.NextSingle();
        biased *= biased;
        flake.Size = _profile.MinSize + (_profile.MaxSize - _profile.MinSize) * biased;
        float speedT = 0.30f + 0.70f * _random.NextSingle();
        flake.Speed = _profile.MinFall + (_profile.MaxFall - _profile.MinFall) * speedT;
        float sizeNorm = flake.Size / MathF.Max(0.1f, _profile.MaxSize);
        flake.Speed *= 0.82f + 0.40f * sizeNorm;
        flake.Phase = _random.NextSingle() * MathF.PI * 2f;
        flake.SwayAmp = _profile.Sway * (0.20f + 0.80f * _random.NextSingle());
        flake.SwayFreq = 0.55f + _random.NextSingle() * 1.5f;
        flake.Alpha = 0.50f + _random.NextSingle() * 0.50f;
        flake.Sprite = FlakeSprites.Nearest(flake.Size);
    }

    static float GaussianSum()
    {
        float inv = 1f / (2f * DepositSigma * DepositSigma);
        float sum = 0f;
        for (int i = -DepositRadius; i <= DepositRadius; i++)
            sum += MathF.Exp(-(i * i) * inv);
        return sum;
    }
}
