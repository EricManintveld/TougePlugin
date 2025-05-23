using System.Numerics;

namespace TougePlugin.Models;

public class Course
{
    public required CarSpawn Leader { get; set; }
    public required CarSpawn Follower { get; set; }
    public Vector2[]? FinishLine { get; set; } // optional
}
