using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using TextMateSharp.Internal.Utils;
using TextMateSharp.Registry;

namespace TextMateSharp.Themes
{
    public class Theme
    {
        private ParsedTheme _theme;
        private ParsedTheme _include;
        private ColorMap _colorMap;
        private Dictionary<string, string> _guiColorDictionary;

        public static Theme CreateFromRawTheme(
            IRawTheme source,
            IRegistryOptions registryOptions)
        {
            ColorMap colorMap = new ColorMap();
            var guiColorsDictionary = new Dictionary<string, string>();

            var themeRuleList = ParsedTheme.ParseTheme(source,0);
            
            ParsedTheme theme = ParsedTheme.CreateFromParsedTheme(
                themeRuleList,
                colorMap);

            IRawTheme themeInclude;
            ParsedTheme include = ParsedTheme.CreateFromParsedTheme(
                ParsedTheme.ParseInclude(source, registryOptions, 0, out themeInclude),
                colorMap);

            // First get colors from include, then try and overwrite with local colors..
            // I don't see this happening currently, but here just in case that ever happens.
            if (themeInclude != null)
            {
                ParsedTheme.ParsedGuiColors(themeInclude, guiColorsDictionary);
            }
            ParsedTheme.ParsedGuiColors(source, guiColorsDictionary);

            return new Theme(colorMap, theme, include, guiColorsDictionary);
        }

        Theme(ColorMap colorMap, ParsedTheme theme, ParsedTheme include, Dictionary<string,string> guiColorDictionary)
        {
            _colorMap = colorMap;
            _theme = theme;
            _include = include;
            _guiColorDictionary = guiColorDictionary;
        }

        public List<ThemeTrieElementRule> Match(IList<string> scopeNames)
        {
            List<ThemeTrieElementRule> result = new List<ThemeTrieElementRule>();

            for (int i = scopeNames.Count - 1; i >= 0; i--)
                result.AddRange(this._theme.Match(scopeNames[i]));

            for (int i = scopeNames.Count - 1; i >= 0; i--)
                result.AddRange(this._include.Match(scopeNames[i]));

            return result;
        }

        public ReadOnlyDictionary<string, string> GetGuiColorDictionary()
        {
            return new ReadOnlyDictionary<string, string>(this._guiColorDictionary);
        }

        public ICollection<string> GetColorMap()
        {
            return this._colorMap.GetColorMap();
        }

        public int GetColorId(string color)
        {
            return this._colorMap.GetId(color);
        }

        public string GetColor(int id)
        {
            return this._colorMap.GetColor(id);
        }

        internal ThemeTrieElementRule GetDefaults()
        {
            return this._theme.GetDefaults();
        }
    }

    class ParsedTheme
    {
        private ThemeTrieElement _root;
        private ThemeTrieElementRule _defaults;

        private Dictionary<string /* scopeName */, List<ThemeTrieElementRule>> _cachedMatchRoot;

        internal static List<ParsedThemeRule> ParseTheme(IRawTheme source, int priority)
        {
            List<ParsedThemeRule> result = new List<ParsedThemeRule>();

            // process theme rules in vscode-textmate format:
            // see https://github.com/microsoft/vscode-textmate/tree/main/test-cases/themes
            LookupThemeRules(source.GetSettings(), result, priority);

            // process theme rules in vscode format
            // see https://github.com/microsoft/vscode/tree/main/extensions/theme-defaults/themes
            LookupThemeRules(source.GetTokenColors(), result, priority);

            return result;
        }

        internal static void ParsedGuiColors(IRawTheme source, Dictionary<string,string> colorDictionary)
        {
            var colors = source.GetGuiColors();
            if (colors == null)
            {
                return;
            }
            foreach (var kvp in colors)
            {
                colorDictionary[kvp.Key] = (string)kvp.Value;
            }
        }


        internal static List<ParsedThemeRule> ParseInclude(
            IRawTheme source,
            IRegistryOptions registryOptions,
            int priority,
            out IRawTheme themeInclude)
        {
            List<ParsedThemeRule> result = new List<ParsedThemeRule>();

            string include = source.GetInclude();

            if (string.IsNullOrEmpty(include))
            {
                themeInclude = null;
                return result;
            }

            themeInclude = registryOptions.GetTheme(include);

            if (themeInclude == null)
                return result;

            return ParseTheme(themeInclude, priority);

        }

        static void LookupThemeRules(
            ICollection<IRawThemeSetting> settings,
            List<ParsedThemeRule> parsedThemeRules,
            int priority)
        {
            if (settings == null)
                return;

            int i = 0;
            foreach (IRawThemeSetting entry in settings)
            {
                if (entry.GetSetting() == null)
                {
                    continue;
                }

                object settingScope = entry.GetScope();
                List<string> scopes = new List<string>();
                if (settingScope is string)
                {
                    string scope = (string)settingScope;
                    // remove leading and trailing commas
                    scope = scope.Trim(',');
                    scopes = new List<string>(scope.Split(new[] { "," }, StringSplitOptions.RemoveEmptyEntries));
                }
                else if (settingScope is IList<object>)
                {
                    scopes = new List<string>(((IList<object>)settingScope).Cast<string>());
                }
                else
                {
                    scopes.Add("");
                }

                FontStyle fontStyle = FontStyle.NotSet;
                object settingsFontStyle = entry.GetSetting().GetFontStyle();
                if (settingsFontStyle is string)
                {
                    fontStyle = FontStyle.None;

                    string[] segments = ((string)settingsFontStyle).Split(new[] { " " }, StringSplitOptions.None);
                    foreach (string segment in segments)
                    {
                        switch (segment)
                        {
                            case "italic":
                                fontStyle |= FontStyle.Italic;
                                break;
                            case "bold":
                                fontStyle |= FontStyle.Bold;
                                break;
                            case "underline":
                                fontStyle |= FontStyle.Underline;
                                break;
                            case "strikethrough":
                                fontStyle |= FontStyle.Strikethrough;
                                break;
                        }
                    }
                }

                string foreground = null;
                object settingsForeground = entry.GetSetting().GetForeground();
                if (settingsForeground is string && StringUtils.IsValidHexColor((string)settingsForeground))
                {
                    foreground = (string)settingsForeground;
                }

                string background = null;
                object settingsBackground = entry.GetSetting().GetBackground();
                if (settingsBackground is string && StringUtils.IsValidHexColor((string)settingsBackground))
                {
                    background = (string)settingsBackground;
                }
                for (int j = 0, lenJ = scopes.Count; j < lenJ; j++)
                {
                    string _scope = scopes[j].Trim();

                    List<string> segments = new List<string>(_scope.Split(new[] { " " }, StringSplitOptions.None));

                    string scope = segments[segments.Count - 1];
                    List<string> parentScopes = null;
                    if (segments.Count > 1)
                    {
                        parentScopes = new List<string>(segments);
                        parentScopes.Reverse();
                    }
                    var name = entry.GetName();

                    ParsedThemeRule t = new ParsedThemeRule(name, scope, parentScopes, i, fontStyle, foreground, background);
                    parsedThemeRules.Add(t);
                }
                i++;
            }
        }

        public static ParsedTheme CreateFromParsedTheme(
            List<ParsedThemeRule> source,
            ColorMap colorMap)
        {
            return ResolveParsedThemeRules(source, colorMap);
        }

        /**
         * Resolve rules (i.e. inheritance).
         */
        static ParsedTheme ResolveParsedThemeRules(
            List<ParsedThemeRule> parsedThemeRules,
            ColorMap colorMap)
        {
            // Sort rules lexicographically, and then by index if necessary
            parsedThemeRules.Sort((a, b) =>
            {
                int r = StringUtils.StrCmp(a.scope, b.scope);
                if (r != 0)
                {
                    return r;
                }
                r = StringUtils.StrArrCmp(a.parentScopes, b.parentScopes);
                if (r != 0)
                {
                    return r;
                }
                return a.index.CompareTo(b.index);
            });

            // Determine defaults
            FontStyle defaultFontStyle = FontStyle.None;
            string defaultForeground = "#000000";
            string defaultBackground = "#ffffff";
            while (parsedThemeRules.Count >= 1 && "".Equals(parsedThemeRules[0].scope))
            {
                ParsedThemeRule incomingDefaults = parsedThemeRules[0];
                parsedThemeRules.RemoveAt(0); // shift();
                if (incomingDefaults.fontStyle != FontStyle.NotSet)
                {
                    defaultFontStyle = incomingDefaults.fontStyle;
                }
                if (incomingDefaults.foreground != null)
                {
                    defaultForeground = incomingDefaults.foreground;
                }
                if (incomingDefaults.background != null)
                {
                    defaultBackground = incomingDefaults.background;
                }
            }
            ThemeTrieElementRule defaults = new ThemeTrieElementRule(string.Empty, 0, null, defaultFontStyle,
                    colorMap.GetId(defaultForeground), colorMap.GetId(defaultBackground));

            ThemeTrieElement root = new ThemeTrieElement(new ThemeTrieElementRule(string.Empty, 0, null, FontStyle.NotSet, 0, 0),
                    new List<ThemeTrieElementRule>());
            foreach (ParsedThemeRule rule in parsedThemeRules)
            {
                root.Insert(rule.name, 0, rule.scope, rule.parentScopes, rule.fontStyle, colorMap.GetId(rule.foreground),
                        colorMap.GetId(rule.background));
            }
            return new ParsedTheme(defaults, root);
        }

        ParsedTheme(ThemeTrieElementRule defaults, ThemeTrieElement root)
        {
            this._root = root;
            this._defaults = defaults;
            _cachedMatchRoot = new Dictionary<string, List<ThemeTrieElementRule>>();
        }

        internal List<ThemeTrieElementRule> Match(string scopeName)
        {
            lock (this._cachedMatchRoot)
            {
                if (!_cachedMatchRoot.TryGetValue(scopeName, out List<ThemeTrieElementRule> value))
                {
                    value = this._root.Match(scopeName);
                    this._cachedMatchRoot[scopeName] = value;
                }
                return value;
            }
        }

        internal ThemeTrieElementRule GetDefaults()
        {
            return this._defaults;
        }
    }
}