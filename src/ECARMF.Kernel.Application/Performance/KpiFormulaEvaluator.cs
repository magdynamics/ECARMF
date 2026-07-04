using System.Globalization;

namespace ECARMF.Kernel.Application.Performance;

/// <summary>
/// Tiny arithmetic evaluator for declarative KPI formulas: identifiers
/// (resolved from the payload), numeric literals, + - * /, parentheses.
/// Recursive descent; a missing identifier or division by zero returns
/// failure rather than throwing.
/// </summary>
public static class KpiFormulaEvaluator
{
    public static bool TryEvaluate(
        string formula, IReadOnlyDictionary<string, string> payload, out decimal value)
    {
        value = 0;
        try
        {
            var parser = new Parser(formula, payload);
            value = parser.ParseExpression();
            return parser.AtEnd && !parser.Failed;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private sealed class Parser(string text, IReadOnlyDictionary<string, string> payload)
    {
        private int _pos;
        public bool Failed { get; private set; }
        public bool AtEnd
        {
            get { SkipWhitespace(); return _pos >= text.Length; }
        }

        public decimal ParseExpression()
        {
            var left = ParseTerm();
            while (true)
            {
                SkipWhitespace();
                if (_pos < text.Length && (text[_pos] == '+' || text[_pos] == '-'))
                {
                    var op = text[_pos++];
                    var right = ParseTerm();
                    left = op == '+' ? left + right : left - right;
                }
                else
                {
                    return left;
                }
            }
        }

        private decimal ParseTerm()
        {
            var left = ParseFactor();
            while (true)
            {
                SkipWhitespace();
                if (_pos < text.Length && (text[_pos] == '*' || text[_pos] == '/'))
                {
                    var op = text[_pos++];
                    var right = ParseFactor();
                    if (op == '/')
                    {
                        if (right == 0) { Failed = true; return 0; }
                        left /= right;
                    }
                    else
                    {
                        left *= right;
                    }
                }
                else
                {
                    return left;
                }
            }
        }

        private decimal ParseFactor()
        {
            SkipWhitespace();
            if (_pos >= text.Length) { Failed = true; return 0; }

            if (text[_pos] == '(')
            {
                _pos++;
                var inner = ParseExpression();
                SkipWhitespace();
                if (_pos < text.Length && text[_pos] == ')') { _pos++; }
                else { Failed = true; }
                return inner;
            }

            if (text[_pos] == '-')
            {
                _pos++;
                return -ParseFactor();
            }

            var start = _pos;
            if (char.IsDigit(text[_pos]) || text[_pos] == '.')
            {
                while (_pos < text.Length && (char.IsDigit(text[_pos]) || text[_pos] == '.')) _pos++;
                return decimal.Parse(text[start.._pos], CultureInfo.InvariantCulture);
            }

            if (char.IsLetter(text[_pos]) || text[_pos] == '_')
            {
                while (_pos < text.Length && (char.IsLetterOrDigit(text[_pos]) || text[_pos] == '_')) _pos++;
                var identifier = text[start.._pos];
                var raw = payload.FirstOrDefault(kv =>
                    string.Equals(kv.Key, identifier, StringComparison.OrdinalIgnoreCase)).Value;
                if (raw is null || !decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var resolved))
                {
                    Failed = true;
                    return 0;
                }
                return resolved;
            }

            Failed = true;
            return 0;
        }

        private void SkipWhitespace()
        {
            while (_pos < text.Length && char.IsWhiteSpace(text[_pos])) _pos++;
        }
    }
}
