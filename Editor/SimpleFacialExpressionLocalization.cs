using System;
using System.Collections.Generic;
using SimpleFacialExpressionMenuTool;
using UnityEditor;
using UnityEngine;

namespace SimpleFacialExpressionMenuTool.Editor
{
    internal static class SimpleFacialExpressionLocalization
    {
        private const string BasePath = "Packages/me.kirisame.sfem/Editor/Localization/";
        private const SimpleFacialExpressionLanguage FallbackLanguage = SimpleFacialExpressionLanguage.Japanese;

        private static readonly Dictionary<SimpleFacialExpressionLanguage, Dictionary<string, string>> Tables =
            new Dictionary<SimpleFacialExpressionLanguage, Dictionary<string, string>>();

        internal static GUIContent Label(SimpleFacialExpressionLanguage language, string key)
        {
            return new GUIContent(Text(language, key));
        }

        internal static string Text(SimpleFacialExpressionLanguage language, string key)
        {
            var table = GetTable(language);
            if (table.TryGetValue(key, out var value))
            {
                return value;
            }

            var fallback = GetTable(FallbackLanguage);
            return fallback.TryGetValue(key, out value) ? value : key;
        }

        private static Dictionary<string, string> GetTable(SimpleFacialExpressionLanguage language)
        {
            if (Tables.TryGetValue(language, out var table))
            {
                return table;
            }

            table = LoadTable(language);
            Tables[language] = table;
            return table;
        }

        private static Dictionary<string, string> LoadTable(SimpleFacialExpressionLanguage language)
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(BasePath + LocaleCode(language) + ".json");
            if (asset == null)
            {
                return new Dictionary<string, string>();
            }

            var parsed = JsonUtility.FromJson<StringTable>(asset.text);
            var table = new Dictionary<string, string>();
            if (parsed == null || parsed.entries == null)
            {
                return table;
            }

            foreach (var entry in parsed.entries)
            {
                if (!string.IsNullOrEmpty(entry.key))
                {
                    table[entry.key] = entry.value ?? string.Empty;
                }
            }

            return table;
        }

        private static string LocaleCode(SimpleFacialExpressionLanguage language)
        {
            switch (language)
            {
                case SimpleFacialExpressionLanguage.Korean:
                    return "ko-KR";
                case SimpleFacialExpressionLanguage.English:
                    return "en-US";
                default:
                    return "ja-JP";
            }
        }

        [Serializable]
        private sealed class StringTable
        {
            public StringEntry[] entries;
        }

        [Serializable]
        private sealed class StringEntry
        {
            public string key;
            public string value;
        }
    }
}
