using System;
using MpvNet.Windows.WinForms;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MpvNet;
using MpvNet.Windows.WinForms;
using static MpvNet.Global;

namespace MpvNet.Windows.AiFeatures;

/// <summary>
/// 方向六：手机局域网无线遥控器 Web 服务端
/// 零依赖内嵌 HttpListener，提供现代触摸手势遥控网页与媒体库点播 API
/// </summary>
public static class MxWebRemoteServer
{
    private static HttpListener? _listener;
    private static CancellationTokenSource? _cts;
    private static bool _isRunning = false;
    private const int Port = 8999;

    public static bool IsRunning => _isRunning;

    public static string GetPrimaryLocalIp()
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
            socket.Connect("8.8.8.8", 65530);
            var endPoint = socket.LocalEndPoint as IPEndPoint;
            if (endPoint != null && !IPAddress.IsLoopback(endPoint.Address))
                return endPoint.Address.ToString();
        }
        catch {}

        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                foreach (var ip in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ip.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip.Address))
                    {
                        string str = ip.Address.ToString();
                        if (!str.StartsWith("169.254"))
                            return str;
                    }
                }
            }
        }
        catch {}

        return "127.0.0.1";
    }

    public static string GetServerUrl()
    {
        return $"http://{GetPrimaryLocalIp()}:{Port}/";
    }

    public static void StartServer()
    {
        if (_isRunning) return;

        try
        {
            _cts = new CancellationTokenSource();
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://*:{Port}/");
            _listener.Start();
            _isRunning = true;

            Task.Run(() => ListenLoopAsync(_cts.Token));
        }
        catch (Exception)
        {
            // 若 http://*:8999/ 需要特权，自动降级为 localhost 与本机网卡 IP
            try
            {
                _listener?.Close();
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://localhost:{Port}/");
                _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
                string localIp = GetPrimaryLocalIp();
                if (localIp != "127.0.0.1")
                {
                    _listener.Prefixes.Add($"http://{localIp}:{Port}/");
                }
                _listener.Start();
                _isRunning = true;
                Task.Run(() => ListenLoopAsync(_cts!.Token));
            }
            catch (Exception ex2)
            {
                Terminal.WriteError(ex2);
                _isRunning = false;
            }
        }
    }

    public static void StopServer()
    {
        if (!_isRunning) return;
        try
        {
            _isRunning = false;
            _cts?.Cancel();
            _listener?.Stop();
            _listener?.Close();
        }
        catch {}
    }

    private static async Task ListenLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _listener != null && _listener.IsListening)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                Task.Run(() => HandleRequest(context));
            }
            catch when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception)
            {
                await Task.Delay(100, token);
            }
        }
    }

    private static void HandleRequest(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var res = ctx.Response;

        // CORS 允许
        res.Headers.Add("Access-Control-Allow-Origin", "*");
        res.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
        res.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

        if (req.HttpMethod == "OPTIONS")
        {
            res.StatusCode = 204;
            res.Close();
            return;
        }

        try
        {
            string path = req.Url?.AbsolutePath.ToLowerInvariant() ?? "/";

            if (path == "/" || path == "/index.html")
            {
                byte[] htmlBytes = Encoding.UTF8.GetBytes(GetRemoteHtml());
                res.ContentType = "text/html; charset=utf-8";
                res.ContentLength64 = htmlBytes.Length;
                res.OutputStream.Write(htmlBytes, 0, htmlBytes.Length);
            }
            else if (path == "/api/status")
            {
                var status = new
                {
                    playing = !Player.GetPropertyBool("pause"),
                    path = Player.Path,
                    title = Player.GetPropertyString("media-title") ?? Path.GetFileName(Player.Path),
                    time = Player.GetPropertyDouble("time-pos"),
                    duration = Player.Duration.TotalSeconds,
                    volume = Player.GetPropertyDouble("volume"),
                    subText = Player.GetPropertyString("sub-text") ?? "",
                    pip = MainForm.Instance?.IsPipMode ?? false
                };
                byte[] data = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(status));
                res.ContentType = "application/json; charset=utf-8";
                res.ContentLength64 = data.Length;
                res.OutputStream.Write(data, 0, data.Length);
            }
            else if (path == "/api/cmd")
            {
                using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
                string body = reader.ReadToEnd();
                var doc = JsonDocument.Parse(body);
                string action = doc.RootElement.GetProperty("action").GetString() ?? "";

                switch (action)
                {
                    case "toggle":
                        Player.CommandV("cycle", "pause");
                        break;
                    case "seek":
                        double val = doc.RootElement.GetProperty("value").GetDouble();
                        Player.CommandV("seek", val.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        break;
                    case "volume":
                        double volDelta = doc.RootElement.GetProperty("value").GetDouble();
                        Player.CommandV("add", "volume", volDelta.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        break;
                    case "fullscreen":
                        Player.CommandV("cycle", "fullscreen");
                        break;
                    case "pip":
                        MainForm.Instance?.BeginInvoke(new Action(() => MainForm.Instance.TogglePipMode()));
                        break;
                    case "anime4k":
                        MainForm.Instance?.BeginInvoke(new Action(() => MxShaderManager.CycleAnime4K()));
                        break;
                    case "fsr":
                        MainForm.Instance?.BeginInvoke(new Action(() => MxShaderManager.ToggleAmdFsr()));
                        break;
                    case "night_mode":
                        MainForm.Instance?.BeginInvoke(new Action(() => MxEnhancementManager.ToggleNightMode()));
                        break;
                    case "dual_sub":
                        MainForm.Instance?.BeginInvoke(new Action(() => MxSubtitleSuite.ToggleDualSubtitles()));
                        break;
                }

                byte[] ok = Encoding.UTF8.GetBytes("{\"success\":true}");
                res.ContentType = "application/json";
                res.OutputStream.Write(ok, 0, ok.Length);
            }
            else
            {
                res.StatusCode = 404;
            }
        }
        catch (Exception ex)
        {
            res.StatusCode = 500;
            byte[] err = Encoding.UTF8.GetBytes(ex.Message);
            res.OutputStream.Write(err, 0, err.Length);
        }
        finally
        {
            res.Close();
        }
    }

    private static string GetRemoteHtml()
    {
        return @"<!DOCTYPE html>
<html lang=""zh-CN"">
<head>
<meta charset=""UTF-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=no"">
<title>mpv.net MX 手机无线遥控器</title>
<style>
  * { box-sizing: border-box; -webkit-tap-highlight-color: transparent; margin: 0; padding: 0; }
  body { background: #0E0E12; color: #ECECF2; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; min-height: 100vh; display: flex; flex-direction: column; }
  header { padding: 16px 20px; background: #181822; border-bottom: 1px solid #282838; display: flex; align-items: center; justify-content: space-between; }
  .logo { font-size: 17px; font-weight: 800; color: #E50914; display: flex; align-items: center; gap: 6px; }
  .logo span { color: #38BDF8; font-size: 11px; background: #26263A; padding: 2px 6px; border-radius: 4px; font-weight: 600; }
  .now-playing { padding: 18px 20px; background: #13131C; border-bottom: 1px solid #20202E; }
  .title { font-size: 15px; font-weight: 600; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; margin-bottom: 8px; }
  .timeline { display: flex; align-items: center; justify-content: space-between; font-size: 12px; color: #94A3B8; font-variant-numeric: tabular-nums; }
  
  /* 触摸控制手势大面板 */
  .pad-container { flex: 1; padding: 16px; display: flex; flex-direction: column; gap: 12px; }
  .touch-pad { flex: 1; min-height: 180px; background: #1A1A26; border: 2px dashed #303046; border-radius: 16px; display: flex; flex-direction: column; align-items: center; justify-content: center; color: #64748B; font-size: 13px; text-align: center; gap: 6px; touch-action: none; user-select: none; }
  .touch-pad.active { border-color: #E50914; background: #221D28; color: #E50914; }
  
  /* 基础播放控制栏 */
  .controls-row { display: grid; grid-template-columns: repeat(3, 1fr); gap: 10px; }
  button { background: #1E1E2C; color: #F1F5F9; border: 1px solid #32324A; border-radius: 12px; padding: 14px 10px; font-size: 14px; font-weight: 600; display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 4px; cursor: pointer; transition: all 0.1s ease; }
  button:active { transform: scale(0.96); background: #2E2E44; border-color: #E50914; color: #E50914; }
  button.primary { background: #E50914; border-color: #E50914; color: #FFFFFF; }
  button.primary:active { background: #B80710; }
  
  /* 画质与增强矩阵 */
  .section-label { font-size: 12px; font-weight: 700; color: #64748B; text-transform: uppercase; margin-top: 4px; }
  .feature-grid { display: grid; grid-template-columns: repeat(2, 1fr); gap: 8px; }
  .feature-grid button { padding: 12px 8px; font-size: 13px; flex-direction: row; gap: 6px; }
  
  .sub-box { margin-top: auto; padding: 12px 16px; background: #121218; border-top: 1px solid #20202E; font-size: 13px; color: #F59E0B; text-align: center; min-height: 44px; display: flex; align-items: center; justify-content: center; }
</style>
</head>
<body>
<header>
  <div class=""logo"">mpv.net <span>MX REMOTE</span></div>
  <div style=""font-size:12px; color:#10B981; font-weight:600;"">● 已连接</div>
</header>

<div class=""now-playing"">
  <div class=""title"" id=""mediaTitle"">正在加载当前视频...</div>
  <div class=""timeline"">
    <span id=""timePos"">00:00</span>
    <span id=""timeDur"">00:00</span>
  </div>
</div>

<div class=""pad-container"">
  <div class=""touch-pad"" id=""touchPad"">
    <div style=""font-size: 28px;"">🖐</div>
    <div>手势触控板</div>
    <div style=""font-size: 11px; color: #475569;"">左右轻扫快进退 · 上下滑动调音量 · 单击播放暂停</div>
  </div>

  <div class=""controls-row"">
    <button onclick=""sendCmd('seek', -10)"">⏪ -10s</button>
    <button class=""primary"" id=""btnToggle"" onclick=""sendCmd('toggle')"">⏯ 播放/暂停</button>
    <button onclick=""sendCmd('seek', 10)"">⏩ +10s</button>
  </div>

  <div class=""controls-row"">
    <button onclick=""sendCmd('volume', -5)"">🔉 音量 -</button>
    <button onclick=""sendCmd('fullscreen')"">⛶ 全屏切换</button>
    <button onclick=""sendCmd('volume', 5)"">🔊 音量 +</button>
  </div>

  <div class=""section-label"">⚡ MX 画质与神级增强</div>
  <div class=""feature-grid"">
    <button onclick=""sendCmd('night_mode')"">🎧 人声增强</button>
    <button onclick=""sendCmd('dual_sub')"">🌐 双字幕并显</button>
    <button onclick=""sendCmd('anime4k')"">🌸 Anime4K 循环</button>
    <button onclick=""sendCmd('fsr')"">🎮 AMD FSR</button>
    <button onclick=""sendCmd('pip')"" style=""grid-column: span 2;"">📌 切换桌面画中画小窗</button>
  </div>
</div>

<div class=""sub-box"" id=""subText"">当前无字幕</div>

<script>
let startX = 0, startY = 0;
const pad = document.getElementById('touchPad');

pad.addEventListener('touchstart', e => {
  startX = e.touches[0].clientX;
  startY = e.touches[0].clientY;
  pad.classList.add('active');
}, {passive:true});

pad.addEventListener('touchend', e => {
  pad.classList.remove('active');
  const dx = e.changedTouches[0].clientX - startX;
  const dy = e.changedTouches[0].clientY - startY;

  if (Math.abs(dx) < 10 && Math.abs(dy) < 10) {
    sendCmd('toggle');
  } else if (Math.abs(dx) > Math.abs(dy)) {
    if (dx > 30) sendCmd('seek', 10);
    else if (dx < -30) sendCmd('seek', -10);
  } else {
    if (dy < -30) sendCmd('volume', 5);
    else if (dy > 30) sendCmd('volume', -5);
  }
}, {passive:true});

function sendCmd(action, value = 0) {
  fetch('/api/cmd', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ action, value })
  }).catch(()=>{});
}

function formatTime(s) {
  if (!s || s <= 0) return '00:00';
  const m = Math.floor(s / 60);
  const sec = Math.floor(s % 60);
  return String(m).padStart(2,'0') + ':' + String(sec).padStart(2,'0');
}

setInterval(async () => {
  try {
    const res = await fetch('/api/status');
    const data = await res.json();
    document.getElementById('mediaTitle').innerText = data.title || '空闲待命中';
    document.getElementById('timePos').innerText = formatTime(data.time);
    document.getElementById('timeDur').innerText = formatTime(data.duration);
    document.getElementById('subText').innerText = data.subText ? ('💬 ' + data.subText) : '当前无台词';
    document.getElementById('btnToggle').innerText = data.playing ? '⏸ 暂停' : '▶ 播放';
  } catch {}
}, 1000);
</script>
</body>
</html>";
    }
}
