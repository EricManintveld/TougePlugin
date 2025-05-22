using System.Numerics;

namespace TougePlugin;

public class Course
{
    public required Dictionary<string, Vector3> Leader { get; set; }
    public required Dictionary<string, Vector3> Follower { get; set; }
    public Vector2[]? FinishLine { get; set; } // optional
}
