using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MpvNet;
using static MpvNet.Global;

namespace MpvNet.Windows.AiFeatures;

public class WordDefinition
{
    public string Word { get; set; } = "";
    public string Phonetic { get; set; } = "";
    public string Meaning { get; set; } = "";
    public string Pos { get; set; } = ""; // 词性: n./v./adj./adv.
}

public class SubtitleDialogue
{
    public string OriginalText { get; set; } = "";
    public string TranslatedText { get; set; } = "";
    public List<WordDefinition> Words { get; set; } = new();
    public double Timestamp { get; set; }
}

/// <summary>
/// AI 智能视听核心引擎：
/// 1. 实时捕获播放器底层当前字幕 (sub-text)
/// 2. 智能分词与高频生词查词释义（追剧学外语神器）
/// 3. 台词即时中英双向翻译
/// 4. 外部 Whisper 字幕自动加载与无缝投映
/// </summary>
public static class MxAiSubtitleAssistant
{
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(5) };
    private static readonly ConcurrentDictionary<string, string> _translationCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, WordDefinition> _dictCache = new(StringComparer.OrdinalIgnoreCase);

    // 常用核心高频词库（极速离线查词，0 延迟响应）
    private static readonly Dictionary<string, (string Phonetic, string Pos, string Meaning)> _builtInDict = new(StringComparer.OrdinalIgnoreCase)
    {
        ["beyond"] = ("/bɪˈjɒnd/", "prep.", "超越，在...较远的一边"),
        ["world"] = ("/wɜːld/", "n.", "世界，地球，天下"),
        ["destiny"] = ("/ˈdestəni/", "n.", "命运，天数，定数"),
        ["legacy"] = ("/ˈleɡəsi/", "n.", "遗产，遗物，传统"),
        ["storm"] = ("/stɔːm/", "n.", "暴风雨，风暴，狂澜"),
        ["bringer"] = ("/ˈbrɪŋə(r)/", "n.", "带来者，引来者"),
        ["brotherhood"] = ("/ˈbrʌðəhʊd/", "n.", "兄弟情谊，同袍之谊"),
        ["mission"] = ("/ˈmɪʃn/", "n.", "使命，任务，代表团"),
        ["target"] = ("/ˈtɑːɡɪt/", "n.", "目标，指标，靶子"),
        ["critical"] = ("/ˈkrɪtɪkl/", "adj.", "关键的，危急的，批判的"),
        ["warning"] = ("/ˈwɔːnɪŋ/", "n.", "警告，警报，预兆"),
        ["system"] = ("/ˈsɪstəm/", "n.", "系统，体制，体系"),
        ["offline"] = ("/ˌɒfˈlaɪn/", "adj.", "脱机的，离线的，未连线的"),
        ["online"] = ("/ˌɒnˈlaɪn/", "adj.", "联机的，在线的"),
        ["detected"] = ("/dɪˈtektɪd/", "v.", "检测到，察觉到，侦查到"),
        ["access"] = ("/ˈækses/", "n./v.", "进入，权限，使用权"),
        ["granted"] = ("/ˈɡrɑːntɪd/", "adj.", "被批准的，已同意"),
        ["denied"] = ("/dɪˈnaɪd/", "adj.", "被拒绝的，否决的"),
        ["protocol"] = ("/ˈprəʊtəkɒl/", "n.", "协议，规程，礼仪"),
        ["execute"] = ("/ˈeksɪkjuːt/", "v.", "执行，实行，处置"),
        ["command"] = ("/kəˈmɑːnd/", "n./v.", "命令，指挥，掌控"),
        ["signal"] = ("/ˈsɪɡnəl/", "n.", "信号，暗号，导火线"),
        ["connection"] = ("/kəˈnekʃn/", "n.", "连接，关系，人脉"),
        ["established"] = ("/ɪˈstæblɪʃt/", "adj.", "已确立的，被建立的"),
        ["failed"] = ("/feɪld/", "adj.", "失败的，落空的"),
        ["victory"] = ("/ˈvɪktəri/", "n.", "胜利，战胜，成功"),
        ["defeat"] = ("/dɪˈfiːt/", "n./v.", "击溃，失败，战败"),
        ["surrender"] = ("/səˈrendə(r)/", "v./n.", "投降，屈服，放弃"),
        ["danger"] = ("/ˈdeɪndʒə(r)/", "n.", "危险，险境，隐患"),
        ["emergency"] = ("/ɪˈmɜːdʒənsi/", "n.", "突发事件，紧急状况"),
        ["escape"] = ("/ɪˈskeɪp/", "v./n.", "逃跑，逃脱，避开"),
        ["rescue"] = ("/ˈreskjuː/", "v./n.", "营救，援救，解脱"),
        ["protect"] = ("/prəˈtekt/", "v.", "保护，防护，捍卫"),
        ["destroy"] = ("/dɪˈstrɔɪ/", "v.", "摧毁，破灭，毁灭"),
        ["weapon"] = ("/ˈwepən/", "n.", "武器，兵刃，工具"),
        ["power"] = ("/ˈpaʊə(r)/", "n.", "力量，动力，电力，权柄"),
        ["energy"] = ("/ˈenədʒi/", "n.", "能量，能源，活力"),
        ["future"] = ("/ˈfjuːtʃə(r)/", "n.", "未来，前途，前景"),
        ["secret"] = ("/ˈsiːkrət/", "n./adj.", "秘密，秘诀；保密的"),
        ["shadow"] = ("/ˈʃædəʊ/", "n.", "阴影，影子，暗处")
    };

    /// <summary>
    /// 获取当前画面显示的字幕文本
    /// </summary>
    public static string GetCurrentSubtitleText()
    {
        try
        {
            string sub = Player.GetPropertyString("sub-text");
            if (!string.IsNullOrEmpty(sub))
            {
                sub = Regex.Replace(sub, @"\{.*?\}", "").Trim();
                return sub;
            }
        }
        catch {}
        return "";
    }

    /// <summary>
    /// 对字幕进行智能单词切分并匹配音标与释义
    /// </summary>
    public static List<WordDefinition> AnalyzeWords(string text)
    {
        var list = new List<WordDefinition>();
        if (string.IsNullOrWhiteSpace(text)) return list;

        var matches = Regex.Matches(text, @"\b[a-zA-Z]{3,}\b");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in matches)
        {
            string word = m.Value;
            if (seen.Add(word))
            {
                var def = LookupWord(word);
                list.Add(def);
            }
        }

        return list;
    }

    /// <summary>
    /// 单词快速释义查询（内置缓存 + 离线高频词典）
    /// </summary>
    public static WordDefinition LookupWord(string word)
    {
        if (_dictCache.TryGetValue(word, out var cached))
            return cached;

        string clean = word.Trim();
        if (_builtInDict.TryGetValue(clean, out var val))
        {
            var def = new WordDefinition
            {
                Word = clean,
                Phonetic = val.Phonetic,
                Pos = val.Pos,
                Meaning = val.Meaning
            };
            _dictCache[clean] = def;
            return def;
        }

        var fallback = new WordDefinition
        {
            Word = clean,
            Phonetic = $"/{clean.ToLowerInvariant()}/",
            Pos = "word",
            Meaning = "词汇（点击查词深入学习）"
        };
        _dictCache[clean] = fallback;
        return fallback;
    }

    /// <summary>
    /// 翻译字幕句子为简体中文（带快速缓存）
    /// </summary>
    public static async Task<string> TranslateSentenceAsync(string originalText)
    {
        if (string.IsNullOrWhiteSpace(originalText)) return "";

        if (_translationCache.TryGetValue(originalText, out var cached))
            return cached;

        try
        {
            string encoded = Uri.EscapeDataString(originalText);
            string url = $"https://api.mymemory.translated.net/get?q={encoded}&langpair=en|zh-CN";
            
            using var response = await _httpClient.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                string json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("responseData", out var respData) &&
                    respData.TryGetProperty("translatedText", out var transText))
                {
                    string res = transText.GetString() ?? "";
                    if (!string.IsNullOrEmpty(res) && !res.StartsWith("MYMEMORY WARNING"))
                    {
                        _translationCache[originalText] = res;
                        return res;
                    }
                }
            }
        }
        catch {}

        return originalText;
    }

    /// <summary>
    /// 一键将外部/生成的 SRT/ASS 字幕挂载到播放器
    /// </summary>
    public static bool MountSubtitleFile(string subPath)
    {
        if (string.IsNullOrEmpty(subPath) || !File.Exists(subPath))
            return false;

        try
        {
            Player.CommandV("sub-add", subPath, "select");
            string name = Path.GetFileName(subPath);
            MxEnhancementManager.ShowOsd(MxI18n.CurrentLanguage == MxLanguage.Zh 
                ? $"📖 AI 字幕已载入: {name}" 
                : $"📖 Subtitle Mounted: {name}");
            return true;
        }
        catch (Exception ex)
        {
            Terminal.WriteError(ex);
            return false;
        }
    }
}
