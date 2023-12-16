using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.ServerMods.NoObf;

namespace ConfigLib
{
    static internal class RegistryObjectTokensReplacer
    {
        static private readonly HashSet<JTokenType> mAllowedTypes = new()
        {
            JTokenType.String,
            JTokenType.Boolean,
            JTokenType.Integer,
            JTokenType.Float
        };

        static public void ReplaceInBaseTypePatch(RegistryObjectType baseType)
        {
            ReplaceInBaseType(baseType.Code.Domain, baseType.jsonObject, baseType.Code.ToString());
        }

        static public bool ReplaceInBaseType(string domain, JObject data, string code)
        {
            int tokensReplaced = 0;

            if (domain == "game")
            {
                foreach (string configDomain in ConfigLibModSystem.GetDomains())
                {
                    try
                    {
                        Replace(configDomain, data, ref tokensReplaced);
                    }
                    catch (ConfigLibException exception)
                    {
                        ConfigLibModSystem.Logger?.Error($"[Config lib] [config domain: {configDomain}] [target: {code}] {exception.Message}");
                    }
                }
            }
            else
            {
                try
                {
                    Replace(domain, data, ref tokensReplaced);
                }
                catch (ConfigLibException exception)
                {
                    ConfigLibModSystem.Logger?.Error($"[Config lib] [config domain: {domain}] [target: {code}] {exception.Message}");
                }
            }

            if (tokensReplaced > 0) ConfigLibModSystem.Logger?.Notification($"[Config lib] Tokens replaced: {tokensReplaced} in {code}");

            return tokensReplaced > 0;
        }

        static private void Replace(string domain, JToken token, ref int tokensReplaced)
        {
            Config? config = ConfigLibModSystem.GetConfigStatic(domain);
            if (config == null) return;
            ReplaceRecursive(config, token, ref tokensReplaced);
        }
        static private void ReplaceRecursive(Config config, JToken token, ref int tokensReplaced)
        {
            if (token is JArray tokenArray && IsValidToken(config, tokenArray))
            {
                config.ReplaceToken(tokenArray);
                tokensReplaced++;
                return;
            }

            foreach (JToken child in token.Children())
            {
                ReplaceRecursive(config, child, ref tokensReplaced);
            }
        }
        static private bool IsValidToken(Config config, JArray token)
        {
            bool valid = false;
            foreach (JToken element in token.Where((element) => element.Type == JTokenType.String))
            {
                if ((element as JValue)?.Value is not string value) continue;

                if (config.Settings.ContainsKey(value))
                {
                    valid = true;
                    break;
                }
            }

            foreach (JToken element in token)
            {
                if (!mAllowedTypes.Contains(element.Type))
                {
                    valid = false;
                    break;
                }
            }

            return valid;
        }
    }
}
