using Godot;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

// Server Select panel — first screen the user sees on launch.
//
// Lists every configured server with a status dot (green online / red offline /
// grey checking) and the current online player count. The user picks one,
// clicks Connect, and the existing login flow runs against that server's URL.
//
// The list lives in DefaultServers below for now. We probe each entry by
// opening a short-lived WebSocket and (optionally) requesting SERVER_INFO so
// the player count surfaces without going through the login flow.
public partial class MainMenu : Control
{
    // ── Server Select panel nodes ─────────────────────────────────
    private Control _serverSelectPanel;
    private VBoxContainer _serverListContainer;
    private Button _serverConnectButton;
    private Button _serverQuitButton;
    private Label _serverSelectStatus;
    private ButtonGroup _serverButtonGroup;

    private enum ProbeState { Connecting, Online, Offline }

    // A single network endpoint we are racing for one logical server.
    private class ProbeCandidate
    {
        public string Url;
        public WebSocketPeer Peer;
        public ulong StartMsec;
        public ulong OpenedMsec;
        public bool InfoRequested;
        public bool InfoReceived;
        public bool TimedOut;
    }

    private class ServerEntry
    {
        public string DisplayName;
        // All candidate endpoints we will try in parallel (localhost / LAN / WAN).
        public List<ProbeCandidate> Candidates = new();
        // The endpoint that won the race — used when Connect is clicked.
        public string Url;
        public ProbeState State = ProbeState.Connecting;
        public int PlayerCount = -1;

        public Button Row;
        public ColorRect StatusDot;
        public Label NameLabel;
        public Label PlayerCountLabel;
    }

    private readonly List<ServerEntry> _servers = new();
    private int _selectedServerIndex = -1;

    // ── Probe timing ──────────────────────────────────────────────
    private const ulong ProbeConnectTimeoutMsec = 5000;
    private const ulong ProbeInfoWaitMsec = 1500;

    // ── Default list (edit here to add/rename servers) ────────────
    // Persisted overrides are loaded from user://servers.cfg when present.
    //
    // Norrathian sibling naming convention:
    //   Erollisi Marr     → public/stable tester host (the home box, default
    //                       3xxx port range, WAN 24.33.88.252).
    //                       May appear offline until the box is provisioned.
    //   Mithaniel Marr    → dev cluster on the same WAN, different forwarded
    //                       port (4005 vs 3005).
    //   My Server (Local) → eqhost-style self-host slot. Always shown. Lights
    //                       up green the moment the player starts a server on
    //                       their own machine using the default port. If they
    //                       run multiple local servers or use a custom port,
    //                       they can add entries via user://servers.cfg.
    //
    // Each server has MULTIPLE candidate URLs (localhost / LAN / WAN). We probe
    // them in parallel and use whichever opens first. This handles three cases
    // with no per-machine config:
    //   1. Player on the same PC as the server → 127.0.0.1 wins instantly.
    //   2. Player on the same LAN as the server → LAN/WAN wins (works around
    //      routers that don't support hairpin NAT loopback).
    //   3. Player out on the internet → only WAN can connect.
    private static readonly (string DisplayName, string[] Urls)[] DefaultServers =
    {
        ("Erollisi Marr",     new[] { "ws://127.0.0.1:3005", "ws://24.33.88.252:3005" }),
        ("Mithaniel Marr",    new[] { "ws://127.0.0.1:4005", "ws://24.33.88.252:4005" }),
        ("My Server (Local)", new[] { "ws://127.0.0.1:3005" }),
    };

    private const string ServersConfigPath = "user://servers.cfg";

    // ═════════════════════════════════════════════════════════════════
    //  BUILD PANEL
    // ═════════════════════════════════════════════════════════════════

    private void BuildServerSelectPanel()
    {
        var panel = new PanelContainer { Name = "ServerSelectPanel" };
        _serverSelectPanel = panel;

        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.04f, 0.04f, 0.06f, 0.6f);
        style.BorderWidthLeft = style.BorderWidthTop = style.BorderWidthRight = style.BorderWidthBottom = 2;
        style.BorderColor = new Color(0.7f, 0.55f, 0.2f, 0.8f);
        style.CornerRadiusTopLeft = style.CornerRadiusTopRight = style.CornerRadiusBottomLeft = style.CornerRadiusBottomRight = 8;
        style.ShadowColor = new Color(0, 0, 0, 0.6f);
        style.ShadowSize = 14;
        panel.AddThemeStyleboxOverride("panel", style);

        panel.SetAnchorsPreset(Control.LayoutPreset.Center);
        panel.OffsetLeft = -300;
        panel.OffsetRight = 300;
        panel.OffsetTop = -200;
        panel.OffsetBottom = 200;

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 14);
        vbox.OffsetLeft = 22; vbox.OffsetTop = 22; vbox.OffsetRight = -22; vbox.OffsetBottom = -22;
        vbox.SetAnchorsPreset(LayoutPreset.FullRect);

        var header = new Label { Text = "Server Select" };
        header.HorizontalAlignment = HorizontalAlignment.Center;
        header.AddThemeFontSizeOverride("font_size", 22);
        header.AddThemeColorOverride("font_color", new Color(0.85f, 0.7f, 0.25f, 1f));
        vbox.AddChild(header);

        _serverListContainer = new VBoxContainer();
        _serverListContainer.AddThemeConstantOverride("separation", 4);
        _serverListContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _serverListContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
        vbox.AddChild(_serverListContainer);

        // Buttons row (Connect / Quit)
        var btnRow = new HBoxContainer();
        btnRow.Alignment = BoxContainer.AlignmentMode.Center;
        btnRow.AddThemeConstantOverride("separation", 18);

        _serverConnectButton = new Button { Text = "Connect", Disabled = true };
        _serverConnectButton.CustomMinimumSize = new Vector2(130, 38);
        _serverConnectButton.AddThemeFontSizeOverride("font_size", 16);
        _serverConnectButton.Pressed += OnServerConnectPressed;
        btnRow.AddChild(_serverConnectButton);

        _serverQuitButton = new Button { Text = "Quit" };
        _serverQuitButton.CustomMinimumSize = new Vector2(130, 38);
        _serverQuitButton.AddThemeFontSizeOverride("font_size", 16);
        _serverQuitButton.Pressed += () => GetTree().Quit();
        btnRow.AddChild(_serverQuitButton);

        vbox.AddChild(btnRow);

        _serverSelectStatus = new Label();
        _serverSelectStatus.HorizontalAlignment = HorizontalAlignment.Center;
        _serverSelectStatus.AddThemeFontSizeOverride("font_size", 11);
        _serverSelectStatus.AddThemeColorOverride("font_color", new Color(0.55f, 0.55f, 0.55f, 1f));
        _serverSelectStatus.Text = "Checking server status…";
        vbox.AddChild(_serverSelectStatus);

        panel.AddChild(vbox);
        AddChild(panel);

        BuildServerRows();
    }

    private void BuildServerRows()
    {
        _serverButtonGroup = new ButtonGroup();
        _servers.Clear();
        _selectedServerIndex = -1;

        // Source list (defaults + any persisted overrides)
        var sourceList = LoadServerListOverrides();

        for (int i = 0; i < sourceList.Count; i++)
        {
            var (name, urls) = sourceList[i];
            var entry = new ServerEntry { DisplayName = name };
            foreach (var u in urls)
                entry.Candidates.Add(new ProbeCandidate { Url = u });
            // Default the displayed/connect URL to the first candidate until
            // probing finds a working one.
            entry.Url = urls.Count > 0 ? urls[0] : "";

            var row = new Button
            {
                ToggleMode = true,
                ButtonGroup = _serverButtonGroup,
                CustomMinimumSize = new Vector2(0, 46),
                Flat = false,
                Text = "",
            };

            // Row contents — overlay a HBox on top of the button so we can show
            // the dot, name and player count cleanly. MouseFilter = Ignore on
            // children so clicks reach the button itself.
            var hb = new HBoxContainer();
            hb.AddThemeConstantOverride("separation", 12);
            hb.OffsetLeft = 14; hb.OffsetTop = 0; hb.OffsetRight = -14; hb.OffsetBottom = 0;
            hb.SetAnchorsPreset(LayoutPreset.FullRect);
            hb.MouseFilter = MouseFilterEnum.Ignore;

            entry.StatusDot = new ColorRect();
            entry.StatusDot.Color = new Color(0.65f, 0.65f, 0.65f, 0.85f);
            entry.StatusDot.CustomMinimumSize = new Vector2(14, 14);
            entry.StatusDot.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            entry.StatusDot.MouseFilter = MouseFilterEnum.Ignore;
            // Note: ColorRect has no built-in corner_radius, but a small square dot reads fine.
            hb.AddChild(entry.StatusDot);

            entry.NameLabel = new Label { Text = name };
            entry.NameLabel.AddThemeFontSizeOverride("font_size", 18);
            entry.NameLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.85f, 0.7f, 1f));
            entry.NameLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            entry.NameLabel.VerticalAlignment = VerticalAlignment.Center;
            entry.NameLabel.MouseFilter = MouseFilterEnum.Ignore;
            hb.AddChild(entry.NameLabel);

            entry.PlayerCountLabel = new Label { Text = "…" };
            entry.PlayerCountLabel.AddThemeFontSizeOverride("font_size", 16);
            entry.PlayerCountLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.65f, 0.5f, 1f));
            entry.PlayerCountLabel.VerticalAlignment = VerticalAlignment.Center;
            entry.PlayerCountLabel.HorizontalAlignment = HorizontalAlignment.Right;
            entry.PlayerCountLabel.CustomMinimumSize = new Vector2(48, 0);
            entry.PlayerCountLabel.MouseFilter = MouseFilterEnum.Ignore;
            hb.AddChild(entry.PlayerCountLabel);

            row.AddChild(hb);

            int capturedIndex = i;
            row.Toggled += (bool pressed) =>
            {
                if (pressed) OnServerSelected(capturedIndex);
            };

            entry.Row = row;
            _serverListContainer.AddChild(row);

            if (i < sourceList.Count - 1)
                _serverListContainer.AddChild(new HSeparator());

            _servers.Add(entry);
        }
    }

    // Returns the full server list: the curated baked defaults followed by any
    // user-added entries from user://servers.cfg. This mirrors the classic EQ
    // "eqhost.txt" pattern — official servers always show, plus anything the
    // player wants to add locally.
    //
    // servers.cfg format (UTF-8, JSON array). Either "url" (single) or
    // "urls" (array) is accepted per entry; "urls" wins if both are present.
    //   [
    //     {"name":"Friend's Server","url":"ws://example.com:3005"},
    //     {"name":"LAN Box",
    //      "urls":["ws://192.168.1.50:3005","ws://24.33.88.252:3005"]}
    //   ]
    //
    // The defaults are never removed by servers.cfg — if a tester needs to
    // hide an entry they can leave it; offline rows are dimmed and the
    // Connect button stays disabled, so they're harmless.
    private static List<(string DisplayName, List<string> Urls)> LoadServerListOverrides()
    {
        var list = new List<(string, List<string>)>();
        foreach (var s in DefaultServers)
            list.Add((s.DisplayName, new List<string>(s.Urls)));

        try
        {
            if (!Godot.FileAccess.FileExists(ServersConfigPath))
                return list;

            using var f = Godot.FileAccess.Open(ServersConfigPath, Godot.FileAccess.ModeFlags.Read);
            if (f == null || f.GetLength() < 2)
                return list;

            string json = f.GetAsText();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                GD.PrintErr("[SERVERSELECT] servers.cfg must be a JSON array; ignoring file.");
                return list;
            }

            int added = 0;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                if (!el.TryGetProperty("name", out var nameEl)) continue;
                string name = nameEl.GetString();
                if (string.IsNullOrWhiteSpace(name)) continue;

                var urls = new List<string>();

                if (el.TryGetProperty("urls", out var urlsEl) && urlsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var u in urlsEl.EnumerateArray())
                    {
                        if (u.ValueKind == JsonValueKind.String)
                        {
                            string s = u.GetString();
                            if (!string.IsNullOrWhiteSpace(s)) urls.Add(s.Trim());
                        }
                    }
                }
                else if (el.TryGetProperty("url", out var urlEl) && urlEl.ValueKind == JsonValueKind.String)
                {
                    string s = urlEl.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) urls.Add(s.Trim());
                }

                if (urls.Count == 0) continue;
                list.Add((name.Trim(), urls));
                added++;
            }

            if (added > 0)
                GD.Print($"[SERVERSELECT] Loaded {added} additional server(s) from servers.cfg");
            return list;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[SERVERSELECT] servers.cfg read failed ({ex.Message}); defaults only.");
            return list;
        }
    }

    // ═════════════════════════════════════════════════════════════════
    //  PROBING
    // ═════════════════════════════════════════════════════════════════

    private void StartServerProbes()
    {
        ulong now = Time.GetTicksMsec();

        foreach (var entry in _servers)
        {
            // Kill any lingering sockets before restarting.
            foreach (var c in entry.Candidates) CleanupCandidate(c);

            entry.State = ProbeState.Connecting;
            entry.PlayerCount = -1;
            UpdateServerRowVisuals(entry);

            // Fire all candidate connections in parallel. First one to open wins.
            foreach (var c in entry.Candidates)
            {
                c.Peer = new WebSocketPeer();
                c.StartMsec = now;
                c.OpenedMsec = 0;
                c.InfoRequested = false;
                c.InfoReceived = false;
                c.TimedOut = false;

                var err = c.Peer.ConnectToUrl(c.Url);
                if (err != Error.Ok)
                {
                    GD.Print($"[SERVERSELECT] Probe could not start ({entry.DisplayName} → {c.Url}): {err}");
                    c.TimedOut = true;
                    c.Peer = null;
                }
            }

            // If every candidate failed at the very first step, mark offline now.
            if (AllCandidatesFinished(entry))
                ResolveEntryState(entry);
        }

        UpdateConnectButton();
    }

    private void PollServerProbes()
    {
        if (_serverSelectPanel == null || !_serverSelectPanel.Visible) return;

        ulong now = Time.GetTicksMsec();

        foreach (var entry in _servers)
        {
            bool sawOpenThisFrame = false;

            foreach (var c in entry.Candidates)
            {
                if (c.Peer == null) continue;

                c.Peer.Poll();
                var state = c.Peer.GetReadyState();

                switch (state)
                {
                    case WebSocketPeer.State.Connecting:
                        if (now - c.StartMsec > ProbeConnectTimeoutMsec)
                        {
                            c.TimedOut = true;
                            CleanupCandidate(c);
                        }
                        break;

                    case WebSocketPeer.State.Open:
                        if (c.OpenedMsec == 0) c.OpenedMsec = now;
                        sawOpenThisFrame = true;

                        // First candidate to open wins → adopt its URL.
                        if (entry.State != ProbeState.Online)
                        {
                            entry.State = ProbeState.Online;
                            entry.Url = c.Url;
                            UpdateServerRowVisuals(entry);
                            RefreshConnectButtonIfSelected(entry);
                            GD.Print($"[SERVERSELECT] {entry.DisplayName} reachable via {c.Url}");

                            // Cancel the other (still-pending) candidates — we have a winner.
                            foreach (var other in entry.Candidates)
                                if (other != c && other.Peer != null && other.OpenedMsec == 0)
                                    CleanupCandidate(other);
                        }

                        while (c.Peer != null && c.Peer.GetAvailablePacketCount() > 0)
                        {
                            byte[] pkt = c.Peer.GetPacket();
                            string msg = Encoding.UTF8.GetString(pkt);
                            TryParseServerInfo(entry, c, msg);
                        }

                        if (c.Peer != null && !c.InfoRequested && now - c.OpenedMsec > 30)
                        {
                            c.Peer.SendText("{\"type\":\"SERVER_INFO_REQUEST\"}");
                            c.InfoRequested = true;
                        }

                        // Done waiting — close the winning probe so we don't hog a server slot.
                        if (c.Peer != null && now - c.OpenedMsec > ProbeInfoWaitMsec)
                            CleanupCandidate(c);
                        break;

                    case WebSocketPeer.State.Closing:
                        // Wait for fully closed
                        break;

                    case WebSocketPeer.State.Closed:
                        if (c.OpenedMsec == 0) c.TimedOut = true; // never opened
                        c.Peer = null;
                        break;
                }
            }

            // If no candidate is still pending and we never saw one open, mark offline.
            if (!sawOpenThisFrame && entry.State != ProbeState.Online && AllCandidatesFinished(entry))
                ResolveEntryState(entry);
        }
    }

    private static bool AllCandidatesFinished(ServerEntry entry)
    {
        foreach (var c in entry.Candidates)
        {
            // Still actively connecting?
            if (c.Peer != null && c.OpenedMsec == 0 && !c.TimedOut)
                return false;
        }
        return true;
    }

    private void ResolveEntryState(ServerEntry entry)
    {
        bool anyOpened = false;
        foreach (var c in entry.Candidates)
            if (c.OpenedMsec != 0) { anyOpened = true; break; }

        entry.State = anyOpened ? ProbeState.Online : ProbeState.Offline;
        UpdateServerRowVisuals(entry);
        RefreshConnectButtonIfSelected(entry);
    }

    private void TryParseServerInfo(ServerEntry entry, ProbeCandidate candidate, string msg)
    {
        try
        {
            using var doc = JsonDocument.Parse(msg);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var typeEl)) return;
            string type = typeEl.GetString();

            if (type == "SERVER_INFO")
            {
                if (root.TryGetProperty("playerCount", out var pc))
                {
                    entry.PlayerCount = pc.GetInt32();
                    candidate.InfoReceived = true;
                    UpdateServerRowVisuals(entry);
                }
                if (root.TryGetProperty("name", out var nm) && entry.NameLabel != null)
                {
                    string srvName = nm.GetString();
                    if (!string.IsNullOrEmpty(srvName))
                        entry.NameLabel.TooltipText = srvName;
                }
            }
        }
        catch
        {
            // Ignore parse errors — the probe only cares about a SERVER_INFO reply.
        }
    }

    private void CleanupCandidate(ProbeCandidate c)
    {
        if (c.Peer == null) return;
        try
        {
            var s = c.Peer.GetReadyState();
            if (s == WebSocketPeer.State.Open || s == WebSocketPeer.State.Connecting)
                c.Peer.Close();
        }
        catch { }
        c.Peer = null;
    }

    private void ShutdownServerProbes()
    {
        foreach (var entry in _servers)
            foreach (var c in entry.Candidates)
                CleanupCandidate(c);
    }

    // ═════════════════════════════════════════════════════════════════
    //  VISUALS / SELECTION
    // ═════════════════════════════════════════════════════════════════

    private void UpdateServerRowVisuals(ServerEntry entry)
    {
        if (entry.StatusDot != null)
        {
            entry.StatusDot.Color = entry.State switch
            {
                ProbeState.Online    => new Color(0.30f, 0.85f, 0.30f, 1f),
                ProbeState.Offline   => new Color(0.85f, 0.25f, 0.25f, 1f),
                _                    => new Color(0.65f, 0.65f, 0.65f, 0.85f),
            };
        }

        if (entry.PlayerCountLabel != null)
        {
            entry.PlayerCountLabel.Text = entry.State switch
            {
                ProbeState.Connecting              => "…",
                ProbeState.Offline                 => "—",
                ProbeState.Online when entry.PlayerCount >= 0 => entry.PlayerCount.ToString(),
                _                                  => "—",
            };
        }

        if (entry.NameLabel != null)
        {
            entry.NameLabel.AddThemeColorOverride("font_color",
                entry.State == ProbeState.Offline
                    ? new Color(0.55f, 0.45f, 0.4f, 1f)
                    : new Color(0.9f, 0.85f, 0.7f, 1f));
        }
    }

    private void OnServerSelected(int index)
    {
        _selectedServerIndex = index;
        var entry = _servers[index];
        UpdateConnectButton();

        if (entry.State == ProbeState.Offline)
            _serverSelectStatus.Text = $"{entry.DisplayName} is offline.";
        else if (entry.State == ProbeState.Connecting)
            _serverSelectStatus.Text = $"Checking {entry.DisplayName}…";
        else
            _serverSelectStatus.Text = $"Selected: {entry.DisplayName}";
    }

    private void RefreshConnectButtonIfSelected(ServerEntry entry)
    {
        if (_selectedServerIndex < 0 || _selectedServerIndex >= _servers.Count) return;
        if (_servers[_selectedServerIndex] == entry)
            UpdateConnectButton();
    }

    private void UpdateConnectButton()
    {
        if (_serverConnectButton == null) return;
        if (_selectedServerIndex < 0 || _selectedServerIndex >= _servers.Count)
        {
            _serverConnectButton.Disabled = true;
            return;
        }
        var entry = _servers[_selectedServerIndex];
        _serverConnectButton.Disabled = entry.State != ProbeState.Online;
    }

    private void OnServerConnectPressed()
    {
        if (_selectedServerIndex < 0 || _selectedServerIndex >= _servers.Count) return;
        var entry = _servers[_selectedServerIndex];
        if (entry.State != ProbeState.Online)
        {
            _serverSelectStatus.Text = $"{entry.DisplayName} is offline.";
            return;
        }

        ShutdownServerProbes();

        GameClient.Instance.ServerUrl = entry.Url;
        GameState.ServerName = entry.DisplayName;

        if (_serverAddressInput != null)
            _serverAddressInput.Text = entry.Url;

        SaveLastServerSelection(entry.Url);

        GD.Print($"[SERVERSELECT] Selected '{entry.DisplayName}' → {entry.Url}");
        if (!EQAssetConfig.Instance.IsConfigured)
        {
            ShowMandatoryEverQuestLinkGate();
            return;
        }
        ShowPanel("login");
    }

    private void SaveLastServerSelection(string url)
    {
        try
        {
            var config = new ConfigFile();
            config.Load(CredentialsPath); // ignore error — file may not exist yet
            config.SetValue("server", "address", url);
            config.Save(CredentialsPath);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[SERVERSELECT] Could not persist server selection: {ex.Message}");
        }
    }

    // Pre-select the row matching `url` (if any). Matches against any candidate
    // URL on the entry, since the winning URL may differ between machines.
    private void PreselectServerByUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return;
        for (int i = 0; i < _servers.Count; i++)
        {
            var entry = _servers[i];
            bool match = string.Equals(entry.Url, url, StringComparison.OrdinalIgnoreCase);
            if (!match)
            {
                foreach (var c in entry.Candidates)
                {
                    if (string.Equals(c.Url, url, StringComparison.OrdinalIgnoreCase))
                    {
                        match = true;
                        break;
                    }
                }
            }
            if (match)
            {
                entry.Row.ButtonPressed = true; // triggers Toggled → OnServerSelected
                return;
            }
        }
    }
}
