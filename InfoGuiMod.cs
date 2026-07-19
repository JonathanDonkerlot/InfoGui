using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using MelonLoader;
using UnityEngine;

namespace Main
{
    public sealed class InfoGuiMod
    {
        // --- Config ---
        private const float ModsRefreshInterval = 30.0f;
        private const float StatsRefreshInterval = 2.0f;
        private const int WindowId = 88421;

        // --- GUI state ---
        private Rect _window = new Rect(20f, 20f, 280f, 0f);
        private bool _visible = true;
        private bool _modsExpanded;
        private Vector2 _modsScroll;
        private bool _autoSize = true;

        // --- Pre-allocated GUIContent for allocation-free OnGUI rendering ---
        private readonly GUIContent _fpsKeyContent = new GUIContent("FPS");
        private readonly GUIContent _fpsValueContent = new GUIContent("...");
        
        private readonly GUIContent _timeKeyContent = new GUIContent("Uptime");
        private readonly GUIContent _timeValueContent = new GUIContent("...");
        
        private readonly GUIContent _playersKeyContent = new GUIContent("Active Players");
        private readonly GUIContent _playersValueContent = new GUIContent("...");
        
        private readonly GUIContent _chunkKeyContent = new GUIContent("Current Chunk");
        private readonly GUIContent _chunkValueContent = new GUIContent("...");

        private readonly GUIContent _modsButtonContent = new GUIContent("Mods (...)");
        private readonly List<GUIContent> _modNameContents = new List<GUIContent>();

        // --- Smoothed / State Values ---
        private float _fpsSmoothed;
        private int _lastSecond = -1;
        private string[] _cachedModNames = Array.Empty<string>();

        // --- Timers ---
        private float _modsTimer;
        private float _statsTimer;
        private float _fpsDisplayTimer;

        // --- Cached reflection accessors ---
        private Type _playerType;
        private bool _playerTypeLookedUp;
        private MemberAccessor _allPlayersAccessor;
        private MemberAccessor _currentPlayerAccessor;
        private MemberAccessor _positionAccessor;
        private MemberAccessor _transformAccessor;

        // --- UI Textures ---
        private Texture2D _bgTex;
        private Texture2D _accentTex;
        private Texture2D _btnNormalTex;
        private Texture2D _btnHoverTex;
        private Texture2D _btnActiveTex;
        private Color _borderColor = new Color(0.16f, 0.18f, 0.24f, 0.9f);
        private Color _separatorColor = new Color(0.16f, 0.18f, 0.24f, 0.6f);

        // --- GUIStyles ---
        private GUIStyle _headerStyle;
        private GUIStyle _keyStyle;
        private GUIStyle _valueStyle;
        private GUIStyle _modLabelStyle;
        private GUIStyle _buttonStyle;
        private bool _stylesBuilt;

        public void Init()
        {
            MelonLogger.Msg("[InfoGui] Loaded — Press I to show/hide the HUD.");
            RefreshModsList();
        }

        public void OnUpdate()
        {
            if (InputHelper.GetKeyDown(UnityEngine.InputSystem.Key.I))
            {
                _visible = !_visible;
                _autoSize = true;
            }

            if (!_visible) return;

            float dt = Time.unscaledDeltaTime;

            // 1. Smooth FPS calculation (every frame)
            if (dt > 0.0001f)
            {
                float instant = 1f / dt;
                _fpsSmoothed = _fpsSmoothed <= 0f ? instant : Mathf.Lerp(_fpsSmoothed, instant, 0.08f);
            }

            // 2. Rebuild FPS string (4x a second, not every frame)
            _fpsDisplayTimer += dt;
            if (_fpsDisplayTimer >= 0.25f)
            {
                _fpsDisplayTimer = 0f;
                _fpsValueContent.text = Mathf.RoundToInt(_fpsSmoothed).ToString();
            }

            // 3. PC Uptime formatting (only once per second)
            uint uptimeMs = (uint)Environment.TickCount;
            int uptimeSeconds = (int)(uptimeMs / 1000f);
            if (uptimeSeconds != _lastSecond)
            {
                _lastSecond = uptimeSeconds;
                TimeSpan uptime = TimeSpan.FromMilliseconds(uptimeMs);
                if (uptime.TotalDays >= 1.0)
                {
                    _timeValueContent.text = string.Format("{0}d {1:D2}:{2:D2}:{3:D2}", (int)uptime.TotalDays, uptime.Hours, uptime.Minutes, uptime.Seconds);
                }
                else
                {
                    _timeValueContent.text = string.Format("{0:D2}:{1:D2}:{2:D2}", (int)uptime.TotalHours, uptime.Minutes, uptime.Seconds);
                }
            }

            // 4. Online stats & chunk updates (every 2s)
            _statsTimer += dt;
            if (_statsTimer >= StatsRefreshInterval)
            {
                _statsTimer = 0f;
                _playersValueContent.text = GetOnlinePlayerCount();
                _chunkValueContent.text = GetLocalChunkLabel();
            }

            // 5. Scan mods folder (every 30s)
            _modsTimer += dt;
            if (_modsTimer >= ModsRefreshInterval)
            {
                _modsTimer = 0f;
                RefreshModsList();
            }
        }

        public void OnGUI()
        {
            if (!_visible) return;

            EnsureStyles();

            // Auto-calculate height on expand/collapse
            if (_autoSize)
            {
                _window.height = 0f;
                _autoSize = false;
            }

            // Draw window using GUIStyle.none to strip the default Unity frame
            _window = GUILayout.Window(WindowId, _window, DrawWindow, "", GUIStyle.none, GUILayout.MinWidth(280f));
        }

        // ============================================================
        //  HUD Rendering - Completely GC Allocation Free
        // ============================================================
        private void DrawWindow(int id)
        {
            // 1. Draw Glassmorphism Custom HUD Panel
            Rect rect = new Rect(0, 0, _window.width, _window.height);
            if (_bgTex != null) GUI.DrawTexture(rect, _bgTex);
            
            // Draw 1px border
            DrawRectBorder(rect, _borderColor, 1f);

            // Draw cyan top accent strip
            if (_accentTex != null)
            {
                Rect accentRect = new Rect(0, 0, _window.width, 3f);
                GUI.DrawTexture(accentRect, _accentTex);
            }

            // 2. Draw Title Header (Simplified to "Game info")
            GUILayout.Space(8f);
            GUILayout.Label("Game info", _headerStyle);
            DrawSeparator();

            // 3. Draw Telemetry Fields
            DrawRow(_fpsKeyContent, _fpsValueContent);
            DrawRow(_timeKeyContent, _timeValueContent);
            DrawRow(_playersKeyContent, _playersValueContent);
            DrawRow(_chunkKeyContent, _chunkValueContent);

            DrawSeparator();

            // 4. Draw Mods Collapse Section
            if (GUILayout.Button(_modsButtonContent, _buttonStyle))
            {
                _modsExpanded = !_modsExpanded;
                _autoSize = true;
            }

            if (_modsExpanded && _modNameContents.Count > 0)
            {
                float listHeight = _modNameContents.Count * 18f;
                bool needsScroll = listHeight > 144f;

                if (needsScroll)
                {
                    _modsScroll = GUILayout.BeginScrollView(_modsScroll, GUILayout.Height(144f));
                }

                for (int i = 0; i < _modNameContents.Count; i++)
                {
                    GUILayout.Label(_modNameContents[i], _modLabelStyle);
                }

                if (needsScroll)
                {
                    GUILayout.EndScrollView();
                }
                
                GUILayout.Space(4f);
            }

            GUILayout.Space(6f);

            // Window drag region (entire header bar area is draggable)
            GUI.DragWindow(new Rect(0, 0, 10000f, 30f));
        }

        private void DrawRow(GUIContent key, GUIContent value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(key, _keyStyle);
            GUILayout.FlexibleSpace();
            GUILayout.Label(value, _valueStyle);
            GUILayout.EndHorizontal();
        }

        private void DrawSeparator()
        {
            GUILayout.Space(4f);
            Rect r = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true));
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, false, 0f, _separatorColor, 0f, 0f);
            GUILayout.Space(4f);
        }

        private void DrawRectBorder(Rect r, Color c, float thickness)
        {
            // Top border line
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, thickness), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, c, 0f, 0f);
            // Bottom border line
            GUI.DrawTexture(new Rect(r.x, r.yMax - thickness, r.width, thickness), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, c, 0f, 0f);
            // Left border line
            GUI.DrawTexture(new Rect(r.x, r.y, thickness, r.height), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, c, 0f, 0f);
            // Right border line
            GUI.DrawTexture(new Rect(r.xMax - thickness, r.y, thickness, r.height), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, c, 0f, 0f);
        }

        // ============================================================
        //  Mods Scan System
        // ============================================================
        private void RefreshModsList()
        {
            try
            {
                string modsPath = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Mods");

                if (!Directory.Exists(modsPath))
                {
                    _cachedModNames = Array.Empty<string>();
                    _modNameContents.Clear();
                    _modsButtonContent.text = "Mods (0)";
                    return;
                }

                string[] files = Directory.GetFiles(modsPath, "*.dll");
                var names = new List<string>(files.Length);
                for (int i = 0; i < files.Length; i++)
                {
                    names.Add(Path.GetFileNameWithoutExtension(files[i]));
                }
                names.Sort(StringComparer.OrdinalIgnoreCase);
                _cachedModNames = names.ToArray();

                // Update mod GUIContent cache
                _modNameContents.Clear();
                for (int i = 0; i < _cachedModNames.Length; i++)
                {
                    _modNameContents.Add(new GUIContent("  • " + _cachedModNames[i]));
                }

                _modsButtonContent.text = _modsExpanded ? "▼ Mods (" + _cachedModNames.Length + ")" : "► Mods (" + _cachedModNames.Length + ")";
            }
            catch
            {
                _cachedModNames = Array.Empty<string>();
                _modNameContents.Clear();
                _modsButtonContent.text = "Mods (0)";
            }
        }

        // ============================================================
        //  Telemetry Data (Cached reflection members)
        // ============================================================
        private string GetOnlinePlayerCount()
        {
            EnsurePlayerType();
            if (_playerType == null) return "?";

            if (_allPlayersAccessor == null)
                _allPlayersAccessor = MemberAccessor.Create(_playerType, "AllPlayers", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

            object all = _allPlayersAccessor?.GetValue(null);
            if (all == null) return "?";

            if (all is System.Collections.ICollection col)
                return col.Count.ToString();

            if (all is System.Collections.IEnumerable enumerable && !(all is string))
            {
                int c = 0;
                foreach (var _ in enumerable) c++;
                return c.ToString();
            }

            return "1";
        }

        private string GetLocalChunkLabel()
        {
            EnsurePlayerType();
            if (_playerType == null) return "unknown";

            if (_currentPlayerAccessor == null)
                _currentPlayerAccessor = MemberAccessor.Create(_playerType, "Current", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

            object current = _currentPlayerAccessor?.GetValue(null);
            if (current == null) return "unknown";

            if (_positionAccessor == null)
                _positionAccessor = MemberAccessor.Create(current.GetType(), "Position", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            object posObj = _positionAccessor?.GetValue(current);
            if (posObj is Vector3 v)
                return Mathf.FloorToInt(v.x / 64f) + ", " + Mathf.FloorToInt(v.z / 64f);

            if (_transformAccessor == null)
                _transformAccessor = MemberAccessor.Create(current.GetType(), "PlayerTransform", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            object tObj = _transformAccessor?.GetValue(current);
            if (tObj is Transform t)
            {
                Vector3 pos = t.position;
                return Mathf.FloorToInt(pos.x / 64f) + ", " + Mathf.FloorToInt(pos.z / 64f);
            }

            return "unknown";
        }

        private void EnsurePlayerType()
        {
            if (_playerTypeLookedUp) return;
            _playerTypeLookedUp = true;
            _playerType = ReflectionUtil.FindType("Player");
        }

        // ============================================================
        //  Styling system
        // ============================================================
        private void EnsureStyles()
        {
            if (_stylesBuilt) return;
            _stylesBuilt = true;

            // 1. Create UI Textures
            _bgTex = MakeTex(2, 2, new Color(0.05f, 0.05f, 0.07f, 0.94f));
            _accentTex = MakeTex(2, 2, new Color(0f, 0.9f, 1f, 1f));
            _btnNormalTex = MakeTex(2, 2, new Color(0.12f, 0.14f, 0.18f, 0.4f));
            _btnHoverTex = MakeTex(2, 2, new Color(0.18f, 0.22f, 0.28f, 0.6f));
            _btnActiveTex = MakeTex(2, 2, new Color(0f, 0.9f, 1f, 0.25f));

            // 2. Build Text Styles
            _headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0f, 0.94f, 1f, 0.9f) },
                padding = new RectOffset(0, 0, 4, 4)
            };

            _keyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Normal,
                normal = { textColor = new Color(0.54f, 0.58f, 0.65f) },
                padding = new RectOffset(10, 0, 2, 2)
            };

            _valueStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.9f, 0.93f, 0.96f) },
                padding = new RectOffset(0, 10, 2, 2)
            };

            _modLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                normal = { textColor = new Color(0.6f, 0.7f, 0.8f) },
                padding = new RectOffset(12, 12, 1, 1)
            };

            // 3. Build button style
            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { background = _btnNormalTex, textColor = new Color(0f, 0.85f, 1f) },
                hover = { background = _btnHoverTex, textColor = Color.white },
                active = { background = _btnActiveTex, textColor = Color.white },
                margin = new RectOffset(8, 8, 4, 4),
                padding = new RectOffset(6, 6, 4, 4)
            };
        }

        private static Texture2D MakeTex(int w, int h, Color col)
        {
            var pix = new Color[w * h];
            for (int i = 0; i < pix.Length; i++) pix[i] = col;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat
            };
            tex.SetPixels(pix);
            tex.Apply(false, true);
            return tex;
        }
    }

    // ================================================================
    //  Cached Member Accessor (Avoids dynamic Reflection lookup)
    // ================================================================
    internal sealed class MemberAccessor
    {
        private readonly PropertyInfo _prop;
        private readonly FieldInfo _field;

        private MemberAccessor(PropertyInfo p, FieldInfo f)
        {
            _prop = p;
            _field = f;
        }

        public static MemberAccessor Create(Type type, string name, BindingFlags flags)
        {
            if (type == null) return null;

            PropertyInfo p = null;
            FieldInfo f = null;

            try
            {
                p = type.GetProperty(name, flags);
                if (p != null && p.GetIndexParameters().Length > 0)
                    p = null;
            }
            catch { }

            if (p == null)
            {
                try { f = type.GetField(name, flags); }
                catch { }
            }

            if (p == null && f == null) return null;
            return new MemberAccessor(p, f);
        }

        public object GetValue(object instance)
        {
            try
            {
                if (_prop != null) return _prop.GetValue(instance, null);
                if (_field != null) return _field.GetValue(instance);
            }
            catch { }
            return null;
        }
    }

    // ================================================================
    //  Cached Type Lookup System
    // ================================================================
    internal static class ReflectionUtil
    {
        private static readonly Dictionary<string, Type> TypeCache = new Dictionary<string, Type>(32);

        public static Type FindType(string typeName)
        {
            if (TypeCache.TryGetValue(typeName, out Type cached))
                return cached;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type;
                try { type = assembly.GetType(typeName); }
                catch { continue; }

                if (type != null)
                {
                    TypeCache[typeName] = type;
                    return type;
                }
            }

            TypeCache[typeName] = null; // cache negative lookups
            return null;
        }
    }
}
