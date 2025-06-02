using System.Numerics;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace TougePlugin.Serialization;

public class Vector3YamlConverter : IYamlTypeConverter
{
    public bool Accepts(Type type) => type == typeof(Vector3);

    public object? ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        parser.Consume<SequenceStart>();
        float x = float.Parse(((Scalar)parser.Consume<Scalar>()).Value);
        float y = float.Parse(((Scalar)parser.Consume<Scalar>()).Value);
        float z = float.Parse(((Scalar)parser.Consume<Scalar>()).Value);
        parser.Consume<SequenceEnd>();
        return new Vector3(x, y, z);
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
    {
        var v = (Vector3)value!;
        emitter.Emit(new SequenceStart(null, null, false, SequenceStyle.Flow));
        emitter.Emit(new Scalar(v.X.ToString()));
        emitter.Emit(new Scalar(v.Y.ToString()));
        emitter.Emit(new Scalar(v.Z.ToString()));
        emitter.Emit(new SequenceEnd());
    }
}
