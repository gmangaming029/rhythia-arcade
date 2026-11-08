using System.Collections.Generic;
using Godot;
using Util;

public partial class HudManager : Node3D
{
    [Export]
    public Runner Runner;

    private List<IUIComponent> components = [];
    private readonly List<Sprite3D> modifierIcons = [];
    private MeshInstance3D grid;
    private Sprite3D modifierTemplate;
    private Vector3 defaultPosition;
    private Vector3 defaultGridPosition;
    private bool parallaxPositionsInitialized;

    public void Init()
    {
        Runner ??= GetParent<Runner>();
        components = findAllComponents(this);

        grid ??= GetNode<MeshInstance3D>("Grid");
        if (!parallaxPositionsInitialized)
        {
            defaultPosition = Position;
            defaultGridPosition = grid.Position;
            parallaxPositionsInitialized = true;
        }
        Position = defaultPosition;
        grid.Position = defaultGridPosition;

        modifierTemplate ??= GetNode<Sprite3D>("TemplateModifier");
        modifierTemplate.Visible = false;

        foreach (var icon in modifierIcons)
        {
            icon.QueueFree();
        }
        modifierIcons.Clear();

        foreach (var component in components)
        {
            component.Runner = Runner;
            component.Init();
        }
    }

    public void DisplayModifier(Modifier mod)
    {
        Sprite3D icon = (Sprite3D)modifierTemplate.Duplicate();
        icon.Texture = Misc.GetModIcon(mod.Name);
        icon.Visible = true;
        modifierTemplate.GetParent().AddChild(icon);
        modifierIcons.Add(icon);
        positionModifiers();
    }

    private void positionModifiers()
    {
        for (int i = 0; i < modifierIcons.Count; i++)
        {
            Vector3 pos = modifierTemplate.Position;
            pos.X = (i - (modifierIcons.Count - 1) / 2f) * 0.75f;
            modifierIcons[i].Position = pos;
        }
    }

    public override void _Process(double delta)
    {
        if (Runner?.Attempt == null)
            return;

        var attempt = Runner.Attempt;
        Vector3 cursorOffset = new(attempt.CursorPosition.X, attempt.CursorPosition.Y, 0);
        Vector3 worldParallaxOffset =
            (Runner.Camera.GlobalBasis.X * cursorOffset.X + Runner.Camera.GlobalBasis.Y * cursorOffset.Y)
            * (float)(double)attempt.Settings.HUDParallax;
        Vector3 parallaxOffset = Runner.GlobalBasis.Inverse() * worldParallaxOffset;
        Position = defaultPosition + parallaxOffset;
        grid.Position = defaultGridPosition - parallaxOffset;

        foreach (var component in components)
        {
            component.Process(delta, attempt);
        }
    }

    private static List<IUIComponent> findAllComponents(Node root)
    {
        List<IUIComponent> comps = [];

        foreach (Node child in root.GetChildren())
        {
            if (child is IUIComponent component)
                comps.Add(component);

            comps.AddRange(findAllComponents(child));
        }

        return comps;
    }
}
