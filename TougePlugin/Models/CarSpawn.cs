using System.Numerics;

namespace TougePlugin.Models;
public class CarSpawn
{
    public Vector3 Position { get; set; }
    public int Heading { get; set; }

    // For checking if the variables where set
    public bool PositionIsSet { get; set; } = false;
    public bool HeadingIsSet { get; set; } = false;
}
