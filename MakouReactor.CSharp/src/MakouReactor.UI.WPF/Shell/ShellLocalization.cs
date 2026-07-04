namespace MakouReactor.UI.WPF.Shell;

public sealed class ShellLocalization
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Translations =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["en"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["File"] = "_File",
                ["Tools"] = "T_ools",
                ["Settings"] = "_Settings",
                ["View"] = "_View",
                ["Help"] = "_?",
                ["JapaneseCharacters"] = "_Japanese Characters",
                ["Language"] = "_Language",
                ["English"] = "English (default)",
                ["French"] = "French",
                ["Japanese"] = "Japanese",
                ["Configuration"] = "_Configuration...",
                ["FieldList"] = "Field List",
                ["BackgroundPreview"] = "Background Preview",
                ["PreviewMode"] = "Preview Mode",
                ["Background"] = "Background",
                ["Model"] = "Model",
                ["OpenFileTip"] = "Open a file",
                ["SaveTip"] = "Save",
                ["FindTip"] = "Find",
                ["RunFf7Tip"] = "Run FF7",
                ["TextsTip"] = "Text editor",
                ["ModelsTip"] = "Model loader editor",
                ["WalkmeshTip"] = "Walkmesh editor",
                ["LlmTip"] = "Generate scene via LLM",
                ["RevertLlmTip"] = "Revert last LLM apply",
                ["Ready"] = "Ready",
                ["LanguageApplied"] = "Language set to {0}.",
            },
            ["fr"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["File"] = "_Fichier",
                ["Tools"] = "_Outils",
                ["Settings"] = "_Parametres",
                ["View"] = "_Affichage",
                ["Help"] = "_?",
                ["JapaneseCharacters"] = "Caracteres _japonais",
                ["Language"] = "_Langue",
                ["English"] = "Anglais (defaut)",
                ["French"] = "Francais",
                ["Japanese"] = "Japonais",
                ["Configuration"] = "_Configuration...",
                ["FieldList"] = "Liste des champs",
                ["BackgroundPreview"] = "Apercu du fond",
                ["PreviewMode"] = "Mode d'apercu",
                ["Background"] = "Fond",
                ["Model"] = "Modele",
                ["OpenFileTip"] = "Ouvrir un fichier",
                ["SaveTip"] = "Enregistrer",
                ["FindTip"] = "Rechercher",
                ["RunFf7Tip"] = "Lancer FF7",
                ["TextsTip"] = "Editeur de textes",
                ["ModelsTip"] = "Editeur de modeles",
                ["WalkmeshTip"] = "Editeur de walkmesh",
                ["LlmTip"] = "Generer une scene avec LLM",
                ["RevertLlmTip"] = "Annuler la derniere application LLM",
                ["Ready"] = "Pret",
                ["LanguageApplied"] = "Langue definie sur {0}.",
            },
            ["ja"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["File"] = "ファイル(_F)",
                ["Tools"] = "ツール(_O)",
                ["Settings"] = "設定(_S)",
                ["View"] = "表示(_V)",
                ["Help"] = "?",
                ["JapaneseCharacters"] = "日本語文字(_J)",
                ["Language"] = "言語(_L)",
                ["English"] = "英語 (既定)",
                ["French"] = "フランス語",
                ["Japanese"] = "日本語",
                ["Configuration"] = "設定(_C)...",
                ["FieldList"] = "フィールド一覧",
                ["BackgroundPreview"] = "背景プレビュー",
                ["PreviewMode"] = "プレビューモード",
                ["Background"] = "背景",
                ["Model"] = "モデル",
                ["OpenFileTip"] = "ファイルを開く",
                ["SaveTip"] = "保存",
                ["FindTip"] = "検索",
                ["RunFf7Tip"] = "FF7を実行",
                ["TextsTip"] = "テキストエディタ",
                ["ModelsTip"] = "モデルローダーエディタ",
                ["WalkmeshTip"] = "ウォークメッシュエディタ",
                ["LlmTip"] = "LLMでシーンを生成",
                ["RevertLlmTip"] = "最後のLLM適用を戻す",
                ["Ready"] = "準備完了",
                ["LanguageApplied"] = "言語を{0}に設定しました。",
            },
        };

    private readonly IReadOnlyDictionary<string, string> _strings;

    private ShellLocalization(string language)
    {
        Language = NormalizeLanguage(language);
        _strings = Translations[Language];
    }

    public string Language { get; }

    public static IReadOnlyList<string> SupportedLanguages { get; } = ["en", "fr", "ja"];

    public static ShellLocalization ForLanguage(string? language) => new(language ?? "en");

    public string this[string key] =>
        _strings.TryGetValue(key, out var value)
            ? value
            : Translations["en"].TryGetValue(key, out var fallback) ? fallback : key;

    public static string NormalizeLanguage(string? language) =>
        SupportedLanguages.Contains(language ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            ? language!.ToLowerInvariant()
            : "en";
}
