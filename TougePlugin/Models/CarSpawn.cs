using System.Numerics;

namespace TougePlugin.Models;
public readonly struct CarSpawn(Vector3 position, int heading)
{
    public Vector3 Position { get; } = position;
    public int Heading { get; } = heading;
}
