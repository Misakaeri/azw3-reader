namespace Azw3Reader.App.Services;

public enum ThemeType
{
    Default, Gray, Sepia, Grass, Cherry, Sky, Solarized, Gruvbox, Nord, Dark
}

/// <summary>注入 WebView2 的 CSS 主题 (参考 Foliate 设计)。</summary>
public class ThemeManager
{
    private static readonly (string label, string bg, string fg, string link)[] Themes =
    [
        ("默认",   "#ffffff", "#000000", "#0066cc"),
        ("灰色",   "#e0e0e0", "#222222", "#4488cc"),
        ("羊皮纸", "#f1e8d0", "#5b4636", "#008b8b"),
        ("草绿",   "#d7dbbd", "#232c16", "#177b4d"),
        ("樱桃",   "#f0d1d5", "#4e1609", "#de3838"),
        ("天空",   "#cedef5", "#262d48", "#2d53e5"),
        ("Solarized", "#fdf6e3", "#586e75", "#268bd2"),
        ("Gruvbox", "#fbf1c7", "#3c3836", "#076678"),
        ("Nord",    "#eceff4", "#2e3440", "#5e81ac"),
        ("夜间",   "#222222", "#e0e0e0", "#77bbee"),
    ];

    public ThemeType CurrentTheme { get; set; } = ThemeType.Default;
    public int FontSize { get; set; } = 18;
    public double LineHeight { get; set; } = 1.6;

    public static string[] ThemeNames => Themes.Select(t => t.label).ToArray();

    public string GetCss()
    {
        int idx = (int)CurrentTheme;
        var (_, bg, fg, link) = Themes[idx];

        return $@"
body {{
    background-color: {bg};
    color: {fg};
    font-size: {FontSize}px;
    line-height: {LineHeight};
    padding: 15px 20px 30px 20px;
    max-width: 780px;
    margin: 0 auto;
    font-family: 'Georgia', 'Songti SC', 'Noto Serif SC', 'Source Han Serif SC', serif;
    word-wrap: break-word;
    overflow-wrap: break-word;
    -webkit-font-smoothing: antialiased;
}}
p {{
    margin: 0.3em 0;
    text-indent: 2em;
    text-align: justify;
}}
h1, h2, h3, h4 {{
    margin: 0.8em 0 0.3em 0;
    font-weight: bold;
    text-indent: 0;
}}
img {{
    max-width: 100% !important;
    height: auto !important;
    display: block;
    margin: 1em auto;
    background-color: transparent;
}}
a {{ color: {link}; text-decoration: none; }}
blockquote {{
    margin: 0.5em 0;
    padding: 0.5em 1em;
    border-left: 3px solid rgba(0,0,0,.1);
    background-color: rgba(0,0,0,.02);
}}
code, pre {{
    font-family: 'Consolas', 'Courier New', monospace;
    font-size: 0.9em;
}}
";
    }
}
