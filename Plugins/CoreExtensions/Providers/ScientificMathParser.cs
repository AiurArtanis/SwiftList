using System;
using System.Collections.Generic;

namespace SwiftList.Plugins.CoreExtensions.Providers
{
    /// <summary>
    /// Self-contained, robust recursive descent parser for mathematical, trigonometric, and logarithmic expressions.
    /// </summary>
    internal class ScientificMathParser
    {
        private readonly string _expr;
        private int _pos;

        public ScientificMathParser(string expr)
        {
            _expr = expr;
            _pos = 0;
        }

        public double Parse()
        {
            double result = ParseExpression();
            SkipWhitespace();
            if (_pos < _expr.Length)
                throw new Exception("Unexpected character: " + _expr[_pos]);
            return result;
        }

        private double ParseExpression()
        {
            double result = ParseTerm();
            while (true)
            {
                SkipWhitespace();
                if (_pos >= _expr.Length) break;
                char op = _expr[_pos];
                if (op == '+' || op == '-')
                {
                    _pos++;
                    double nextTerm = ParseTerm();
                    if (op == '+') result += nextTerm;
                    else result -= nextTerm;
                }
                else
                {
                    break;
                }
            }
            return result;
        }

        private double ParseTerm()
        {
            double result = ParseFactor();
            while (true)
            {
                SkipWhitespace();
                if (_pos >= _expr.Length) break;
                char op = _expr[_pos];
                if (op == '*' || op == '/' || op == '%')
                {
                    _pos++;
                    double nextFactor = ParseFactor();
                    if (op == '*') result *= nextFactor;
                    else if (op == '/')
                    {
                        if (nextFactor == 0) throw new DivideByZeroException();
                        result /= nextFactor;
                    }
                    else
                    {
                        result %= nextFactor;
                    }
                }
                else
                {
                    break;
                }
            }
            return result;
        }

        private double ParseFactor()
        {
            double result = ParsePrimary();
            SkipWhitespace();
            if (_pos < _expr.Length && _expr[_pos] == '^')
            {
                _pos++;
                double exponent = ParseFactor(); // Right associative
                result = Math.Pow(result, exponent);
            }
            return result;
        }

        private double ParsePrimary()
        {
            SkipWhitespace();
            if (_pos >= _expr.Length)
                throw new Exception("Unexpected end of expression");

            char c = _expr[_pos];

            // Unary plus/minus
            if (c == '+')
            {
                _pos++;
                return ParsePrimary();
            }
            if (c == '-')
            {
                _pos++;
                return -ParsePrimary();
            }

            // Parentheses
            if (c == '(')
            {
                _pos++;
                double result = ParseExpression();
                SkipWhitespace();
                if (_pos >= _expr.Length || _expr[_pos] != ')')
                    throw new Exception("Missing closing parenthesis");
                _pos++;
                return result;
            }

            // Numbers (decimal, hex, binary)
            if (char.IsDigit(c) || c == '.' || (_pos + 1 < _expr.Length && c == '0' && (_expr[_pos + 1] == 'x' || _expr[_pos + 1] == 'b')))
            {
                return ParseNumber();
            }

            // Word/Identifier (constants, functions)
            if (char.IsLetter(c) || c == 'π')
            {
                return ParseIdentifier();
            }

            throw new Exception("Unexpected character: " + c);
        }

        private double ParseNumber()
        {
            int start = _pos;
            if (_pos + 1 < _expr.Length && _expr[_pos] == '0' && _expr[_pos + 1] == 'x')
            {
                _pos += 2;
                while (_pos < _expr.Length && char.IsAsciiHexDigit(_expr[_pos]))
                {
                    _pos++;
                }
                string hexStr = _expr[start.._pos];
                return Convert.ToInt64(hexStr, 16);
            }
            if (_pos + 1 < _expr.Length && _expr[_pos] == '0' && _expr[_pos + 1] == '0' && _expr[_pos + 1] == 'b')
            {
                // Note: The original parser had a slight logic error in "0b" detection where it checked index _pos+1 twice or did start+2, let's keep it safe.
            }
            if (_pos + 1 < _expr.Length && _expr[_pos] == '0' && _expr[_pos + 1] == 'b')
            {
                _pos += 2;
                while (_pos < _expr.Length && (_expr[_pos] == '0' || _expr[_pos] == '1'))
                {
                    _pos++;
                }
                string binStr = _expr[start.._pos];
                return Convert.ToInt64(binStr[2..], 2);
            }

            while (_pos < _expr.Length && (char.IsDigit(_expr[_pos]) || _expr[_pos] == '.' || _expr[_pos] == 'e' || _expr[_pos] == 'E'))
            {
                // Handle scientific notation e.g. 1e+5, 2e-3
                if ((_expr[_pos] == 'e' || _expr[_pos] == 'E') && _pos + 1 < _expr.Length)
                {
                    char next = _expr[_pos + 1];
                    if (next == '+' || next == '-' || char.IsDigit(next))
                    {
                        _pos += 2;
                        continue;
                    }
                }
                _pos++;
            }
            string numStr = _expr[start.._pos];
            if (double.TryParse(numStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double val))
            {
                return val;
            }
            throw new Exception("Invalid number format: " + numStr);
        }

        private double ParseIdentifier()
        {
            int start = _pos;
            if (_expr[_pos] == 'π')
            {
                _pos++;
                return Math.PI;
            }

            while (_pos < _expr.Length && char.IsLetterOrDigit(_expr[_pos]))
            {
                _pos++;
            }
            string id = _expr[start.._pos].ToLowerInvariant();

            // Constants
            if (id == "pi") return Math.PI;
            if (id == "e") return Math.E;

            // Functions
            SkipWhitespace();
            if (_pos >= _expr.Length || _expr[_pos] != '(')
            {
                throw new Exception("Expected '(' after function " + id);
            }
            _pos++; // Skip '('

            List<double> args = new List<double>();
            while (true)
            {
                args.Add(ParseExpression());
                SkipWhitespace();
                if (_pos < _expr.Length && _expr[_pos] == ',')
                {
                    _pos++;
                    continue;
                }
                break;
            }

            if (_pos >= _expr.Length || _expr[_pos] != ')')
                throw new Exception("Expected ')' after function arguments");
            _pos++; // Skip ')'

            switch (id)
            {
                case "sin": return Math.Sin(args[0]);
                case "cos": return Math.Cos(args[0]);
                case "tan": return Math.Tan(args[0]);
                case "asin": return Math.Asin(args[0]);
                case "acos": return Math.Acos(args[0]);
                case "atan": return Math.Atan(args[0]);
                case "sqrt": return Math.Sqrt(args[0]);
                case "cbrt": return Math.Cbrt(args[0]);
                case "abs": return Math.Abs(args[0]);
                case "ln": return Math.Log(args[0]);
                case "log": return args.Count > 1 ? Math.Log(args[0], args[1]) : Math.Log10(args[0]);
                case "log2": return Math.Log2(args[0]);
                case "log10": return Math.Log10(args[0]);
                case "exp": return Math.Exp(args[0]);
                case "floor": return Math.Floor(args[0]);
                case "ceil": return Math.Ceiling(args[0]);
                case "round": return args.Count > 1 ? Math.Round(args[0], (int)args[1]) : Math.Round(args[0]);
                case "min": return Math.Min(args[0], args[1]);
                case "max": return Math.Max(args[0], args[1]);
                default:
                    throw new Exception("Unknown function: " + id);
            }
        }

        private void SkipWhitespace()
        {
            while (_pos < _expr.Length && char.IsWhiteSpace(_expr[_pos]))
            {
                _pos++;
            }
        }
    }
}
