using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MpvNet;
using static MpvNet.Global;

namespace MpvNet.Windows.AiFeatures;

public class SubTrackInfo
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Lang { get; set; } = "";
    public bool Selected { get; set; }
    public bool IsExternal { get; set; }
}

/// <summary>
/// 方向三：字幕引擎全能套件
/// 1. 双字幕智能分层并显与排版（支持外语在上、中文在下的双语追剧学习排版）
/// 2. 射手网 (Shooter.cn) / 云端字幕智能秒搜与一键挂载
/// </summary>
public static class MxSubtitleSuite
{
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(6) };
    private static int _dualSubMode = 0; // 0=Off, 1=Split(Top/Bottom), 2=Stacked(Both Bottom)

    /// <summary>
    /// 一键开启/切换双字幕并显模式 (Shift+D)
    /// </summary>
    public static void ToggleDualSubtitles()
    {
        try
        {
            var tracks = GetSubtitleTracks();
            if (tracks.Count == 0)
            {
                MxEnhancementManager.ShowOsd(MxI18n.CurrentLanguage == MxLanguage.Zh 
                    ? "🌐 当前视频无字幕轨，请按 Ctrl+Shift+D 载入或搜索字幕！" 
                    : "🌐 No subtitles found. Press Ctrl+Shift+D to load!");
                return;
            }

            if (tracks.Count == 1)
            {
                MxEnhancementManager.ShowOsd(MxI18n.CurrentLanguage == MxLanguage.Zh 
                    ? "🌐 仅检测到 1 条字幕轨，双字幕需至少 2 条字幕（已载入主字幕）" 
                    : "🌐 Only 1 subtitle track detected. Need 2 tracks for dual mode.");
                return;
            }

            // 切换状态: 0 -> 1(上下分层) -> 2(底部堆叠) -> 0(关闭)
            _dualSubMode = (_dualSubMode + 1) % 3;

            if (_dualSubMode == 0)
            {
                // 关闭双字幕
                Player.SetPropertyString("secondary-sid", "no");
                Player.SetPropertyInt("sub-pos", 100);
                MxEnhancementManager.ShowOsd(MxI18n.CurrentLanguage == MxLanguage.Zh 
                    ? "🌐 双字幕 [已关闭]: 恢复原生单字幕" 
                    : "🌐 Dual Subtitles: OFF");
            }
            else if (_dualSubMode == 1)
            {
                // 模式 1: 上下分层排版 (中文在底 sub-pos=100, 外语在顶 secondary-sub-pos=10)
                ApplyDualTracks(tracks, secondaryPos: 10, primaryPos: 100);
                MxEnhancementManager.ShowOsd(MxI18n.CurrentLanguage == MxLanguage.Zh 
                    ? "🌐 双字幕 [上下分层]: [底]主字幕 + [顶]次字幕 (学习模式)" 
                    : "🌐 Dual Subtitles: Top / Bottom Split");
            }
            else if (_dualSubMode == 2)
            {
                // 模式 2: 底部紧凑堆叠 (主字幕在底 sub-pos=98, 次字幕紧邻上方 secondary-sub-pos=82)
                ApplyDualTracks(tracks, secondaryPos: 82, primaryPos: 98);
                MxEnhancementManager.ShowOsd(MxI18n.CurrentLanguage == MxLanguage.Zh 
                    ? "🌐 双字幕 [底部堆叠]: 双语同行紧凑排版" 
                    : "🌐 Dual Subtitles: Stacked Bottom");
            }
        }
        catch (Exception ex)
        {
            Terminal.WriteError(ex);
        }
    }

    private static void ApplyDualTracks(List<SubTrackInfo> tracks, int secondaryPos, int primaryPos)
    {
        // 智能甄选：首选把中文定为主字幕(底)，外语/英文定为次字幕(顶)
        int primaryId = -1;
        int secondaryId = -1;

        // 寻找当前选中的主字幕
        var currentSelected = tracks.FirstOrDefault(t => t.Selected);
        if (currentSelected != null)
        {
            primaryId = currentSelected.Id;
        }

        // 若主字幕没选定，优先找中文
        if (primaryId == -1)
        {
            var zhTrack = tracks.FirstOrDefault(t => IsChinese(t));
            primaryId = zhTrack?.Id ?? tracks[0].Id;
        }

        // 次字幕：找与主字幕不同的轨道，优先找英文/外语
        var otherTracks = tracks.Where(t => t.Id != primaryId).ToList();
        if (otherTracks.Count > 0)
        {
            var engTrack = otherTracks.FirstOrDefault(t => IsEnglish(t));
            secondaryId = engTrack?.Id ?? otherTracks[0].Id;
        }

        if (primaryId > 0)
            Player.SetPropertyInt("sid", primaryId);

        if (secondaryId > 0)
        {
            Player.SetPropertyInt("secondary-sid", secondaryId);
            Player.SetPropertyInt("secondary-sub-pos", secondaryPos);
            Player.SetPropertyInt("sub-pos", primaryPos);
        }
    }

    private static bool IsChinese(SubTrackInfo t)
    {
        string s = $"{t.Title} {t.Lang}".ToLowerInvariant();
        return s.Contains("chi") || s.Contains("zh") || s.Contains("chs") || s.Contains("cht") || s.Contains("中文") || s.Contains("简") || s.Contains("繁");
    }

    private static bool IsEnglish(SubTrackInfo t)
    {
        string s = $"{t.Title} {t.Lang}".ToLowerInvariant();
        return s.Contains("eng") || s.Contains("en") || s.Contains("英文") || s.Contains("english");
    }

    /// <summary>
    /// 获取播放器当前载入的所有字幕轨
    /// </summary>
    public static List<SubTrackInfo> GetSubtitleTracks()
    {
        var list = new List<SubTrackInfo>();
        try
        {
            int count = Player.GetPropertyInt("track-list/count");
            for (int i = 0; i < count; i++)
            {
                string type = Player.GetPropertyString($"track-list/{i}/type");
                if (type == "sub")
                {
                    int id = Player.GetPropertyInt($"track-list/{i}/id");
                    string title = Player.GetPropertyString($"track-list/{i}/title") ?? "";
                    string lang = Player.GetPropertyString($"track-list/{i}/lang") ?? "";
                    bool selected = Player.GetPropertyBool($"track-list/{i}/selected");
                    bool external = Player.GetPropertyBool($"track-list/{i}/external");

                    list.Add(new SubTrackInfo
                    {
                        Id = id,
                        Title = string.IsNullOrEmpty(title) ? (string.IsNullOrEmpty(lang) ? $"字幕轨 #{id}" : lang) : title,
                        Lang = lang,
                        Selected = selected,
                        IsExternal = external
                    });
                }
            }
        }
        catch {}
        return list;
    }

    /// <summary>
    /// 射手网 (Shooter.cn) 在线字幕智能秒搜与自动下载挂载
    /// </summary>
    public static async Task<bool> SearchAndMountShooterSubtitlesAsync(string videoPath)
    {
        if (string.IsNullOrEmpty(videoPath) || !File.Exists(videoPath))
            return false;

        try
        {
            MxEnhancementManager.ShowOsd(MxI18n.CurrentLanguage == MxLanguage.Zh 
                ? "🔍 正在连接射手字幕库秒搜精准字幕..." 
                : "🔍 Searching Shooter.cn cloud subtitles...");

            string fileHash = ComputeShooterHash(videoPath);
            if (string.IsNullOrEmpty(fileHash)) return false;

            string fileName = Path.GetFileName(videoPath);
            var form = new Dictionary<string, string>
            {
                ["filehash"] = fileHash,
                ["pathinfo"] = fileName,
                ["format"] = "json"
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, "https://www.shooter.cn/api/subapi.php")
            {
                Content = new FormUrlEncodedContent(form)
            };

            var resp = await _httpClient.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
            {
                MxEnhancementManager.ShowOsd(MxI18n.CurrentLanguage == MxLanguage.Zh ? "⚠️ 在线字幕服务器响应超时" : "⚠️ Cloud subtitle timeout");
                return false;
            }

            string json = await resp.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(json) || json.Length < 10)
            {
                MxEnhancementManager.ShowOsd(MxI18n.CurrentLanguage == MxLanguage.Zh ? "ℹ️ 射手库暂无该视频精准匹配字幕" : "ℹ️ No matching cloud subtitles");
                return false;
            }

            // 解析返回的字幕文件下载直链
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
            {
                var firstMatch = doc.RootElement[0];
                if (firstMatch.TryGetProperty("Files", out var filesElem) && filesElem.GetArrayLength() > 0)
                {
                    var fileObj = filesElem[0];
                    string subExt = fileObj.GetProperty("Ext").GetString() ?? "srt";
                    string link = fileObj.GetProperty("Link").GetString() ?? "";

                    if (!string.IsNullOrEmpty(link))
                    {
                        byte[] subBytes = await _httpClient.GetByteArrayAsync(link);
                        if (subBytes != null && subBytes.Length > 0)
                        {
                            // 存储到本地专用字幕缓存目录
                            string cacheSubDir = Path.Combine(Player.ConfigFolder, "subtitles");
                            if (!Directory.Exists(cacheSubDir))
                                Directory.CreateDirectory(cacheSubDir);

                            string cleanName = Path.GetFileNameWithoutExtension(videoPath);
                            string savedSub = Path.Combine(cacheSubDir, $"{cleanName}.shooter.{subExt}");
                            await File.WriteAllBytesAsync(savedSub, subBytes);

                            // 瞬间在播放器中挂载并选定
                            MxAiSubtitleAssistant.MountSubtitleFile(savedSub);
                            MxEnhancementManager.ShowOsd(MxI18n.CurrentLanguage == MxLanguage.Zh 
                                ? "🎉 射手字幕已自动秒级匹配并挂载！" 
                                : "🎉 Shooter subtitle auto-mounted!");
                            return true;
                        }
                    }
                }
            }

            MxEnhancementManager.ShowOsd(MxI18n.CurrentLanguage == MxLanguage.Zh ? "ℹ️ 未在射手库找到有效字幕" : "ℹ️ Subtitle not found");
            return false;
        }
        catch (Exception ex)
        {
            Terminal.WriteError(ex);
            MxEnhancementManager.ShowOsd(MxI18n.CurrentLanguage == MxLanguage.Zh ? "⚠️ 在线字幕检索失败" : "⚠️ Subtitle search failed");
            return false;
        }
    }

    /// <summary>
    /// 计算标准的射手网 4 点特征 MD5 Hash (4KB, 2/3处, 1/3处, 末尾)
    /// </summary>
    public static string ComputeShooterHash(string filePath)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            long len = fs.Length;
            if (len < 8192) return "";

            long[] offsets = new long[]
            {
                4096,
                (long)(len / 3.0 * 2.0),
                (long)(len / 3.0),
                len - 8192
            };

            using var md5 = MD5.Create();
            var hashes = new List<string>();
            byte[] buf = new byte[4096];

            foreach (var off in offsets)
            {
                fs.Seek(off, SeekOrigin.Begin);
                int read = fs.Read(buf, 0, 4096);
                byte[] hashBytes = md5.ComputeHash(buf, 0, read);
                hashes.Add(BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant());
            }

            return string.Join(";", hashes);
        }
        catch
        {
            return "";
        }
    }
}
