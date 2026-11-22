using Godot;

public class HardRockModifier : Modifier, IMapModifier
{
    public override string Name => "HardRock";
    public override Color Color => new(0xff7a45ff);
    public override bool Rankable => true;
    public override double ScoreMultiplier => 1.15;

    public void ModifyMap(Map map, Attempt attempt)
    {
        foreach (var note in map.Notes)
            note.HitWindow = 35;
    }

    public override void Activate(Attempt attempt)
    {
        base.Activate(attempt);
        attempt.ModsMultiplier *= ScoreMultiplier;
    }
}
