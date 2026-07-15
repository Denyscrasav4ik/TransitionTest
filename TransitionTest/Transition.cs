using Godot;
using System.Collections.Generic;

public partial class Transition : Node2D
{
    public enum RippleMode
    {
        CenterOut,
        EdgesIn
    }

    [ExportCategory("Grid Configuration")]
    [Export] public int CellSize { get; set; } = 64;
    [Export] public float CellSpacing { get; set; } = 0f; // How tightly tiles are packed (negative = overlap, positive = gap)
    [Export] public Texture2D? CustomTexture { get; set; } // Customizable shape sprite

    [ExportCategory("Explosion & Physics")]
    [Export] public RippleMode Mode { get; set; } = RippleMode.CenterOut; // Toggle between radial modes
    [Export] public float WaveSpeed { get; set; } = 600f;       // Speed of the radial ripple propagation (pixels/sec)
    [Export] public float PushDistance { get; set; } = 400f;     // How far squares travel outward during explosion
    [Export] public float FadeDuration { get; set; } = 1.2f;     // Duration of the fade/color shift once triggered

    [ExportCategory("Visuals")]
    [Export] public Color TargetColor { get; set; } = Color.FromHtml("2596be"); // Customizable ending color
    [Export(PropertyHint.Range, "0,1")] public float TargetAlpha { get; set; } = 0.0f; // Customizable ending transparency

    [ExportCategory("Spin / Rotation")]
    [Export] public float MinSpin { get; set; } = 2.0f;          // Minimum total spin (radians)
    [Export] public float MaxSpin { get; set; } = 6.0f;          // Maximum total spin (radians)

    private Texture2D? _whiteTexture;
    private List<GridCell> _cells = new List<GridCell>();

    // State machine properties
    private bool _isAnimating = false;
    private bool _isExplodedState = false; // False = Grid form, True = Exploded form

    // Helper class to manage individual cell state
    private class GridCell
    {
        public Sprite2D? Sprite;
        public Vector2 StartPosition;          // Grid slot coordinate
        public Vector2 TargetExplodedPosition; // Far-out coordinate
        public float Delay;
        public float ActiveTime;
        public float TargetRotation;
        public bool HasFinished;
    }

    public override void _Ready()
    {
        // Only generate the default texture if a custom one isn't provided
        if (CustomTexture == null)
        {
            _whiteTexture = CreateWhiteTexture(CellSize);
        }
        GenerateGrid();
    }

    private Texture2D CreateWhiteTexture(int size)
    {
        Image image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        image.Fill(Colors.White);
        return ImageTexture.CreateFromImage(image);
    }

    private void GenerateGrid()
    {
        ClearGrid();

        Vector2 viewportSize = GetViewportRect().Size;

        // Calculate the physical step size of each cell including the spacing
        float step = CellSize + CellSpacing;

        // Safety check to prevent dividing by zero or negative infinite loops if packed too tightly
        if (step <= 0.1f) step = 0.1f;

        // Add +1 to columns and rows to ensure the screen edges remain fully covered when spacing is applied
        int cols = Mathf.CeilToInt(viewportSize.X / step) + 1;
        int rows = Mathf.CeilToInt(viewportSize.Y / step) + 1;

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                var sprite = new Sprite2D();
                sprite.Texture = CustomTexture != null ? CustomTexture : _whiteTexture;

                Vector2 pos = new Vector2(
                    c * step + CellSize / 2f,
                    r * step + CellSize / 2f
                );
                sprite.Position = pos;

                AddChild(sprite);

                _cells.Add(new GridCell
                {
                    Sprite = sprite,
                    StartPosition = pos,
                    TargetExplodedPosition = pos,
                    ActiveTime = 0f,
                    HasFinished = true
                });
            }
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouseEvent && mouseEvent.Pressed && mouseEvent.ButtonIndex == MouseButton.Left)
        {
            // Prevent interrupting a transition mid-flight
            if (_isAnimating) return;

            TriggerTransition(mouseEvent.Position);
        }
    }

    private void TriggerTransition(Vector2 clickPos)
    {
        _isAnimating = true;

        // Calculate the maximum possible distance to the corners of the screen for EdgesIn mode
        float maxDistance = 0f;
        if (Mode == RippleMode.EdgesIn)
        {
            Vector2 viewportSize = GetViewportRect().Size;
            Vector2[] corners = {
                Vector2.Zero,
                new Vector2(viewportSize.X, 0),
                new Vector2(0, viewportSize.Y),
                new Vector2(viewportSize.X, viewportSize.Y)
            };

            foreach (var corner in corners)
            {
                float dist = (corner - clickPos).Length();
                if (dist > maxDistance) maxDistance = dist;
            }
        }

        foreach (var cell in _cells)
        {
            if (!GodotObject.IsInstanceValid(cell.Sprite)) continue;

            cell.Sprite.Visible = true;
            cell.ActiveTime = 0f;
            cell.HasFinished = false;

            if (!_isExplodedState)
            {
                // --- PHASE 1: EXPLODE OUTWARD ---
                Vector2 offset = cell.StartPosition - clickPos;
                float distance = offset.Length();
                Vector2 direction = distance > 0.01f ? offset.Normalized() : Vector2.Right;

                // Pre-calculate where this block will land
                cell.TargetExplodedPosition = cell.StartPosition + direction * PushDistance;

                // Randomize total spin trajectory (and direction)
                float spinAmount = (float)GD.RandRange(MinSpin, MaxSpin);
                cell.TargetRotation = GD.Randf() > 0.5f ? spinAmount : -spinAmount;

                // Delay logic based on selected Mode
                if (Mode == RippleMode.CenterOut)
                {
                    // Ripple delay spreads outward from the click point
                    cell.Delay = distance / WaveSpeed;
                }
                else
                {
                    // Ripple delay begins at the edges and caves inward
                    cell.Delay = (maxDistance - distance) / WaveSpeed;
                }
            }
            else
            {
                // --- PHASE 2: ENCLOSE INWARD ---
                // For a highly dynamic feel, the return ripple originates from where the click occurred
                // relative to the floating outer blocks!
                float distance = (cell.TargetExplodedPosition - clickPos).Length();

                if (Mode == RippleMode.CenterOut)
                {
                    cell.Delay = distance / WaveSpeed;
                }
                else
                {
                    cell.Delay = (maxDistance - distance) / WaveSpeed;
                }
            }
        }
    }

    public override void _Process(double delta)
    {
        if (!_isAnimating) return;

        float dt = (float)delta;
        bool allFinished = true;

        foreach (var cell in _cells)
        {
            if (!GodotObject.IsInstanceValid(cell.Sprite)) continue;

            if (cell.HasFinished) continue;

            allFinished = false; // At least one cell is still moving

            // Process individual delay wave
            if (cell.Delay > 0)
            {
                cell.Delay -= dt;
                continue;
            }

            cell.ActiveTime += dt;
            float progress = Mathf.Clamp(cell.ActiveTime / FadeDuration, 0f, 1f);

            // Quadratic ease-in curve: starts slow, accelerates dramatically
            float easeProgress = progress * progress * progress;

            if (!_isExplodedState)
            {
                // Animating Outward
                cell.Sprite.Position = cell.StartPosition.Lerp(cell.TargetExplodedPosition, easeProgress);
                cell.Sprite.Rotation = Mathf.Lerp(0f, cell.TargetRotation, easeProgress);

                // Fade: White Opaque -> Target Color at Target Alpha
                Color col = Colors.White.Lerp(TargetColor, progress);
                col.A = Mathf.Lerp(1.0f, TargetAlpha, progress * progress); // Quadratic fade out
                cell.Sprite.Modulate = col;
            }
            else
            {
                // Animating Inward (Enclosing)
                cell.Sprite.Position = cell.TargetExplodedPosition.Lerp(cell.StartPosition, easeProgress);
                cell.Sprite.Rotation = Mathf.Lerp(cell.TargetRotation, 0f, easeProgress);

                // Fade: Target Color at Target Alpha -> White Opaque
                Color col = TargetColor.Lerp(Colors.White, progress);
                col.A = Mathf.Lerp(TargetAlpha, 1.0f, progress * progress); // Quadratic fade in
                cell.Sprite.Modulate = col;
            }

            // Mark cell as done once transition is complete
            if (progress >= 1.0f)
            {
                cell.HasFinished = true;

                // Optimization: Hide sprites that are fully exploded and invisible
                // Only hide if TargetAlpha is completely transparent
                if (!_isExplodedState && TargetAlpha <= 0.001f)
                {
                    cell.Sprite.Visible = false;
                }
            }
        }

        // Toggle state once every block has finished its sequence
        if (allFinished)
        {
            _isAnimating = false;
            _isExplodedState = !_isExplodedState;
        }
    }

    private void ClearGrid()
    {
        foreach (var cell in _cells)
        {
            if (GodotObject.IsInstanceValid(cell.Sprite))
            {
                cell.Sprite.QueueFree();
            }
        }
        _cells.Clear();
    }

    // Dynamic clean up when leaving the scene
    public override void _ExitTree()
    {
        ClearGrid();
    }
}
