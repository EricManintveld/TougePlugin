using System.Numerics;
using TougePlugin.Models;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

public class CarSpawnYamlConverter : IYamlTypeConverter
{
    public bool Accepts(Type type) => type == typeof(CarSpawn);

    public object ReadYaml(IParser parser, Type type, ObjectDeserializer nestedObjectDeserializer)
    {
        parser.Consume<MappingStart>();

        var spawn = new CarSpawn();

        while (parser.Current is not MappingEnd)
        {
            var property = parser.Consume<Scalar>().Value;

            switch (property)
            {
                case "Position":
                    parser.Consume<SequenceStart>();
                    float x = float.Parse(parser.Consume<Scalar>().Value);
                    float y = float.Parse(parser.Consume<Scalar>().Value);
                    float z = float.Parse(parser.Consume<Scalar>().Value);
                    parser.Consume<SequenceEnd>();

                    spawn.Position = new Vector3(x, y, z);
                    spawn.PositionIsSet = true;
                    break;

                case "Heading":
                    spawn.Heading = int.Parse(parser.Consume<Scalar>().Value);
                    spawn.HeadingIsSet = true;
                    break;

                default:
                    SkipCurrentAndNested(parser);
                    break;
            }
        }

        parser.Consume<MappingEnd>();

        return spawn;
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer nestedObjectSerializer)
    {
        throw new NotImplementedException("Serialization is not needed.");
    }

    private void SkipCurrentAndNested(IParser parser)
    {
        int depth = 0;

        do
        {
            if (parser.TryConsume<MappingStart>(out _) || parser.TryConsume<SequenceStart>(out _))
            {
                depth++;
            }
            else if (parser.TryConsume<MappingEnd>(out _) || parser.TryConsume<SequenceEnd>(out _))
            {
                depth--;
            }
            else
            {
                parser.MoveNext(); // Scalar or other simple node
            }
        }
        while (depth > 0 || !(parser.Current is MappingEnd || parser.Current is SequenceEnd));

        parser.MoveNext(); // Move past the end node
    }
}
