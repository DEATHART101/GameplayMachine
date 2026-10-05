using System.Text;

namespace ODStudio.Generator;

public sealed class CodeWriter
{
    private readonly StringBuilder _builder = new();
    private int _indent;

    public void Line(string value = "")
    {
        if (value.Length > 0)
            _builder.Append(' ', _indent * 4);
        _builder.AppendLine(value);
    }

    public IDisposable Block(string header)
    {
        Line(header);
        Line("{");
        _indent++;
        return new Scope(this);
    }

    public override string ToString() => _builder.ToString();

    private sealed class Scope(CodeWriter owner) : IDisposable
    {
        public void Dispose()
        {
            owner._indent--;
            owner.Line("}");
        }
    }
}
