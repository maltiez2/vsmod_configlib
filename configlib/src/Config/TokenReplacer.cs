using Newtonsoft.Json.Linq;
using org.mariuszgromada.math.mxparser;
using org.mariuszgromada.math.mxparser.syntaxchecker;
using System.Collections.Generic;
using System.Text;

namespace ConfigLib
{
    public class TokenReplacer
    {
        private readonly Dictionary<string, ConfigSetting> mSettings;

        public TokenReplacer(Dictionary<string, ConfigSetting> settings)
        {
            mSettings = settings;
        }

        public void ReplaceToken(JArray token)
        {
            if (token.Count == 0) return;

            if (token.Count == 1)
            {
                ReplaceSingleToken(token[0]);
                token.Replace(token[0]);
                return;
            }

            ReplaceArray(token);
        }

        private void ReplaceSingleToken(JToken token)
        {
            if (
                token.Type != JTokenType.String ||
                token is not JValue value ||
                value.Value == null
                )
            {
                throw new InvalidTokenException($"Single value token should contain a setting code, but this value was passed: '{token}'.");
            }

            string code = (string)value.Value;

            if (!mSettings.ContainsKey(code))
            {
                throw new InvalidTokenException($"Setting code '{code}' was not found in the config.");
            }

            token.Replace(mSettings[code].Value.Token);
        }

        private void ReplaceArray(JToken tokens)
        {
            if (tokens is not JArray tokensArray) return;

            foreach (JToken token in tokensArray)
            {
                if (GetConfigTokenType(token) != JTokenType.String)
                {
                    ReplaceFormula(tokens);
                    return;
                }
            }

            ConcatenateTokens(tokens);
        }

        private JTokenType GetConfigTokenType(JToken token)
        {
            if (token.Type != JTokenType.String) return token.Type;

            string code = (token as JValue)?.Value as string ?? "";

            if (mSettings.ContainsKey(code)) return mSettings[code].JsonType;

            return JTokenType.String;
        }

        private void ConcatenateTokens(JToken tokens)
        {
            StringBuilder result = new();
            foreach (JToken token in tokens)
            {
                string value = (token as JValue)?.Value as string ?? "";
                ReplaceWithStringValue(ref value);
                result.Append(value);
            }
            tokens.Replace(new JValue(result.ToString()));
        }

        private void ReplaceWithStringValue(ref string token)
        {
            if (!mSettings.ContainsKey(token)) return;

            switch (mSettings[token].JsonType)
            {
                case JTokenType.String:
                    token = mSettings[token].Value.AsString("");
                    return;
                default:
                    throw new InvalidConfigException($"This value type: '{mSettings[token].Value}' of token: {token} - is unsupported in concatenation.");
            }
        }

        private void ReplaceWithValue(ref string token)
        {
            if (!mSettings.ContainsKey(token)) return;

            switch (mSettings[token].JsonType)
            {
                case JTokenType.String:
                    token = mSettings[token].Value.AsString("");
                    return;
                case JTokenType.Integer:
                    token = mSettings[token].Value.AsInt(0).ToString();
                    return;
                case JTokenType.Float:
                    token = mSettings[token].Value.AsFloat(0).ToString();
                    return;
                default:
                    throw new InvalidConfigException($"This value type: '{mSettings[token].Value}' of token: {token} - is unsupported in formulas");
            }
        }

        private void ReplaceFormula(JToken tokens)
        {
            StringBuilder result = new();
            foreach (JToken token in tokens)
            {
                string value = (token as JValue)?.Value as string ?? "";
                ReplaceWithValue(ref value);
                result.Append(value);
            }
            string formula = result.ToString();
            Expression expression;

            try
            {
                expression = new Expression(formula);
            }
            catch (ParseException exception)
            {
                throw new InvalidTokenException($"Error on parsing config token as formula. Token: {formula}. Parser exception: {exception.Message}");
            }

            double resultValue = expression.calculate();

            if (double.IsNaN(resultValue))
            {
                throw new InvalidTokenException($"Error on calculating formula: {formula}.");
            }

            tokens.Replace(new JValue(resultValue));
        }
    }
}
