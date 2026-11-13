using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using BepInEx;
using BepInEx.Configuration;
using BokuMono;
using BokuMono.Data;
using BokuMono.Sound;
using HarmonyLib;
using UnityEngine;

namespace OliveTownQoL
{
    // Plays the player's own WAV songs through Windows (the game has Unity audio disabled).
    sealed class WavePlayer
    {
        [StructLayout(LayoutKind.Sequential)]
        struct WaveFormat { public ushort tag, channels; public uint rate, bytesPerSec; public ushort align, bits, cb; }
        [StructLayout(LayoutKind.Sequential)]
        struct WaveHdr { public IntPtr data; public uint length, recorded; public IntPtr user; public uint flags, loops; public IntPtr next, reserved; }

        [DllImport("winmm.dll")] static extern int waveOutOpen(out IntPtr h, uint dev, ref WaveFormat f, IntPtr cb, IntPtr inst, uint flags);
        [DllImport("winmm.dll")] static extern int waveOutPrepareHeader(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveOutUnprepareHeader(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveOutWrite(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveOutReset(IntPtr h);
        [DllImport("winmm.dll")] static extern int waveOutClose(IntPtr h);

        const int Frames = 4096, Bytes = Frames * 4, Count = 4;
        public volatile string Target;   // wav path, or null for silence
        public volatile float Volume = 1f;
        public volatile bool Paused;

        string _current; FileStream _fs; long _dataStart, _dataEnd;
        float _gain;     // fade gain 0..1
        Thread _thread;

        public void Start()
        {
            if (_thread != null) return;
            _thread = new Thread(Run) { IsBackground = true, Name = "OliveTownQoL music" };
            _thread.Start();
        }

        void Run()
        {
            var fmt = new WaveFormat { tag = 1, channels = 2, rate = 44100, bits = 16, align = 4, bytesPerSec = 44100 * 4 };
            if (waveOutOpen(out var h, 0xFFFFFFFF, ref fmt, IntPtr.Zero, IntPtr.Zero, 0) != 0) { Plugin.L.LogError("Music: could not open audio output"); return; }
            int hsz = Marshal.SizeOf<WaveHdr>();
            var hdrs = new IntPtr[Count]; var bufs = new IntPtr[Count];
            for (int i = 0; i < Count; i++)
            {
                bufs[i] = Marshal.AllocHGlobal(Bytes);
                hdrs[i] = Marshal.AllocHGlobal(hsz);
                Marshal.StructureToPtr(new WaveHdr { data = bufs[i], length = Bytes, flags = 1 /*DONE*/ }, hdrs[i], false);
            }
            var pcm = new byte[Bytes]; var prepared = new bool[Count];
            try
            {
                while (true)
                {
                    bool wrote = false;
                    for (int i = 0; i < Count; i++)
                    {
                        var hd = Marshal.PtrToStructure<WaveHdr>(hdrs[i]);
                        if ((hd.flags & 1) == 0) continue; // still playing
                        if (prepared[i]) waveOutUnprepareHeader(h, hdrs[i], hsz);
                        Fill(pcm);
                        Marshal.Copy(pcm, 0, bufs[i], Bytes);
                        Marshal.StructureToPtr(new WaveHdr { data = bufs[i], length = Bytes }, hdrs[i], false);
                        waveOutPrepareHeader(h, hdrs[i], hsz); prepared[i] = true;
                        waveOutWrite(h, hdrs[i], hsz);
                        wrote = true;
                    }
                    if (!wrote) Thread.Sleep(15);
                }
            }
            catch (Exception e) { Plugin.L.LogError("Music thread: " + e); }
            finally { try { waveOutReset(h); waveOutClose(h); } catch { } }
        }

        readonly byte[] _src = new byte[Bytes];

        int ReadBlock(int want)
        {
            int got = 0;
            while (got < want && _fs != null)
            {
                long left = _dataEnd - _fs.Position;
                if (left < 4) { _fs.Position = _dataStart; left = _dataEnd - _dataStart; if (left < 4) break; }
                int n = _fs.Read(_src, got, (int)Math.Min(want - got, left & ~3L));
                if (n <= 0) { _fs.Position = _dataStart; continue; }
                got += n;
            }
            return got;
        }

        void Fill(byte[] pcm)
        {
            const float step = 1f / (44100f * 1.2f); // ~1.2 s fades
            float vol = Math.Max(0f, Math.Min(1f, Volume));
            int f = 0;
            while (f < Frames)
            {
                string want = Paused ? _current : Target;
                bool switching = want != _current;
                if (switching && _gain <= 0f) Open(want);
                int chunk = Math.Min(Frames - f, 512);
                int got = (_fs != null && !Paused) ? ReadBlock(chunk * 4) / 4 : 0;
                for (int i = 0; i < chunk; i++)
                {
                    switching = want != _current;
                    float goal = (switching || Paused || _current == null) ? 0f : 1f;
                    if (_gain < goal) _gain = Math.Min(goal, _gain + step); else if (_gain > goal) _gain = Math.Max(goal, _gain - step);
                    short l = 0, r = 0;
                    if (i < got) { int s = i * 4; l = (short)(_src[s] | (_src[s + 1] << 8)); r = (short)(_src[s + 2] | (_src[s + 3] << 8)); }
                    float g = _gain * vol;
                    short ol = (short)(l * g), or = (short)(r * g);
                    int o = (f + i) * 4;
                    pcm[o] = (byte)ol; pcm[o + 1] = (byte)(ol >> 8); pcm[o + 2] = (byte)or; pcm[o + 3] = (byte)(or >> 8);
                }
                f += chunk;
            }
        }

        void Open(string path)
        {
            try { _fs?.Dispose(); } catch { }
            _fs = null; _current = path;
            if (path == null || !File.Exists(path)) return;
            try
            {
                var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
                var br = new BinaryReader(fs);
                fs.Position = 12;
                while (fs.Position + 8 <= fs.Length)
                {
                    var id = new string(br.ReadChars(4)); uint len = br.ReadUInt32();
                    if (id == "data") { _dataStart = fs.Position; _dataEnd = Math.Min(fs.Length, _dataStart + len); break; }
                    fs.Position += len + (len & 1);
                }
                fs.Position = _dataStart;
                _fs = fs;
            }
            catch (Exception e) { Plugin.L.LogError("Music: can't open " + path + ": " + e.Message); }
        }
    }

    static class MusicDirector
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> Volume;
        internal static ConfigEntry<string> Spring, Summer, Fall, Winter, Town, Evening;

        enum Ctx { Game, Outdoor, TownArea, Home, Mine }
        static Ctx _ctx = Ctx.Game;
        static readonly WavePlayer _player = new();
        static int _override = -1; // F9 pick: index into Songs, -1 = automatic
        static float _nextEval;
        static bool _selfStop;
        static SoundBgmController _ctl;
        static bool _loggedVol;
        static string _lastLogged;

        static string Dir => Path.Combine(Paths.PluginPath, "OliveTownQoL", "Music");
        static string[] Names => new[] { Spring.Value, Summer.Value, Fall.Value, Winter.Value, Town.Value, Evening.Value };
        static readonly string[] Labels = { "Spring", "Summer", "Fall", "Winter", "Town", "Evening" };

        internal static void Bind(ConfigFile c)
        {
            Enabled = c.Bind("MyMusic", "Enabled", true, "Play your own songs: season songs outdoors, Town song in town, Evening song at night outdoors; silence in bad weather, at home and in mines. Festivals, cutscenes, shops and DLC areas keep the game's music. Any song file that is missing plays the game's own music instead; with no songs at all, everything sounds like the normal game.");
            Volume = c.Bind("MyMusic", "Volume", 0.8f, "Volume of your songs (0-1). The game's own music volume setting is applied on top.");
            Spring = c.Bind("MyMusic", "Spring", "Spring.wav", "File in BepInEx/plugins/OliveTownQoL/Music");
            Summer = c.Bind("MyMusic", "Summer", "Summer.wav", "");
            Fall = c.Bind("MyMusic", "Fall", "Fall.wav", "");
            Winter = c.Bind("MyMusic", "Winter", "Winter.wav", "");
            Town = c.Bind("MyMusic", "Town", "Town.wav", "");
            Evening = c.Bind("MyMusic", "Evening", "Evening.wav", "");
        }

        static string P(string f) => string.IsNullOrWhiteSpace(f) ? null : Path.Combine(Dir, f);

        // Decide what a game music request means. Returns true if we take over.
        internal static bool Classify(BgmMasterData d)
        {
            if (!Enabled.Value || d == null || d.Scene != BgmMaster.Scene.Field) { _ctx = Ctx.Game; return false; }
            switch (d.BgmFieldId)
            {
                case 1001: case 1002: case 1003: case 1004:
                    _ctx = d.DayType == BgmMaster.DayType.Holidays ? Ctx.Game : Ctx.Outdoor; break;
                case 1011: _ctx = Ctx.TownArea; break;
                case 1000: _ctx = Ctx.Home; break;
                case 1005: case 1006: case 1021: _ctx = Ctx.Mine; break;
                default: _ctx = Ctx.Game; break;
            }
            return _ctx != Ctx.Game;
        }

        static bool IsNight()
        {
            try
            {
                if (!DateManager.IsInstance || !MasterDataManager.IsInstance) return false;
                var now = DateManager.Instance.Now;
                foreach (var b in MasterDataManager.Instance.BgmMasterData)
                    if (b != null && b.BgmFieldId == 1011 && b.Time == BgmMaster.Time.NigntTime)
                        return b.IsApplyTime(ref now);
            }
            catch { }
            return false;
        }

        static bool BadWeather()
        {
            try { return GameController.IsInstance && GameController.Instance.NowWeather != BokuMonoWeather.Sunny; } catch { return false; }
        }

        static string Season()
        {
            try
            {
                return DateManager.Instance.Now.Season switch
                {
                    BokuMonoSeason.Spring => Spring.Value,
                    BokuMonoSeason.Summer => Summer.Value,
                    BokuMonoSeason.Autumn => Fall.Value,
                    _ => Winter.Value
                };
            }
            catch { return Spring.Value; }
        }

        // Marker meaning "let the game play its own music here".
        const string GameMusic = "<game>";

        static float _songsCheckedAt = -100f;
        static bool _anySongs;
        static bool AnySongs()
        {
            if (Time.unscaledTime - _songsCheckedAt > 10f)
            {
                _songsCheckedAt = Time.unscaledTime;
                _anySongs = false;
                foreach (var n in Names) { var p = P(n); if (p != null && File.Exists(p)) { _anySongs = true; break; } }
            }
            return _anySongs;
        }

        // A song that isn't there falls back to the game's own music.
        static string Song(string file) { var p = P(file); return p != null && File.Exists(p) ? p : GameMusic; }

        static string Desired()
        {
            if (_ctx == Ctx.Game || !AnySongs()) return GameMusic; // no songs installed: game music everywhere
            if (_ctx == Ctx.Home || _ctx == Ctx.Mine) return _override >= 0 ? Song(Names[_override]) : null;
            if (_override >= 0) return Song(Names[_override]);
            if (BadWeather()) return null;
            if (IsNight()) return Song(Evening.Value);
            return Song(_ctx == Ctx.TownArea ? Town.Value : Season());
        }

        internal static void Tick()
        {
            if (Time.unscaledTime < _nextEval) return;
            _nextEval = Time.unscaledTime + 0.5f;
            _player.Start();
            float v = Volume.Value;
            try
            {
                if (SoundManager.IsInstance)
                {
                    var sm = SoundManager.Instance;
                    float bv = sm.BgmMasterVolume;
                    if (!_loggedVol) { _loggedVol = true; Plugin.L.LogInfo($"Game BGM volume setting = {bv}, mute = {sm.IsMuteBgm}"); }
                    if (bv > 1.001f) bv /= 10f;
                    v *= sm.IsMuteBgm ? 0f : Mathf.Clamp01(bv);
                }
            }
            catch { }
            _player.Volume = v;
            var t = Enabled.Value ? Desired() : GameMusic;
            if (t != _lastLogged)
            {
                bool wasGame = _lastLogged == GameMusic;
                _lastLogged = t;
                Plugin.L.LogInfo($"My music: {(t == GameMusic ? "game music" : t == null ? "silence" : Path.GetFileName(t))} [{_ctx}]");
                // Hand over between the game's music and ours when the situation changes (night, weather, F9...).
                if (_ctx != Ctx.Game && _ctl != null)
                {
                    if (t == GameMusic && !wasGame) RestartGameMusic();
                    else if (t != GameMusic && wasGame) StopGame(_ctl);
                }
            }
            _player.Target = t == GameMusic ? null : t;
        }

        internal static void Cycle()
        {
            if (_ctx == Ctx.Game) { Toast.Show("The game's own music is playing here"); return; }
            _override++;
            if (_override >= Labels.Length) _override = -1;
            _nextEval = 0;
            Toast.Show(_override < 0 ? "Music: automatic" : $"Music: {Labels[_override]}  (F9 again for next)", 3f);
        }

        static bool _restarting, _inPlay;
        static void RestartGameMusic()
        {
            _restarting = true;
            try { if (SoundManager.IsInstance && FieldManager.IsInstance) SoundManager.Instance.PlayFieldBgm(FieldManager.Instance.CurrentFieldId); }
            catch { }
            finally { _restarting = false; }
        }

        internal static void StopGame(SoundBgmController ctl)
        {
            _selfStop = true;
            try { ctl.StopBgm(true, 1f); } catch { } finally { _selfStop = false; }
        }

        [HarmonyPatch(typeof(SoundBgmController), nameof(SoundBgmController.PlayBgm))]
        static class PlayPatch
        {
            [HarmonyPriority(Priority.First)]
            static bool Prefix(SoundBgmController __instance, BgmMasterData bgmData)
            {
                _ctl = __instance;
                try
                {
                    _inPlay = true;
                    if (_restarting || !Classify(bgmData)) return true;
                    _nextEval = 0;
                    var want = Enabled.Value ? Desired() : GameMusic;
                    _lastLogged = want == GameMusic ? GameMusic : "<pending>";
                    if (want == GameMusic) return true; // song missing: keep the game's track
                    StopGame(__instance);
                    return false;
                }
                catch (Exception e) { Plugin.L.LogError(e); return true; }
            }

            static void Postfix() => _inPlay = false;
        }

        [HarmonyPatch(typeof(SoundBgmController), nameof(SoundBgmController.StopBgm))]
        static class StopPatch
        {
            // The game silencing music (e.g. a cutscene) silences ours too.
            static void Postfix() { if (!_selfStop && !_inPlay && !_restarting) { _ctx = Ctx.Game; _nextEval = 0; } }
        }

        [HarmonyPatch(typeof(SoundBgmController), nameof(SoundBgmController.PauseBgm))]
        static class PausePatch { static void Postfix() => _player.Paused = true; }

        [HarmonyPatch(typeof(SoundBgmController), nameof(SoundBgmController.ResumeBgm))]
        static class ResumePatch { static void Postfix() => _player.Paused = false; }
    }
}
