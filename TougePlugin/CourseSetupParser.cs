using System.Numerics;
using System.Text.RegularExpressions;
using Serilog;

namespace TougePlugin;

public static partial class CourseSetupParser
{
    public static Course[] Parse(string filePath, string trackName, bool useTrackFinish)
    {
        var lines = File.ReadAllLines(filePath);
        var finishLines = new Dictionary<string, Vector2[]>(); // key = finish_line name
        var courseSlots = new List<Course>();

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();

            // Parse finish line block
            var finishMatch = FinishLineRegex().Match(line);
            if (finishMatch.Success)
            {
                if (i + 2 >= lines.Length)
                    throw new FormatException($"Incomplete finish line block at line {i + 1}");

                var point1 = ParseVector2From3D(lines[++i], "point_1");
                var point2 = ParseVector2From3D(lines[++i], "point_2");

                string key = $"finish_{finishMatch.Groups[1].Value}_{finishMatch.Groups[2].Value}";
                finishLines[key] = [point1, point2];
                continue;
            }

            // Parse track block
            var match = StartingAreaRegex().Match(line);
            if (match.Success)
            {
                string name = match.Groups[1].Value;
                if (!name.Equals(trackName, StringComparison.OrdinalIgnoreCase))
                {
                    Log.Debug($"Ignoring because name does not match. {trackName} != {name}");

                    // Always skip 5 lines if there's a 'finish_line' after the track block
                    if (i + 5 < lines.Length && lines[i + 5].Trim().StartsWith("finish_line"))
                        i += 5;
                    else
                        i += 4;

                    continue;
                }

                if (i + 4 >= lines.Length)
                    throw new FormatException($"Incomplete block at line {i + 1}");

                var slot1 = ParseSlot(lines[i + 1], lines[i + 2], "leader_pos", "leader_heading", i + 1);
                var slot2 = ParseSlot(lines[i + 3], lines[i + 4], "chaser_pos", "chaser_heading", i + 3);

                Vector2[]? finishLine = null;

                if (i + 5 < lines.Length && lines[i + 5].Trim().StartsWith("finish_line"))
                {
                    var finishLineLine = lines[i + 5].Trim();

                    if (!useTrackFinish)
                    {
                        var finishRefMatch = Regex.Match(finishLineLine, @"^finish_line\s*=\s*(\S+)$");
                        if (!finishRefMatch.Success)
                            throw new FormatException($"Invalid finish_line reference at line {i + 6}");

                        string finishKey = finishRefMatch.Groups[1].Value;
                        if (!finishLines.TryGetValue(finishKey, out finishLine))
                            throw new KeyNotFoundException($"Finish line '{finishKey}' not found (referenced at line {i + 6})");
                    }

                    i += 5; // Always skip the line, even if ignored
                }
                else
                {
                    i += 4;
                }

                courseSlots.Add(new Course
                {
                    Leader = slot1,
                    Follower = slot2,
                    FinishLine = finishLine
                });

                continue;
            }

            // Malformed headers or unexpected lines
            if (MIssingSuffixRegex().IsMatch(line))
                throw new FormatException($"Malformed block header at line {i + 1}: '{line}'. Did you forget to add a numeric suffix like '_1'?");
            if (!string.IsNullOrWhiteSpace(line) && !line.StartsWith('#'))
                throw new FormatException($"Unexpected line outside of a block at line {i + 1}: '{line}'");
        }

        // Validation if finish lines are required
        if (!useTrackFinish)
        {
            if (finishLines.Count == 0)
                throw new InvalidOperationException("No finish lines defined, but 'useTrackFinish' is false.");

            var missingFinish = courseSlots.FirstOrDefault(c => c.FinishLine == null);
            if (missingFinish != null)
                throw new InvalidOperationException("At least one course is missing a finish line, but 'useTrackFinish' is false.");
        }

        return courseSlots.ToArray();
    }

    private static Vector2 ParseVector2From3D(string line, string expectedKey)
    {
        var match = Regex.Match(line.Trim(), $@"^{expectedKey}\s*=\s*(-?\d+\.?\d*),\s*(-?\d+\.?\d*),\s*(-?\d+\.?\d*)$");
        if (!match.Success)
            throw new FormatException($"Expected '{expectedKey} = x, y, z', got: '{line}'");

        return new Vector2(
            float.Parse(match.Groups[1].Value),
            float.Parse(match.Groups[3].Value)
        );
    }

    private static Dictionary<string, Vector3> ParseSlot(string posLine, string headingLine, string expectedPosKey, string expectedHeadingKey, int baseLine)
    {
        var posMatch = Regex.Match(posLine.Trim(), $@"^{expectedPosKey}\s*=\s*(-?\d+\.?\d*),\s*(-?\d+\.?\d*),\s*(-?\d+\.?\d*)$");
        var headingMatch = Regex.Match(headingLine.Trim(), $@"^{expectedHeadingKey}\s*=\s*(-?\d+\.?\d*)$");

        if (!posMatch.Success)
            throw new FormatException($"Expected '{expectedPosKey} = x, y, z' at line {baseLine + 1}, got: '{posLine}'");

        if (!headingMatch.Success)
            throw new FormatException($"Expected '{expectedHeadingKey} = float' at line {baseLine + 2}, got: '{headingLine}'");

        float x = float.Parse(posMatch.Groups[1].Value);
        float y = float.Parse(posMatch.Groups[2].Value);
        float z = float.Parse(posMatch.Groups[3].Value);
        float headingDeg = 64f + float.Parse(headingMatch.Groups[1].Value); // Add the magic 64. Still don't know why.
        float headingRad = headingDeg * MathF.PI / 180f;

        Vector3 direction = new(MathF.Sin(headingRad), 0f, MathF.Cos(headingRad));

        return new Dictionary<string, Vector3>
        {
            ["Position"] = new Vector3(x, y, z),
            ["Direction"] = direction
        };
    }

    [GeneratedRegex(@"^\[finish_(.+?)_(\d+)\]$")]
    private static partial Regex FinishLineRegex();
    [GeneratedRegex(@"^\[(.+?)_(\d+)\]$")]
    private static partial Regex StartingAreaRegex();
    [GeneratedRegex(@"^\[[a-zA-Z0-9_]+\]$")]
    private static partial Regex MIssingSuffixRegex();
}
