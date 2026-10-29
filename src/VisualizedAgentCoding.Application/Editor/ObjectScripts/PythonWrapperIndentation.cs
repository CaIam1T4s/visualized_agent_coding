using System.Text;

namespace VisualizedAgentCoding.Application.Editor.ObjectScripts;

/// <summary>Removes wrapper whitespace only outside literals; never formats Python statements.</summary>
public sealed class PythonWrapperIndentation
{
    public sealed record Line(string Raw, string Visible, bool LiteralContinuation);
    private readonly int indentation;
    private readonly Line[] original;
    public string ViewSource => string.Concat(original.Select(l => l.Visible));

    public PythonWrapperIndentation(string source, int indentation)
    {
        this.indentation = indentation;
        var lexer = new LiteralState();
        original = Lines(source).Select(raw =>
        {
            var literal = lexer.InLiteral;
            var visible = literal ? raw : RemoveWrapper(raw, indentation);
            lexer.Scan(raw);
            return new Line(raw, visible, literal);
        }).ToArray();
    }

    public Line[] Encode(string source)
    {
        var lexer = new LiteralState();
        var available = original.GroupBy(l => (l.Visible, l.LiteralContinuation))
            .ToDictionary(g => g.Key, g => new Queue<Line>(g));
        return Lines(source).Select(visible =>
        {
            var literal = lexer.InLiteral;
            string raw;
            if (available.TryGetValue((visible, literal), out var matches) && matches.Count > 0)
                raw = matches.Dequeue().Raw;
            else raw = literal || visible.TrimEnd('\r', '\n').Length == 0 ? visible : new string(' ', indentation) + visible;
            lexer.Scan(visible);
            return new Line(raw, visible, literal);
        }).ToArray();
    }

    public static IEnumerable<string> Lines(string source)
    {
        var start = 0;
        for (var i = 0; i < source.Length; i++)
            if (source[i] == '\n') { yield return source[start..(i + 1)]; start = i + 1; }
        if (start < source.Length) yield return source[start..];
    }

    private static string RemoveWrapper(string line, int count)
    {
        var index = 0;
        var column = 0;
        while (index < line.Length && column < count && line[index] is ' ' or '\t')
        {
            column = line[index++] == '\t' ? (column / 8 + 1) * 8 : column + 1;
        }
        return column >= count ? new string(' ', column - count) + line[index..] : line;
    }

    // Tracks literal ownership, including multiline f/t-string expressions and
    // nested strings. This is not an AST or a syntax validator. Incomplete input
    // is kept as text and remains editable.
    private sealed class LiteralState
    {
        private sealed class Context(char quote, bool triple, bool formatted, bool expression = false)
        {
            public char Quote = quote;
            public bool Triple = triple;
            public bool Formatted = formatted;
            public bool Expression = expression;
            public int Braces = 1;
            public int Brackets;
            public bool Format;
        }
        private readonly List<Context> stack = [];
        public bool InLiteral => stack.Any(c => !c.Expression);

        public void Scan(string line)
        {
            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];
                var top = stack.LastOrDefault();
                if (top is { Expression: false })
                {
                    if (c == '\\') { i += i + 2 < line.Length && line[i + 1] == '\r' && line[i + 2] == '\n' ? 2 : 1; continue; }
                    if (top.Formatted && c == '{')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '{') { i++; continue; }
                        stack.Add(new('\0', false, false, true));
                        continue;
                    }
                    if (c == top.Quote && (!top.Triple || (i + 2 < line.Length && line[i + 1] == c && line[i + 2] == c)))
                    {
                        stack.RemoveAt(stack.Count - 1);
                        if (top.Triple) i += 2;
                    }
                    else if (!top.Triple && c == '\n') stack.RemoveAt(stack.Count - 1);
                    continue;
                }
                if (c == '#' && top is not { Expression: true, Format: true }) break;
                if (top is { Expression: true })
                {
                    if (top.Format)
                    {
                        if (c == '{') stack.Add(new('\0', false, false, true));
                        else if (c == '}') stack.RemoveAt(stack.Count - 1);
                        continue;
                    }
                    if (c is '(' or '[') top.Brackets++;
                    if (c is ')' or ']') top.Brackets--;
                    if (c == ':' && top.Braces == 1 && top.Brackets == 0) { top.Format = true; continue; }
                    if (c == '{') { top.Braces++; continue; }
                    if (c == '}')
                    {
                        if (--top.Braces == 0) stack.RemoveAt(stack.Count - 1);
                        continue;
                    }
                }
                if (c is not ('\'' or '"')) continue;
                var triple = i + 2 < line.Length && line[i + 1] == c && line[i + 2] == c;
                var prefixEnd = i;
                var prefixStart = prefixEnd;
                while (prefixStart > 0 && char.IsLetter(line[prefixStart - 1])) prefixStart--;
                var prefix = line[prefixStart..prefixEnd];
                var formatted = prefix.ToLowerInvariant() is "f" or "fr" or "rf" or "t" or "tr" or "rt";
                stack.Add(new(c, triple, formatted));
                if (triple) i += 2;
            }
        }
    }
}
