using System.Numerics;

namespace TougePlugin.Models;

public class Course
{
    public string? Name { get; set; }
    public Vector2[]? FinishLine { get; set; } // optional
    public List<SpawnPair> StartingSlots { get; set; } = [];
}
